using System;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using NativeWebSocket;
using NativeWebSocket.MonoGame;

namespace MonoGameExample
{
    public class Game1 : Game
    {
        private GraphicsDeviceManager _graphics;
        private WebSocket _websocket;
        private float _sendTimer;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            IsMouseVisible = true;
        }

        protected override void Initialize()
        {
            // Install the WebSocket game component.
            // This sets up a SynchronizationContext so all WebSocket events
            // fire on the game thread automatically.
            Components.Add(new WebSocketGameComponent(this));
            base.Initialize();
        }

        protected override async void LoadContent()
        {
            _websocket = new WebSocket("ws://localhost:3000");

            _websocket.OnOpen += () =>
            {
                Console.WriteLine("Connection open!");
            };

            _websocket.OnError += (e) =>
            {
                Console.WriteLine("Error! " + e);
            };

            _websocket.OnClose += (code) =>
            {
                Console.WriteLine("Connection closed! Code: " + code);
            };

            _websocket.OnMessage += (bytes) =>
            {
                var message = Encoding.UTF8.GetString(bytes);
                Console.WriteLine("Received OnMessage! (" + bytes.Length + " bytes) " + message);
            };

            await _websocket.Connect();
        }

        protected override async void Update(GameTime gameTime)
        {
            if (Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            // Send a message every 0.3 seconds
            if (_websocket?.State == WebSocketState.Open)
            {
                _sendTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;
                if (_sendTimer >= 0.3f)
                {
                    _sendTimer = 0;

                    // Send binary data
                    await _websocket.Send(new byte[] { 10, 20, 30 });

                    // Send text data
                    await _websocket.SendText("hello from MonoGame!");
                }
            }

            base.Update(gameTime);
        }

        protected override async void OnExiting(object sender, EventArgs args)
        {
            if (_websocket != null)
            {
                await _websocket.Close();
            }
            base.OnExiting(sender, args);
        }
    }
}
