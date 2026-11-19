// Testes — #2877 backup pre-sync com fallback na memoria interna do app
//
// No Android, o backup publico (MediaStore) e o Documents do app podem falhar
// juntos e travar o envio para sempre (#2184 exige backup antes de enviar).
// O fallback grava em Directory.Data (memoria interna do app). So quando todos
// os caminhos falham o flush devolve 'backup-failed', e o motivo vai ao servidor.
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

  async function comFila(itens, fn) {
    const cdbSync = sandbox.window.cdbSync;
    const lojaOriginal = sandbox.cdbQueueStore;
    sandbox.localStorage.setItem(QUEUE_KEY, JSON.stringify(itens));
    sandbox.cdbQueueStore = fakeStore(itens);
    cdbSync._internal.resetAvisosBackup();
    try {
      await cdbSync._internal.initQueueStore();
      await fn(cdbSync);
    } finally {
      sandbox.cdbQueueStore = lojaOriginal;
      sandbox.localStorage.setItem(QUEUE_KEY, '[]');
      await cdbSync._internal.initQueueStore();
      cdbSync.clearQueue();
      cdbSync._internal.resetAvisosBackup();
    }
  }

  // Publico e Documents falham; 'DATA' grava ou falha conforme dataOk.
  async function comPlugins(dataOk, log, fn) {
    const anterior = sandbox.window.Capacitor;
    sandbox.window.Capacitor = {
      Plugins: {
        PublicBackup: {
          async writePublicBackup() { log.push('publico'); throw new Error('MediaStore negou'); }
        },
        Filesystem: {
          async writeFile(opts) {
            log.push('fs:' + opts.directory);
            if (opts.directory === 'DATA' && dataOk) return { uri: 'file:///data/backup-latest.json' };
            throw new Error('EACCES ' + opts.directory);
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

  // Flush com fetch controlado: POST /sync registra 'envio'; /diagnostics/errors guarda o corpo.
  async function flushCom(log, relatos) {
    const app = sandbox.cdbApp;
    sandbox.cdbApp = Object.assign({}, app, {
      showToast() {}, setPendingSync() {}, markAllSynced() {}, onSyncConflict() {}
    });
    sandbox.AbortController = AbortController;
    sandbox.URLSearchParams = URLSearchParams;
    sandbox.fetch = async (url, init) => {
      const u = String(url);
      if (u.indexOf('/sync/pull') >= 0) return fakeResp(200, { serverTime: 0, mutations: [] });
      if (u.indexOf('/diagnostics/errors') >= 0) {
        relatos.push(JSON.parse(init.body));
        return fakeResp(204, null);
      }
      log.push('envio');
      return fakeResp(200, { acceptedIds: [], rejected: [] });
    };
    try {
      return await sandbox.window.cdbSync.flush();
    } finally {
      if (app) sandbox.cdbApp = app; else delete sandbox.cdbApp;
      sandbox.fetch = () => Promise.reject(new Error('fetch nao implementado em testes'));
    }
  }

  test('#2877: publico e Documents falham, memoria interna grava e o envio segue', async () => {
    const log = [];
    const relatos = [];
    await comFila([mut('fb_a1')], async () => {
      await comPlugins(true, log, async () => {
        const status = await flushCom(log, relatos);
        assert.notStrictEqual(status, 'backup-failed', 'fallback interno destrava o envio');
      });
    });
    assert.ok(log.indexOf('fs:DATA') >= 0, 'tentou a memoria interna');
    assert.ok(log.indexOf('envio') > log.indexOf('fs:DATA'), 'envio depois do backup interno');
  });

  test('#2877: todos os caminhos falham, nada sai e o motivo vai ao servidor', async () => {
    const log = [];
    const relatos = [];
    await comFila([mut('fb_b1'), mut('fb_b2')], async (cdbSync) => {
      await comPlugins(false, log, async () => {
        const status = await flushCom(log, relatos);
        assert.strictEqual(status, 'backup-failed');
      });
      assert.strictEqual(cdbSync.queueSize(), 2, 'fila intacta');
    });
    assert.strictEqual(log.indexOf('envio'), -1, 'nenhum POST /sync');
    assert.strictEqual(relatos.length, 1, 'um relato de diagnostico');
    const erro = relatos[0].errors[0];
    assert.strictEqual(erro.context, 'backup-pre-sync');
    assert.ok(/MediaStore negou/.test(erro.message), 'motivo do publico no relato');
    assert.ok(/EACCES DATA/.test(erro.message), 'motivo da memoria interna no relato');
  });

  test('#2877: relato do backup falho respeita a janela de 5 minutos', async () => {
    const log = [];
    const relatos = [];
    await comFila([mut('fb_c1')], async () => {
      await comPlugins(false, log, async () => {
        await flushCom(log, relatos);
        await flushCom(log, relatos);
      });
    });
    assert.strictEqual(relatos.length, 1, 'segundo flush nao relata de novo');
  });

  test('#2877: estimativa de pouco espaco nao impede a gravacao', async () => {
    const log = [];
    const relatos = [];
    const nav = sandbox.navigator;
    const storageAnterior = nav.storage;
    nav.storage = { estimate: async () => ({ quota: 1000, usage: 999 }) };
    try {
      await comFila([mut('fb_d1')], async () => {
        await comPlugins(true, log, async () => {
          const status = await flushCom(log, relatos);
          assert.notStrictEqual(status, 'backup-failed', 'estimativa e so aviso');
        });
      });
    } finally {
      nav.storage = storageAnterior;
    }
    assert.ok(log.indexOf('envio') >= 0, 'envio aconteceu');
  });
};
