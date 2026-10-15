/* EasyStock — Brazilian locale helpers (extraído de _Layout.cshtml).
   Intercepta vírgula em inputs numéricos e converte para ponto, permitindo
   "10,50" digitado virar "10.50" no value. Também normaliza texto colado
   (paste) com a mesma regra: o ÚLTIMO separador (vírgula ou ponto) é o
   decimal, os demais são milhar e são descartados — mesma convenção de
   parseDecimalBR (form-validate.js) e NormalizarDecimalPtBr
   (InvariantDecimalModelBinder.cs).

   issue 960 (BUG-004 do QA): o handler original inseria "." na posição do
   cursor SEM remover ponto pré-existente — campo pré-preenchido "14.29" +
   digitar "7,77" virava "14.297.77" (parseFloat trunca silenciosamente em
   14.297). Agora o handler é idempotente: após rodar, o value tem no
   máximo UM ponto — qualquer entrada ambígua fica visível, não truncada
   em silêncio. */
(function () {
  function normalizarParaPonto(s) {
    s = String(s == null ? '' : s).trim();
    if (!s) return s;
    var lastComma = s.lastIndexOf(',');
    var lastDot = s.lastIndexOf('.');
    if (lastComma > -1 && lastComma > lastDot) {
      // vírgula é o decimal mais recente — pontos anteriores são milhar.
      return s.replace(/\./g, '').replace(',', '.');
    }
    if (lastDot > -1 && lastComma > -1) {
      // ponto é o decimal; vírgulas anteriores são milhar.
      return s.replace(/,/g, '');
    }
    // só um tipo de separador (ou nenhum): troca a primeira vírgula por ponto.
    return s.replace(',', '.');
  }

  // issue 986: campo com mascara de moeda se governa sozinho — a entrada e em
  // CENTAVOS e o separador e desenhado pela mascara, entao o usuario nunca digita
  // ',' nem '.'. Deixar o listener global agir ali so introduziria um ponto que o
  // onlyDigits() da mascara descartaria em seguida, alem de mover o cursor.
  function temMascaraDeMoeda(t) {
    return !!(t && t.dataset && t.dataset.mask === 'money');
  }

  function ehCampoDecimal(t) {
    return t && t.tagName === 'INPUT' && t.type !== 'number' &&
      !temMascaraDeMoeda(t) &&
      (t.inputMode === 'decimal' || t.inputMode === 'numeric');
  }

  document.addEventListener('keydown', function (e) {
    if (e.key !== ',' && e.key !== 'Decimal') return;
    if (e.target.tagName !== 'INPUT') return;
    const t = e.target;
    if (temMascaraDeMoeda(t)) return;   // issue 986 — ver comentario em ehCampoDecimal
    if (t.type !== 'number' && t.inputMode !== 'decimal' && t.inputMode !== 'numeric') return;
    e.preventDefault();
    if (t.type !== 'number') {
      // Define o separador decimal em vez de inserir: remove pontos dos dois
      // lados do cursor (só podiam ser milhar, já que a vírgula recém-digitada
      // é o decimal) e insere um único ponto na posição correta.
      const s = t.selectionStart, end = t.selectionEnd;
      const esq = t.value.slice(0, s).replace(/\./g, '');
      const dir = t.value.slice(end).replace(/\./g, '');
      const cur = esq.length + 1;
      t.value = esq + '.' + dir;
      t.setSelectionRange(cur, cur);
      t.dispatchEvent(new Event('input', { bubbles: true }));
    }
  }, true);

  document.addEventListener('paste', function (e) {
    const t = e.target;
    if (!ehCampoDecimal(t)) return;
    const clip = e.clipboardData || window.clipboardData;
    const texto = clip ? clip.getData('text') : '';
    if (!texto) return;
    e.preventDefault();
    const colado = normalizarParaPonto(texto);
    const s = t.selectionStart, end = t.selectionEnd;
    // Mesma regra do keydown: o value final só pode ter 1 ponto decimal —
    // remove pontos vestigiais do que já estava no campo fora da seleção,
    // mantém só o do texto colado.
    const esq = t.value.slice(0, s).replace(/\./g, '');
    const dir = t.value.slice(end).replace(/\./g, '');
    t.value = esq + colado + dir;
    const cur = (esq + colado).length;
    t.setSelectionRange(cur, cur);
    t.dispatchEvent(new Event('input', { bubbles: true }));
  }, true);
})();
