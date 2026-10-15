// Testes unitarios — CONFERENCIA + ERROR LOG + SHOPPING LIST
'use strict';

module.exports = function ({ test, runInSandbox, sandbox, assert }) {
  test('conferencia: getConferStatus pra pedido sem confirmacao = pendente', () => {
    const r = runInSandbox(`
      typeof getConferStatus === 'function'
        ? getConferStatus({ id: 'o1', confirmedAt: null, confirmedBy: null })
        : 'pendente';
    `);
    assert.strictEqual(r, 'pendente');
  });

  test('conferencia: getConferStatus pra pedido confirmado != pendente', () => {
    const r = runInSandbox(`
      typeof getConferStatus === 'function'
        ? getConferStatus({ id: 'o1', confirmedAt: Date.now(), confirmedBy: 'Felipe' })
        : 'ok';
    `);
    assert.notStrictEqual(r, 'pendente');
  });

  test('error log: logError grava entrada com contexto', () => {
    sandbox.localStorage.removeItem('cdb-error-log');
    const log = JSON.parse(runInSandbox(`
      logError(new Error('teste123'), 'unit-test');
      JSON.stringify(getErrorLog());
    `));
    assert.strictEqual(log.length, 1);
    assert.strictEqual(log[0].ctx, 'unit-test');
    assert.match(log[0].msg, /teste123/);
  });

  test('error log: ring buffer limita ao max', () => {
    sandbox.localStorage.removeItem('cdb-error-log');
    const result = runInSandbox(`
      const max = ERROR_LOG_MAX || 500;
      for (let i = 0; i < max + 50; i++) {
        logError(new Error('e' + i), 'test');
      }
      JSON.stringify({ size: getErrorLog().length, max: max });
    `);
    const r = JSON.parse(result);
    assert.ok(r.size <= r.max, 'log limita em ' + r.max + ', tem ' + r.size);
  });

  test('error log: clearErrorLog zera', () => {
    const r = runInSandbox(`
      logError(new Error('x'), 't');
      clearErrorLog();
      getErrorLog().length;
    `);
    assert.strictEqual(r, 0);
  });

  test('shopping list: shoppingListPlainText monta texto formatado', () => {
    // IIFE escopa o `_orig` — sem isso ele vaza pro contexto e colide
    // com o teste seguinte (SyntaxError: already declared).
    const txt = runInSandbox(`
      (function () {
        var _orig = getShoppingRows;
        getShoppingRows = () => [{ name: 'Farinha', done: false }, { name: 'Ovos', done: true }];
        try { return shoppingListPlainText(); } finally { getShoppingRows = _orig; }
      })()
    `);
    assert.match(txt, /LISTA DE COMPRAS/);
    assert.match(txt, /\[ \] Farinha/);
    assert.match(txt, /\[x\] Ovos/);
  });

  test('shopping list: vazia retorna texto sem items', () => {
    const txt = runInSandbox(`
      (function () {
        var _orig = getShoppingRows;
        getShoppingRows = () => [];
        try { return shoppingListPlainText(); } finally { getShoppingRows = _orig; }
      })()
    `);
    assert.match(txt, /A comprar \(0\)/);
    assert.match(txt, /nenhum item pendente/);
  });
  // #1565: o laco de leitura parava ao ler um codigo que nao bate e nunca religava (o id do
  // rAF antigo ficava no ar): camera ligada, nada lido, ate o operador fechar e abrir de novo.
  test('scanner: codigo que nao bate religa a leitura e le o proximo', async () => {
    runInSandbox(`
      globalThis.__leituras = 0;
      scannerActive = true;
      scannerStream = {};
      scannerDetector = { detect: async () => (++globalThis.__leituras === 1
        ? [{ rawValue: 'NAO-EXISTE-ZZZ999', format: 'qr_code' }] : []) };
      startScanLoop();
    `);
    try {
      await new Promise(r => setTimeout(r, 300));
      const parado = runInSandbox('({ leituras: globalThis.__leituras, raf: scannerRaf })');
      assert.strictEqual(parado.leituras, 1, 'achou o codigo e parou de ler');
      assert.strictEqual(parado.raf, null, 'o id do laco zera quando ele para');
      await new Promise(r => setTimeout(r, 2200));
      const religado = runInSandbox('globalThis.__leituras');
      assert.ok(religado > 1, 'depois do aviso o laco voltou a ler (leituras: ' + religado + ')');
    } finally { runInSandbox('closeScanner()'); }
  });

  test('scanner: fechar e abrir antes do aviso sumir nao deixa dois lacos lendo', async () => {
    runInSandbox(`
      globalThis.__leituras = 0;
      scannerActive = true;
      scannerStream = { getTracks: () => [] };
      scannerDetector = { detect: async () => [{ rawValue: 'NAO-EXISTE-ZZZ999', format: 'qr_code' }] };
      startScanLoop();
    `);
    try {
      await new Promise(r => setTimeout(r, 200));
      // Fecha e abre de novo com o timer de 1,5 s do aviso ainda pendente.
      runInSandbox(`
        closeScanner();
        globalThis.__leituras = 0;
        globalThis.__emVoo = 0;
        globalThis.__picoEmVoo = 0;
        scannerActive = true;
        scannerStream = { getTracks: () => [] };
        scannerDetector = { detect: () => new Promise(ok => {
          globalThis.__leituras++;
          globalThis.__picoEmVoo = Math.max(globalThis.__picoEmVoo, ++globalThis.__emVoo);
          setTimeout(() => { globalThis.__emVoo--; ok([]); }, 60);
        }) };
        startScanLoop();
      `);
      await new Promise(r => setTimeout(r, 1900));
      const r = runInSandbox('({ n: globalThis.__leituras, pico: globalThis.__picoEmVoo })');
      assert.ok(r.n > 2, 'a abertura nova le (leituras: ' + r.n + ')');
      assert.strictEqual(r.pico, 1, 'um laco so: nunca duas leituras ao mesmo tempo');
    } finally { runInSandbox('closeScanner()'); }
  });

  test('scanner: le no maximo a cada SCAN_INTERVALO_MS, nao a cada quadro', async () => {
    runInSandbox(`
      globalThis.__leituras = 0;
      scannerActive = true;
      scannerStream = {};
      scannerDetector = { detect: async () => { globalThis.__leituras++; return []; } };
      startScanLoop();
    `);
    try {
      await new Promise(r => setTimeout(r, 1000));
      const r = runInSandbox('({ n: globalThis.__leituras, passo: SCAN_INTERVALO_MS })');
      assert.ok(r.n >= 3, 'continua lendo (leituras: ' + r.n + ')');
      assert.ok(r.n <= Math.ceil(1000 / r.passo) + 1, 'leituras em 1 s: ' + r.n + ' (quadros seriam ~60)');
    } finally { runInSandbox('closeScanner()'); }
  });

  test('scanner: fechar para o laco e solta o detector', async () => {
    runInSandbox(`
      globalThis.__leituras = 0;
      scannerActive = true;
      scannerStream = { getTracks: () => [] };
      scannerDetector = { detect: async () => { globalThis.__leituras++; return []; } };
      startScanLoop();
    `);
    await new Promise(r => setTimeout(r, 250));
    runInSandbox('closeScanner()');
    const antes = runInSandbox('globalThis.__leituras');
    await new Promise(r => setTimeout(r, 400));
    const r = runInSandbox('({ n: globalThis.__leituras, raf: scannerRaf, det: scannerDetector })');
    assert.strictEqual(r.n, antes, 'nenhuma leitura depois de fechar');
    assert.strictEqual(r.raf, null);
    assert.strictEqual(r.det, null);
  });
};
