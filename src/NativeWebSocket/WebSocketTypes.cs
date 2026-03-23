using System;

namespace NativeWebSocket
{
    public static class WebSocketHelpers
    {
        public static WebSocketCloseCode ParseCloseCodeEnum(int closeCode)
        {
            if (WebSocketCloseCode.IsDefined(typeof(WebSocketCloseCode), closeCode))
            {
                return (WebSocketCloseCode)closeCode;
            }
            else
            {
                return WebSocketCloseCode.Undefined;
            }
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
}
