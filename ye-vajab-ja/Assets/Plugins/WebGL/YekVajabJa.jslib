mergeInto(LibraryManager.library, {
  YV_UpdateUi: function (jsonPtr) {
    if (typeof window.yvReceiveState === "function") {
      window.yvReceiveState(UTF8ToString(jsonPtr));
    }
  }
});
