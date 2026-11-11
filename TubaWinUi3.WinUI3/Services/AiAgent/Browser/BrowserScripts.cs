namespace TubaWinUi3.Services.AiAgent;

/// <summary>
/// The small, dependency-free page scripts used by browser automation. They only read or
/// exercise the current DOM; nothing is fetched or executed from the page itself.
/// </summary>
internal static class BrowserScripts
{
    internal const string Snapshot = """
        (() => {
          const ATTR = 'data-tuba-ref';
          document.querySelectorAll('[' + ATTR + ']').forEach(el => el.removeAttribute(ATTR));
          const compact = value => String(value ?? '').replace(/\s+/g, ' ').trim().slice(0, 140);
          const visible = el => {
            const style = getComputedStyle(el);
            if (style.display === 'none' || style.visibility === 'hidden' || parseFloat(style.opacity || '1') < 0.02) return false;
            const rect = el.getBoundingClientRect();
            return rect.width > 1 && rect.height > 1;
          };
          const selector = 'a[href],button,input,textarea,select,summary,[role="button"],[role="link"],[role="checkbox"],[role="radio"],[role="tab"],[role="menuitem"],[role="switch"],[contenteditable="true"],[onclick],[tabindex]:not([tabindex="-1"])';
          const items = [];
          let index = 0;
          for (const el of document.querySelectorAll(selector)) {
            if (items.length >= 140) break;
            if (!visible(el)) continue;
            const tag = el.tagName.toLowerCase();
            const rect = el.getBoundingClientRect();
            const ref = 'e' + (++index);
            el.setAttribute(ATTR, ref);
            const type = String(el.getAttribute('type') || '').toLowerCase();
            const label = compact(el.getAttribute('aria-label') || el.getAttribute('placeholder') || el.getAttribute('title') ||
              el.getAttribute('alt') || el.innerText || (tag === 'input' ? el.value : ''));
            const item = { ref, tag, type, label, disabled: !!el.disabled, x: Math.round(rect.left + rect.width / 2), y: Math.round(rect.top + rect.height / 2) };
            if ((type === 'checkbox' || type === 'radio') && 'checked' in el) item.checked = !!el.checked;
            if ((tag === 'input' || tag === 'textarea' || tag === 'select') && type !== 'password') item.value = compact(el.value);
            if (tag === 'a') item.href = String(el.href || '').slice(0, 300);
            items.push(item);
          }
          const text = String(document.body ? document.body.innerText : '')
            .replace(/[ \t]+/g, ' ').replace(/\n{3,}/g, '\n\n').trim().slice(0, 6000);
          return {
            url: location.href,
            title: document.title,
            ready: document.readyState,
            text,
            items,
            scrollY: Math.round(window.scrollY),
            scrollHeight: Math.round(document.documentElement.scrollHeight),
            viewportHeight: Math.round(window.innerHeight)
          };
        })()
        """;

    private const string ResolveTemplate = """
        (() => {
          const ref = __REF__;
          const selector = __SELECTOR__;
          const text = __TEXT__;
          const compact = value => String(value ?? '').replace(/\s+/g, ' ').trim();
          let candidates = [];
          if (ref) {
            const found = document.querySelector('[data-tuba-ref="' + CSS.escape(ref) + '"]');
            if (found) candidates = [found];
          } else if (selector) {
            candidates = Array.from(document.querySelectorAll(selector));
          } else if (text) {
            const all = document.querySelectorAll('a,button,input,textarea,select,summary,[role="button"],[role="link"],[role="menuitem"],[contenteditable="true"],[onclick]');
            candidates = Array.from(all).filter(el => compact(el.innerText || el.value || el.getAttribute('aria-label') ||
              el.getAttribute('placeholder') || el.getAttribute('title')).includes(text));
          }
          candidates = candidates.filter(el => {
            const rect = el.getBoundingClientRect();
            const style = getComputedStyle(el);
            return rect.width > 1 && rect.height > 1 && style.display !== 'none' && style.visibility !== 'hidden';
          });
          if (candidates.length === 0) return { ok: false, error: 'not_found' };
          if (text && candidates.length > 1) {
            const matches = candidates.slice(0, 10).map((el, index) => {
              let assigned = el.getAttribute('data-tuba-ref');
              if (!assigned) { assigned = 'm' + (index + 1); el.setAttribute('data-tuba-ref', assigned); }
              return { ref: assigned, label: compact(el.innerText || el.value || el.getAttribute('aria-label') || el.getAttribute('placeholder')).slice(0, 120) };
            });
            return { ok: false, error: 'ambiguous', matches };
          }
          const el = candidates[0];
          let assigned = el.getAttribute('data-tuba-ref');
          if (!assigned) {
            assigned = 'e' + (document.querySelectorAll('[data-tuba-ref]').length + 1);
            el.setAttribute('data-tuba-ref', assigned);
          }
          el.scrollIntoView({ block: 'center', inline: 'center', behavior: 'auto' });
          const rect = el.getBoundingClientRect();
          return {
            ok: true,
            ref: assigned,
            tag: el.tagName.toLowerCase(),
            label: compact(el.innerText || el.value || el.getAttribute('aria-label') || el.getAttribute('placeholder') || el.getAttribute('title')).slice(0, 120),
            x: rect.left + rect.width / 2,
            y: rect.top + rect.height / 2
          };
        })()
        """;

    private const string FocusTemplate = """
        (() => {
          const ref = __REF__;
          const selector = __SELECTOR__;
          const el = ref ? document.querySelector('[data-tuba-ref="' + CSS.escape(ref) + '"]') :
            (selector ? document.querySelector(selector) : null);
          if (!el) return { ok: false, error: 'not_found' };
          el.scrollIntoView({ block: 'center', inline: 'center', behavior: 'auto' });
          el.focus({ preventScroll: true });
          if (__CLEAR__) {
            if (el.isContentEditable) {
              el.textContent = '';
              el.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'deleteContentBackward' }));
            } else if ('value' in el) {
              const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype :
                (el instanceof HTMLInputElement ? HTMLInputElement.prototype : null);
              const setter = proto ? Object.getOwnPropertyDescriptor(proto, 'value')?.set : null;
              if (setter) setter.call(el, ''); else el.value = '';
              el.dispatchEvent(new Event('input', { bubbles: true }));
              el.dispatchEvent(new Event('change', { bubbles: true }));
            }
          }
          return { ok: true, tag: el.tagName.toLowerCase(), editable: !el.disabled && !el.readOnly };
        })()
        """;

    private const string SelectTemplate = """
        (() => {
          const ref = __REF__;
          const selector = __SELECTOR__;
          const wantedValue = __VALUE__;
          const wantedLabel = __LABEL__;
          const el = ref ? document.querySelector('[data-tuba-ref="' + CSS.escape(ref) + '"]') :
            (selector ? document.querySelector(selector) : null);
          if (!el || el.tagName.toLowerCase() !== 'select') return { ok: false, error: 'not_select' };
          const options = Array.from(el.options);
          const option = wantedValue ? options.find(item => item.value === wantedValue) :
            options.find(item => item.text.trim() === wantedLabel || item.text.includes(wantedLabel));
          if (!option) {
            return { ok: false, error: 'option_not_found', options: options.slice(0, 30).map(item => ({ value: item.value, label: item.text.trim() })) };
          }
          el.value = option.value;
          el.dispatchEvent(new Event('input', { bubbles: true }));
          el.dispatchEvent(new Event('change', { bubbles: true }));
          return { ok: true, value: option.value, label: option.text.trim() };
        })()
        """;

    private const string ScrollTemplate = """
        (() => {
          const ref = __REF__;
          const dx = __DX__;
          const dy = __DY__;
          if (ref) {
            const el = document.querySelector('[data-tuba-ref="' + CSS.escape(ref) + '"]');
            if (!el) return { ok: false, error: 'not_found' };
            el.scrollBy(dx, dy);
          } else {
            window.scrollBy(dx, dy);
          }
          return { ok: true, x: Math.round(window.scrollX), y: Math.round(window.scrollY) };
        })()
        """;

    private const string WaitTemplate = """
        (() => {
          const selector = __SELECTOR__;
          const text = __TEXT__;
          if (selector && !document.querySelector(selector)) return { ok: false, state: 'selector' };
          if (text && !String(document.body ? document.body.innerText : '').includes(text)) return { ok: false, state: 'text' };
          return { ok: true, state: document.readyState };
        })()
        """;

    internal const string PageStatus = """
        (() => ({
          url: location.href,
          title: document.title,
          ready: document.readyState,
          scrollY: Math.round(window.scrollY),
          scrollHeight: Math.round(document.documentElement.scrollHeight),
          viewportHeight: Math.round(window.innerHeight)
        }))()
        """;

    internal static string ResolveElement(string? reference, string? selector, string? text) => ResolveTemplate
        .Replace("__REF__", BrowserCdp.Json(reference ?? ""))
        .Replace("__SELECTOR__", BrowserCdp.Json(selector ?? ""))
        .Replace("__TEXT__", BrowserCdp.Json(text ?? ""));

    internal static string FocusElement(string? reference, string? selector, bool clear) => FocusTemplate
        .Replace("__REF__", BrowserCdp.Json(reference ?? ""))
        .Replace("__SELECTOR__", BrowserCdp.Json(selector ?? ""))
        .Replace("__CLEAR__", clear ? "true" : "false");

    internal static string SelectOption(string? reference, string? selector, string? value, string? label) => SelectTemplate
        .Replace("__REF__", BrowserCdp.Json(reference ?? ""))
        .Replace("__SELECTOR__", BrowserCdp.Json(selector ?? ""))
        .Replace("__VALUE__", BrowserCdp.Json(value ?? ""))
        .Replace("__LABEL__", BrowserCdp.Json(label ?? ""));

    internal static string Scroll(string? reference, double dx, double dy) => ScrollTemplate
        .Replace("__REF__", BrowserCdp.Json(reference ?? ""))
        .Replace("__DX__", dx.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .Replace("__DY__", dy.ToString(System.Globalization.CultureInfo.InvariantCulture));

    internal static string WaitFor(string? selector, string? text) => WaitTemplate
        .Replace("__SELECTOR__", BrowserCdp.Json(selector ?? ""))
        .Replace("__TEXT__", BrowserCdp.Json(text ?? ""));
}
