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
      websocket = new WebSocket("wss://echo.websocket.org");

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
        // Reading a plain text message
        var message = System.Text.Encoding.UTF8.GetString(bytes);
        // Debug.Log("OnMessage! " + message);

        // Sending bytes
        websocket.Send(new byte[] { 10, 20, 30 });

        // Sending plain text
         websocket.SendText("plain text message");
      };

    await websocket.Connect();
    }

    // Update is called once per frame
    void Update()
    {
    }

    private async void OnApplicationQuit()
    {
      await websocket.Close();
    }
}
