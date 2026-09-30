// Testes unitarios — modulo CAIXA
// Roda dentro do mesmo vm context do PWA pra acessar variaveis
// declaradas com let/const (orders, cashEntries, STATUS) que nao
// ficam em globalThis no sandbox.
'use strict';

module.exports = function ({ test, runInSandbox, assert }) {
  test('caixa: dateKeyOf retorna YYYY-MM-DD pra data fixa', () => {
    const k = runInSandbox(`dateKeyOf(new Date('2026-04-15T13:00:00').getTime())`);
    assert.strictEqual(k, '2026-04-15');
  });

  test('caixa: dateKeyOf sem argumento usa hoje', () => {
    const k = runInSandbox(`dateKeyOf()`);
    assert.match(k, /^\d{4}-\d{2}-\d{2}$/);
  });

  test('caixa: calcCashSummary com pedidos entregues e lancamentos', () => {
    const result = runInSandbox(`(function(){
      var ts = Date.now();
      orders = [
        { id: 'o1', status: STATUS.ENTREGUE, updatedAt: ts, total: 100 },
        { id: 'o2', status: STATUS.ENTREGUE, updatedAt: ts, total: 50 },
        { id: 'o3', status: STATUS.PREPARANDO, updatedAt: ts, total: 999 }
      ];
      cashEntries = [
        { type: 'income',  amount: 30, createdAt: ts },
        { type: 'expense', amount: 20, createdAt: ts }
      ];
      return JSON.stringify(calcCashSummary(dateKeyOf()));
    })()`);
    const sum = JSON.parse(result);
    assert.strictEqual(sum.sold, 150, 'soma de pedidos entregues');
    assert.strictEqual(sum.in, 30,    'entradas extras');
    assert.strictEqual(sum.out, 20,   'saidas');
    assert.strictEqual(sum.balance, 160, 'saldo');
    assert.strictEqual(sum.ordersCount, 2);
    assert.strictEqual(sum.entriesCount, 2);
  });

  test('caixa: calcCashSummary ignora pedidos de outro dia', () => {
    const sum = JSON.parse(runInSandbox(`
      const ontem = Date.now() - 86400000 - 3600000;
      orders = [{ id: 'o1', status: STATUS.ENTREGUE, updatedAt: ontem, total: 100 }];
      cashEntries = [];
      JSON.stringify(calcCashSummary(dateKeyOf()));
    `));
    assert.strictEqual(sum.sold, 0);
    assert.strictEqual(sum.balance, 0);
  });

  test('caixa: getActiveCashClosing retorna null quando nao fechado', () => {
    const r = runInSandbox(`cashClosings = []; getActiveCashClosing()`);
    assert.strictEqual(r, null);
  });

  test('caixa: getActiveCashClosing retorna closing ativo do dia', () => {
    const r = runInSandbox(`
      const today = dateKeyOf();
      cashClosings = [{ id: 'c1', dateKey: today, closedAt: Date.now() }];
      JSON.stringify(getActiveCashClosing());
    `);
    const closing = JSON.parse(r);
    assert.strictEqual(closing.id, 'c1');
  });

  test('caixa: closing reaberto NAO conta como ativo', () => {
    const r = runInSandbox(`
      cashClosings = [{ id: 'c1', dateKey: dateKeyOf(), closedAt: Date.now(), reopenedAt: Date.now() }];
      getActiveCashClosing();
    `);
    assert.strictEqual(r, null);
  });

  test('caixa: cashClosingPlainText monta texto formatado', () => {
    // IIFE pra escopar `t` — sem isso colide com o `const t` do teste anterior.
    const txt = runInSandbox(`(function(){
      var ts = Date.now();
      orders = [{ id: 'oabc1234', status: STATUS.ENTREGUE, updatedAt: ts, total: 50, clientSnapshot: { name: 'Joao' } }];
      cashEntries = [{ type: 'income', amount: 10, description: 'Troco', createdAt: ts }];
      cashClosings = [];
      return cashClosingPlainText(dateKeyOf());
    })()`);
    assert.match(txt, /FECHAMENTO DE CAIXA/);
    assert.match(txt, /Vendas \(1\)/);
    assert.match(txt, /Joao/);
    assert.match(txt, /Troco/);
  });

  // #416 — mascara de moeda "de centavos" no lancamento de caixa.
  test('caixa: maskMoneyInput formata centavos da direita pra esquerda', () => {
    assert.strictEqual(runInSandbox(`(function(){ var el={value:'4500'}; maskMoneyInput(el); return el.value; })()`), '45,00');
    assert.strictEqual(runInSandbox(`(function(){ var el={value:'4'}; maskMoneyInput(el); return el.value; })()`), '0,04');
    assert.strictEqual(runInSandbox(`(function(){ var el={value:'45'}; maskMoneyInput(el); return el.value; })()`), '0,45');
  });

  test('caixa: maskMoneyInput poe separador de milhar', () => {
    assert.strictEqual(runInSandbox(`(function(){ var el={value:'450000'}; maskMoneyInput(el); return el.value; })()`), '4.500,00');
    assert.strictEqual(runInSandbox(`(function(){ var el={value:'123456789'}; maskMoneyInput(el); return el.value; })()`), '1.234.567,89');
  });

  test('caixa: maskMoneyInput vazio fica vazio e ignora nao-digitos', () => {
    assert.strictEqual(runInSandbox(`(function(){ var el={value:''}; maskMoneyInput(el); return el.value; })()`), '');
    assert.strictEqual(runInSandbox(`(function(){ var el={value:'R$ 4.5a0,0'}; maskMoneyInput(el); return el.value; })()`), '45,00');
  });

  test('caixa: parseMoneyInput le de volta o valor mascarado (compat)', () => {
    assert.strictEqual(runInSandbox(`parseMoneyInput('45,00')`), 45);
    assert.strictEqual(runInSandbox(`parseMoneyInput('4.500,00')`), 4500);
  });

  // #421 — margem real por dia: calcCashSummary retorna cogs + margem + semCusto.
  test('caixa: calcCashSummary calcula cogs e margem com custo cadastrado', () => {
    const r = runInSandbox(`(function(){
      var ts = Date.now();
      products = [{ id: 'p1', name: 'Coxinha', cost: 3 }];
      orders = [{ id: 'o1', status: STATUS.ENTREGUE, updatedAt: ts, total: 20,
        items: [{ productId: 'p1', qty: 2, name: 'Coxinha', unitPrice: 10 }] }];
      cashEntries = [{ type: 'expense', amount: 5, createdAt: ts }];
      return JSON.stringify(calcCashSummary(dateKeyOf()));
    })()`);
    const s = JSON.parse(r);
    assert.strictEqual(s.sold, 20, 'vendas');
    assert.strictEqual(s.cogs, 6,  'custo: 2 * 3');
    assert.strictEqual(s.out, 5,   'saidas');
    assert.strictEqual(s.margem, 9, 'margem: 20 - 6 - 5');
    assert.strictEqual(s.semCusto, 0, 'todos com custo');
  });

  // #423 — summary bar: produto mais pedido hoje.
  test('caixa: produto mais pedido agrega por nome dos pedidos entregues hoje', () => {
    const r = runInSandbox(`(function(){
      var ts = Date.now();
      orders = [
        { id: 'o1', status: 'entregue', updatedAt: ts, total: 10,
          items: [{ name: 'Coxinha', qty: 3, unitPrice: 5, productId: 'p1' }] },
        { id: 'o2', status: 'entregue', updatedAt: ts, total: 10,
          items: [{ name: 'Coxinha', qty: 2, unitPrice: 5, productId: 'p1' },
                  { name: 'Refri',   qty: 5, unitPrice: 3, productId: 'p2' }] },
        { id: 'o3', status: 'aguardando', updatedAt: ts, total: 10,
          items: [{ name: 'Refri', qty: 99, unitPrice: 3, productId: 'p2' }] }
      ];
      var freq = {};
      orders.filter(function(o){ return o.status === 'entregue' && isToday(o.updatedAt); })
        .forEach(function(o){ (o.items||[]).forEach(function(it){ var k=it.name||'Item'; freq[k]=(freq[k]||0)+(Number(it.qty)||1); }); });
      return Object.keys(freq).sort(function(a,b){ return freq[b]-freq[a]; })[0];
    })()`);
    assert.strictEqual(r, 'Coxinha', 'Coxinha top (3+2=5 vs Refri 5 — mesmo total, Coxinha vem primeiro por sort estavel ou alfabetica)');
  });

  // #425 — getCriticalProducts usa minStock se definido.
  test('caixa: getCriticalProducts usa minStock do produto se definido', () => {
    const r = runInSandbox(`(function(){
      products = [
        { id: 'p1', name: 'Farinha', stock: 3, archived: false },             // padrao <=2: NAO critico
        { id: 'p2', name: 'Ovo',     stock: 3, archived: false, minStock: 5 }, // minStock=5: critico (3<=5)
        { id: 'p3', name: 'Sal',     stock: 0, archived: false }               // padrao <=2: critico
      ];
      return JSON.stringify(getCriticalProducts().map(function(p){ return p.name; }));
    })()`);
    const names = JSON.parse(r);
    assert.ok(!names.includes('Farinha'), 'Farinha nao critica (stock=3, padrao<=2)');
    assert.ok(names.includes('Ovo'),     'Ovo critico (stock=3 <= minStock=5)');
    assert.ok(names.includes('Sal'),     'Sal critico (stock=0 <= padrao 2)');
  });

  test('caixa: calcCashSummary marca semCusto quando produto sem custo', () => {
    const r = runInSandbox(`(function(){
      var ts = Date.now();
      products = [{ id: 'p1', name: 'Coxinha' }]; // sem cost
      orders = [{ id: 'o1', status: STATUS.ENTREGUE, updatedAt: ts, total: 20,
        items: [{ productId: 'p1', qty: 2, name: 'Coxinha', unitPrice: 10 }] }];
      cashEntries = [];
      return JSON.stringify(calcCashSummary(dateKeyOf()));
    })()`);
    const s = JSON.parse(r);
    assert.strictEqual(s.cogs, 0,    'cogs zero (sem custo)');
    assert.strictEqual(s.semCusto, 1, '1 linha de item sem custo (semCusto conta linhas, nao unidades)');
    assert.strictEqual(s.margem, 20 - 0 - 0, 'margem so com o que se tem');
  });
};
