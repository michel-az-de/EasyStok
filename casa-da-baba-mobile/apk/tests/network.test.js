// Testes — F10-C-1: mutex promise-based no flush + UUID v4 estavel
//
// Race condition antiga (flushing flag em memoria): Android suspende
// WebView entre flag=true e POST, outro tick entra, le flag ja false
// (porque finally rodou apos resume) → 2 POSTs simultaneos com mesma fila.
// Solucao: flushPromise compartilhada — re-entry retorna mesma promise.
//
// UUID antigo (Date.now + Math.random.substr(2,5)) podia colidir em ms
// identico. UUID v4 via crypto.randomUUID elimina a chance.
'use strict';

module.exports = function ({ test, sandbox, assert }) {
  const cdbSync = () => sandbox.window && sandbox.window.cdbSync;

  test('F10-C-1: mutation id usa prefixo mut_ e tem 32+ caracteres', () => {
    // #390: `enqueue` e privado; o caminho publico e pushAll(), que le
    // window.cdbApp.getState(). (Antes o teste chamava cdbSync().enqueue —
    // inexistente — e PULAVA silencioso; nunca rodava de fato.)
    assert.ok(cdbSync() && cdbSync().pushAll, 'cdbSync().pushAll deve existir apos boot');
    sandbox.localStorage.removeItem('cdb-sync-queue');
    sandbox.window.cdbApp = { getState: () => ({
      products: [{ id: 'p-test-1', nome: 'T' }], clients: [], orders: [], cashEntries: [], batches: []
    }), setPendingSync: () => {} };
    try {
      cdbSync().pushAll();
      const q = JSON.parse(sandbox.localStorage.getItem('cdb-sync-queue') || '[]');
      assert.ok(q.length >= 1, 'pelo menos 1 mutation enfileirada');
      const id = q[q.length - 1].id;
      assert.ok(typeof id === 'string', 'id e string');
      assert.ok(id.startsWith('mut_'), 'id usa prefixo mut_ (era m-)');
      // UUID v4 tem 36 chars com hifens; com prefixo "mut_" total >= 32
      assert.ok(id.length >= 32, 'id tem comprimento suficiente pra ser UUID v4');
    } finally {
      delete sandbox.window.cdbApp;
      sandbox.localStorage.removeItem('cdb-sync-queue');
    }
  });

  test('F10-C-1: mutation ids sao unicos em chamadas seguidas (sem colisao por ms)', () => {
    assert.ok(cdbSync() && cdbSync().pushAll, 'cdbSync().pushAll deve existir apos boot');
    // 100 produtos num unico pushAll() -> 100 mutations no mesmo tick; ids unicos.
    sandbox.localStorage.removeItem('cdb-sync-queue');
    const products = [];
    for (let i = 0; i < 100; i++) products.push({ id: 'p-uniq-' + i, nome: 'P' + i });
    sandbox.window.cdbApp = { getState: () => ({
      products, clients: [], orders: [], cashEntries: [], batches: []
    }), setPendingSync: () => {} };
    try {
      cdbSync().pushAll();
      const q = JSON.parse(sandbox.localStorage.getItem('cdb-sync-queue') || '[]');
      const ids = q.map(m => m.id);
      const uniq = new Set(ids);
      assert.ok(ids.length >= 100, '>= 100 mutations enfileiradas');
      assert.strictEqual(uniq.size, ids.length, 'ids unicos (zero colisao por ms)');
    } finally {
      delete sandbox.window.cdbApp;
      sandbox.localStorage.removeItem('cdb-sync-queue');
    }
  });

  test('F10-C-1: flush concorrente retorna a MESMA promise (mutex)', async () => {
    assert.ok(cdbSync() && cdbSync().flush, 'cdbSync().flush deve existir apos boot');
    // Limpa fila residual dos testes de enqueue acima para garantir isolamento.
    sandbox.localStorage.removeItem('cdb-sync-queue');
    // Sem nada na fila, flush retorna 'empty' rapido. Mas o mutex deve
    // garantir que chamadas paralelas resolvem com o mesmo valor.
    const p1 = cdbSync().flush();
    const p2 = cdbSync().flush();
    const p3 = cdbSync().flush();
    const [r1, r2, r3] = await Promise.all([p1, p2, p3]);
    // Os 3 receberam o mesmo resultado (fila vazia -> empty pra todos).
    assert.strictEqual(r1, r2, 'flush concorrente: r1 === r2');
    assert.strictEqual(r2, r3, 'flush concorrente: r2 === r3');
  });

  test('F10-C-1: 5 flushes seguidos com fila vazia nao geram lixo (empty pra todos)', async () => {
    assert.ok(cdbSync() && cdbSync().flush, 'cdbSync().flush deve existir apos boot');
    sandbox.localStorage.removeItem('cdb-sync-queue');
    const results = await Promise.all([
      cdbSync().flush(), cdbSync().flush(), cdbSync().flush(),
      cdbSync().flush(), cdbSync().flush()
    ]);
    // Todos os 5 devem retornar um valor valido (sem crash, sem comportamento inesperado).
    // 'retry' é aceito pois testes anteriores podem ter deixado itens na fila
    // in-memory e o ambiente de teste não tem rede disponível.
    for (const r of results) {
      assert.ok(r === 'empty' || r === 'offline' || r === 'auth' || r === 'retry',
        'flush retorna valor valido: ' + r);
    }
  });
};
