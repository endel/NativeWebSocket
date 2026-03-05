// Licensed under the MIT License. See LICENSE in the project root for license information.
// Authored originally by Stephen Hodgson
(function () { // wrap the method insertion in a function to avoid polluting global scope
  /**
   * Initializes the dynCall_* function table lookups.
   * Thanks to @De-Panther for the following code snippet.
   * Checks if specific dynCall functions exist,
   * if not, it will create them using the getWasmTableEntry function.
   * @see https://discussions.unity.com/t/makedyncall-replacing-dyncall-in-unity-6/1543088
  */
  if (Module["ENVIRONMENT_IS_PTHREAD"]) {
    // If we are in a pthread, we need to initialize the dynCalls immediately
    initDynCalls();
  } else {
    Module['preRun'].push(function () {
      initDynCalls();
    });
  }

  function initDynCalls() {
    if (typeof getWasmTableEntry !== "undefined") {
      Module.dynCall_vi = Module.dynCall_vi || function (cb, arg1) {
        return getWasmTableEntry(cb)(arg1);
      }
      Module.dynCall_vii = Module.dynCall_vii || function (cb, arg1, arg2) {
        return getWasmTableEntry(cb)(arg1, arg2);
      }
      Module.dynCall_viii = Module.dynCall_viii || function (cb, arg1, arg2, arg3) {
        return getWasmTableEntry(cb)(arg1, arg2, arg3);
      }
      Module.dynCall_viiii = Module.dynCall_viiii || function (cb, arg1, arg2, arg3, arg4) {
        return getWasmTableEntry(cb)(arg1, arg2, arg3, arg4);
      }
    }
  }
})();