# UnityWebSockets

This client uses `System.Net.WebSockets`, with support for WebGL/HTML5 builds.
Most of the implementation is highly based on [Jiri Hybek](https://github.com/jirihybek/unity-websocket-webgl)'s work.

## Usage

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityWebSockets;

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
      };

      // waiting for messages
      await websocket.Connect();
    }
}
```

## License

Apache 2.0
