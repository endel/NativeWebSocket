using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NativeWebSocket
{
    public class WebSocket : IWebSocket
    {
        public event WebSocketOpenEventHandler OnOpen;
        public event WebSocketMessageEventHandler OnMessage;
        public event WebSocketErrorEventHandler OnError;
        public event WebSocketCloseEventHandler OnClose;

        private Uri uri;
        private Dictionary<string, string> headers;
        private List<string> subprotocols;
        private ClientWebSocket m_Socket = new ClientWebSocket();

        private CancellationTokenSource m_TokenSource;
        private CancellationToken m_CancellationToken;

        private readonly SynchronizationContext _syncContext;
        private List<Action> m_EventQueue = new List<Action>();
        private List<Action> m_DispatchQueue = new List<Action>();
        private readonly object EventQueueLock = new object();

        private readonly object OutgoingMessageLock = new object();
        private bool isSending = false;
        private Queue<ArraySegment<byte>> sendBytesQueue = new Queue<ArraySegment<byte>>();
        private Queue<ArraySegment<byte>> sendTextQueue = new Queue<ArraySegment<byte>>();

        public WebSocket(string url, Dictionary<string, string> headers = null)
        {
            uri = new Uri(url);
            _syncContext = SynchronizationContext.Current;
            this.headers = headers ?? new Dictionary<string, string>();
            subprotocols = new List<string>();

            string protocol = uri.Scheme;
            if (!protocol.Equals("ws") && !protocol.Equals("wss"))
                throw new ArgumentException("Unsupported protocol: " + protocol);
        }

        public WebSocket(string url, string subprotocol, Dictionary<string, string> headers = null)
        {
            uri = new Uri(url);
            _syncContext = SynchronizationContext.Current;
            this.headers = headers ?? new Dictionary<string, string>();
            subprotocols = new List<string> { subprotocol };

            string protocol = uri.Scheme;
            if (!protocol.Equals("ws") && !protocol.Equals("wss"))
                throw new ArgumentException("Unsupported protocol: " + protocol);
        }

        public WebSocket(string url, List<string> subprotocols, Dictionary<string, string> headers = null)
        {
            uri = new Uri(url);
            _syncContext = SynchronizationContext.Current;
            this.headers = headers ?? new Dictionary<string, string>();
            this.subprotocols = subprotocols;

            string protocol = uri.Scheme;
            if (!protocol.Equals("ws") && !protocol.Equals("wss"))
                throw new ArgumentException("Unsupported protocol: " + protocol);
        }

        private void EnqueueEvent(Action action)
        {
            if (_syncContext != null)
            {
                _syncContext.Post(_ => action(), null);
            }
            else
            {
                lock (EventQueueLock)
                {
                    m_EventQueue.Add(action);
                }
            }
        }

        /// <summary>
        /// Dispatches queued events when no SynchronizationContext is available.
        /// Not needed when a SynchronizationContext is present (Unity, Godot, MonoGame with WebSocketGameComponent).
        /// </summary>
        public void DispatchMessageQueue()
        {
            if (m_EventQueue.Count == 0) return;

            lock (EventQueueLock)
            {
                var tmp = m_DispatchQueue;
                m_DispatchQueue = m_EventQueue;
                m_EventQueue = tmp;
            }

            foreach (var action in m_DispatchQueue)
            {
                action();
            }

            m_DispatchQueue.Clear();
        }

        public void CancelConnection()
        {
            m_TokenSource?.Cancel();
        }

        public async Task Connect()
        {
            try
            {
                m_TokenSource = new CancellationTokenSource();
                m_CancellationToken = m_TokenSource.Token;

                m_Socket = new ClientWebSocket();

                foreach (var header in headers)
                {
                    m_Socket.Options.SetRequestHeader(header.Key, header.Value);
                }

                foreach (string subprotocol in subprotocols)
                {
                    m_Socket.Options.AddSubProtocol(subprotocol);
                }

                await m_Socket.ConnectAsync(uri, m_CancellationToken).ConfigureAwait(false);

                EnqueueEvent(() => OnOpen?.Invoke());

                await Receive().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                EnqueueEvent(() => OnError?.Invoke(ex.Message));
                EnqueueEvent(() => OnClose?.Invoke(WebSocketCloseCode.Abnormal));
            }
            finally
            {
                if (m_Socket != null)
                {
                    m_TokenSource.Cancel();
                    m_Socket.Dispose();
                }
            }
        }

        public WebSocketState State
        {
            get
            {
                switch (m_Socket.State)
                {
                    case System.Net.WebSockets.WebSocketState.Connecting:
                        return WebSocketState.Connecting;

                    case System.Net.WebSockets.WebSocketState.Open:
                        return WebSocketState.Open;

                    case System.Net.WebSockets.WebSocketState.CloseSent:
                    case System.Net.WebSockets.WebSocketState.CloseReceived:
                        return WebSocketState.Closing;

                    case System.Net.WebSockets.WebSocketState.Closed:
                        return WebSocketState.Closed;

                    default:
                        return WebSocketState.Closed;
                }
            }
        }

        public Task Send(byte[] bytes)
        {
            return SendMessage(sendBytesQueue, WebSocketMessageType.Binary, new ArraySegment<byte>(bytes));
        }

        public Task SendText(string message)
        {
            var encoded = Encoding.UTF8.GetBytes(message);
            return SendMessage(sendTextQueue, WebSocketMessageType.Text, new ArraySegment<byte>(encoded, 0, encoded.Length));
        }

        private async Task SendMessage(Queue<ArraySegment<byte>> queue, WebSocketMessageType messageType, ArraySegment<byte> buffer)
        {
            if (buffer.Count == 0 || State != WebSocketState.Open)
            {
                return;
            }

            bool sending;

            lock (OutgoingMessageLock)
            {
                sending = isSending;

                if (!isSending)
                {
                    isSending = true;
                }
            }

            if (!sending)
            {
                try
                {
                    await m_Socket.SendAsync(buffer, messageType, true, m_CancellationToken).ConfigureAwait(false);

                    // Drain the queue iteratively instead of recursively
                    while (true)
                    {
                        ArraySegment<byte> next;
                        lock (OutgoingMessageLock)
                        {
                            if (queue.Count == 0)
                                break;
                            next = queue.Dequeue();
                        }

                        await m_Socket.SendAsync(next, messageType, true, m_CancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    lock (OutgoingMessageLock)
                    {
                        isSending = false;
                    }
                }
            }
            else
            {
                lock (OutgoingMessageLock)
                {
                    queue.Enqueue(buffer);
                }
            }
        }

        public async Task Receive()
        {
            WebSocketCloseCode closeCode = WebSocketCloseCode.Abnormal;
            ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[8192]);
            try
            {
                while (m_Socket.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    var result = await m_Socket.ReceiveAsync(buffer, m_CancellationToken).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await Close().ConfigureAwait(false);
                        closeCode = WebSocketHelpers.ParseCloseCodeEnum((int)result.CloseStatus);
                        break;
                    }

                    if (result.EndOfMessage)
                    {
                        // Fast path: single-frame message, avoid MemoryStream
                        var data = new byte[result.Count];
                        Buffer.BlockCopy(buffer.Array, buffer.Offset, data, 0, result.Count);
                        EnqueueEvent(() => OnMessage?.Invoke(data));
                    }
                    else
                    {
                        // Multi-frame message: accumulate in MemoryStream
                        using (var ms = new MemoryStream())
                        {
                            ms.Write(buffer.Array, buffer.Offset, result.Count);

                            do
                            {
                                result = await m_Socket.ReceiveAsync(buffer, m_CancellationToken).ConfigureAwait(false);
                                ms.Write(buffer.Array, buffer.Offset, result.Count);
                            }
                            while (!result.EndOfMessage);

                            var data = ms.ToArray();
                            EnqueueEvent(() => OnMessage?.Invoke(data));
                        }
                    }
                }
            }
            catch (Exception)
            {
                m_TokenSource.Cancel();
            }
            finally
            {
                EnqueueEvent(() => OnClose?.Invoke(closeCode));
            }
        }

        public async Task Close(WebSocketCloseCode code = WebSocketCloseCode.Normal, string reason = null)
        {
            if (State != WebSocketState.Open)
                return;

            await m_Socket.CloseAsync((WebSocketCloseStatus)(int)code, reason ?? string.Empty, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Factory for creating WebSocket instances.
    /// </summary>
    public static class WebSocketFactory
    {
        /// <summary>
        /// Create WebSocket client instance
        /// </summary>
        /// <returns>The WebSocket instance.</returns>
        /// <param name="url">WebSocket valid URL.</param>
        public static WebSocket CreateInstance(string url)
        {
            return new WebSocket(url);
        }
    }
}
