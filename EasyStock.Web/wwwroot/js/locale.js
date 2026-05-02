/* EasyStock — Brazilian locale helpers (extraído de _Layout.cshtml).
   Intercepta vírgula em inputs numéricos e converte para ponto, permitindo
   "10,50" digitado virar "10.50" no value. */
(function () {
  document.addEventListener('keydown', function (e) {
    if (e.key !== ',' && e.key !== 'Decimal') return;
    if (e.target.tagName !== 'INPUT') return;
    const t = e.target;
    if (t.type !== 'number' && t.inputMode !== 'decimal' && t.inputMode !== 'numeric') return;
    e.preventDefault();
    if (t.type !== 'number') {
      const s = t.selectionStart, end = t.selectionEnd;
      t.value = t.value.slice(0, s) + '.' + t.value.slice(end);
      t.setSelectionRange(s + 1, s + 1);
      t.dispatchEvent(new Event('input', { bubbles: true }));
    }
  }, true);
})();
