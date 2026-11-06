// #2247 (v3, G1 item 2): uma regra efetiva alimenta a prévia, o papel e a tela
// Ajustes. A regra da loja (servidor) vence o aparelho; sem ela vale o modo
// salvo; sem nada, só o QR (D4 da v2: a barra fica desligada no padrão).
'use strict';

module.exports = function ({ test, runInSandbox, sandbox, assert }) {
  const REGRA = 'cdb-regra-impressao.v1';
  const MODO = 'cdb-prod-scan-mode';
  const ORIENT = 'cdb-etq-orient';
  const ls = sandbox.localStorage;
  const regraDaLoja = doc => ls.setItem(REGRA, JSON.stringify({ v: 1, documentos: { 'etiqueta-producao': doc } }));
  const limpar = () => { ls.removeItem(REGRA); ls.removeItem(MODO); ls.removeItem(ORIENT); };
  const puro = v => JSON.parse(JSON.stringify(v));

  test('v3 regra: regraEfetiva diz de onde vem cada escolha', () => {
    const I = sandbox.window.cdbImpressoras;
    limpar();
    let r = puro(I.regraEfetiva(ls, 'etiqueta-producao'));
    assert.deepStrictEqual(r.codigos, ['qr']);
    assert.deepStrictEqual(r.daLoja, { codigos: false, orientacao: false });
    ls.setItem(MODO, 'both');
    regraDaLoja({ codigos: ['barras'], orientacao: 'horizontal' });
    r = puro(I.regraEfetiva(ls, 'etiqueta-producao'));
    assert.deepStrictEqual(r.codigos, ['barras']);
    assert.strictEqual(r.orientacao, 'horizontal');
    assert.deepStrictEqual(r.daLoja, { codigos: true, orientacao: true });
    limpar();
  });

  test('v3 regra: modoDosCodigos traduz a lista para o modo da tela', () => {
    const I = sandbox.window.cdbImpressoras;
    assert.strictEqual(I.modoDosCodigos(['qr', 'barras']), 'both');
    assert.strictEqual(I.modoDosCodigos(['barras', 'qr']), 'both');
    assert.strictEqual(I.modoDosCodigos(['barras']), 'barcode');
    assert.strictEqual(I.modoDosCodigos(['qr']), 'qr');
    assert.strictEqual(I.modoDosCodigos([]), 'none');
  });

  test('v3 regra: a 58 mm (getProdScanMode) segue a regra da loja', () => {
    limpar();
    ls.setItem(MODO, 'both');
    regraDaLoja({ codigos: ['qr'] });
    assert.strictEqual(runInSandbox('getProdScanMode()'), 'qr');
    ls.removeItem(REGRA);
    assert.strictEqual(runInSandbox('getProdScanMode()'), 'both');
    ls.removeItem(MODO);
    assert.strictEqual(runInSandbox('getProdScanMode()'), 'qr');
    ls.setItem(MODO, 'both');
  });

  test('v3 regra: a orientação da tela segue a regra da loja', () => {
    limpar();
    ls.setItem(ORIENT, 'vertical');
    regraDaLoja({ orientacao: 'horizontal' });
    assert.strictEqual(runInSandbox('getEtqOrient()'), 'horizontal');
    limpar();
    ls.setItem(MODO, 'both');
  });

  // Elementos com estado, só para estes testes: o stub padrão devolve um novo a cada chamada.
  function telaAjustes() {
    runInSandbox(`
      (function () {
        const els = {};
        const el = id => els[id] || (els[id] = { id, value: '', textContent: '', hidden: false, disabled: false, style: {}, dataset: {},
          classList: { _s: new Set(), add(c) { this._s.add(c); }, remove(c) { this._s.delete(c); }, toggle(c, on) { (on === undefined ? !this._s.has(c) : on) ? this._s.add(c) : this._s.delete(c); }, contains(c) { return this._s.has(c); } },
          attrs: {}, setAttribute(k, v) { this.attrs[k] = String(v); }, getAttribute(k) { return k in this.attrs ? this.attrs[k] : null; }, removeAttribute(k) { delete this.attrs[k]; },
          getContext() { return { fillRect() {}, drawImage() {}, save() {}, restore() {}, scale() {}, set fillStyle(v) {}, set imageSmoothingEnabled(v) {} }; } });
        const cartoes = ['both', 'barcode', 'qr', 'none'].map(m => Object.assign(el('card-' + m), { dataset: { mode: m } }));
        const orient = ['vertical', 'horizontal'].map(o => Object.assign(el('orient-' + o), { dataset: { orient: o } }));
        globalThis.__ajustes = { els, cartoes, orient, toasts: [],
          get: document.getElementById, qsa: document.querySelectorAll, toast: globalThis.showToast };
        document.getElementById = el;
        document.querySelectorAll = sel => sel.indexOf('#scanModeOptions') === 0 ? cartoes : sel.indexOf('#orientModeOptions') === 0 ? orient : [];
        globalThis.showToast = m => __ajustes.toasts.push(m);
      })();
    `);
  }
  function restaurar() {
    runInSandbox(`document.getElementById = __ajustes.get; document.querySelectorAll = __ajustes.qsa; globalThis.showToast = __ajustes.toast;`);
  }

  test('v3 regra: Ajustes mostra o código da loja e trava a escolha local', () => {
    limpar();
    ls.setItem(MODO, 'both');
    regraDaLoja({ codigos: ['qr'] });
    telaAjustes();
    try {
      runInSandbox(`setScanModeChoice(getProdScanMode())`);
      const ativo = runInSandbox(`__ajustes.cartoes.filter(c => c.classList.contains('active')).map(c => c.dataset.mode).join()`);
      assert.strictEqual(ativo, 'qr');
      assert.strictEqual(runInSandbox(`__ajustes.cartoes.every(c => c.disabled)`), true);
      assert.strictEqual(runInSandbox(`__ajustes.els.ajusteProdScanMode.disabled`), true);
      const aviso = runInSandbox(`__ajustes.els.scanModeDaLoja`);
      assert.strictEqual(aviso.hidden, false);
      assert.match(aviso.textContent, /loja/i);
      runInSandbox(`setScanModeChoice('barcode', true)`);
      assert.strictEqual(ls.getItem(MODO), 'both', 'escolha local não grava quando a loja decide');
      assert.match(runInSandbox(`__ajustes.toasts.join('|')`), /loja/i);
      ls.removeItem(REGRA);
      runInSandbox(`setScanModeChoice(getProdScanMode())`);
      assert.strictEqual(runInSandbox(`__ajustes.cartoes.some(c => c.disabled)`), false);
      assert.strictEqual(runInSandbox(`__ajustes.els.scanModeDaLoja.hidden`), true);
    } finally { restaurar(); limpar(); ls.setItem(MODO, 'both'); }
  });

  test('v3 regra: Ajustes trava a orientação quando a loja decide', () => {
    limpar();
    regraDaLoja({ orientacao: 'horizontal' });
    telaAjustes();
    try {
      runInSandbox(`setOrientChoice(getEtqOrient())`);
      assert.strictEqual(runInSandbox(`__ajustes.orient.every(c => c.disabled)`), true);
      assert.strictEqual(runInSandbox(`__ajustes.els.orientDaLoja.hidden`), false);
      runInSandbox(`setOrientChoice('vertical', true)`);
      assert.strictEqual(ls.getItem(ORIENT), null);
    } finally { restaurar(); limpar(); ls.setItem(MODO, 'both'); }
  });

  test('v3 regra: a miniatura de Ajustes é a prévia da produção', () => {
    telaAjustes();
    runInSandbox(`globalThis.__previaOrig = previaDaEtiquetaProducao; globalThis.__chamadas = [];
      previaDaEtiquetaProducao = (et, nota) => { __chamadas.push(et); return { canvas: { width: 416, height: 240 } }; };`);
    try {
      runInSandbox(`redrawAjustesEtqThumb()`);
      assert.strictEqual(runInSandbox(`__chamadas.length`), 1);
      assert.strictEqual(runInSandbox(`__chamadas[0].productName`), 'Bolo de Cenoura');
    } finally { runInSandbox(`previaDaEtiquetaProducao = __previaOrig;`); restaurar(); }
  });
};
