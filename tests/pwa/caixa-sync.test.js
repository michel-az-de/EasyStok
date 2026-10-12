// Testes — #1520 (ADR-0060) edicao e exclusao de lancamento de caixa chegam ao servidor
//
// A edicao ja saia do aparelho como cashEntry.upsert (o servidor e que ignorava).
// A exclusao nao saia: o lancamento sumia do array e o diff so olha o que existe.
// Agora a exclusao e uma mutation explicita ("cashEntry.delete"), nascida da acao
// do operador. Nunca por ausencia: purgeOldData e clearTestData tiram lancamentos
// do aparelho e isso nao pode virar exclusao no servidor.
'use strict';

module.exports = function ({ test, sandbox, assert, runInSandbox }) {
  const QUEUE_KEY = 'cdb-sync-queue';
  const PENDENTES_KEY = 'cdb-cash-exclusoes-pendentes';

  function fila() {
    return JSON.parse(sandbox.localStorage.getItem(QUEUE_KEY) || '[]');
  }
  function exclusoes() {
    return fila().filter(m => m.type === 'cashEntry.delete');
  }
  function pendentes() {
    return JSON.parse(sandbox.localStorage.getItem(PENDENTES_KEY) || '{}');
  }
  function limpar() {
    sandbox.window.cdbSync.clearQueue();
    sandbox.localStorage.removeItem(PENDENTES_KEY);
  }
  function lancamento(id, extra) {
    return Object.assign({ id, type: 'expense', amount: 30, description: 'Gas', metodo: 'dinheiro', createdAt: Date.now() }, extra || {});
  }
  function estado(cashEntries) {
    return { products: [], clients: [], orders: [], cashEntries, batches: [] };
  }

  test('#1520: editar lancamento manda cashEntry.upsert com os valores novos', () => {
    limpar();
    sandbox.window.cdbOnPersist(estado([lancamento('cash-ed-1')]));
    sandbox.window.cdbSync.clearQueue();

    sandbox.window.cdbOnPersist(estado([lancamento('cash-ed-1', { type: 'income', amount: 45.5, description: 'Troco', metodo: 'pix' })]));

    const muts = fila().filter(m => m.type === 'cashEntry.upsert' && m.payload.id === 'cash-ed-1');
    assert.strictEqual(muts.length, 1);
    assert.strictEqual(muts[0].payload.amount, 45.5);
    assert.strictEqual(muts[0].payload.type, 'income');
    assert.strictEqual(muts[0].payload.metodo, 'pix');
    limpar();
  });

  test('#1520: lancamento que some do aparelho sem acao do operador nao vira exclusao', () => {
    limpar();
    sandbox.window.cdbOnPersist(estado([lancamento('cash-purge-1'), lancamento('cash-purge-2')]));
    sandbox.window.cdbSync.clearQueue();

    sandbox.window.cdbOnPersist(estado([]));   // purgeOldData / clearTestData

    assert.strictEqual(exclusoes().length, 0, 'ausencia nunca e exclusao');
    limpar();
  });

  test('#1520: exclusao so entra na fila quando o "Desfazer" expira', () => {
    limpar();
    const sync = sandbox.window.cdbSync;
    const e = lancamento('cash-del-1');

    sync.agendarExclusaoLancamento(e);
    assert.strictEqual(exclusoes().length, 0, 'ainda pode ser desfeita');
    assert.ok(pendentes()['cash-del-1'], 'pendencia gravada: sobrevive ao app fechar');

    sync.confirmarExclusaoLancamento('cash-del-1');

    const muts = exclusoes();
    assert.strictEqual(muts.length, 1);
    assert.strictEqual(muts[0].payload.id, 'cash-del-1');
    assert.strictEqual(muts[0].payload.description, 'Gas', 'lancamento inteiro: servidor antigo nao quebra com payload so de id');
    assert.strictEqual(pendentes()['cash-del-1'], undefined);

    sync.confirmarExclusaoLancamento('cash-del-1');
    assert.strictEqual(exclusoes().length, 1, 'confirmar duas vezes nao duplica');
    limpar();
  });

  test('#1520: desfazer cancela a exclusao e nada sobe', () => {
    limpar();
    const sync = sandbox.window.cdbSync;
    sync.agendarExclusaoLancamento(lancamento('cash-del-2'));

    sync.cancelarExclusaoLancamento('cash-del-2');
    sync.confirmarExclusaoLancamento('cash-del-2');

    assert.strictEqual(exclusoes().length, 0);
    assert.strictEqual(pendentes()['cash-del-2'], undefined);
    limpar();
  });

  test('#1520: app fechado durante o "Desfazer": a exclusao sobe no proximo boot', () => {
    limpar();
    const sync = sandbox.window.cdbSync;
    sync.agendarExclusaoLancamento(lancamento('cash-del-3'));

    sync._internal.confirmarExclusoesPendentes();   // o boot do sync.js chama isto

    assert.deepStrictEqual(exclusoes().map(m => m.payload.id), ['cash-del-3']);
    assert.deepStrictEqual(pendentes(), {});
    limpar();
  });

  test('#1520: deleteCashEntry agenda a exclusao; Desfazer devolve o lancamento e cancela', () => {
    limpar();
    runInSandbox(`cashEntries = [{ id: 'cash-ui-1', type: 'expense', amount: 30, description: 'Gas', metodo: 'dinheiro', createdAt: Date.now() }];`);

    runInSandbox(`deleteCashEntry('cash-ui-1')`);
    assert.strictEqual(runInSandbox(`cashEntries.length`), 0);
    assert.ok(pendentes()['cash-ui-1'], 'exclusao agendada na acao do operador');
    assert.strictEqual(exclusoes().length, 0);

    runInSandbox(`window._handleUndoClick()`);
    assert.strictEqual(runInSandbox(`cashEntries.length`), 1, 'lancamento restaurado');
    assert.strictEqual(pendentes()['cash-ui-1'], undefined, 'pendencia cancelada');
    assert.strictEqual(exclusoes().length, 0);
    limpar();
  });

  test('#1520: deleteCashEntry sem Desfazer manda cashEntry.delete', () => {
    limpar();
    runInSandbox(`cashEntries = [{ id: 'cash-ui-2', type: 'expense', amount: 30, description: 'Gas', metodo: 'dinheiro', createdAt: Date.now() }];`);

    runInSandbox(`deleteCashEntry('cash-ui-2')`);
    // O toast confirma sozinho apos 5 s; aqui o commit e disparado como o timer faria.
    runInSandbox(`(function(){ var c = window._pendingCommit; window._pendingCommit = null; c(); })()`);

    assert.deepStrictEqual(exclusoes().map(m => m.payload.id), ['cash-ui-2']);
    assert.strictEqual(pendentes()['cash-ui-2'], undefined);
    limpar();
  });
};
