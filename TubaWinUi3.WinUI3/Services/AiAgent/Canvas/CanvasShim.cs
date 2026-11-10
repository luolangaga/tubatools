namespace TubaWinUi3.Services.AiAgent;

/// <summary>
/// Script injected before any canvas document runs. It exposes the small <c>window.tuba</c>
/// surface, turns <c>data-tuba-*</c> attributes into delegated clicks, reports the content
/// height so the host can size the bubble to the page (no dead space), and keeps the page
/// usable outside the app by simply leaving <c>window.tuba</c> undefined.
/// </summary>
public static class CanvasShim
{
    public const string HostName = "canvas.local";
    public const string Origin = "https://" + HostName + "/";

    public const string Script = """
        (function () {
          var webview = (window.chrome && window.chrome.webview) || null;
          if (!webview) return;
          var listeners = { message: [], state: [], images: [], notice: [] };
          function post(payload) {
            try { webview.postMessage(payload); } catch (e) { }
          }
          function emit(kind, value) {
            listeners[kind].forEach(function (fn) {
              try { fn(value); } catch (e) { if (window.console) console.error(e); }
            });
          }
          var api = {
            ready: function () { post({ type: 'ready' }); },
            send: function (text) { if (text) post({ type: 'prompt', text: String(text) }); },
            insert: function (text) { if (text) post({ type: 'insert', text: String(text) }); },
            title: function (text) { if (text) post({ type: 'title', text: String(text) }); },
            open: function (url) { if (url) post({ type: 'open', text: String(url) }); },
            images: function () { return api._images || []; },
            reportHeight: reportHeight,
            onMessage: function (fn) { if (typeof fn === 'function') listeners.message.push(fn); },
            onState: function (fn) { if (typeof fn === 'function') listeners.state.push(fn); },
            onImages: function (fn) { if (typeof fn === 'function') { listeners.images.push(fn); if (api._images) fn(api._images); } },
            onNotice: function (fn) { if (typeof fn === 'function') listeners.notice.push(fn); }
          };
          window.tuba = api;

          // The bubble is sized to the document so short lessons do not sit in a tall empty box.
          var lastHeight = 0;
          var heightTimer = 0;
          function measure() {
            var body = document.body;
            if (!body) return 0;
            var previous = body.style.overflow;
            var height = Math.max(
              body.scrollHeight || 0,
              body.offsetHeight || 0,
              document.documentElement ? document.documentElement.scrollHeight || 0 : 0
            );
            body.style.overflow = previous;
            return Math.ceil(height);
          }
          function reportHeight() {
            var height = measure();
            if (height <= 0 || Math.abs(height - lastHeight) < 2) return;
            lastHeight = height;
            post({ type: 'height', value: height });
          }
          function scheduleHeight() {
            clearTimeout(heightTimer);
            heightTimer = setTimeout(reportHeight, 60);
          }

          webview.addEventListener('message', function (event) {
            var value = event.data;
            if (typeof value === 'string') { try { value = JSON.parse(value); } catch (e) { return; } }
            if (!value || typeof value.type !== 'string') return;
            if (value.type === 'message') emit('message', value);
            else if (value.type === 'state') emit('state', value);
            else if (value.type === 'notice') emit('notice', value);
            else if (value.type === 'images') { api._images = value.items || []; emit('images', api._images); }
          });

          document.addEventListener('click', function (event) {
            var node = event.target;
            while (node && node !== document.body) {
              if (node.getAttribute) {
                var prompt = node.getAttribute('data-tuba-prompt');
                if (prompt) { event.preventDefault(); api.send(prompt); return; }
                var insert = node.getAttribute('data-tuba-insert');
                if (insert) { event.preventDefault(); api.insert(insert); return; }
                var url = node.getAttribute('data-tuba-open');
                if (url) { event.preventDefault(); api.open(url); return; }
              }
              node = node.parentNode;
            }
          }, true);

          function start() {
            api.ready();
            reportHeight();
            try {
              var Observer = window.ResizeObserver;
              if (Observer && document.documentElement)
                new Observer(scheduleHeight).observe(document.documentElement);
            } catch (e) { }
            try {
              var Mutations = window.MutationObserver;
              if (Mutations && document.body)
                new Mutations(scheduleHeight).observe(document.body, { childList: true, subtree: true, attributes: true, characterData: true });
            } catch (e) { }
            window.addEventListener('load', scheduleHeight);
            window.addEventListener('resize', scheduleHeight);
            // Late webfonts/images change the flow after load.
            setTimeout(scheduleHeight, 200);
            setTimeout(scheduleHeight, 800);
          }
          if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
          else start();
        })();
        """;
}
