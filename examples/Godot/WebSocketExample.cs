using System.Text;
using Godot;
using NativeWebSocket;

/// <summary>
/// Godot has a built-in GodotSynchronizationContext, so all WebSocket
/// events (OnOpen, OnMessage, OnError, OnClose) automatically fire on the main thread.
/// No special integration or DispatchMessageQueue() call is needed.
/// </summary>
public partial class WebSocketExample : Node
{
    private WebSocket _websocket;
    private double _sendTimer;

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

        _websocket.OnClose += (code) =>
        {
            GD.Print("Connection closed! Code: " + code);
        };

        _websocket.OnMessage += (bytes) =>
        {
            var message = Encoding.UTF8.GetString(bytes);
            GD.Print("Received OnMessage! (" + bytes.Length + " bytes) " + message);
        };

        await _websocket.Connect();
    }

    public override async void _Process(double delta)
    {
        if (_websocket?.State == WebSocketState.Open)
        {
            _sendTimer += delta;
            if (_sendTimer >= 0.3)
            {
                _sendTimer = 0;

                // Send binary data
                await _websocket.Send(new byte[] { 10, 20, 30 });

                // Send text data
                await _websocket.SendText("hello from Godot!");
            }
        }
    }

    public override void _ExitTree()
    {
        _websocket?.Close();
    }
}
