using System.Text;
using UnityEngine;
using NativeWebSocket;

/// <summary>
/// Unity example — attach this script to a GameObject in your scene.
///
/// Setup:
///   1. Import NativeWebSocket via UPM:
///      Window > Package Manager > "+" > Add package from git URL:
///      https://github.com/endel/NativeWebSocket.git#upm
///
///   2. Create an empty GameObject and attach this script.
///
///   3. Start the test server: cd NodeServer && npm install && node index.js
///
///   4. Press Play.
///
/// Unity's UnitySynchronizationContext handles event dispatching automatically,
/// so no DispatchMessageQueue() call is needed in Update().
/// </summary>
public class Connection : MonoBehaviour
{
    WebSocket websocket;

    async void Start()
    {
        websocket = new WebSocket("ws://localhost:3000");

        websocket.OnOpen += () =>
        {
            Debug.Log("Connection open!");
        };

        websocket.OnError += (e) =>
        {
            Debug.Log("Error! " + e);
        };

        websocket.OnClose += (code) =>
        {
            Debug.Log("Connection closed! Code: " + code);
        };

        websocket.OnMessage += (bytes) =>
        {
            var message = Encoding.UTF8.GetString(bytes);
            Debug.Log("Received OnMessage! (" + bytes.Length + " bytes) " + message);
        };

        // Send messages every 0.3 seconds
        InvokeRepeating("SendWebSocketMessage", 0.0f, 0.3f);

        await websocket.Connect();
    }

    async void SendWebSocketMessage()
    {
        if (websocket.State == WebSocketState.Open)
        {
            // Send binary data
            await websocket.Send(new byte[] { 10, 20, 30 });

            // Send text data
            await websocket.SendText("hello from Unity!");
        }
    }

    private async void OnApplicationQuit()
    {
        await websocket.Close();
    }
}
