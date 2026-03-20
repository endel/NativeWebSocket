import { WebSocket, WebSocketServer } from 'ws';

const port = parseInt(process.env.PORT || '3000', 10);
const wss = new WebSocketServer({ port });

const MAX_BUFFERED_BYTES = 16 * 1024 * 1024;

function startFlood(ws, count, size) {
  const payload = Buffer.alloc(size);
  let sent = 0;

  function sendBatch() {
    if (ws.readyState !== WebSocket.OPEN) {
      return;
    }

    for (let i = 0; i < 1000 && sent < count; i++, sent++) {
      if (ws.bufferedAmount > MAX_BUFFERED_BYTES) {
        setTimeout(sendBatch, 1);
        return;
      }

      if (size >= 4) {
        payload.writeInt32LE(sent, 0);
      }

      ws.send(payload);

      if (ws.readyState !== WebSocket.OPEN) {
        return;
      }
    }

    if (sent < count) {
      setImmediate(sendBatch);
    }
  }

  sendBatch();
}

function startContinuousFlood(ws, size) {
  const payload = Buffer.alloc(size);
  let sent = 0;
  let active = true;

  function sendBatch() {
    if (!active || ws.readyState !== WebSocket.OPEN) {
      return;
    }

    for (let i = 0; i < 1000; i++, sent++) {
      if (ws.bufferedAmount > MAX_BUFFERED_BYTES) {
        setTimeout(sendBatch, 1);
        return;
      }

      if (size >= 4) {
        payload.writeInt32LE(sent, 0);
      }

      ws.send(payload);

      if (!active || ws.readyState !== WebSocket.OPEN) {
        return;
      }
    }

    setImmediate(sendBatch);
  }

  sendBatch();

  return () => {
    active = false;
  };
}

wss.on('connection', (ws) => {
  console.log('Client connected');

  let sinkSessionId = null;
  let stopContinuousFlood = null;

  ws.on('message', (data, isBinary) => {
    if (isBinary) {
      if (sinkSessionId !== null) {
        return;
      }

      // Echo binary messages back immediately
      ws.send(data);
      return;
    }

    const msg = data.toString();

    if (msg.startsWith('flood:')) {
      // flood:<count>:<sizeBytes> — send count messages as fast as possible
      const parts = msg.split(':');
      const count = parseInt(parts[1], 10);
      const size = parseInt(parts[2], 10);
      startFlood(ws, count, size);

    } else if (msg.startsWith('flood_continuous:')) {
      const size = parseInt(msg.slice('flood_continuous:'.length), 10);
      stopContinuousFlood?.();
      stopContinuousFlood = startContinuousFlood(ws, size);

    } else if (msg === 'flood_stop') {
      stopContinuousFlood?.();
      stopContinuousFlood = null;
      ws.send('flood_stopped');

    } else if (msg.startsWith('sink:start:')) {
      sinkSessionId = msg.slice('sink:start:'.length);
      ws.send(`sink_ready:${sinkSessionId}`);

    } else if (msg.startsWith('burst:')) {
      // burst:<count>:<sizeBytes> — send count messages + "burst_done" signal
      const parts = msg.split(':');
      const count = parseInt(parts[1], 10);
      const size = parseInt(parts[2], 10);
      const payload = Buffer.alloc(size);

      for (let i = 0; i < count; i++) {
        payload.writeInt32LE(i, 0);
        ws.send(payload);
      }
      // Signal burst complete
      ws.send('burst_done');

    } else {
      // Echo text messages back
      ws.send(msg);
    }
  });

  ws.on('close', () => {
    stopContinuousFlood?.();

    console.log('Client disconnected');
  });
  ws.on('error', (err) => console.error('Client error:', err.message));
});

console.log(`Benchmark server running on ws://localhost:${port}`);
