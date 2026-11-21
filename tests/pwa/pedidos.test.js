// Testes unitarios — modulo PEDIDOS
'use strict';

module.exports = function ({ test, runInSandbox, sandbox, assert }) {
  function control(id, dataset = {}) {
    const classes = new Set(), attributes = {};
    return {
      id, dataset, value: '', textContent: '',
      classList: {
        toggle(name, active) { if (active) classes.add(name); else classes.delete(name); },
        contains(name) { return classes.has(name); }
      },
      setAttribute(name, value) { attributes[name] = String(value); },
      getAttribute(name) { return attributes[name] ?? null; }
    };
  }

  for (const [name, prefix, action, icon] of [
    ['retroativo', 'retro', 'setRetroActive', '🕐'],
    ['agendamento', 'schedule', 'setAgendaActive', '📅']
  ]) {
    test(`pedidos: ${name} anuncia estado e preserva um unico icone`, () => {
      const nodes = Object.fromEntries(['Fields', 'Toggle', 'ToggleLabel', 'Datetime']
        .map(suffix => [prefix + suffix, control(prefix + suffix)]));
      const originalGet = sandbox.document.getElementById;
      const originalUpdate = sandbox.updateSaveOrderBtn;
      sandbox.document.getElementById = id => nodes[id] || originalGet(id);
      sandbox.updateSaveOrderBtn = () => {};
      try {
        for (const active of [true, false]) {
          runInSandbox(`${action}(${active})`);
          assert.strictEqual(nodes[prefix + 'Toggle'].getAttribute('aria-pressed'), String(active));
          assert.strictEqual(nodes[prefix + 'Fields'].classList.contains('show'), active);
          assert.strictEqual(nodes[prefix + 'Toggle'].classList.contains('active'), active);
          const renderedText = icon + ' ' + nodes[prefix + 'ToggleLabel'].textContent;
          assert.strictEqual(renderedText.split(icon).length - 1, 1, 'icone estatico nao deve ser repetido pelo texto dinamico');
          assert.ok(nodes[prefix + 'ToggleLabel'].textContent.length > 0);
          assert.strictEqual(Boolean(nodes[prefix + 'Datetime'].value), active);
        }
      } finally {
        sandbox.document.getElementById = originalGet;
        sandbox.updateSaveOrderBtn = originalUpdate;
      }
    });
  }

  for (const [group, key, action, values] of [
    ['finFilter', 'f', 'setFinFilter', ['hoje', '7d', '30d', 'all']],
    ['finStatus', 's', 'setFinStatus', ['todos', 'entregue', 'cancelado']]
  ]) {
    test(`pedidos: ${group} anuncia o mesmo filtro destacado visualmente`, () => {
      const buttons = values.map(value => control(value, { [key]: value }));
      const originalQuery = sandbox.document.querySelectorAll;
      const originalRender = sandbox.renderFinalizadosScreen;
      sandbox.document.querySelectorAll = selector => selector === `#${group} button` ? buttons : originalQuery(selector);
      sandbox.renderFinalizadosScreen = () => {};
      try {
        for (const value of [...values, values[0]]) {
          runInSandbox(`${action}('${value}')`);
          for (const button of buttons) {
            assert.strictEqual(button.classList.contains('active'), button.dataset[key] === value);
            assert.strictEqual(button.getAttribute('aria-pressed'), String(button.dataset[key] === value));
          }
        }
      } finally {
        sandbox.document.querySelectorAll = originalQuery;
        sandbox.renderFinalizadosScreen = originalRender;
      }
    });
  }

  test('pedidos: STATUS contem todos os estados esperados', () => {
    const s = JSON.parse(runInSandbox(`JSON.stringify(STATUS)`));
    ['AGUARDANDO', 'PREPARANDO', 'PRONTO', 'ENTREGUE', 'CANCELADO', 'CRIADO'].forEach(k => {
      assert.ok(s[k], 'STATUS.' + k + ' existe');
    });
  });

  // #417 — observacao por item deve aparecer no render de impressao do pedido.
  test('pedidos: orderLabelHtml mostra obs por item', () => {
    const html = runInSandbox(`(function(){
      try { localStorage.setItem('cdb-prod-scan-mode', 'qr'); } catch(_) {}
      var o = { id: 'oABC1234', total: 20, factAt: 0, createdAt: 0, clientSnapshot: { name: 'Ana' },
        items: [{ qty: 2, name: 'Coxinha', unitPrice: 5, note: 'sem cebola' },
                { qty: 1, name: 'Refri', unitPrice: 10 }] };
      return orderLabelHtml(o, false, null);
    })()`);
    assert.match(html, /obs: sem cebola/, 'obs do item aparece no HTML');
    assert.ok(html.indexOf('Coxinha') >= 0 && html.indexOf('Refri') >= 0, 'ambos os itens presentes');
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
