using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Xna.Framework;

namespace NativeWebSocket.MonoGame
{
    /// <summary>
    /// A SynchronizationContext that queues callbacks to be executed on the game thread.
    /// </summary>
    public class SingleThreadSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        public override void Post(SendOrPostCallback d, object state)
        {
            _queue.Enqueue(() => d(state));
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            // Queue for execution on the game thread (same as Post for single-threaded pump)
            _queue.Enqueue(() => d(state));
        }

        public void ProcessQueue()
        {
            while (_queue.TryDequeue(out var action))
            {
                action();
            }
        }
    }

    /// <summary>
    /// GameComponent that installs a SynchronizationContext on the game thread
    /// and pumps queued callbacks each frame.
    ///
    /// Usage:
    ///   Components.Add(new WebSocketGameComponent(this));
    ///
    /// After this, all NativeWebSocket events (OnOpen, OnMessage, OnError, OnClose)
    /// will automatically fire on the game thread.
    /// </summary>
    public class WebSocketGameComponent : GameComponent
    {
        private readonly SingleThreadSynchronizationContext _syncContext;

        public WebSocketGameComponent(Game game) : base(game)
        {
            _syncContext = new SingleThreadSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(_syncContext);
        }

        public override void Update(GameTime gameTime)
        {
            _syncContext.ProcessQueue();
            base.Update(gameTime);
        }
    }
}
