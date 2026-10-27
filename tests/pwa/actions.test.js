// Testes unitarios — F2 Acoes Contextuais (getOrderPrimaryActions)
'use strict';

module.exports = function ({ test, runInSandbox, sandbox, assert }) {
  test('actions: aguardando -> primary "Iniciar preparo" via applyAdvanceTransition', () => {
    const r = JSON.parse(runInSandbox(`
      JSON.stringify(getOrderPrimaryActions({ id: 'o1', status: 'aguardando' }));
    `));
    assert.strictEqual(r.length, 1);
    assert.strictEqual(r[0].key, 'start');
    assert.strictEqual(r[0].primary, true);
    assert.match(r[0].label, /Iniciar/);
    assert.strictEqual(r[0].handler, 'applyAdvanceTransition');
    assert.deepStrictEqual(r[0].args, ['o1', 'preparando']);
  });

  test('actions: preparando -> primary "Marcar pronto"', () => {
    const r = JSON.parse(runInSandbox(`
      JSON.stringify(getOrderPrimaryActions({ id: 'o1', status: 'preparando' }));
    `));
    assert.strictEqual(r[0].key, 'ready');
    assert.deepStrictEqual(r[0].args, ['o1', 'pronto']);
  });

  test('actions: pronto sem conferencia -> primary "Conferir" + secondary "Preparar cobrança"', () => {
    const r = JSON.parse(runInSandbox(`
      JSON.stringify(getOrderPrimaryActions({
        id: 'o1', status: 'pronto', confirmedAt: null
      }));
    `));
    assert.strictEqual(r.length, 2);
    assert.strictEqual(r[0].key, 'confer');
    assert.strictEqual(r[0].primary, true);
    assert.strictEqual(r[0].handler, 'openConfer');
    assert.strictEqual(r[1].key, 'charge');
    assert.strictEqual(r[1].primary, false);
    assert.strictEqual(r[1].handler, 'openCobranca');
    assert.strictEqual(r[1].label, 'Preparar cobrança');
  });

  test('actions: pronto JA conferido -> primary "Marcar entregue"', () => {
    const r = JSON.parse(runInSandbox(`
      JSON.stringify(getOrderPrimaryActions({
        id: 'o1', status: 'pronto', confirmedAt: Date.now(), confirmedBy: 'Felipe'
      }));
    `));
    assert.strictEqual(r[0].key, 'deliver');
    assert.strictEqual(r[0].handler, 'applyAdvanceTransition');
    assert.deepStrictEqual(r[0].args, ['o1', 'entregue']);
  });

  test('actions: entregue -> primary "Reimprimir"', () => {
    const r = JSON.parse(runInSandbox(`
      JSON.stringify(getOrderPrimaryActions({ id: 'o1', status: 'entregue' }));
    `));
    assert.strictEqual(r[0].key, 'reprint');
    assert.strictEqual(r[0].handler, 'printEtiqueta');
  });

  test('actions: status desconhecido -> array vazio (fallback layout antigo)', () => {
    const r = JSON.parse(runInSandbox(`
      JSON.stringify(getOrderPrimaryActions({ id: 'o1', status: 'inexistente' }));
    `));
    assert.deepStrictEqual(r, []);
  });

  test('actions: input nulo -> array vazio (sem crash)', () => {
    const r1 = JSON.parse(runInSandbox(`JSON.stringify(getOrderPrimaryActions(null))`));
    const r2 = JSON.parse(runInSandbox(`JSON.stringify(getOrderPrimaryActions(undefined))`));
    const r3 = JSON.parse(runInSandbox(`JSON.stringify(getOrderPrimaryActions('texto'))`));
    assert.deepStrictEqual(r1, []);
    assert.deepStrictEqual(r2, []);
    assert.deepStrictEqual(r3, []);
  });

  test('actions: handlers referenciam funcoes existentes', () => {
    // Verifica que cada handler retornado existe como funcao no escopo
    const r = runInSandbox(`(function(){
      var statuses = ['aguardando','preparando','pronto','entregue'];
      var checks = {};
      statuses.forEach(s => {
        var acts = getOrderPrimaryActions({ id: 'x', status: s, confirmedAt: null });
        acts.forEach(a => { checks[a.handler] = (typeof globalThis[a.handler] === 'function'); });
      });
      return JSON.stringify(checks);
    })()`);
    const checks = JSON.parse(r);
    Object.entries(checks).forEach(([handler, exists]) => {
      assert.ok(exists, 'handler ' + handler + ' deve ser funcao no escopo');
    });
  });
  test('actions: cards de pedidos priorizam a etapa e mantêm cobrança fora de Mais ações', () => {
    for (const [status, next, expected] of [
      ['aguardando', 'Iniciar preparo', 'Iniciar preparo'],
      ['preparando', 'Marcar pronto', 'Marcar pronto'],
      ['pronto', 'Marcar entregue', 'Conferir entrega'],
    ]) {
      const html = runInSandbox(`renderKanbanCard({
        id: 'pedido-densidade', status: ${JSON.stringify(status)}, total: 34,
        items: [{ name: 'Massa', qty: 1 }], clientSnapshot: { name: 'Cliente' }, createdAt: Date.now()
      }, { key: ${JSON.stringify(status)}, label: 'Etapa', next: ${JSON.stringify(next)} });`);
      const details = html.match(/<details class="k-more-actions">([\s\S]*?)<\/details>/);
      assert.ok(details, 'ações secundárias ficam em disclosure nativo');
      const visible = html.slice(0, details.index);
      assert.match(visible, new RegExp(`onclick="advanceOrder\\('pedido-densidade'\\)">${expected}</button>`));
      assert.match(visible, /onclick="openCobranca\('pedido-densidade'\)"/);
      assert.match(visible, /Conf\. pendente/, 'conferência pendente continua visível sem abrir o menu');
      for (const handler of ['openConfer', 'printEtiqueta', 'openHistorico', 'cancelOrder']) {
        assert.ok(details[1].includes(`${handler}('pedido-densidade')`), handler + ' permanece disponível');
        assert.ok(!visible.includes(`${handler}('pedido-densidade')`), handler + ' não compete com a próxima etapa');
      }
      assert.match(details[1], />Cancelar pedido<\/button>/, 'cancelamento tem rótulo explícito');
      assert.strictEqual(details[1].includes("openCestaCalcFromOrder('pedido-densidade')"), status !== 'pronto');
    }
  });

  test('actions: pedido sem valor mantém Mais ações e não oferece cobrança', () => {
    const html = runInSandbox(`renderKanbanCard({
      id: 'pedido-zero', status: 'aguardando', total: 0, items: [],
      clientSnapshot: { name: 'Cliente' }, createdAt: Date.now()
    }, { key: 'aguardando', label: 'Aguardando', next: 'Iniciar preparo' });`);
    assert.ok(!html.includes('openCobranca('));
    assert.match(html, /<summary aria-label="Mais ações do pedido ido-zero">Mais ações<\/summary>/);
    assert.match(html, /onclick="advanceOrder\('pedido-zero'\)"/);
    assert.match(html, /onclick="cancelOrder\('pedido-zero'\)"/);
  });
};
