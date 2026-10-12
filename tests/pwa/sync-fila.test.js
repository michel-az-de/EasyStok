// Testes — #1509 fila de sync do PWA
//
// 1) Mutation rejeitada por validacao ficava na fila para sempre: o servidor ja
//    gravou a recusa (dedup por MutationId) e responde a mesma coisa a cada 30s.
//    Agora vai para a dead-letter, sai da fila e o operador e avisado.
// 2) Conflito saia da fila sem deixar copia: agora fica na fila de conflitos.
// 3) Boot lia a fila so do IndexedDB e o proximo save sobrescrevia o
//    localStorage (gravacao sincrona); venda que nao chegou ao IDB sumia.
'use strict';

module.exports = function ({ test, sandbox, assert }) {
  const QUEUE_KEY = 'cdb-sync-queue';

  function fakeResp(status, body) {
    return {
      ok: status >= 200 && status < 300,
      status,
      headers: { get() { return null; } },
      json: async () => body,
      text: async () => JSON.stringify(body)
    };
  }

  function mut(id, type) {
    return { id, deviceId: 'd', type: type || 'order.upsert', payload: { id: 'o-' + id }, ts: 1 };
  }

  // queue-store falso (IDB) que registra dead-letter e conflitos.
  function fakeStore(idbItems, onInit) {
    const store = {
      deadletter: [], conflicts: [], saved: null,
      ready: true, degraded: false,
      init: async () => { if (onInit) onInit(); return { storage: 'idb', degraded: false }; },
      loadAll: async () => idbItems.slice(),
      saveAll: async (q) => { store.saved = q; },
      moveToDeadletter: async (items, reason) => { items.forEach(i => store.deadletter.push({ item: i, reason })); },
      addConflict: async (item) => { store.conflicts.push(item); }
    };
    return store;
  }

  async function withStore(store, lsItems, fn) {
    const cdbSync = sandbox.window.cdbSync;
    const original = sandbox.cdbQueueStore;
    sandbox.localStorage.setItem(QUEUE_KEY, JSON.stringify(lsItems || []));
    sandbox.cdbQueueStore = store;
    try {
      await cdbSync._internal.initQueueStore();
      await fn(cdbSync);
    } finally {
      sandbox.cdbQueueStore = original;
      sandbox.localStorage.setItem(QUEUE_KEY, '[]');
      await cdbSync._internal.initQueueStore();
      cdbSync.clearQueue();
    }
  }

  async function flushCom(cdbSync, body) {
    const toasts = [];
    const app = sandbox.cdbApp;
    sandbox.cdbApp = Object.assign({}, app, {
      showToast: (msg) => toasts.push(msg),
      setPendingSync() {}, markAllSynced() {}, onSyncConflict() {}
    });
    sandbox.AbortController = AbortController;
    sandbox.URLSearchParams = URLSearchParams;
    sandbox.fetch = async (url) => {
      if (String(url).indexOf('/sync/pull') >= 0) return fakeResp(200, { serverTime: 0, mutations: [] });
      return fakeResp(200, body);
    };
    try {
      await cdbSync.flush();
    } finally {
      if (app) sandbox.cdbApp = app; else delete sandbox.cdbApp;
      sandbox.fetch = () => Promise.reject(new Error('fetch nao implementado em testes'));
    }
    return toasts;
  }

  test('#1509: rejeicao de validacao sai da fila, vai para a dead-letter e avisa', async () => {
    const store = fakeStore([mut('mut_a'), mut('mut_b')]);
    await withStore(store, [mut('mut_a'), mut('mut_b')], async (cdbSync) => {
      const toasts = await flushCom(cdbSync, {
        acceptedIds: ['mut_b'],
        rejected: [{ mutationId: 'mut_a', reason: "Item 'Lasanha' precisa de peso" }]
      });
      assert.strictEqual(cdbSync.queueSize(), 0, 'rejeitada nao fica para reenvio eterno');
      assert.strictEqual(store.deadletter.length, 1, 'rejeitada vai para a dead-letter');
      assert.strictEqual(store.deadletter[0].item.id, 'mut_a');
      assert.ok(/peso/.test(store.deadletter[0].reason), 'motivo do servidor guardado');
      assert.ok(toasts.length >= 1, 'operador e avisado');
    });
  });

  test('#1509: conflito guarda copia na fila de conflitos antes de sair da fila', async () => {
    const store = fakeStore([mut('mut_c')]);
    await withStore(store, [mut('mut_c')], async (cdbSync) => {
      await flushCom(cdbSync, {
        acceptedIds: [],
        rejected: [{ mutationId: 'mut_c', reason: 'conflict: servidor mais novo' }]
      });
      assert.strictEqual(cdbSync.queueSize(), 0, 'conflito sai da fila');
      assert.strictEqual(store.conflicts.length, 1, 'conflito guardado');
      assert.strictEqual(store.conflicts[0].id, 'mut_c');
      assert.strictEqual(store.deadletter.length, 0, 'conflito nao vai para a dead-letter');
    });
  });

  test('#1509: boot une IndexedDB e localStorage, sem perder a venda que nao chegou ao IDB', async () => {
    // O IDB e gravado em segundo plano; o localStorage e a gravacao sincrona.
    const store = fakeStore([mut('mut_1')]);
    await withStore(store, [mut('mut_1'), mut('mut_2')], async (cdbSync) => {
      assert.strictEqual(cdbSync.queueSize(), 2, 'mutation so do localStorage sobrevive ao boot');
    });
  });

  test('#1509: boot le o localStorage antes da limpeza pos-migracao do queue-store', async () => {
    // Passados 7 dias da migracao, init() apaga a copia do localStorage.
    const store = fakeStore([mut('mut_1')], () => sandbox.localStorage.removeItem(QUEUE_KEY));
    await withStore(store, [mut('mut_1'), mut('mut_2')], async (cdbSync) => {
      assert.strictEqual(cdbSync.queueSize(), 2, 'a limpeza nao leva a venda junto');
    });
  });
};
