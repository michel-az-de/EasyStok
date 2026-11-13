// Testes — #2184 backup local antes do envio da fila
//
// Com canal de backup nativo (Capacitor PublicBackup/Filesystem), o flush grava o
// backup 'pre-sync' ANTES de enviar. Se o backup falha, nada e enviado, a fila
// fica intacta e o status e 'backup-failed'. Offline continua 'offline' sem backup.
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

  function mut(id) {
    return { id, deviceId: 'd', type: 'order.upsert', payload: { id: 'o-' + id }, ts: 1 };
  }

  function fakeStore(idbItems) {
    return {
      deadletter: [], conflicts: [], saved: null,
      ready: true, degraded: false,
      init: async () => ({ storage: 'idb', degraded: false }),
      loadAll: async () => idbItems.slice(),
      saveAll: async (q) => { sandbox.cdbQueueStore.saved = q; },
      moveToDeadletter: async () => {},
      addConflict: async () => {}
    };
  }

  // Fila de teste isolada: restaura a loja e a fila reais no fim.
  async function comFila(itens, fn) {
    const cdbSync = sandbox.window.cdbSync;
    const lojaOriginal = sandbox.cdbQueueStore;
    sandbox.localStorage.setItem(QUEUE_KEY, JSON.stringify(itens));
    sandbox.cdbQueueStore = fakeStore(itens);
    try {
      await cdbSync._internal.initQueueStore();
      await fn(cdbSync);
    } finally {
      sandbox.cdbQueueStore = lojaOriginal;
      sandbox.localStorage.setItem(QUEUE_KEY, '[]');
      await cdbSync._internal.initQueueStore();
      cdbSync.clearQueue();
    }
  }

  // Plugin nativo de backup: 'ok' grava, 'falha' lanca. Registra as chamadas em log.
  async function comBackupNativo(comportamento, log, fn) {
    const anterior = sandbox.window.Capacitor;
    sandbox.window.Capacitor = {
      Plugins: {
        PublicBackup: {
          async writePublicBackup() {
            log.push('backup');
            if (comportamento === 'falha') throw new Error('armazenamento indisponivel');
            return { documentsUri: 'content://documents', downloadsUri: null };
          }
        }
      }
    };
    try {
      await fn();
    } finally {
      sandbox.window.Capacitor = anterior;
    }
  }

  // Roda um flush com resposta de envio controlada. Cada POST em /sync registra 'envio'.
  async function flushCom(log, responder) {
    const app = sandbox.cdbApp;
    const toasts = [];
    sandbox.cdbApp = Object.assign({}, app, {
      showToast: (msg) => toasts.push(msg),
      setPendingSync() {}, markAllSynced() {}, onSyncConflict() {}
    });
    sandbox.AbortController = AbortController;
    sandbox.URLSearchParams = URLSearchParams;
    sandbox.fetch = async (url) => {
      if (String(url).indexOf('/sync/pull') >= 0) return fakeResp(200, { serverTime: 0, mutations: [] });
      log.push('envio');
      return responder();
    };
    let status;
    try {
      status = await sandbox.window.cdbSync.flush();
    } finally {
      if (app) sandbox.cdbApp = app; else delete sandbox.cdbApp;
      sandbox.fetch = () => Promise.reject(new Error('fetch nao implementado em testes'));
    }
    return { status, toasts };
  }

  test('#2184: backup local acontece antes do primeiro envio da fila', async () => {
    const log = [];
    await comFila([mut('pre_a1'), mut('pre_a2')], async (cdbSync) => {
      await comBackupNativo('ok', log, async () => {
        const r = await flushCom(log, () => fakeResp(200, { acceptedIds: ['pre_a1', 'pre_a2'], rejected: [] }));
        assert.notStrictEqual(r.status, 'backup-failed', 'backup ok nao bloqueia o envio');
      });
      assert.strictEqual(cdbSync.queueSize(), 0, 'fila enviada apos backup ok');
    });
    assert.deepStrictEqual(log, ['backup', 'envio'], 'backup antes do envio');
  });

  test('#2184: backup falhou: nada e enviado, status backup-failed e fila intacta', async () => {
    const log = [];
    await comFila([mut('pre_b1'), mut('pre_b2')], async (cdbSync) => {
      await comBackupNativo('falha', log, async () => {
        const antes = sandbox.localStorage.getItem(QUEUE_KEY);
        const r = await flushCom(log, () => fakeResp(200, { acceptedIds: ['pre_b1', 'pre_b2'], rejected: [] }));
        assert.strictEqual(r.status, 'backup-failed', 'status de backup falho');
        assert.ok(r.toasts.some((t) => /backup/i.test(t)), 'operador avisado do backup');
        assert.strictEqual(sandbox.localStorage.getItem(QUEUE_KEY), antes, 'localStorage da fila identico');
      });
      assert.strictEqual(cdbSync.queueSize(), 2, 'fila intacta: nenhuma mutation saiu');
    });
    assert.deepStrictEqual(log, ['backup'], 'nenhum envio com backup falho');
  });

  test('#2184: offline continua retornando offline, sem backup e sem envio', async () => {
    const log = [];
    const nav = sandbox.navigator;
    const onlineAnterior = nav.onLine;
    await comFila([mut('pre_c1')], async () => {
      await comBackupNativo('ok', log, async () => {
        nav.onLine = false;
        try {
          const r = await flushCom(log, () => fakeResp(200, { acceptedIds: ['pre_c1'], rejected: [] }));
          assert.strictEqual(r.status, 'offline');
        } finally {
          nav.onLine = onlineAnterior;
        }
      });
    });
    assert.deepStrictEqual(log, [], 'offline nao faz backup nem envio');
  });

  test('#2184: retry com a mesma fila nao grava backup pre-sync de novo', async () => {
    const log = [];
    await comFila([mut('pre_d1')], async () => {
      await comBackupNativo('ok', log, async () => {
        const r1 = await flushCom(log, () => fakeResp(500, {}));
        const r2 = await flushCom(log, () => fakeResp(500, {}));
        assert.strictEqual(r1.status, 'retry');
        assert.strictEqual(r2.status, 'retry');
      });
    });
    assert.deepStrictEqual(log, ['backup', 'envio', 'envio'], 'um backup cobre a mesma fila repetida');
  });
};
