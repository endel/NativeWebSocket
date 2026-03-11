<img src="Media/header.png?raw=true" alt="Native WebSocket" />

A simple, dependency-free WebSocket client library for **Unity**, **MonoGame**, **Godot**, and any **.NET** project.

- No external DLL's required (uses built-in `System.Net.WebSockets`)
- WebGL/HTML5 support (Unity)
- Supports all major build targets
- Automatic main-thread event dispatching via `SynchronizationContext`
- Very simple API

Used in [Colyseus Unity SDK](https://github.com/colyseus/colyseus-unity-sdk).

# Installation

## Unity

*Requires Unity 2019.1+ with .NET 4.x+ Runtime*

> **Note:** Do not copy the raw source files from this repository directly into
> your Unity project. The core `WebSocket.cs` requires a build-time
> transformation to add WebGL conditional compilation guards. Use one of the
> install methods below instead.

**Via UPM (Unity Package Manager):**
1. Open Unity
2. Open Package Manager Window
3. Click Add Package From Git URL
4. Enter URL: `https://github.com/endel/NativeWebSocket.git#upm-2.0`

**Via .unitypackage:**
1. Download `NativeWebSocket.unitypackage` from the [Releases](https://github.com/endel/NativeWebSocket/releases) page
2. In Unity, go to Assets > Import Package > Custom Package and select the downloaded file

## MonoGame / .NET

Add a project reference to `src/NativeWebSocket/NativeWebSocket.csproj` and
`integrations/MonoGame/NativeWebSocket.MonoGame.csproj`:

```bash
dotnet add reference path/to/NativeWebSocket.csproj
dotnet add reference path/to/NativeWebSocket.MonoGame.csproj
```

## Godot (C#)

Add a project reference to `src/NativeWebSocket/NativeWebSocket.csproj` in your `.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/NativeWebSocket.csproj" />
</ItemGroup>
```

# Usage

## Unity

```csharp
using UnityEngine;
using NativeWebSocket;

public class Connection : MonoBehaviour
{
    WebSocket websocket;

    async void Start()
    {
        Application.runInBackground = true; // Recommended for WebGL

        websocket = new WebSocket("ws://localhost:3000");

        websocket.OnOpen += () => Debug.Log("Connection open!");
        websocket.OnError += (e) => Debug.Log("Error! " + e);
        websocket.OnClose += (code) => Debug.Log("Connection closed!");

        websocket.OnMessage += (bytes) =>
        {
            var message = System.Text.Encoding.UTF8.GetString(bytes);
            Debug.Log("Received: " + message);
        };

        InvokeRepeating("SendWebSocketMessage", 0.0f, 0.3f);

        await websocket.Connect();
    }

    async void SendWebSocketMessage()
    {
        if (websocket.State == WebSocketState.Open)
        {
            await websocket.Send(new byte[] { 10, 20, 30 });
            await websocket.SendText("plain text message");
        }
    }

    private async void OnApplicationQuit()
    {
        await websocket.Close();
    }
}
```

**WebGL note:** Unity pauses the game loop when the browser tab loses focus, which
stops all WebSocket send/receive callbacks. To keep the connection active in the
background, set `Application.runInBackground = true` in your script or enable
**Run In Background** in Player Settings > Resolution and Presentation.

## MonoGame

Add the `WebSocketGameComponent` to your game. This installs a
`SynchronizationContext` so all WebSocket events fire on the game thread
automatically.

```csharp
using System;
using System.Text;
using Microsoft.Xna.Framework;
using NativeWebSocket;
using NativeWebSocket.MonoGame;

public class Game1 : Game
{
    private WebSocket _websocket;

    protected override void Initialize()
    {
        Components.Add(new WebSocketGameComponent(this));
        base.Initialize();
    }

    protected override async void LoadContent()
    {
        _websocket = new WebSocket("ws://localhost:3000");

        _websocket.OnOpen += () => Console.WriteLine("Connected!");
        _websocket.OnError += (e) => Console.WriteLine("Error! " + e);
        _websocket.OnClose += (code) => Console.WriteLine("Closed: " + code);

        _websocket.OnMessage += (bytes) =>
        {
            var message = Encoding.UTF8.GetString(bytes);
            Console.WriteLine("Received: (" + bytes.Length + " bytes) " + message);
        };

        await _websocket.Connect();
    }

    protected override async void OnExiting(object sender, EventArgs args)
    {
        if (_websocket != null)
            await _websocket.Close();
        base.OnExiting(sender, args);
    }
}
```

## Godot

Godot Mono has a built-in `GodotSynchronizationContext`, so no special
integration is needed. All WebSocket events fire on the main thread
automatically.

```csharp
using System.Text;
using Godot;
using NativeWebSocket;

public partial class WebSocketExample : Node
{
    private WebSocket _websocket;

    public override async void _Ready()
    {
        _websocket = new WebSocket("ws://localhost:3000");

        _websocket.OnOpen += () => GD.Print("Connected!");
        _websocket.OnError += (e) => GD.Print("Error! " + e);
        _websocket.OnClose += (code) => GD.Print("Closed: " + code);

        _websocket.OnMessage += (bytes) =>
        {
            var message = Encoding.UTF8.GetString(bytes);
            GD.Print("Received: (" + bytes.Length + " bytes) " + message);
        };

        await _websocket.Connect();
    }

    public override void _ExitTree()
    {
        _websocket?.Close();
    }
}
```

## Generic .NET (no SynchronizationContext)

If your environment doesn't have a `SynchronizationContext` (e.g. a console
app), call `DispatchMessageQueue()` from your main loop to process events:

```csharp
var ws = new WebSocket("ws://localhost:3000");
ws.OnMessage += (bytes) => Console.WriteLine("Received " + bytes.Length + " bytes");
_ = ws.Connect();

while (true)
{
    ws.DispatchMessageQueue();
    Thread.Sleep(16);
}
```

# Examples

Full runnable examples are in the [`examples/`](examples/) directory:

| Engine | Path | How to run |
|--------|------|------------|
| **MonoGame** | [`examples/MonoGame/`](examples/MonoGame/) | `dotnet run --project examples/MonoGame/MonoGameExample.csproj` |
| **Godot** | [`examples/Godot/`](examples/Godot/) | Open in Godot Editor (4.x+ with C#), build, and press Play |
| **Unity** | [`examples/Unity/`](examples/Unity/) | Import NativeWebSocket via UPM, add `Connection.cs` to a GameObject |

All examples connect to the included test server:

```bash
cd NodeServer
npm install
node index.js
```

The server listens on `ws://localhost:3000`, sends periodic text and binary
messages, and logs anything received from the client.

# API

## Constructor

```csharp
new WebSocket(string url)
new WebSocket(string url, Dictionary<string, string> headers)
new WebSocket(string url, string subprotocol)
new WebSocket(string url, List<string> subprotocols)
```

## Events

| Event | Signature | Description |
|-------|-----------|-------------|
| `OnOpen` | `()` | Connection established |
| `OnMessage` | `(byte[] data)` | Message received (text or binary) |
| `OnError` | `(string errorMsg)` | Error occurred |
| `OnClose` | `(WebSocketCloseCode code)` | Connection closed |

## Methods

| Method | Description |
|--------|-------------|
| `Connect()` | Connect to the server (async) |
| `Close(code, reason)` | Gracefully close the connection (async) |
| `Send(byte[])` | Send binary data (async) |
| `SendText(string)` | Send text data (async) |
| `CancelConnection()` | Cancel a pending connection attempt |
| `DispatchMessageQueue()` | Manually dispatch queued events (only needed without a `SynchronizationContext`) |

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `State` | `WebSocketState` | `Connecting`, `Open`, `Closing`, or `Closed` |

---

## Acknowledgements

Big thanks to [Jiri Hybek](https://github.com/jirihybek/unity-websocket-webgl).
This implementation is based on his work.

## License

Apache 2.0
