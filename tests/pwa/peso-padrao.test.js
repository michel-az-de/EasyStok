// Testes — #1474 peso por unidade pre-preenchido no "Registrar produção"
'use strict';

module.exports = function ({ test, runInSandbox, assert }) {
  const peso = (p) => runInSandbox(`pesoPadraoDoProduto(${JSON.stringify(p)})`);

  test('#1474 peso: usa o peso padrao do cadastro quando existe', () => {
    assert.strictEqual(peso({ name: 'Talharim Fresco', unit: '300g', defaultWeightG: 280 }), 280);
  });

  test('#1474 peso: sem padrao, usa o peso declarado na unidade (300g, 1,5 kg)', () => {
    assert.strictEqual(peso({ name: 'Talharim Fresco', unit: '300g' }), 300);
    assert.strictEqual(peso({ name: 'Lasanha', unit: '1,5 kg' }), 1500);
  });

  test('#1474 peso: volume e unidade generica nao viram peso', () => {
    assert.strictEqual(peso({ name: 'Molho', unit: '500ml' }), null);
    assert.strictEqual(peso({ name: 'Kit', unit: 'un' }), null);
    assert.strictEqual(peso({ name: 'Sem unidade' }), null);
    assert.strictEqual(peso({ name: 'Zero', unit: '300g', defaultWeightG: 0 }), 300);
  });

  test('#1474 lote: codigo mostrado ao abrir o modal e o mesmo do batch gravado', () => {
    const r = JSON.parse(runInSandbox(`(function(){
      products = [{ id: 'talharim', name: 'Talharim Fresco', emoji: '', category: 'massa', unit: '300g',
                    price: 12, stock: 0, count: 2, tipoEmbalagem: 'Avulso' }];
      batches = [];
      openProductionConfirm();
      const previsto = codigoDoLote({ id: currentBatchId, lote: getLoteDoDia(new Date()) });
      confirmProduction();
      const b = batches[batches.length - 1];
      return JSON.stringify({ previsto, gravado: b && codigoDoLote(b), resto: currentBatchId });
    })()`));
    assert.match(r.gravado, /^LOT-\d{6}-\d{6}$/, 'codigo com sufixo do id');
    assert.strictEqual(r.previsto, r.gravado, 'modal mostra o codigo final');
    assert.strictEqual(r.resto, '', 'id consumido, nao reaproveita');
  });
};
