// Testes unitarios — modulo PRODUCAO
'use strict';

module.exports = function ({ test, runInSandbox, sandbox, assert }) {
  test('producao: getProdScanMode default = both', () => {
    sandbox.localStorage.removeItem('cdb-prod-scan-mode');
    const r = runInSandbox(`getProdScanMode()`);
    assert.strictEqual(r, 'both');
  });

  test('producao: getProdScanMode retorna valor valido', () => {
    sandbox.localStorage.setItem('cdb-prod-scan-mode', 'qr');
    assert.strictEqual(runInSandbox(`getProdScanMode()`), 'qr');
    sandbox.localStorage.setItem('cdb-prod-scan-mode', 'barcode');
    assert.strictEqual(runInSandbox(`getProdScanMode()`), 'barcode');
  });

  test('producao: getProdScanMode normaliza valor invalido pra both', () => {
    sandbox.localStorage.setItem('cdb-prod-scan-mode', 'lixo');
    assert.strictEqual(runInSandbox(`getProdScanMode()`), 'both');
  });

  test('producao: scanModeFlags reflete cada modo', () => {
    const both = JSON.parse(runInSandbox(`JSON.stringify(scanModeFlags('both'))`));
    assert.deepStrictEqual({ qr: both.showQr, bars: both.showBars }, { qr: true, bars: true });
    const qr = JSON.parse(runInSandbox(`JSON.stringify(scanModeFlags('qr'))`));
    assert.deepStrictEqual({ qr: qr.showQr, bars: qr.showBars }, { qr: true, bars: false });
    const bc = JSON.parse(runInSandbox(`JSON.stringify(scanModeFlags('barcode'))`));
    assert.deepStrictEqual({ qr: bc.showQr, bars: bc.showBars }, { qr: false, bars: true });
  });

  test('producao: code128Modules gera string com digitos pra dado simples', () => {
    const m = runInSandbox(`code128Modules('ABC')`);
    assert.ok(typeof m === 'string', 'retorna string');
    assert.ok(m.length > 0, 'nao vazio');
    assert.match(m, /^[1-7]+$/, 'so digitos 1-7');
  });

  test('producao: prodScanModeLabel rotula corretamente', () => {
    assert.strictEqual(runInSandbox(`prodScanModeLabel('qr')`), 'Só QR');
    assert.strictEqual(runInSandbox(`prodScanModeLabel('barcode')`), 'Só barras');
    assert.strictEqual(runInSandbox(`prodScanModeLabel('both')`), 'QR + barras');
    assert.strictEqual(runInSandbox(`prodScanModeLabel('xyz')`), 'QR + barras');
  });

  // A4 — switchLoja deve drenar a fila antes de trocar pra evitar mutations
  // da loja antiga subirem APOS o switch atribuidas a loja nova (drama
  // silencioso cross-loja). Testes validam o contrato de entrada.
  test('A4: switchLoja rejeita lojaId vazio', async () => {
    const cdbSync = sandbox.window && sandbox.window.cdbSync;
    assert.ok(cdbSync && cdbSync.switchLoja, 'cdbSync.switchLoja deve existir apos boot');
    // Pairing valido + online pra chegar no check de lojaId (ordem: rede,
    // pareado, lojaId, fila, fetch).
    sandbox.localStorage.setItem('cdb-pairing', JSON.stringify({ apiKey: 'mk_test' }));
    sandbox.navigator.onLine = true;
    try {
      await assert.rejects(cdbSync.switchLoja(null), /lojaId/);
      await assert.rejects(cdbSync.switchLoja(''), /lojaId/);
      await assert.rejects(cdbSync.switchLoja(undefined), /lojaId/);
    } finally {
      sandbox.localStorage.removeItem('cdb-pairing');
    }
  });

  test('A4: switchLoja rejeita quando sem rede', async () => {
    const cdbSync = sandbox.window && sandbox.window.cdbSync;
    assert.ok(cdbSync && cdbSync.switchLoja, 'cdbSync.switchLoja deve existir apos boot');
    const origOnLine = sandbox.navigator.onLine;
    sandbox.navigator.onLine = false;
    try {
      await assert.rejects(cdbSync.switchLoja('lj-x'), /sem rede/);
    } finally {
      sandbox.navigator.onLine = origOnLine;
    }
  });

  test('A4: switchLoja rejeita quando nao pareado', async () => {
    const cdbSync = sandbox.window && sandbox.window.cdbSync;
    assert.ok(cdbSync && cdbSync.switchLoja, 'cdbSync.switchLoja deve existir apos boot');
    sandbox.localStorage.removeItem('cdb-pairing');
    // navigator.onLine padrao do sandbox = true; entao chega no check de pairing.
    await assert.rejects(cdbSync.switchLoja('lj-x'), /pareado/);
  });

  test('producao: etiquetasFromBatch expande N etiquetas (1 por unidade)', () => {
    const r = runInSandbox(`
      products = [{ id: 'p1', name: 'Pao', emoji: '🍞', archived: false }];
      const batch = {
        id: 'b1', lote: 'L-2026-001', createdAt: Date.now(),
        items: [{ productId: 'p1', name: 'Pao', emoji: '🍞', qty: 3, unit: '500g', expiresAt: null }]
      };
      JSON.stringify(etiquetasFromBatch(batch));
    `);
    const ets = JSON.parse(r);
    assert.strictEqual(ets.length, 3, '3 etiquetas pra qty=3');
    assert.strictEqual(ets[0].productName, 'Pao');
    // #1471: a etiqueta mostra o codigo do lote igual ao ERP (lote do dia + sufixo do batch).
    assert.strictEqual(ets[0].lote, 'L-2026-001-b1');
    assert.strictEqual(ets[0].idxStr, '001');
    assert.strictEqual(ets[2].idxStr, '003');
    assert.match(ets[0].codigoBarras, /-001$/);
  });

  // #1471 — mesma regra do BatchLinker: base + '-' + ultimos 6 do id, so letras e digitos.
  test('producao: codigoDoLote bate com o codigo do Lote no ERP', () => {
    const r = runInSandbox(`
      JSON.stringify([
        codigoDoLote({ id: 'b-1728412345678', lote: 'LOT-261008' }),
        codigoDoLote({ id: 'b-12', code: 'BATCH-X' }),
        codigoDoLote({ id: 'ab-c-d', lote: 'LOT-261008' })
      ]);
    `);
    assert.deepStrictEqual(JSON.parse(r), ['LOT-261008-345678', 'BATCH-X-b12', 'LOT-261008-abcd']);
  });

  test('producao: dois lotes do mesmo produto no mesmo dia nao colidem na etiqueta', () => {
    const r = runInSandbox(`
      products = [{ id: 'p1', name: 'Lasanha', sku: 'LASA', archived: false }];
      const item = { productId: 'p1', name: 'Lasanha', qty: 1, unit: 'Un', expiresAt: null };
      const a = etiquetasFromBatch({ id: 'b-1728400000001', lote: 'LOT-261008', createdAt: Date.now(), items: [item] });
      const b = etiquetasFromBatch({ id: 'b-1728400000002', lote: 'LOT-261008', createdAt: Date.now(), items: [item] });
      JSON.stringify([a[0].codigoBarras, b[0].codigoBarras]);
    `);
    const [a, b] = JSON.parse(r);
    assert.notStrictEqual(a, b, 'cada lote tem codigo proprio');
    assert.strictEqual(a, 'LASA-LOT-261008-000001-001');
    assert.ok(a.length <= 32, 'cabe no CODE128 ESC/POS (max 32)');
  });

  test('producao: findUnitByCode acha etiqueta nova e etiqueta antiga ja impressa', () => {
    const r = runInSandbox(`
      products = [{ id: 'p1', name: 'Lasanha', sku: 'LASA', archived: false }];
      batches = [{ id: 'b-1728400000001', lote: 'LOT-261008', createdAt: Date.now(),
        items: [{ productId: 'p1', name: 'Lasanha', qty: 2, unit: 'Un', expiresAt: null }] }];
      const nova = findUnitByCode('LASA-LOT-261008-000001-002');
      const antiga = findUnitByCode('lasa-lot-261008-002');
      JSON.stringify([nova && nova.idxStr, antiga && antiga.idxStr]);
    `);
    assert.deepStrictEqual(JSON.parse(r), ['002', '002']);
  });

  // #412 — demanda agrega so pedidos abertos (aguardando+preparando), soma qty
  // por produto e preserva unit (usado pra alimentar a cesta /calcular-cesta).
  test('producao: aggregateProductionDemand soma qty por produto e preserva unit', () => {
    const r = runInSandbox(`
      orders = [
        { status: 'aguardando', items: [{ productId: 'p1', name: 'Lasanha', emoji: '🍝', unit: 'Un', qty: 2 }] },
        { status: 'preparando', items: [{ productId: 'p1', name: 'Lasanha', emoji: '🍝', unit: 'Un', qty: 3 }] },
        { status: 'entregue',   items: [{ productId: 'p1', name: 'Lasanha', unit: 'Un', qty: 99 }] }
      ];
      JSON.stringify(aggregateProductionDemand());
    `);
    const out = JSON.parse(r);
    assert.strictEqual(out.pedidos, 2, 'so aguardando+preparando');
    assert.strictEqual(out.items.length, 1, 'agrega por productId');
    assert.strictEqual(out.items[0].qty, 5, 'soma 2+3 (ignora entregue)');
    assert.strictEqual(out.items[0].unit, 'Un', 'preserva unit pra cesta');
  });
};
