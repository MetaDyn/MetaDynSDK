mergeInto(LibraryManager.library, {
  MetaDynOpenImagePicker: function (gameObjectPtr, successPtr, failurePtr, maxBytes) {
    var gameObject = UTF8ToString(gameObjectPtr);
    var success = UTF8ToString(successPtr);
    var failure = UTF8ToString(failurePtr);
    var overlay = document.createElement('div');
    overlay.style.cssText = 'position:fixed;inset:0;z-index:2147483647;display:flex;align-items:center;justify-content:center;background:rgba(0,0,0,.72);font-family:Arial,sans-serif';
    var panel = document.createElement('div');
    panel.style.cssText = 'width:min(90vw,360px);padding:24px;border-radius:12px;background:#1d2430;color:white;box-shadow:0 12px 36px rgba(0,0,0,.4);text-align:center';
    var title = document.createElement('div');
    title.textContent = 'Choose a frame image';
    title.style.cssText = 'font-size:18px;font-weight:bold;margin-bottom:8px';
    var description = document.createElement('div');
    description.textContent = 'JPEG or PNG, up to 8 MiB and 2048 pixels per edge.';
    description.style.cssText = 'font-size:13px;line-height:1.4;margin-bottom:20px';
    var chooser = document.createElement('label');
    chooser.style.cssText = 'position:relative;display:block;overflow:hidden;padding:12px;border-radius:6px;background:#2674d9;color:white;cursor:pointer;font-size:14px';
    chooser.textContent = 'Choose Image';
    var input = document.createElement('input');
    input.type = 'file';
    input.accept = '.jpg,.jpeg,.png,image/jpeg,image/png';
    input.setAttribute('aria-label', 'Choose a frame image');
    input.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;opacity:0;cursor:pointer';
    chooser.appendChild(input);
    var cancel = document.createElement('button');
    cancel.type = 'button';
    cancel.textContent = 'Cancel';
    cancel.style.cssText = 'margin-top:12px;padding:8px 18px;border:0;background:transparent;color:white;cursor:pointer';
    panel.appendChild(title);
    panel.appendChild(description);
    panel.appendChild(chooser);
    panel.appendChild(cancel);
    overlay.appendChild(panel);
    document.body.appendChild(overlay);

    var finished = false;
    function finish(method, message) {
      if (finished) return;
      finished = true;
      if (overlay.parentNode) overlay.parentNode.removeChild(overlay);
      SendMessage(gameObject, method, message);
      if (Module['canvas']) Module['canvas'].focus();
    }
    cancel.onclick = function () { finish(failure, 'Image selection was cancelled.'); };

    input.onchange = function () {
      var file = input.files && input.files[0];
      if (!file) return;
      if (file.size === 0 || file.size > maxBytes) {
        finish(failure, 'Image must be between 1 byte and 8 MiB.');
        return;
      }
      var extension = /\.png$/i.test(file.name) ? '.png' : /\.jpe?g$/i.test(file.name) ? '.jpg' : '';
      if (!extension) {
        finish(failure, 'Choose a JPEG or PNG image.');
        return;
      }
      var reader = new FileReader();
      reader.onload = function () {
        try {
          var directory = '/metadyn_image_uploads';
          try { FS.mkdir(directory); } catch (ignored) {}
          var path = directory + '/' + Date.now() + '_' + Math.random().toString(36).slice(2) + extension;
          FS.writeFile(path, new Uint8Array(reader.result));
          finish(success, path);
        } catch (error) {
          finish(failure, 'Could not read the selected image.');
        }
      };
      reader.onerror = function () {
        finish(failure, 'Could not read the selected image.');
      };
      try { reader.readAsArrayBuffer(file); }
      catch (error) { finish(failure, 'Could not read the selected image.'); }
    };
  }
});
