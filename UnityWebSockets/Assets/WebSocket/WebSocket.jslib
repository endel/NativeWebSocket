/*
 * unity-websocket-webgl
 *
 * @author Jiri Hybek <jiri@hybek.cz>
 * @copyright 2018 Jiri Hybek <jiri@hybek.cz>
 * @license Apache 2.0 - See LICENSE file distributed with this source code.
 */

var LibraryWebSocket = {
	$webSocketState: {
		/*
		 * Map of instances
		 *
		 * Instance structure:
		 * {
		 * 	url: string,
		 * 	ws: WebSocket
		 * }
		 */
		instances: {},

		/* Last instance ID */
		lastId: 0,

		/* Event listeners */
		onOpen: null,
		onMesssage: null,
		onError: null,
		onClose: null,

		/* Debug mode */
		debug: false
	},

	/**
	 * Set onOpen callback
	 *
	 * @param callback Reference to C# static function
	 */
	WebSocketSetOnOpen: function(callback) {

		webSocketState.onOpen = callback;

	},

	/**
	 * Set onMessage callback
	 *
	 * @param callback Reference to C# static function
	 */
	WebSocketSetOnMessage: function(callback) {

		webSocketState.onMessage = callback;

	},

	/**
	 * Set onError callback
	 *
	 * @param callback Reference to C# static function
	 */
	WebSocketSetOnError: function(callback) {

		webSocketState.onError = callback;

	},

	/**
	 * Set onClose callback
	 *
	 * @param callback Reference to C# static function
	 */
	WebSocketSetOnClose: function(callback) {

		webSocketState.onClose = callback;

	},

	/**
	 * Allocate new WebSocket instance struct
	 *
	 * @param url Server URL
	 */
	WebSocketAllocate: function(url) {

		var urlStr = Pointer_stringify(url);
		var id = webSocketState.lastId++;

		webSocketState.instances[id] = {
			url: urlStr,
			ws: null
		};

		return id;

	},

	/**
	 * Remove reference to WebSocket instance
	 *
	 * If socket is not closed function will close it but onClose event will not be emitted because
	 * this function should be invoked by C# WebSocket destructor.
	 *
	 * @param instanceId Instance ID
	 */
	WebSocketFree: function(instanceId) {

		var instance = webSocketState.instances[instanceId];

		if (!instance) return 0;

		// Close if not closed
		if (instance.ws !== null && instance.ws.readyState < 2)
			instance.ws.close();

		// Remove reference
		delete webSocketState.instances[instanceId];

		return 0;

	},

	/**
	 * Connect WebSocket to the server
	 *
	 * @param instanceId Instance ID
	 */
	WebSocketConnect: function(instanceId) {

		var instance = webSocketState.instances[instanceId];
		if (!instance) return -1;

		if (instance.ws !== null)
			return -2;

		instance.ws = new WebSocket(instance.url);

		instance.ws.binaryType = 'arraybuffer';

		instance.ws.onopen = function() {

			if (webSocketState.debug)
				console.log("[JSLIB WebSocket] Connected.");

			if (webSocketState.onOpen)
				Runtime.dynCall('vi', webSocketState.onOpen, [ instanceId ]);

		};

		instance.ws.onmessage = function(ev) {

			if (webSocketState.debug)
				console.log("[JSLIB WebSocket] Received message:", ev.data);

			if (webSocketState.onMessage === null)
				return;

			if (ev.data instanceof ArrayBuffer) {

				var dataBuffer = new Uint8Array(ev.data);

				var buffer = _malloc(dataBuffer.length);
				HEAPU8.set(dataBuffer, buffer);

				try {
					Runtime.dynCall('viii', webSocketState.onMessage, [ instanceId, buffer, dataBuffer.length ]);
				} finally {
					_free(buffer);
				}

			}

		};

		instance.ws.onerror = function(ev) {

			if (webSocketState.debug)
				console.log("[JSLIB WebSocket] Error occured.");

			if (webSocketState.onError) {

				var msg = "WebSocket error.";
				var msgBytes = lengthBytesUTF8(msg);
				var msgBuffer = _malloc(msgBytes + 1);
				stringToUTF8(msg, msgBuffer, msgBytes);

				try {
					Runtime.dynCall('vii', webSocketState.onError, [ instanceId, msgBuffer ]);
				} finally {
					_free(msgBuffer);
				}

			}

		};

		instance.ws.onclose = function(ev) {

			if (webSocketState.debug)
				console.log("[JSLIB WebSocket] Closed.");

			if (webSocketState.onClose)
				Runtime.dynCall('vii', webSocketState.onClose, [ instanceId, ev.code ]);

			delete instance.ws;

		};

		return 0;

	},

	/**
	 * Close WebSocket connection
	 *
	 * @param instanceId Instance ID
	 * @param code Close status code
	 * @param reasonPtr Pointer to reason string
	 */
	WebSocketClose: function(instanceId, code, reasonPtr) {

		var instance = webSocketState.instances[instanceId];
		if (!instance) return -1;

		if (instance.ws === null)
			return -3;

		if (instance.ws.readyState === 2)
			return -4;

		if (instance.ws.readyState === 3)
			return -5;

		var reason = ( reasonPtr ? Pointer_stringify(reasonPtr) : undefined );

		try {
			instance.ws.close(code, reason);
		} catch(err) {
			return -7;
		}

		return 0;

	},

	/**
	 * Send message over WebSocket
	 *
	 * @param instanceId Instance ID
	 * @param bufferPtr Pointer to the message buffer
	 * @param length Length of the message in the buffer
	 */
	WebSocketSend: function(instanceId, bufferPtr, length) {

		var instance = webSocketState.instances[instanceId];
		if (!instance) return -1;

		if (instance.ws === null)
			return -3;

		if (instance.ws.readyState !== 1)
			return -6;

		instance.ws.send(HEAPU8.buffer.slice(bufferPtr, bufferPtr + length));

		return 0;

	},

	/**
	 * Return WebSocket readyState
	 *
	 * @param instanceId Instance ID
	 */
	WebSocketGetState: function(instanceId) {

		var instance = webSocketState.instances[instanceId];
		if (!instance) return -1;

		if (instance.ws)
			return instance.ws.readyState;
		else
			return 3;

	}

};

autoAddDeps(LibraryWebSocket, '$webSocketState');
mergeInto(LibraryManager.library, LibraryWebSocket);

// var LibraryWebSockets = {
// 	$webSocketInstances: [],
//
// 	SocketCreate: function(url)
// 	{
// 		var str = Pointer_stringify(url);
// 		var socket = {
// 			socket: new WebSocket(str),
// 			buffer: new Uint8Array(0),
// 			error: null,
// 			messages: []
// 		}
//
// 		socket.socket.binaryType = 'arraybuffer';
//
// 		socket.socket.onmessage = function (e) {
// 			// Todo: handle other data types?
// 			if (e.data instanceof Blob)
// 			{
// 				var reader = new FileReader();
// 				reader.addEventListener("loadend", function() {
// 					var array = new Uint8Array(reader.result);
// 					socket.messages.push(array);
// 				});
// 				reader.readAsArrayBuffer(e.data);
// 			}
// 			else if (e.data instanceof ArrayBuffer)
// 			{
// 				var array = new Uint8Array(e.data);
// 				socket.messages.push(array);
// 			}
// 		};
//
// 		socket.socket.onclose = function (e) {
// 			if (e.code != 1000)
// 			{
// 				if (e.reason != null && e.reason.length > 0)
// 					socket.error = e.reason;
// 				else
// 				{
// 					switch (e.code)
// 					{
// 						case 1001:
// 							socket.error = "Endpoint going away.";
// 							break;
// 						case 1002:
// 							socket.error = "Protocol error.";
// 							break;
// 						case 1003:
// 							socket.error = "Unsupported message.";
// 							break;
// 						case 1005:
// 							socket.error = "No status.";
// 							break;
// 						case 1006:
// 							socket.error = "Abnormal disconnection.";
// 							break;
// 						case 1009:
// 							socket.error = "Data frame too large.";
// 							break;
// 						default:
// 							socket.error = "Error "+e.code;
// 					}
// 				}
// 			}
// 		}
// 		var instance = webSocketInstances.push(socket) - 1;
// 		return instance;
// 	},
//
// 	SocketState: function (socketInstance)
// 	{
// 		var socket = webSocketInstances[socketInstance];
// 		return socket.socket.readyState;
// 	},
//
// 	SocketError: function (socketInstance, ptr, bufsize)
// 	{
// 		var socket = webSocketInstances[socketInstance];
// 		if (socket.error == null)
// 			return 0;
// 		var str = socket.error.slice(0, Math.max(0, bufsize - 1));
// 		writeStringToMemory(str, ptr, false);
// 		return 1;
// 	},
//
// 	SocketSend: function (socketInstance, ptr, length)
// 	{
// 		var socket = webSocketInstances[socketInstance];
// 		socket.socket.send (HEAPU8.buffer.slice(ptr, ptr+length));
// 	},
//
// 	SocketRecvLength: function(socketInstance)
// 	{
// 		var socket = webSocketInstances[socketInstance];
// 		if (socket.messages.length == 0)
// 			return 0;
// 		return socket.messages[0].length;
// 	},
//
// 	SocketRecv: function (socketInstance, ptr, length)
// 	{
// 		var socket = webSocketInstances[socketInstance];
// 		if (socket.messages.length == 0)
// 			return 0;
// 		if (socket.messages[0].length > length)
// 			return 0;
// 		HEAPU8.set(socket.messages[0], ptr);
// 		socket.messages = socket.messages.slice(1);
// 	},
//
// 	SocketClose: function (socketInstance)
// 	{
// 		var socket = webSocketInstances[socketInstance];
// 		socket.socket.close();
// 	}
// };
//
// autoAddDeps(LibraryWebSockets, '$webSocketInstances');
// mergeInto(LibraryManager.library, LibraryWebSockets);
