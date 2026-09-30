// Photon Fusion's menu declares FusionCopyToClipboard as an __Internal import for
// WebGL (FusionMenuUIGameplay.cs), but this Fusion install ships no browser side
// for it, so wasm-ld fails the WebGL link with "undefined symbol:
// FusionCopyToClipboard". This supplies it.
//
// The async Clipboard API is tried first and the hidden-textarea fallback covers
// the browsers that only expose it over a secure context or refuse it inside the
// canvas' event, which includes some of the mobile browsers the CrazyGames build
// has to run in.
var FusionClipboardLibrary = {
  FusionCopyToClipboard: function (textPointer) {
    var value = UTF8ToString(textPointer);
    try {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(value);
        return;
      }
    } catch (error) { /* fall through to the textarea path */ }

    try {
      var area = document.createElement('textarea');
      area.value = value;
      area.setAttribute('readonly', '');
      area.style.position = 'fixed';
      area.style.top = '-1000px';
      area.style.opacity = '0';
      document.body.appendChild(area);
      area.select();
      area.setSelectionRange(0, value.length);
      document.execCommand('copy');
      document.body.removeChild(area);
    } catch (error) {
      console.warn('FusionCopyToClipboard failed: ' + error);
    }
  },
};

mergeInto(LibraryManager.library, FusionClipboardLibrary);
