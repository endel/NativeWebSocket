# Changelog

All notable changes to this project are documented in this file.

This changelog starts at 2.0.5 — see the [GitHub Releases](https://github.com/endel/NativeWebSocket/releases) page for earlier versions.

## 2.0.8

### Fixed

- **WebGL: `CancelConnection()` no longer throws on an open socket.** Browsers only accept 1000 or 3000-4999 in `close()`, so closing with 1006 threw. It now closes with 1000 and still reports `Abnormal` (1006) to `OnClose`, like the other platforms.

## 2.0.7 - 2026-08-06

### Fixed

- **Queued events and messages are now delivered in arrival order.** `DispatchMessageQueue()` drained the message queue first and a separate event queue afterwards, so an event queued *before* a batch of messages was delivered *after* them. `OnOpen` in particular arrived after the first messages had already been handed to the consumer, so anything gating on it — such as Colyseus' `Connection.IsOpen` — still looked closed while messages were being processed, and calls guarded by it silently did nothing.

  Events now record their position as a `null` placeholder in the message queue, with the actions in a side queue; both are appended under the same lock, so a `null` entry always has an event waiting for it.

  This only affected consumers without a `SynchronizationContext` — MonoGame, console apps and test hosts. With one present (as in Unity) events are posted directly and were already ordered correctly. Regression introduced in 2.0.1.

> 2.0.6 was never published — its release workflow was stranded by a GitHub Actions outage and could not be re-queued, so this content ships as 2.0.7.

## 2.0.5 - 2026-08-06

### Fixed

- **Close codes outside the enum are no longer reported as `1004`.** `WebSocketHelpers.ParseCloseCodeEnum()` collapsed every code without a matching `WebSocketCloseCode` member into `WebSocketCloseCode.Undefined` (1004), so application close codes (RFC 6455 reserves 3000-4999 for applications) were indistinguishable from one another. Any valid close code (1000-4999) is now preserved as-is.

- **The closing handshake now echoes the code the server sent.** When a close frame arrived, the receive loop called `Close()` — which short-circuits on its `State == Open` check, because a socket that just received a close frame is in `CloseReceived`. The result was that no closing handshake response was ever sent, and had it been, it would have carried `Normal` (1000) rather than the received code. Servers read their own close code from that response, so a deliberate close looked to them like a dropped connection (1006). The response is now sent via `CloseOutputAsync` with the received code.

  Together these fixes resolve [colyseus/colyseus#948](https://github.com/colyseus/colyseus/issues/948), where a server-side `client.leave(4000)` (Colyseus' `CloseCode.CONSENTED`) surfaced on the client as close code 1004, and made the server run `onDrop()` — opening a reconnection window — instead of treating the leave as consented. Thanks to [@trueicecold](https://github.com/trueicecold) for the detailed report.

### Changed

- **Renamed the Unity WebGL plugin files** from `WebSocket.jslib` / `WebSocket.jspre` to `NativeWebSocket.jslib` / `NativeWebSocket.jspre`. Unity flattens WebGL plugins into a single output directory, so the previous generic name collided with identically named plugins from other packages (Photon ships a `WebSocket.jslib`), failing the build with `Plugin 'WebSocket.jslib' is used from several locations`. Only the file names changed — the exported JavaScript symbols are unchanged, and nothing references these files by name. Thanks to [@Alaadel](https://github.com/Alaadel) for reporting.

  **Upgrading:** `.unitypackage` imports do not delete removed files. If you upgrade in place, delete the old `WebSocket.jslib` and `WebSocket.jspre` from your project, otherwise the new and old plugins will collide with each other. Users installing via UPM are not affected.
