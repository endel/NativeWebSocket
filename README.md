<img src="Media/big.png?raw=true" alt="Native WebSocket" />

This is the simplest and easiest WebSocket library for Unity you can find!

- No external DLL's required
- WebGL/HTML5 support
- Supports all major build targets

Requires Unity 2019+ with .NET 4.x+ Runtime

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

## Acknowledgements

Big thanks to [Jiri Hybek](https://github.com/jirihybek/unity-websocket-webgl).
This implementation is based on his work.

## License

Apache 2.0
