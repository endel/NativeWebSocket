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
      websocket = WebSocketFactory.CreateInstance("ws://localhost:2567");

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

      await websocket.Connect();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
