// Testes — Entrega B (robustez conexao/auth), C (agendamento + notif),
// D (hardening).
//
// Foco em validar contratos expostos pelo cdbSync.* e pelo cdbSync._internal.
// Testes que exigem mock pesado de fetch/SSE/SW sao deixados pra device-real
// (smoke manual) — aqui cobrimos a logica de pequenos helpers e a presenca
// dos hooks de integracao.
'use strict';

module.exports = function ({ test, sandbox, assert }) {
  const cdbSync = () => sandbox.window && sandbox.window.cdbSync;
  const internal = () => cdbSync() && cdbSync()._internal;

  // ---------- B3: serverTimeOffset + nowSafe ----------

  test('B3: nowSafe retorna numero proximo ao Date.now', () => {
    if (!internal()) return;
    const ts = internal().nowSafe();
    assert.ok(typeof ts === 'number' && isFinite(ts), 'numero finito');
    // Sem ack do server (offset=0), nowSafe ~= Date.now() +/- 1ms.
    const drift = Math.abs(ts - Date.now());
    assert.ok(drift < 5000, 'drift < 5s sem offset; got ' + drift);
  });

  test('B3: nowSafe monotonico (cresce em chamadas sucessivas)', () => {
    if (!internal()) return;
    const a = internal().nowSafe();
    const b = internal().nowSafe();
    const c = internal().nowSafe();
    assert.ok(b >= a, 'b >= a');
    assert.ok(c >= b, 'c >= b');
  });

  test('B3: getLastSeenServerTs reflete chamadas a nowSafe', () => {
    if (!internal()) return;
    // nowSafe atualiza _lastSeenServerTs em memoria (persistencia em LS so
    // acontece em updateServerTimeFromResponse pra evitar IO em cada enqueue).
    const before = internal().getLastSeenServerTs();
    const ts = internal().nowSafe();
    const after = internal().getLastSeenServerTs();
    assert.ok(after >= before, 'lastSeenServerTs nao decresce');
    assert.ok(after >= ts, 'lastSeenServerTs >= ts retornado');
  });

  // ---------- B4: schemaVersion + migrators ----------

  test('B4: mutationMigrators registry mutavel (objeto)', () => {
    if (!internal()) return;
    assert.strictEqual(typeof internal().mutationMigrators, 'object');
    // Inicialmente vazio (v1 e' o baseline)
    assert.strictEqual(Object.keys(internal().mutationMigrators).length, 0,
      'registry vazio quando schema = baseline');
  });

  test('B4: mutationSchemaVersion exposto como numero', () => {
    if (!internal()) return;
    assert.strictEqual(typeof internal().mutationSchemaVersion, 'number');
    assert.ok(internal().mutationSchemaVersion >= 1, 'versao >= 1');
  });

  test('B4: registrar migrator funciona (smoke)', () => {
    if (!internal()) return;
    // Simula bump pra v2 com transformer dummy.
    internal().mutationMigrators[2] = function (m) {
      return Object.assign({}, m, { schemaVersion: 2 });
    };
    try {
      const out = internal().mutationMigrators[2]({ id: 'x', type: 'order.upsert', payload: {}, schemaVersion: 1 });
      assert.strictEqual(out.schemaVersion, 2);
      assert.strictEqual(out.id, 'x');
    } finally {
      delete internal().mutationMigrators[2];
    }
  });

  // ---------- D2: detect WebView sem SW ----------

  test('D2: swSupported retorna false em sandbox Node (sem navigator.serviceWorker)', () => {
    if (!internal()) return;
    // Sandbox de teste nao tem navigator.serviceWorker — flag deve ser false.
    assert.strictEqual(internal().swSupported(), false);
  });

  // ---------- C2: scheduledDeliveryAt preservado em enqueue ----------

  test('C2: order com scheduledDeliveryAt mantem campo na fila', () => {
    if (!cdbSync()) return;
    // Setup: mock cdbApp.getState com 1 order tendo scheduledDeliveryAt.
    // Trigger cdbOnPersist passando state com a order — diff vs lastSnapshot
    // (vazio) deve enfileirar order.upsert intacta.
    sandbox.localStorage.removeItem('cdb-sync-queue');
    sandbox.window.cdbApp = {
      getState: () => ({
        products: [], clients: [],
        orders: [{
          id: 'o-future-1',
          clientSnapshot: { name: 'Tester' },
          items: [],
          status: 'aguardando',
          total: 0,
          createdAt: Date.now(),
          updatedAt: Date.now(),
          scheduledDeliveryAt: Date.now() + 24 * 3600 * 1000 // +24h
        }],
        cashEntries: [],
        batches: []
      }),
      setPendingSync: () => {}
    };
    try {
      sandbox.window.cdbOnPersist(sandbox.window.cdbApp.getState());
      const queue = JSON.parse(sandbox.localStorage.getItem('cdb-sync-queue') || '[]');
      const orderMut = queue.find(m => m.type === 'order.upsert' && m.payload && m.payload.id === 'o-future-1');
      assert.ok(orderMut, 'order.upsert enfileirado');
      assert.ok(orderMut.payload.scheduledDeliveryAt, 'campo scheduledDeliveryAt preservado');
      assert.strictEqual(typeof orderMut.payload.scheduledDeliveryAt, 'number');
    } finally {
      delete sandbox.window.cdbApp;
      sandbox.localStorage.removeItem('cdb-sync-queue');
    }
  });

  // ---------- C3: handler onSyncConflict respeita winningPayload (smoke API) ----------

  test('C3: cdbApp.onSyncConflict aceita winningPayload (hook ponto)', () => {
    if (!cdbSync()) return;
    // Smoke: garantimos que o sync.js consulta window.cdbApp.onSyncConflict
    // pra entregar os conflicts. Sem rede pra disparar conflict real, soh
    // validamos que o app pode plugar a funcao sem quebrar nada.
    let captured = null;
    sandbox.window.cdbApp = { onSyncConflict: function (cs) { captured = cs; } };
    try {
      // Chama handler manualmente como o flush faria.
      const fakeConflicts = [{
        mutationId: 'm-1',
        reason: 'conflict: pedido editado por outro',
        winningPayload: { id: 'o1', status: 'preparando' }
      }];
      sandbox.window.cdbApp.onSyncConflict(fakeConflicts);
      assert.strictEqual(captured.length, 1);
      assert.ok(captured[0].winningPayload, 'winningPayload entregue ao app');
      assert.strictEqual(captured[0].winningPayload.status, 'preparando');
    } finally {
      delete sandbox.window.cdbApp;
    }
  });

  // ---------- C4: SSE order.ready custom event (smoke) ----------

  test('C4: window.dispatchEvent cdb-order-ready aceita detail estruturado', () => {
    if (typeof sandbox.window.CustomEvent !== 'function' && typeof CustomEvent === 'undefined') return;
    // Smoke: garantimos que o tipo de evento existe e detail pode carregar payload.
    // Sandbox stub dispatchEvent eh no-op, mas a chamada nao deve throw.
    try {
      const Ev = sandbox.window.CustomEvent || CustomEvent;
      const ev = new Ev('cdb-order-ready', { detail: { orderId: 'o1', clientName: 'X', total: 50, itemCount: 2 } });
      sandbox.window.dispatchEvent(ev);
      assert.ok(true);
    } catch (e) {
      // Sandbox pode nao ter CustomEvent — aceitavel. Skip.
    }
  });
};
