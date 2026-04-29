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
    assert.strictEqual(runInSandbox(`prodScanModeLabel('qr')`), 'QR Code');
    assert.strictEqual(runInSandbox(`prodScanModeLabel('barcode')`), 'Codigo de barras');
    assert.strictEqual(runInSandbox(`prodScanModeLabel('both')`), 'QR + barras');
    assert.strictEqual(runInSandbox(`prodScanModeLabel('xyz')`), 'QR + barras');
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
    assert.strictEqual(ets[0].lote, 'L-2026-001');
    assert.strictEqual(ets[0].idxStr, '001');
    assert.strictEqual(ets[2].idxStr, '003');
    assert.match(ets[0].codigoBarras, /-001$/);
  });
};
