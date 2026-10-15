// Testes unitarios — F1 Centro de Atencao (buildAttentionItems)
// IIFEs sao necessarios pra usar `return` e isolar variaveis (`_orig` colide
// entre testes se vazar pro contexto compartilhado do sandbox).
'use strict';

module.exports = function ({ test, runInSandbox, assert }) {
  // Helper inline pra zerar todas as fontes do attention
  const RESET = `
    products = []; orders = []; batches = []; cashEntries = []; cashClosings = [];
    var _origGS = getShoppingRows; getShoppingRows = () => [];
    var _origPD = (typeof getOpenPriorDays === 'function') ? getOpenPriorDays : null;
    var _origEL = (typeof getErrorLog === 'function') ? getErrorLog : null;
    if (_origPD) getOpenPriorDays = () => [];
    if (_origEL) getErrorLog = () => [];
  `;
  const RESTORE = `
    getShoppingRows = _origGS;
    if (_origPD) getOpenPriorDays = _origPD;
    if (_origEL) getErrorLog = _origEL;
  `;

  test('attention: sem pendencia retorna array vazio', () => {
    const r = runInSandbox(`(function(){
      ${RESET}
      try { return JSON.stringify(buildAttentionItems().map(i => i.kind)); }
      finally { ${RESTORE} }
    })()`);
    assert.deepStrictEqual(JSON.parse(r), []);
  });

  test('attention: estoque baixo (1-2) gera card warn com nomes detalhados', () => {
    const r = runInSandbox(`(function(){
      ${RESET}
      products = [
        { id: 'p1', name: 'Pao', archived: false, stock: 1 },
        { id: 'p2', name: 'Coxinha', archived: false, stock: 2 }
      ];
      try { return JSON.stringify(buildAttentionItems()); }
      finally { ${RESTORE} }
    })()`);
    const items = JSON.parse(r);
    const stock = items.find(i => i.kind === 'stock');
    assert.ok(stock, 'deve ter card stock');
    assert.strictEqual(stock.severity, 'warn');
    assert.strictEqual(stock.count, 2);
    assert.match(stock.reason, /Pao \(1\)/, 'mostra nome+estoque');
    assert.match(stock.reason, /Coxinha \(2\)/);
  });

  test('attention: estoque baixo 3+ vira err', () => {
    const r = runInSandbox(`(function(){
      ${RESET}
      products = [
        { id: 'p1', name: 'A', archived: false, stock: 1 },
        { id: 'p2', name: 'B', archived: false, stock: 0 },
        { id: 'p3', name: 'C', archived: false, stock: 2 }
      ];
      try { return JSON.stringify(buildAttentionItems()); }
      finally { ${RESTORE} }
    })()`);
    const items = JSON.parse(r);
    const stock = items.find(i => i.kind === 'stock');
    assert.strictEqual(stock.severity, 'err');
    assert.strictEqual(stock.count, 3);
  });

  test('attention: estoque mostra menor stock primeiro', () => {
    const r = runInSandbox(`(function(){
      ${RESET}
      products = [
        { id: 'p1', name: 'TemDois', archived: false, stock: 2 },
        { id: 'p2', name: 'Zerou', archived: false, stock: 0 },
        { id: 'p3', name: 'TemUm', archived: false, stock: 1 }
      ];
      try { return JSON.stringify(buildAttentionItems()); }
      finally { ${RESTORE} }
    })()`);
    const stock = JSON.parse(r).find(i => i.kind === 'stock');
    // 'Zerou' deve aparecer primeiro (stock=0)
    const idxZerou = stock.reason.indexOf('Zerou');
    const idxDois  = stock.reason.indexOf('TemDois');
    assert.ok(idxZerou >= 0 && idxDois >= 0, 'ambos presentes');
    assert.ok(idxZerou < idxDois, 'menor stock vem primeiro');
  });

  test('attention: caixa de dia anterior aberto vira err', () => {
    const r = runInSandbox(`(function(){
      ${RESET}
      // mocka prior days
      getOpenPriorDays = () => ['2026-04-28'];
      try { return JSON.stringify(buildAttentionItems()); }
      finally { ${RESTORE} }
    })()`);
    const cash = JSON.parse(r).find(i => i.kind === 'cash-prior');
    assert.ok(cash, 'card cash-prior presente');
    assert.strictEqual(cash.severity, 'err');
    assert.strictEqual(cash.count, 1);
  });

  test('attention: validade proxima (<7d) vira card warn', () => {
    const r = runInSandbox(`(function(){
      ${RESET}
      const d3 = Date.now() + 3*24*3600*1000;
      const d10 = Date.now() + 10*24*3600*1000;
      batches = [{
        id: 'b1', lote: 'L1', deleted: false,
        items: [
          { name: 'Lasanha', expiresAt: d3 },
          { name: 'Coxinha', expiresAt: d10 }
        ]
      }];
      try { return JSON.stringify(buildAttentionItems()); }
      finally { ${RESTORE} }
    })()`);
    const exp = JSON.parse(r).find(i => i.kind === 'expiring');
    assert.ok(exp, 'card expiring presente');
    assert.strictEqual(exp.count, 1, 'so o de 3d, nao o de 10d');
    assert.match(exp.reason, /Lasanha/);
  });

  test('attention: pedido preparando >30min vira card stuck com NOME e TEMPO', () => {
    const r = runInSandbox(`(function(){
      ${RESET}
      orders = [{ id: 'o1', status: 'preparando', updatedAt: Date.now() - 90*60*1000, clientSnapshot: { name: 'Maria' } }];
      try { return JSON.stringify(buildAttentionItems()); }
      finally { ${RESTORE} }
    })()`);
    const stuck = JSON.parse(r).find(i => i.kind === 'stuck');
    assert.ok(stuck, 'deve ter card stuck');
    assert.match(stuck.reason, /Maria/, 'mostra nome do cliente');
    assert.match(stuck.reason, /1h/, 'mostra tempo decorrido');
  });

  test('attention: A receber soma valor de pedidos prontos hoje', () => {
    const r = runInSandbox(`(function(){
      ${RESET}
      const todayMs = (function(){ const d=new Date(); d.setHours(0,0,0,0); return d.getTime()+3600000; })();
      orders = [
        { id: 'o1', status: 'pronto', createdAt: todayMs, total: 100 },
        { id: 'o2', status: 'pronto', createdAt: todayMs, total: 50 }
      ];
      try { return JSON.stringify(buildAttentionItems()); }
      finally { ${RESTORE} }
    })()`);
    const rec = JSON.parse(r).find(i => i.kind === 'receivable');
    assert.ok(rec, 'card receivable presente');
    assert.strictEqual(rec.count, 2);
    assert.match(rec.reason, /150/, 'soma os valores');
  });

  test('attention: erros recentes (24h) viram card', () => {
    const r = runInSandbox(`(function(){
      ${RESET}
      // Mock 3 erros nas ultimas 24h
      getErrorLog = () => [
        { t: Date.now() - 1000, ctx: 'a', msg: 'x' },
        { t: Date.now() - 2*3600*1000, ctx: 'b', msg: 'y' },
        { t: Date.now() - 30*3600*1000, ctx: 'velho', msg: 'z' }
      ];
      try { return JSON.stringify(buildAttentionItems()); }
      finally { ${RESTORE} }
    })()`);
    const err = JSON.parse(r).find(i => i.kind === 'errors');
    assert.ok(err, 'card errors presente');
    assert.strictEqual(err.count, 2, 'so os <24h');
  });

  test('attention: shopping pendente vira card info', () => {
    const r = runInSandbox(`(function(){
      ${RESET}
      getShoppingRows = () => [{ name: 'Farinha', done: false }, { name: 'Ovos', done: true }];
      try { return JSON.stringify(buildAttentionItems()); }
      finally { ${RESTORE} }
    })()`);
    const sh = JSON.parse(r).find(i => i.kind === 'shopping');
    assert.ok(sh, 'deve ter card shopping');
    assert.strictEqual(sh.severity, 'info');
    assert.strictEqual(sh.count, 1);
  });

  test('attention: ordena por severidade err > warn > info', () => {
    const r = runInSandbox(`(function(){
      ${RESET}
      products = [
        { id: 'p1', name: 'A', archived: false, stock: 1 },
        { id: 'p2', name: 'B', archived: false, stock: 0 },
        { id: 'p3', name: 'C', archived: false, stock: 2 }
      ];
      getShoppingRows = () => [{ name: 'X', done: false }];
      try { return JSON.stringify(buildAttentionItems().map(i => i.severity)); }
      finally { ${RESTORE} }
    })()`);
    const sevs = JSON.parse(r);
    assert.strictEqual(sevs[0], 'err', 'err vem primeiro');
    assert.strictEqual(sevs[sevs.length - 1], 'info', 'info vem por ultimo');
  });

  test('attention: cap em 5 cards', () => {
    const r = runInSandbox(`(function(){
      ${RESET}
      // Cenario com mais de 5 categorias possiveis
      getOpenPriorDays = () => ['2026-04-28'];
      products = Array.from({length:5}, (_,i) => ({ id: 'p'+i, name: 'P'+i, archived: false, stock: 0 }));
      orders = [
        { id: 'o1', status: 'pronto', updatedAt: Date.now(), createdAt: Date.now(), total: 50 },
        { id: 'o2', status: 'preparando', updatedAt: Date.now() - 60*60*1000, clientSnapshot: { name: 'X' } }
      ];
      const d = Date.now() + 2*24*3600*1000;
      batches = [{ id: 'b1', deleted: false, items: [{ name: 'X', expiresAt: d }] }];
      getShoppingRows = () => [{ name: 'X', done: false }];
      getErrorLog = () => [{ t: Date.now(), ctx:'x', msg:'y' }];
      try { return buildAttentionItems().length; }
      finally { ${RESTORE} }
    })()`);
    assert.ok(r <= 5, 'limite de 5 respeitado, tem ' + r);
  });
};
