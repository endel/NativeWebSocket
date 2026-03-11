using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using AOT;
using UnityEngine;

public class MainThreadUtil : MonoBehaviour
{
    public static MainThreadUtil Instance { get; private set; }
    public static SynchronizationContext synchronizationContext { get; private set; }

    private static readonly ConcurrentQueue<IEnumerator> coroutineQueue = new ConcurrentQueue<IEnumerator>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Setup()
    {
        if (Instance != null)
            return;

        var go = new GameObject("MainThreadUtil");
        Instance = go.AddComponent<MainThreadUtil>();
        synchronizationContext = SynchronizationContext.Current;
    }

    public static void Run(IEnumerator waitForUpdate)
    {
        if (waitForUpdate == null)
            return;

        if (Instance == null)
            Setup();

        coroutineQueue.Enqueue(waitForUpdate);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (synchronizationContext == null)
            synchronizationContext = SynchronizationContext.Current;

        gameObject.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        while (coroutineQueue.TryDequeue(out var work))
        {
            try
            {
                StartCoroutine(work);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}

public class WaitForUpdate : CustomYieldInstruction
{
    public override bool keepWaiting
    {
        get { return false; }
    }

    public MainThreadAwaiter GetAwaiter()
    {
        var awaiter = new MainThreadAwaiter();
        MainThreadUtil.Run(CoroutineWrapper(this, awaiter));
        return awaiter;
    }

    public class MainThreadAwaiter : INotifyCompletion
    {
        private Action continuation;

        public bool IsCompleted { get; set; }

        public void GetResult() { }

        public void Complete()
        {
            IsCompleted = true;
            continuation?.Invoke();
        }

        void INotifyCompletion.OnCompleted(Action continuation)
        {
            this.continuation = continuation;
        }
    }

    public static IEnumerator CoroutineWrapper(IEnumerator theWorker, MainThreadAwaiter awaiter)
    {
        yield return theWorker;
        awaiter.Complete();
    }
}

namespace NativeWebSocket
{
    public delegate void WebSocketOpenEventHandler();
    public delegate void WebSocketMessageEventHandler(byte[] data);
    public delegate void WebSocketErrorEventHandler(string errorMsg);
    public delegate void WebSocketCloseEventHandler(WebSocketCloseCode closeCode);

    public enum WebSocketCloseCode
    {
        /* Do NOT use NotSet - it's only purpose is to indicate that the close code cannot be parsed. */
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

    public interface IWebSocket
    {
        event WebSocketOpenEventHandler OnOpen;
        event WebSocketMessageEventHandler OnMessage;
        event WebSocketErrorEventHandler OnError;
        event WebSocketCloseEventHandler OnClose;

        WebSocketState State { get; }
    }

    public static class WebSocketHelpers
    {
        public static WebSocketCloseCode ParseCloseCodeEnum(int closeCode)
        {
            if (WebSocketCloseCode.IsDefined(typeof(WebSocketCloseCode), closeCode))
                return (WebSocketCloseCode)closeCode;

            return WebSocketCloseCode.Undefined;
        }

        public static WebSocketException GetErrorMessageFromCode(int errorCode, Exception inner)
        {
            switch (errorCode)
            {
                case -1:
                    return new WebSocketUnexpectedException("WebSocket instance not found.", inner);
                case -2:
                    return new WebSocketInvalidStateException("WebSocket is already connected or in connecting state.", inner);
                case -3:
                    return new WebSocketInvalidStateException("WebSocket is not connected.", inner);
                case -4:
                    return new WebSocketInvalidStateException("WebSocket is already closing.", inner);
                case -5:
                    return new WebSocketInvalidStateException("WebSocket is already closed.", inner);
                case -6:
                    return new WebSocketInvalidStateException("WebSocket is not in open state.", inner);
                case -7:
                    return new WebSocketInvalidArgumentException("Cannot close WebSocket. An invalid code was specified or reason is too long.", inner);
                default:
                    return new WebSocketUnexpectedException("Unknown error.", inner);
            }
        }
    }

    public class WebSocketException : Exception
    {
        public WebSocketException() { }
        public WebSocketException(string message) : base(message) { }
        public WebSocketException(string message, Exception inner) : base(message, inner) { }
    }

    public class WebSocketUnexpectedException : WebSocketException
    {
        public WebSocketUnexpectedException() { }
        public WebSocketUnexpectedException(string message) : base(message) { }
        public WebSocketUnexpectedException(string message, Exception inner) : base(message, inner) { }
    }

    public class WebSocketInvalidArgumentException : WebSocketException
    {
        public WebSocketInvalidArgumentException() { }
        public WebSocketInvalidArgumentException(string message) : base(message) { }
        public WebSocketInvalidArgumentException(string message, Exception inner) : base(message, inner) { }
    }

    public class WebSocketInvalidStateException : WebSocketException
    {
        public WebSocketInvalidStateException() { }
        public WebSocketInvalidStateException(string message) : base(message) { }
        public WebSocketInvalidStateException(string message, Exception inner) : base(message, inner) { }
    }

    public class WaitForBackgroundThread
    {
        public ConfiguredTaskAwaitable.ConfiguredTaskAwaiter GetAwaiter()
        {
            return Task.Run(() => { }).ConfigureAwait(false).GetAwaiter();
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR

    /// <summary>
    /// WebSocket class bound to JSLIB.
    /// </summary>
    public class WebSocket : IWebSocket
    {
        /* WebSocket JSLIB functions */
        [DllImport("__Internal")]
        public static extern int WebSocketConnect(int instanceId);

        [DllImport("__Internal")]
        public static extern int WebSocketClose(int instanceId, int code, string reason);

        [DllImport("__Internal")]
        public static extern int WebSocketSend(int instanceId, byte[] dataPtr, int dataLength);

        [DllImport("__Internal")]
        public static extern int WebSocketSendText(int instanceId, string message);

        [DllImport("__Internal")]
        public static extern int WebSocketGetState(int instanceId);

        protected int instanceId;

        public event WebSocketOpenEventHandler OnOpen;
        public event WebSocketMessageEventHandler OnMessage;
        public event WebSocketErrorEventHandler OnError;
        public event WebSocketCloseEventHandler OnClose;

        public WebSocket(string url, Dictionary<string, string> headers = null)
        {
            if (!WebSocketFactory.isInitialized)
            {
                WebSocketFactory.Initialize();
            }

            int id = WebSocketFactory.WebSocketAllocate(url);
            WebSocketFactory.instances.Add(id, this);

            this.instanceId = id;
        }

        public WebSocket(string url, string subprotocol, Dictionary<string, string> headers = null)
        {
            if (!WebSocketFactory.isInitialized)
            {
                WebSocketFactory.Initialize();
            }

            int id = WebSocketFactory.WebSocketAllocate(url);
            WebSocketFactory.instances.Add(id, this);

            WebSocketFactory.WebSocketAddSubProtocol(id, subprotocol);

            this.instanceId = id;
        }

        public WebSocket(string url, List<string> subprotocols, Dictionary<string, string> headers = null)
        {
            if (!WebSocketFactory.isInitialized)
            {
                WebSocketFactory.Initialize();
            }

            int id = WebSocketFactory.WebSocketAllocate(url);
            WebSocketFactory.instances.Add(id, this);

            foreach (string subprotocol in subprotocols)
            {
                WebSocketFactory.WebSocketAddSubProtocol(id, subprotocol);
            }

            this.instanceId = id;
        }

        ~WebSocket()
        {
            WebSocketFactory.HandleInstanceDestroy(this.instanceId);
        }

        public int GetInstanceId()
        {
            return this.instanceId;
        }

        public Task Connect()
        {
            int ret = WebSocketConnect(this.instanceId);

            if (ret < 0)
                throw WebSocketHelpers.GetErrorMessageFromCode(ret, null);

            return Task.CompletedTask;
        }

        public void CancelConnection()
        {
            if (State == WebSocketState.Open)
                Close(WebSocketCloseCode.Abnormal);
        }

        public Task Close(WebSocketCloseCode code = WebSocketCloseCode.Normal, string reason = null)
        {
            int ret = WebSocketClose(this.instanceId, (int)code, reason);

            if (ret < 0)
                throw WebSocketHelpers.GetErrorMessageFromCode(ret, null);

            return Task.CompletedTask;
        }

        public Task Send(byte[] data)
        {
            int ret = WebSocketSend(this.instanceId, data, data.Length);

            if (ret < 0)
                throw WebSocketHelpers.GetErrorMessageFromCode(ret, null);

            return Task.CompletedTask;
        }

        public Task SendText(string message)
        {
            int ret = WebSocketSendText(this.instanceId, message);

            if (ret < 0)
                throw WebSocketHelpers.GetErrorMessageFromCode(ret, null);

            return Task.CompletedTask;
        }

        public WebSocketState State
        {
            get
            {
                int state = WebSocketGetState(this.instanceId);

                if (state < 0)
                    throw WebSocketHelpers.GetErrorMessageFromCode(state, null);

                switch (state)
                {
                    case 0: return WebSocketState.Connecting;
                    case 1: return WebSocketState.Open;
                    case 2: return WebSocketState.Closing;
                    case 3: return WebSocketState.Closed;
                    default: return WebSocketState.Closed;
                }
            }
        }

        public void DelegateOnOpenEvent()
        {
            this.OnOpen?.Invoke();
        }

        public void DelegateOnMessageEvent(byte[] data)
        {
            this.OnMessage?.Invoke(data);
        }

        public void DelegateOnErrorEvent(string errorMsg)
        {
            this.OnError?.Invoke(errorMsg);
        }

        public void DelegateOnCloseEvent(int closeCode)
        {
            this.OnClose?.Invoke(WebSocketHelpers.ParseCloseCodeEnum(closeCode));
        }
    }

#else

    public class WebSocket : IWebSocket
    {
        public event WebSocketOpenEventHandler OnOpen;
        public event WebSocketMessageEventHandler OnMessage;
        public event WebSocketErrorEventHandler OnError;
        public event WebSocketCloseEventHandler OnClose;

        private readonly Uri uri;
        private readonly Dictionary<string, string> headers;
        private readonly List<string> subprotocols;

        private ClientWebSocket m_Socket = new ClientWebSocket();

        private CancellationTokenSource m_TokenSource;
        private CancellationToken m_CancellationToken;

        private readonly object IncomingMessageLock = new object();
        private readonly List<byte[]> m_MessageList = new List<byte[]>();

        private Task m_ReceiveTask;
        private int m_CleanupOnce = 0;

        private sealed class PendingSend
        {
            public WebSocketMessageType MessageType;
            public ArraySegment<byte> Buffer;
            public TaskCompletionSource<bool> Tcs;
        }

        private readonly ConcurrentQueue<PendingSend> m_SendQueue = new ConcurrentQueue<PendingSend>();
        private readonly SemaphoreSlim m_SendPumpGate = new SemaphoreSlim(1, 1);
        private int m_SendPumpRunning = 0;

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

        public WebSocket(string url, List<string> subprotocols, Dictionary<string, string> headers = null)
        {
            uri = new Uri(url);

            this.headers = headers ?? new Dictionary<string, string>();
            this.subprotocols = subprotocols ?? new List<string>();

            string protocol = uri.Scheme;
            if (!protocol.Equals("ws") && !protocol.Equals("wss"))
                throw new ArgumentException("Unsupported protocol: " + protocol);
        }

        public void CancelConnection()
        {
            try
            {
                m_TokenSource?.Cancel();
            }
            catch
            {
            }
        }

        private void CleanupNoThrow()
        {
            if (Interlocked.Exchange(ref m_CleanupOnce, 1) != 0)
                return;

            try
            {
                m_TokenSource?.Cancel();
            }
            catch
            {
            }

            try
            {
                m_Socket?.Dispose();
            }
            catch
            {
            }

            try
            {
                m_Socket = new ClientWebSocket();
            }
            catch
            {
            }
        }

        private void FailAllPendingSends(Exception ex)
        {
            while (m_SendQueue.TryDequeue(out var ps))
            {
                try
                {
                    ps.Tcs.TrySetException(ex);
                }
                catch
                {
                }
            }
        }

        public async Task Connect()
        {
            try
            {
                m_CleanupOnce = 0;

                m_TokenSource = new CancellationTokenSource();
                m_CancellationToken = m_TokenSource.Token;

                try
                {
                    m_Socket?.Dispose();
                }
                catch
                {
                }

                m_Socket = new ClientWebSocket();

                foreach (var header in headers)
                {
                    m_Socket.Options.SetRequestHeader(header.Key, header.Value);
                }

                foreach (string subprotocol in subprotocols)
                {
                    m_Socket.Options.AddSubProtocol(subprotocol);
                }

                await m_Socket.ConnectAsync(uri, m_CancellationToken);
                OnOpen?.Invoke();

                m_ReceiveTask = Receive();
                await m_ReceiveTask;
            }
            catch (Exception ex)
            {
                try
                {
                    OnError?.Invoke(ex.ToString());
                }
                catch
                {
                }

                try
                {
                    OnClose?.Invoke(WebSocketCloseCode.Abnormal);
                }
                catch
                {
                }

                CleanupNoThrow();
                throw;
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
            if (bytes == null || bytes.Length == 0)
                return Task.CompletedTask;

            return EnqueueSend(WebSocketMessageType.Binary, new ArraySegment<byte>(bytes));
        }

        public Task SendText(string message)
        {
            if (string.IsNullOrEmpty(message))
                return Task.CompletedTask;

            var encoded = Encoding.UTF8.GetBytes(message);
            return EnqueueSend(WebSocketMessageType.Text, new ArraySegment<byte>(encoded, 0, encoded.Length));
        }

        private Task EnqueueSend(WebSocketMessageType messageType, ArraySegment<byte> buffer)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            m_SendQueue.Enqueue(new PendingSend
            {
                MessageType = messageType,
                Buffer = buffer,
                Tcs = tcs
            });

            EnsureSendPump();
            return tcs.Task;
        }

        private void EnsureSendPump()
        {
            if (Interlocked.CompareExchange(ref m_SendPumpRunning, 1, 0) != 0)
                return;

            _ = PumpSendQueueAsync();
        }

        private async Task PumpSendQueueAsync()
        {
            try
            {
                await m_SendPumpGate.WaitAsync(m_CancellationToken);

                try
                {
                    while (true)
                    {
                        if (!m_SendQueue.TryDequeue(out var ps))
                            break;

                        try
                        {
                            if (m_Socket.State != System.Net.WebSockets.WebSocketState.Open)
                                throw new WebSocketInvalidStateException("WebSocket is not in open state.");

                            await m_Socket.SendAsync(ps.Buffer, ps.MessageType, true, m_CancellationToken);
                            ps.Tcs.TrySetResult(true);
                        }
                        catch (Exception ex)
                        {
                            ps.Tcs.TrySetException(ex);

                            if (m_Socket.State != System.Net.WebSockets.WebSocketState.Open)
                            {
                                FailAllPendingSends(ex);
                                break;
                            }
                        }
                    }
                }
                finally
                {
                    m_SendPumpGate.Release();
                }
            }
            catch (OperationCanceledException oce)
            {
                FailAllPendingSends(oce);
            }
            catch (Exception ex)
            {
                FailAllPendingSends(ex);

                try
                {
                    OnError?.Invoke(ex.ToString());
                }
                catch
                {
                }
            }
            finally
            {
                Interlocked.Exchange(ref m_SendPumpRunning, 0);

                if (!m_SendQueue.IsEmpty)
                    EnsureSendPump();
            }
        }

        public void DispatchMessageQueue()
        {
            List<byte[]> messageListCopy;

            lock (IncomingMessageLock)
            {
                if (m_MessageList.Count == 0)
                    return;

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

                        if (result.MessageType == WebSocketMessageType.Text ||
                            result.MessageType == WebSocketMessageType.Binary)
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
            catch (Exception ex)
            {
                try
                {
                    OnError?.Invoke(ex.ToString());
                }
                catch
                {
                }

                try
                {
                    m_TokenSource?.Cancel();
                }
                catch
                {
                }
            }
            finally
            {
                try
                {
                    await new WaitForUpdate();
                }
                catch
                {
                }

                try
                {
                    OnClose?.Invoke(closeCode);
                }
                catch
                {
                }

                CleanupNoThrow();
                FailAllPendingSends(new WebSocketInvalidStateException("WebSocket closed."));
            }
        }

        public async Task Close()
        {
            if (State == WebSocketState.Open)
            {
                await m_Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, m_CancellationToken);
            }
        }

        public Task WaitForClose()
        {
            return m_ReceiveTask ?? Task.CompletedTask;
        }
    }

#endif

    ///
    /// Factory
    ///

    /// <summary>
    /// Class providing static access methods to work with JSLIB WebSocket or WebSocketSharp interface
    /// </summary>
    public static class WebSocketFactory
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        /* Map of websocket instances */
        public static Dictionary<Int32, WebSocket> instances = new Dictionary<Int32, WebSocket>();

        /* Delegates */
        public delegate void OnOpenCallback(int instanceId);
        public delegate void OnMessageCallback(int instanceId, System.IntPtr msgPtr, int msgSize);
        public delegate void OnErrorCallback(int instanceId, System.IntPtr errorPtr);
        public delegate void OnCloseCallback(int instanceId, int closeCode);

        /* WebSocket JSLIB callback setters and other functions */
        [DllImport("__Internal")]
        public static extern int WebSocketAllocate(string url);

        [DllImport("__Internal")]
        public static extern int WebSocketAddSubProtocol(int instanceId, string subprotocol);

        [DllImport("__Internal")]
        public static extern void WebSocketFree(int instanceId);

        [DllImport("__Internal")]
        public static extern void WebSocketSetOnOpen(OnOpenCallback callback);

        [DllImport("__Internal")]
        public static extern void WebSocketSetOnMessage(OnMessageCallback callback);

        [DllImport("__Internal")]
        public static extern void WebSocketSetOnError(OnErrorCallback callback);

        [DllImport("__Internal")]
        public static extern void WebSocketSetOnClose(OnCloseCallback callback);

        /* If callbacks was initialized and set */
        public static bool isInitialized = false;

        /*
         * Initialize WebSocket callbacks to JSLIB
         */
        public static void Initialize()
        {
            WebSocketSetOnOpen(DelegateOnOpenEvent);
            WebSocketSetOnMessage(DelegateOnMessageEvent);
            WebSocketSetOnError(DelegateOnErrorEvent);
            WebSocketSetOnClose(DelegateOnCloseEvent);

            isInitialized = true;
        }

        /// <summary>
        /// Called when instance is destroyed (by destructor)
        /// Method removes instance from map and free it in JSLIB implementation
        /// </summary>
        /// <param name="instanceId">Instance identifier.</param>
        public static void HandleInstanceDestroy(int instanceId)
        {
            instances.Remove(instanceId);
            WebSocketFree(instanceId);
        }

        [MonoPInvokeCallback(typeof(OnOpenCallback))]
        public static void DelegateOnOpenEvent(int instanceId)
        {
            WebSocket instanceRef;

            if (instances.TryGetValue(instanceId, out instanceRef))
            {
                instanceRef.DelegateOnOpenEvent();
            }
        }

        [MonoPInvokeCallback(typeof(OnMessageCallback))]
        public static void DelegateOnMessageEvent(int instanceId, System.IntPtr msgPtr, int msgSize)
        {
            WebSocket instanceRef;

            if (instances.TryGetValue(instanceId, out instanceRef))
            {
                byte[] msg = new byte[msgSize];
                Marshal.Copy(msgPtr, msg, 0, msgSize);

                instanceRef.DelegateOnMessageEvent(msg);
            }
        }

        [MonoPInvokeCallback(typeof(OnErrorCallback))]
        public static void DelegateOnErrorEvent(int instanceId, System.IntPtr errorPtr)
        {
            WebSocket instanceRef;

            if (instances.TryGetValue(instanceId, out instanceRef))
            {
                string errorMsg = Marshal.PtrToStringAuto(errorPtr);
                instanceRef.DelegateOnErrorEvent(errorMsg);
            }
        }

        [MonoPInvokeCallback(typeof(OnCloseCallback))]
        public static void DelegateOnCloseEvent(int instanceId, int closeCode)
        {
            WebSocket instanceRef;

            if (instances.TryGetValue(instanceId, out instanceRef))
            {
                instanceRef.DelegateOnCloseEvent(closeCode);
            }
        }
#endif

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
