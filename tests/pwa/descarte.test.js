// Testes — #1466: o saldo local do produto acompanha descarte e exclusao de lote.
// Regra: o lote conta no saldo enquanto nao esta excluido nem descartado.
'use strict';

module.exports = function ({ test, runInSandbox, assert }) {
  const setup = `
    products = [{ id: 'p1', name: 'Lasanha', stock: 10, archived: false }];
    batches = [{ id: 'b-1', lote: 'LOT-261008', createdAt: Date.now(),
      items: [{ productId: 'p1', name: 'Lasanha', qty: 4, unit: 'Un' }] }];
  `;

  test('descarte: descartar lote baixa o saldo local e desfazer devolve', () => {
    const r = runInSandbox(`(function () {
      ${setup}
      _discardingBatchId = 'b-1';
      confirmBatchDiscard();
      const depois = products[0].stock;
      revertBatchDiscard('b-1');
      return JSON.stringify([depois, products[0].stock]);
    })()`);
    assert.deepStrictEqual(JSON.parse(r), [6, 10]);
  });

  test('descarte: excluir lote ja descartado nao baixa de novo; restaurar mantem descartado fora', () => {
    const r = runInSandbox(`(function () {
      ${setup}
      _discardingBatchId = 'b-1';
      confirmBatchDiscard();
      deleteBatch('b-1');
      const aposExcluir = products[0].stock;
      _silentRestoreBatch('b-1');
      return JSON.stringify([aposExcluir, products[0].stock]);
    })()`);
    assert.deepStrictEqual(JSON.parse(r), [6, 6]);
  });

  test('descarte: saldo nunca fica negativo', () => {
    const r = runInSandbox(`(function () {
      ${setup}
      products[0].stock = 1;
      _discardingBatchId = 'b-1';
      confirmBatchDiscard();
      return products[0].stock;
    })()`);
    assert.strictEqual(r, 0);
  });

  test('descarte: desfazer descarte antigo (antes do #1466) nao infla o saldo', () => {
    const r = runInSandbox(`(function () {
      ${setup}
      batches[0].discarded = true;
      batches[0].discardedAt = Date.UTC(2026, 8, 1);
      revertBatchDiscard('b-1');
      return products[0].stock;
    })()`);
    assert.strictEqual(r, 10);
  });
};
