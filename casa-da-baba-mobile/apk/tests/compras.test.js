// Testes unitarios — helper de Compras (adicionarFaltantesNaCompra)
// Cobre o fecho do ciclo calculadora/cesta -> lista de Compras (#411 + #412).
'use strict';

module.exports = function ({ test, runInSandbox, assert }) {
  test('compras: adicionarFaltantesNaCompra formata decimal + unidade', () => {
    const r = runInSandbox(`
      shoppingItems = [];
      JSON.stringify({
        added: adicionarFaltantesNaCompra([{ insumoNome: 'Farinha', falta: 2.5, unidadeReceita: 'kg' }]),
        names: shoppingItems.map(function (s) { return s.name; })
      });
    `);
    const out = JSON.parse(r);
    assert.strictEqual(out.added, 1);
    // toFixed(2): falta nao-inteira sai com 2 casas (comportamento do #411).
    assert.deepStrictEqual(out.names, ['Farinha (faltam 2,50 kg)']);
  });

  test('compras: adicionarFaltantesNaCompra nao poe decimal em falta inteira', () => {
    const r = runInSandbox(`
      shoppingItems = [];
      adicionarFaltantesNaCompra([{ insumoNome: 'Ovo', falta: 12, unidadeReceita: 'un' }]);
      shoppingItems[0].name;
    `);
    assert.strictEqual(r, 'Ovo (faltam 12 un)');
  });

  test('compras: adicionarFaltantesNaCompra deduplica item nao-comprado', () => {
    const r = runInSandbox(`
      shoppingItems = [];
      var f = [{ insumoNome: 'Acucar', falta: 1, unidadeReceita: 'kg' }];
      JSON.stringify({ a1: adicionarFaltantesNaCompra(f), a2: adicionarFaltantesNaCompra(f), count: shoppingItems.length });
    `);
    const out = JSON.parse(r);
    assert.strictEqual(out.a1, 1, 'primeira adiciona');
    assert.strictEqual(out.a2, 0, 'segunda deduplica');
    assert.strictEqual(out.count, 1, 'so 1 item na lista');
  });

  test('compras: adicionarFaltantesNaCompra retorna 0 pra lista vazia', () => {
    const r = runInSandbox(`
      shoppingItems = [];
      JSON.stringify({ added: adicionarFaltantesNaCompra([]), count: shoppingItems.length });
    `);
    const out = JSON.parse(r);
    assert.strictEqual(out.added, 0);
    assert.strictEqual(out.count, 0);
  });
};
