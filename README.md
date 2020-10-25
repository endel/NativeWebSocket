<img src="Media/header.png?raw=true" alt="Native WebSocket" />

This is the simplest and easiest WebSocket library for Unity you can find!

- No external DLL's required (uses built-in `System.Net.WebSockets`)
- WebGL/HTML5 support
- Supports all major build targets
- Very simple API

Requires Unity 2019+ with .NET 4.x+ Runtime

> This WebSocket client is used on [colyseus-unity3d](https://github.com/colyseus/colyseus-unity3d). <br />
> Consider supporting my work on [Patreon](https://patreon.com/endel). <br />
> [![Support me on Patreon](https://img.shields.io/endpoint.svg?url=https%3A%2F%2Fshieldsio-patreon.vercel.app%2Fapi%3Fusername%3Dendel%26type%3Dpatrons&style=for-the-badge)](https://patreon.com/endel)

## Installation

1. [Download this project](https://github.com/endel/NativeWebSocket/archive/master.zip)
2. Copy the sources from `NativeWebSocket/Assets/WebSocket` into your `Assets` directory.

## Usage

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using NativeWebSocket;

public class Connection : MonoBehaviour
{
  WebSocket websocket;

  // Start is called before the first frame update
  async void Start()
  {
    websocket = new WebSocket("ws://localhost:2567");

    websocket.OnOpen += () =>
    {
      Debug.Log("Connection open!");
    };

    websocket.OnError += (e) =>
    {
      Debug.Log("Error! " + e);
    };

    websocket.OnClose += (e) =>
    {
      Debug.Log("Connection closed!");
    };

    websocket.OnMessage += (bytes) =>
    {
      Debug.Log("OnMessage!");
      Debug.Log(bytes);

      // getting the message as a string
      // var message = System.Text.Encoding.UTF8.GetString(bytes);
      // Debug.Log("OnMessage! " + message);
    };

    // Keep sending messages at every 0.3s
    InvokeRepeating("SendWebSocketMessage", 0.0f, 0.3f);

    // waiting for messages
    await websocket.Connect();
  }

  void Update()
  {
    #if !UNITY_WEBGL || UNITY_EDITOR
      websocket.DispatchMessageQueue();
    #endif
  }

  async void SendWebSocketMessage()
  {
    if (websocket.State == WebSocketState.Open)
    {
      // Sending bytes
      await websocket.Send(new byte[] { 10, 20, 30 });

      // Sending plain text
      await websocket.SendText("plain text message");
    }
  }

  private async void OnApplicationQuit()
  {
    await websocket.Close();
  }

}
```

# Demonstration

**1.** Start the local WebSocket server:

```
cd Server
npm install
npm start
```

**2.** Open the `NativeWebSocket/Assets/WebSocketExample/WebSocketExampleScene.unity` on Unity and Run.


## Acknowledgements

Big thanks to [Jiri Hybek](https://github.com/jirihybek/unity-websocket-webgl).
This implementation is based on his work.

## License

Apache 2.0
