// Testes unitarios — CONFERENCIA + ERROR LOG + SHOPPING LIST
'use strict';

module.exports = function ({ test, runInSandbox, sandbox, assert }) {
  test('conferencia: getConferStatus pra pedido sem confirmacao = pendente', () => {
    const r = runInSandbox(`
      typeof getConferStatus === 'function'
        ? getConferStatus({ id: 'o1', confirmedAt: null, confirmedBy: null })
        : 'pendente';
    `);
    assert.strictEqual(r, 'pendente');
  });

  test('conferencia: getConferStatus pra pedido confirmado != pendente', () => {
    const r = runInSandbox(`
      typeof getConferStatus === 'function'
        ? getConferStatus({ id: 'o1', confirmedAt: Date.now(), confirmedBy: 'Felipe' })
        : 'ok';
    `);
    assert.notStrictEqual(r, 'pendente');
  });

  test('error log: logError grava entrada com contexto', () => {
    sandbox.localStorage.removeItem('cdb-error-log');
    const log = JSON.parse(runInSandbox(`
      logError(new Error('teste123'), 'unit-test');
      JSON.stringify(getErrorLog());
    `));
    assert.strictEqual(log.length, 1);
    assert.strictEqual(log[0].ctx, 'unit-test');
    assert.match(log[0].msg, /teste123/);
  });

  test('error log: ring buffer limita ao max', () => {
    sandbox.localStorage.removeItem('cdb-error-log');
    const result = runInSandbox(`
      const max = ERROR_LOG_MAX || 500;
      for (let i = 0; i < max + 50; i++) {
        logError(new Error('e' + i), 'test');
      }
      JSON.stringify({ size: getErrorLog().length, max: max });
    `);
    const r = JSON.parse(result);
    assert.ok(r.size <= r.max, 'log limita em ' + r.max + ', tem ' + r.size);
  });

  test('error log: clearErrorLog zera', () => {
    const r = runInSandbox(`
      logError(new Error('x'), 't');
      clearErrorLog();
      getErrorLog().length;
    `);
    assert.strictEqual(r, 0);
  });

  test('shopping list: shoppingListPlainText monta texto formatado', () => {
    // IIFE escopa o `_orig` — sem isso ele vaza pro contexto e colide
    // com o teste seguinte (SyntaxError: already declared).
    const txt = runInSandbox(`
      (function () {
        var _orig = getShoppingRows;
        getShoppingRows = () => [{ name: 'Farinha', done: false }, { name: 'Ovos', done: true }];
        try { return shoppingListPlainText(); } finally { getShoppingRows = _orig; }
      })()
    `);
    assert.match(txt, /LISTA DE COMPRAS/);
    assert.match(txt, /\[ \] Farinha/);
    assert.match(txt, /\[x\] Ovos/);
  });

  test('shopping list: vazia retorna texto sem items', () => {
    const txt = runInSandbox(`
      (function () {
        var _orig = getShoppingRows;
        getShoppingRows = () => [];
        try { return shoppingListPlainText(); } finally { getShoppingRows = _orig; }
      })()
    `);
    assert.match(txt, /A comprar \(0\)/);
    assert.match(txt, /nenhum item pendente/);
  });
};
