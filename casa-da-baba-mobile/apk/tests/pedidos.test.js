// Testes unitarios — modulo PEDIDOS
'use strict';

module.exports = function ({ test, runInSandbox, sandbox, assert }) {
  test('pedidos: STATUS contem todos os estados esperados', () => {
    const s = JSON.parse(runInSandbox(`JSON.stringify(STATUS)`));
    ['AGUARDANDO', 'PREPARANDO', 'PRONTO', 'ENTREGUE', 'CANCELADO', 'CRIADO'].forEach(k => {
      assert.ok(s[k], 'STATUS.' + k + ' existe');
    });
  });

  test('pedidos: getOrderById retorna pedido existente', () => {
    const r = runInSandbox(`
      orders = [{ id: 'order-abc', total: 100 }, { id: 'order-def', total: 50 }];
      JSON.stringify(getOrderById('order-abc'));
    `);
    assert.strictEqual(JSON.parse(r).total, 100);
  });

  test('pedidos: getOrderById retorna falsy pra id inexistente', () => {
    const r = runInSandbox(`orders = []; getOrderById('inexistente') == null ? 'null' : 'found'`);
    assert.strictEqual(r, 'null');
  });

  test('pedidos: openOrders filtra status nao final', () => {
    const r = runInSandbox(`
      orders = [
        { id: 'o1', status: STATUS.AGUARDANDO },
        { id: 'o2', status: STATUS.PREPARANDO },
        { id: 'o3', status: STATUS.PRONTO },
        { id: 'o4', status: STATUS.ENTREGUE },
        { id: 'o5', status: STATUS.CANCELADO }
      ];
      JSON.stringify(openOrders().map(o => o.id).sort());
    `);
    assert.deepStrictEqual(JSON.parse(r), ['o1', 'o2', 'o3']);
  });

  test('pedidos: orderLabelHtml gera HTML com TOTAL e itens', () => {
    sandbox.localStorage.setItem('cdb-empresa-name', 'Casa da Baba');
    const html = runInSandbox(`
      orders = [];
      const o = {
        id: 'order-12345678', total: 150, createdAt: Date.now(), factAt: Date.now(),
        clientSnapshot: { name: 'Maria' },
        items: [
          { qty: 2, name: 'Pao', unitPrice: 50, unit: '500g' },
          { qty: 1, name: 'Coxinha', unitPrice: 50 }
        ]
      };
      orderLabelHtml(o);
    `);
    assert.match(html, /pa-receipt/);
    assert.match(html, /Maria/);
    assert.match(html, /Pao/);
    assert.match(html, /Coxinha/);
    assert.match(html, /TOTAL/);
    assert.match(html, /12345678/);
  });

  test('pedidos: orderLabelHtml respeita scan mode (qr only)', () => {
    sandbox.localStorage.setItem('cdb-prod-scan-mode', 'qr');
    const html = runInSandbox(`
      orderLabelHtml({ id: 'order-1', total: 0, createdAt: Date.now(), items: [], clientSnapshot: { name: 'X' } });
    `);
    assert.match(html, /pa-confer-qr/);
    assert.ok(!/pa-confer-bars/.test(html), 'sem barcode quando modo qr');
  });

  test('pedidos: orderLabelHtml respeita scan mode (barcode only)', () => {
    sandbox.localStorage.setItem('cdb-prod-scan-mode', 'barcode');
    const html = runInSandbox(`
      orderLabelHtml({ id: 'order-1', total: 0, createdAt: Date.now(), items: [], clientSnapshot: { name: 'X' } });
    `);
    assert.match(html, /pa-confer-bars/);
    assert.ok(!/pa-confer-qr/.test(html), 'sem QR quando modo barcode');
  });
};
