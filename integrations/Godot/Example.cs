using Godot;
using NativeWebSocket;

/// <summary>
/// Godot example - no special integration needed.
/// Godot Mono has a built-in GodotSynchronizationContext, so all WebSocket
/// events (OnOpen, OnMessage, OnError, OnClose) automatically fire on the main thread.
/// </summary>
public partial class WebSocketExample : Node
{
    private WebSocket _websocket;

    public override async void _Ready()
    {
        _websocket = new WebSocket("ws://localhost:3000");

        _websocket.OnOpen += () =>
        {
            GD.Print("Connection open!");
        };

        _websocket.OnError += (e) =>
        {
            GD.Print("Error! " + e);
        };

        _websocket.OnClose += (e) =>
        {
            GD.Print("Connection closed!");
        };

        _websocket.OnMessage += (bytes) =>
        {
            var message = System.Text.Encoding.UTF8.GetString(bytes);
            GD.Print("Received OnMessage! (" + bytes.Length + " bytes) " + message);
        };

        await _websocket.Connect();
    }

    public override void _ExitTree()
    {
        _websocket?.Close();
    }
}
