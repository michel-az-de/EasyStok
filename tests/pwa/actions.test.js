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

  test('actions: pronto sem conferencia -> primary "Conferir" + secondary "Cobrar"', () => {
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
};
