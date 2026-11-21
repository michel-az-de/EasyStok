'use strict';

module.exports = function ({ test, sandbox, assert }) {
  async function context(action) {
    const previous = sandbox.window.CDB_CONFIG;
    sandbox.window.CDB_CONFIG = { ...(previous || {}), isPilot: true };
    const storage = sandbox.localStorage;
    sandbox.window.cdbSync.clearQueue();
    for (const key of ['cdb-pairing', 'cdb-pilot-data-context', 'cdb-products', 'cdb-clients', 'cdb-orders', 'cdb-sync-queue', 'cdb-batches', 'cdb-cash', 'cdb-cash-closings', 'cdb-shopping', 'cdb-audit-log']) storage.removeItem(key);
    try { return await action(sandbox.window.cdbSync, storage); }
    finally { sandbox.window.CDB_CONFIG = previous; }
  }
  const connection = sync => ({ deviceId: sync.getDeviceId(), apiKey: 'chave-da-sessao', empresaId: 'empresa-a', lojaId: 'loja-a', empresaNome: 'Casa de teste' });

  test('#2184: primeiro login sem dados conecta sem código manual', () => context((sync, storage) => {
    sync.applySessionPairing(connection(sync));
    assert.strictEqual(sync.getPairing().empresaId, 'empresa-a');
    assert.strictEqual(storage.getItem('cdb-pull-cursor'), '0');
  }));

  test('#2184: fila sem origem não é assumida pelo primeiro login', () => context((sync, storage) => {
    storage.setItem('cdb-orders', '[{"id":"pedido-local"}]');
    storage.setItem('cdb-sync-queue', '[{"id":"pendencia-preservada"}]');
    assert.throws(() => sync.applySessionPairing(connection(sync)), error => error.code === 'LOCAL_CONTEXT_REQUIRED');
    assert.strictEqual(sync.getPairing(), null);
    assert.strictEqual(JSON.parse(storage.getItem('cdb-sync-queue'))[0].id, 'pendencia-preservada');
    sync.applySessionPairing(connection(sync), { confirmLocalContext: true });
    assert.strictEqual(sync.getPairing().lojaId, 'loja-a');
  }));

  test('#2184: retomar a mesma empresa e loja não apaga dados nem reinicia o cursor', () => context((sync, storage) => {
    const current = connection(sync);
    storage.setItem('cdb-pairing', JSON.stringify(current));
    storage.setItem('cdb-orders', '[{"id":"pedido-preservado"}]');
    storage.setItem('cdb-pull-cursor', '12345');
    sync.applySessionPairing({ ...current, apiKey: 'chave-renovada' });
    assert.strictEqual(storage.getItem('cdb-pull-cursor'), '12345');
    assert.strictEqual(JSON.parse(storage.getItem('cdb-orders'))[0].id, 'pedido-preservado');
    sync.clearPairing();
    assert.strictEqual(JSON.parse(storage.getItem('cdb-orders'))[0].id, 'pedido-preservado');
    sync.applySessionPairing(current);
    assert.strictEqual(sync.getPairing().empresaId, 'empresa-a');
  }));

  test('#2184: outra empresa, loja ou aparelho não assume dados existentes', () => context((sync, storage) => {
    const current = connection(sync);
    storage.setItem('cdb-pairing', JSON.stringify(current));
    storage.setItem('cdb-orders', '[{"id":"pedido-da-empresa-a"}]');
    for (const mismatch of [{ empresaId: 'empresa-b' }, { lojaId: 'loja-b' }, { deviceId: 'outro-aparelho' }]) {
      assert.throws(() => sync.applySessionPairing({ ...current, ...mismatch }, { confirmLocalContext: true }));
      assert.strictEqual(sync.getPairing().empresaId, 'empresa-a');
      assert.strictEqual(JSON.parse(storage.getItem('cdb-orders'))[0].id, 'pedido-da-empresa-a');
    }
  }));

  async function network(action) {
    const old = { fetch: sandbox.fetch, events: sandbox.EventSource, abort: sandbox.AbortController, online: sandbox.navigator.onLine, auth: sandbox.window.cdbAuth };
    try {
      sandbox.AbortController = AbortController;
      sandbox.navigator.onLine = true;
      await context(async (sync, storage) => {
        sandbox.window.CDB_CONFIG.requiresGoogleSession = true;
        storage.setItem('cdb-pairing', JSON.stringify(connection(sync)));
        await action(sync);
      });
    } finally {
      sandbox.window.cdbSync.stopRealtime();
      sandbox.fetch = old.fetch; sandbox.EventSource = old.events; sandbox.AbortController = old.abort;
      sandbox.navigator.onLine = old.online; sandbox.window.cdbAuth = old.auth;
    }
  }

  test('#2184: chave legada não sincroniza antes do login Google e da origem conferida', () => network(async sync => {
    let requests = 0;
    sandbox.fetch = async () => { requests++; throw new Error('Rede não deve ser usada antes da conta.'); };
    sandbox.EventSource = function () { throw new Error('Realtime não deve abrir antes da conta.'); };
    sandbox.window.cdbAuth = { canSync: () => false };
    await sync.flush(); await sync.pull(); await sync.startRealtime();
    assert.strictEqual(requests, 0);
  }));

  test('#2184: resposta SSE atrasada após Sair não reconecta a conta anterior', () => network(async sync => {
    let finish, streams = 0, ready = true;
    sandbox.window.cdbAuth = { canSync: () => ready };
    sandbox.EventSource = function () { streams++; this.close = () => {}; };
    sandbox.fetch = () => new Promise(resolve => { finish = resolve; });
    const opening = sync.startRealtime();
    assert.strictEqual(typeof finish, 'function', 'solicitou token SSE antes da saída');
    ready = false; sync.stopRealtime(); sync.clearPairing();
    finish({ ok: true, json: async () => ({ token: 'token-efemero-anterior' }) });
    await opening;
    assert.strictEqual(streams, 0, 'Sair vence o token SSE recebido depois');
  }));
};
