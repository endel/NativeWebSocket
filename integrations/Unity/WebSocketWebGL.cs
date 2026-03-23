#if UNITY_WEBGL && !UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace NativeWebSocket
{
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

            int instanceId = WebSocketFactory.WebSocketAllocate(url);
            WebSocketFactory.instances.Add(instanceId, this);

            this.instanceId = instanceId;
        }

        public WebSocket(string url, string subprotocol, Dictionary<string, string> headers = null)
        {
            if (!WebSocketFactory.isInitialized)
            {
                WebSocketFactory.Initialize();
            }

            int instanceId = WebSocketFactory.WebSocketAllocate(url);
            WebSocketFactory.instances.Add(instanceId, this);

            WebSocketFactory.WebSocketAddSubProtocol(instanceId, subprotocol);

            this.instanceId = instanceId;
        }

        public WebSocket(string url, List<string> subprotocols, Dictionary<string, string> headers = null)
        {
            if (!WebSocketFactory.isInitialized)
            {
                WebSocketFactory.Initialize();
            }

            int instanceId = WebSocketFactory.WebSocketAllocate(url);
            WebSocketFactory.instances.Add(instanceId, this);

            foreach (string subprotocol in subprotocols)
            {
                WebSocketFactory.WebSocketAddSubProtocol(instanceId, subprotocol);
            }

            this.instanceId = instanceId;
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
                    case 0:
                        return WebSocketState.Connecting;

                    case 1:
                        return WebSocketState.Open;

                    case 2:
                        return WebSocketState.Closing;

                    case 3:
                        return WebSocketState.Closed;

                    default:
                        return WebSocketState.Closed;
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
}

#endif
