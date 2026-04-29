// Testes unitarios — F1 Centro de Atencao (buildAttentionItems)
// IIFEs sao necessarios pra usar `return` e isolar variaveis (`_orig` colide
// entre testes se vazar pro contexto compartilhado do sandbox).
'use strict';

module.exports = function ({ test, runInSandbox, assert }) {
  test('attention: sem pendencia retorna array vazio', () => {
    const r = runInSandbox(`(function(){
      products = []; orders = [];
      var _orig = getShoppingRows; getShoppingRows = () => [];
      try { return JSON.stringify(buildAttentionItems().map(i => i.kind)); }
      finally { getShoppingRows = _orig; }
    })()`);
    assert.deepStrictEqual(JSON.parse(r), []);
  });

  test('attention: estoque baixo (1-2) gera card warn', () => {
    const r = runInSandbox(`(function(){
      products = [
        { id: 'p1', name: 'Pao', archived: false, stock: 1 },
        { id: 'p2', name: 'Coxinha', archived: false, stock: 2 }
      ];
      orders = []; cashEntries = []; batches = [];
      var _orig = getShoppingRows; getShoppingRows = () => [];
      try { return JSON.stringify(buildAttentionItems()); }
      finally { getShoppingRows = _orig; }
    })()`);
    const items = JSON.parse(r);
    const stock = items.find(i => i.kind === 'stock');
    assert.ok(stock, 'deve ter card stock');
    assert.strictEqual(stock.severity, 'warn');
    assert.strictEqual(stock.count, 2);
  });

  test('attention: estoque baixo 3+ vira err', () => {
    const r = runInSandbox(`(function(){
      products = [
        { id: 'p1', name: 'A', archived: false, stock: 1 },
        { id: 'p2', name: 'B', archived: false, stock: 0 },
        { id: 'p3', name: 'C', archived: false, stock: 2 }
      ];
      orders = [];
      var _o = getShoppingRows; getShoppingRows = () => [];
      try { return JSON.stringify(buildAttentionItems()); }
      finally { getShoppingRows = _o; }
    })()`);
    const items = JSON.parse(r);
    const stock = items.find(i => i.kind === 'stock');
    assert.strictEqual(stock.severity, 'err');
    assert.strictEqual(stock.count, 3);
  });

  test('attention: estoque ignora produto archived', () => {
    const r = runInSandbox(`(function(){
      products = [
        { id: 'p1', name: 'Vivo', archived: false, stock: 1 },
        { id: 'p2', name: 'Morto', archived: true, stock: 0 }
      ];
      orders = [];
      var _o = getShoppingRows; getShoppingRows = () => [];
      try { return JSON.stringify(buildAttentionItems()); }
      finally { getShoppingRows = _o; }
    })()`);
    const stock = JSON.parse(r).find(i => i.kind === 'stock');
    assert.strictEqual(stock.count, 1, 'archived nao conta');
  });

  test('attention: pedido preparando ha >30min vira card stuck', () => {
    const r = runInSandbox(`(function(){
      products = [];
      orders = [{ id: 'o1', status: 'preparando', updatedAt: Date.now() - 31*60*1000 }];
      var _o = getShoppingRows; getShoppingRows = () => [];
      try { return JSON.stringify(buildAttentionItems()); }
      finally { getShoppingRows = _o; }
    })()`);
    const stuck = JSON.parse(r).find(i => i.kind === 'stuck');
    assert.ok(stuck, 'deve ter card stuck');
    assert.strictEqual(stuck.count, 1);
  });

  test('attention: pedido preparando ha 5min NAO vira stuck', () => {
    const r = runInSandbox(`(function(){
      products = [];
      orders = [{ id: 'o1', status: 'preparando', updatedAt: Date.now() - 5*60*1000 }];
      var _o = getShoppingRows; getShoppingRows = () => [];
      try { return JSON.stringify(buildAttentionItems()); }
      finally { getShoppingRows = _o; }
    })()`);
    const stuck = JSON.parse(r).find(i => i.kind === 'stuck');
    assert.ok(!stuck, 'pedido recente nao deve gerar stuck');
  });

  test('attention: shopping pendente vira card info', () => {
    const r = runInSandbox(`(function(){
      products = []; orders = [];
      var _o = getShoppingRows;
      getShoppingRows = () => [{ name: 'Farinha', done: false }, { name: 'Ovos', done: true }];
      try { return JSON.stringify(buildAttentionItems()); }
      finally { getShoppingRows = _o; }
    })()`);
    const sh = JSON.parse(r).find(i => i.kind === 'shopping');
    assert.ok(sh, 'deve ter card shopping');
    assert.strictEqual(sh.severity, 'info');
    assert.strictEqual(sh.count, 1, 'so 1 pendente (Ovos esta done)');
  });

  test('attention: ordena por severidade err > warn > info', () => {
    const r = runInSandbox(`(function(){
      products = [
        { id: 'p1', name: 'A', archived: false, stock: 1 },
        { id: 'p2', name: 'B', archived: false, stock: 0 },
        { id: 'p3', name: 'C', archived: false, stock: 2 }
      ];
      orders = [];
      var _o = getShoppingRows;
      getShoppingRows = () => [{ name: 'X', done: false }];
      try { return JSON.stringify(buildAttentionItems().map(i => i.severity)); }
      finally { getShoppingRows = _o; }
    })()`);
    const sevs = JSON.parse(r);
    assert.strictEqual(sevs[0], 'err', 'err vem primeiro');
    assert.ok(sevs[sevs.length - 1] === 'info', 'info vem por ultimo');
  });

  test('attention: cap em 5 cards', () => {
    const r = runInSandbox(`(function(){
      products = Array.from({length:10}, (_,i) => ({ id: 'p'+i, name: 'P'+i, archived: false, stock: 0 }));
      orders = [
        { id: 'o1', status: 'pronto', updatedAt: Date.now() },
        { id: 'o2', status: 'preparando', updatedAt: Date.now() - 60*60*1000 }
      ];
      var _o = getShoppingRows;
      getShoppingRows = () => [{ name: 'X', done: false }];
      try { return buildAttentionItems().length; }
      finally { getShoppingRows = _o; }
    })()`);
    assert.ok(r <= 5, 'limite de 5 respeitado, tem ' + r);
  });
};
