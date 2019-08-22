Native WebSocket Client!
------------------------

This is the simplest and easiest WebSocket library for Unity you can find!
No external DLL's required, supports all major build targets, including WebGL / HTML5.

For the usage example, check out the "WebSocketExample" folder. It contains a
"Connection.cs" script file, demonstrating the entire API of the Native
WebSocket Client. You can attach it as a script component to any object in your
scene for testing.

This package basically provides a "NativeWebSocket.WebSocket" class, which has
these public methods:

- new WebSocket(string url)
The WebSocket constructor. Use the URL of your WebSocket server as argument.

- websocket.Send(byte[] bytes)
Sends a binary message to the server.

- websocket.SendText(string message)
Sends a text message to the server.

- websocket.OnMessage += (byte[] bytes) => {}
An event that triggers whenever a message comes from the server. To parse string
messages, use "System.Text.Encoding.UTF8.GetString(bytes)"

- websocket.OnOpen += () => {}
An event that triggers whenever a connection has been sucessfully established.

- websocket.OnClose += (int code) => {}
An event that triggers whenever an error occurs with the connection.

- websocket.OnError += (string message) => {}
An event that triggers whenever an error occurs with the connection.

- websocket.Connect()
Connects into the WebSocket server.

- websocket.Close()
Force to close the WebSocket connection with the server. Make sure to call
websocket.Close() when quitting your application (OnApplicationQuit).

- websocket.State
Contains the current state of the WebSocket connection. Can be one of:
WebSocketState.Connecting, WebSocketState.Open, WebSocketState.Closing,
WebSocketState.Closed.

