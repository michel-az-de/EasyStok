// EasyStok — shared theme toggle.
// Auto-mounts a floating switch in the bottom-right unless `data-theme-toggle="manual"` is on <html>.
(function () {
  var KEY = 'easystok-theme';
  var root = document.documentElement;
  var saved = localStorage.getItem(KEY);
  if (saved === 'dark') root.setAttribute('data-theme', 'dark');

  function applyTheme(t) {
    if (t === 'dark') root.setAttribute('data-theme', 'dark');
    else root.removeAttribute('data-theme');
    localStorage.setItem(KEY, t);
    document.querySelectorAll('[data-theme-btn]').forEach(function (b) {
      b.classList.toggle('on', b.getAttribute('data-theme-btn') === t);
    });
  }
  window.__easystokTheme = { apply: applyTheme, current: function () { return root.getAttribute('data-theme') === 'dark' ? 'dark' : 'light'; } };

  function buildFloating() {
    if (root.getAttribute('data-theme-toggle') === 'manual') return;
    if (document.getElementById('es-theme-fab')) return;
    var wrap = document.createElement('div');
    wrap.id = 'es-theme-fab';
    wrap.innerHTML = '<button data-theme-btn="light" type="button">☀</button><button data-theme-btn="dark" type="button">☾</button>';
    document.body.appendChild(wrap);
    var css = document.createElement('style');
    css.textContent =
      '#es-theme-fab{position:fixed;right:18px;bottom:18px;z-index:9999;display:inline-flex;gap:2px;padding:4px;background:var(--bg-surface,#fff);border:1px solid var(--border-soft,#DEE2EB);border-radius:999px;box-shadow:var(--shadow-md,0 4px 12px rgba(10,21,48,0.08));font-family:"JetBrains Mono",ui-monospace,monospace}' +
      '#es-theme-fab button{border:0;background:transparent;width:34px;height:30px;border-radius:999px;cursor:pointer;font-size:14px;color:var(--text-muted,#707892);font-weight:600}' +
      '#es-theme-fab button.on{background:#E85814;color:#fff}';
    document.head.appendChild(css);
  }
  function init() {
    buildFloating();
    document.querySelectorAll('[data-theme-btn]').forEach(function (b) {
      b.addEventListener('click', function () { applyTheme(b.getAttribute('data-theme-btn')); });
      b.classList.toggle('on', b.getAttribute('data-theme-btn') === (saved === 'dark' ? 'dark' : 'light'));
    });
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();
