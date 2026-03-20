// Adapted from master branch WebSocket.cs — Unity dependencies removed, namespace changed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NativeWebSocket.Legacy
{
    public delegate void WebSocketOpenEventHandler();
    public delegate void WebSocketMessageEventHandler(byte[] data);
    public delegate void WebSocketErrorEventHandler(string errorMsg);
    public delegate void WebSocketCloseEventHandler(WebSocketCloseCode closeCode);

    public enum WebSocketCloseCode
    {
        NotSet = 0,
        Normal = 1000,
        Away = 1001,
        ProtocolError = 1002,
        UnsupportedData = 1003,
        Undefined = 1004,
        NoStatus = 1005,
        Abnormal = 1006,
        InvalidData = 1007,
        PolicyViolation = 1008,
        TooBig = 1009,
        MandatoryExtension = 1010,
        ServerError = 1011,
        TlsHandshakeFailure = 1015
    }

    public enum WebSocketState
    {
        Connecting,
        Open,
        Closing,
        Closed
    }

    public static class WebSocketHelpers
    {
        public static WebSocketCloseCode ParseCloseCodeEnum(int closeCode)
        {
            if (Enum.IsDefined(typeof(WebSocketCloseCode), closeCode))
                return (WebSocketCloseCode)closeCode;
            return WebSocketCloseCode.Undefined;
        }
    }

    public class WaitForBackgroundThread
    {
        public ConfiguredTaskAwaitable.ConfiguredTaskAwaiter GetAwaiter()
        {
            return Task.Run(() => { }).ConfigureAwait(false).GetAwaiter();
        }
    }

    public class WebSocket
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

        private readonly object OutgoingMessageLock = new object();
        private readonly object IncomingMessageLock = new object();

        private bool isSending = false;
        private List<ArraySegment<byte>> sendBytesQueue = new List<ArraySegment<byte>>();
        private List<ArraySegment<byte>> sendTextQueue = new List<ArraySegment<byte>>();

        public WebSocket(string url, Dictionary<string, string> headers = null)
        {
            uri = new Uri(url);
            this.headers = headers ?? new Dictionary<string, string>();
            subprotocols = new List<string>();

            string protocol = uri.Scheme;
            if (!protocol.Equals("ws") && !protocol.Equals("wss"))
                throw new ArgumentException("Unsupported protocol: " + protocol);
        }

        public WebSocket(string url, string subprotocol, Dictionary<string, string> headers = null)
        {
            uri = new Uri(url);
            this.headers = headers ?? new Dictionary<string, string>();
            subprotocols = new List<string> { subprotocol };

            string protocol = uri.Scheme;
            if (!protocol.Equals("ws") && !protocol.Equals("wss"))
                throw new ArgumentException("Unsupported protocol: " + protocol);
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
                    m_Socket.Options.SetRequestHeader(header.Key, header.Value);

                foreach (string subprotocol in subprotocols)
                    m_Socket.Options.AddSubProtocol(subprotocol);

                await m_Socket.ConnectAsync(uri, m_CancellationToken);
                OnOpen?.Invoke();

                await Receive();
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex.Message);
                OnClose?.Invoke(WebSocketCloseCode.Abnormal);
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

        private async Task SendMessage(List<ArraySegment<byte>> queue, WebSocketMessageType messageType, ArraySegment<byte> buffer)
        {
            if (buffer.Count == 0)
                return;

            bool sending;

            lock (OutgoingMessageLock)
            {
                sending = isSending;
                if (!isSending)
                    isSending = true;
            }

            if (!sending)
            {
                // Monitor.TryEnter + .Wait() pattern from old implementation
                if (!Monitor.TryEnter(m_Socket, 1000))
                {
                    await m_Socket.CloseAsync(WebSocketCloseStatus.InternalServerError, string.Empty, m_CancellationToken);
                    return;
                }

                try
                {
                    var t = m_Socket.SendAsync(buffer, messageType, true, m_CancellationToken);
                    t.Wait(m_CancellationToken);
                }
                finally
                {
                    Monitor.Exit(m_Socket);
                    lock (OutgoingMessageLock)
                    {
                        isSending = false;
                    }
                }

                await HandleQueue(queue, messageType);
            }
            else
            {
                lock (OutgoingMessageLock)
                {
                    queue.Add(buffer);
                }
            }
        }

        private async Task HandleQueue(List<ArraySegment<byte>> queue, WebSocketMessageType messageType)
        {
            var buffer = new ArraySegment<byte>();
            lock (OutgoingMessageLock)
            {
                if (queue.Count > 0)
                {
                    buffer = queue[0];
                    queue.RemoveAt(0);
                }
            }

            if (buffer.Count > 0)
            {
                await SendMessage(queue, messageType, buffer);
            }
        }

        private List<byte[]> m_MessageList = new List<byte[]>();

        // Old implementation: copies list on dispatch
        public void DispatchMessageQueue()
        {
            if (m_MessageList.Count == 0)
                return;

            List<byte[]> messageListCopy;

            lock (IncomingMessageLock)
            {
                messageListCopy = new List<byte[]>(m_MessageList);
                m_MessageList.Clear();
            }

            var len = messageListCopy.Count;
            for (int i = 0; i < len; i++)
            {
                OnMessage?.Invoke(messageListCopy[i]);
            }
        }

        public async Task Receive()
        {
            WebSocketCloseCode closeCode = WebSocketCloseCode.Abnormal;
            await new WaitForBackgroundThread();

            ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[8192]);
            try
            {
                while (m_Socket.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    WebSocketReceiveResult result = null;

                    using (var ms = new MemoryStream())
                    {
                        do
                        {
                            result = await m_Socket.ReceiveAsync(buffer, m_CancellationToken);
                            ms.Write(buffer.Array, buffer.Offset, result.Count);
                        }
                        while (!result.EndOfMessage);

                        ms.Seek(0, SeekOrigin.Begin);

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            lock (IncomingMessageLock)
                            {
                                m_MessageList.Add(ms.ToArray());
                            }
                        }
                        else if (result.MessageType == WebSocketMessageType.Binary)
                        {
                            lock (IncomingMessageLock)
                            {
                                m_MessageList.Add(ms.ToArray());
                            }
                        }
                        else if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await Close();
                            closeCode = WebSocketHelpers.ParseCloseCodeEnum((int)result.CloseStatus);
                            break;
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
                // Original used WaitForUpdate (Unity main thread). Without Unity, invoke directly.
                OnClose?.Invoke(closeCode);
            }
        }

        public async Task Close()
        {
            if (State == WebSocketState.Open)
            {
                await m_Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, m_CancellationToken);
            }
        }
    }
}
