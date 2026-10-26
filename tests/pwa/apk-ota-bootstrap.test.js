'use strict';

const fs = require('fs');
const path = require('path');
const vm = require('vm');
const source = fs.readFileSync(path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa/sync.js'), 'utf8');
const tick = () => new Promise(resolve => setImmediate(resolve));

module.exports = function ({ test, sandbox, assert }) {
  function fixture({ queueInit, queueLoad, photoInit, pilot = true } = {}) {
    const data = new Map([['cdb-sync-queue', JSON.stringify([{ id: 'ls-pending', type: 'order.upsert', payload: { id: 'ls-order' }, ts: 2 }])]]);
    const timers = [], calls = [];
    const store = {
      ready: false, degraded: false,
      async init() { store.ready = true; return queueInit ? queueInit(store) : { storage: 'idb', degraded: false }; },
      async loadAll() { return queueLoad ? queueLoad() : [{ id: 'idb-pending', type: 'batch.upsert', payload: { id: 'idb-batch' }, ts: 1 }]; },
      async saveAll(items) { calls.push(['save-queue', items.length]); }
    };
    const photos = { ready: false, async init() { if (photoInit) return photoInit(photos); photos.ready = true; } };
    const context = {
      ...sandbox,
      CDB_CONFIG: { isPilot: pilot, apiBaseUrl: 'https://fixture.invalid' },
      navigator: { ...sandbox.navigator, onLine: false },
      URLSearchParams, AbortController,
      location: { ...sandbox.location, reload() { calls.push(['reload']); } },
      localStorage: {
        getItem: key => data.has(key) ? data.get(key) : null,
        setItem: (key, value) => data.set(key, String(value)), removeItem: key => data.delete(key),
        key: index => [...data.keys()][index] || null, get length() { return data.size; }
      },
      cdbQueueStore: store, cdbPhotoStore: photos,
      cdbApp: { getState: () => ({ products: [], clients: [], orders: [], cashEntries: [], batches: [], cashClosings: [] }), setPendingSync() {}, showToast() {} },
      Capacitor: { Plugins: { PilotUpdater: { async check() { calls.push(['check']); return { pendingVersion: 'fixture-next' }; } } } },
      setTimeout: (fn, delay) => { timers.push({ fn, delay }); return timers.length; }, clearTimeout() {},
      setInterval: () => 0, clearInterval() {},
      fetch: async () => { calls.push(['fetch']); throw new Error('No network in fixture'); },
      console: { log() {}, warn() {}, error() {} },
      addEventListener() {}, removeEventListener() {}, dispatchEvent() {}
    };
    context.window = context; context.self = context; context.globalThis = context;
    vm.createContext(context); vm.runInContext(source, context, { timeout: 5000 });
    return { context, store, photos, calls, data,
      boot: () => { const timer = timers.find(item => item.delay === 500); assert.ok(timer, 'boot timer exists'); timer.fn(); } };
  }

  test('#2065 OTA: bootstrap espera hidratação e migração da fila e abertura das fotos', async () => {
    let finishMigration, finishLoad, finishPhotos;
    const f = fixture({
      queueInit: () => new Promise(resolve => { finishMigration = resolve; }),
      queueLoad: () => new Promise(resolve => { finishLoad = resolve; }),
      photoInit: photos => new Promise(resolve => { finishPhotos = () => { photos.ready = true; resolve(); }; })
    });
    let settled = false;
    f.context.cdbSync.localReady.then(() => { settled = true; });
    await tick(); assert.equal(settled, false, 'before boot');
    f.boot(); await tick(); assert.equal(settled, false, 'ready flag before migration is insufficient');
    finishMigration({ storage: 'idb', degraded: false }); await tick();
    assert.equal(settled, false, 'queue must still hydrate');
    finishLoad([{ id: 'idb-pending', type: 'batch.upsert', payload: {}, ts: 1 }]); await tick();
    assert.equal(settled, false, 'photos must still open');
    finishPhotos();
    assert.equal((await f.context.cdbSync.localReady).ready, true);
    assert.equal(f.context.cdbSync.queueSize(), 2, 'IDB and localStorage pending work are preserved');
    assert.equal(f.calls.some(call => call[0] === 'fetch'), false, 'offline and unpaired does not prevent local readiness');
  });

  for (const [label, options] of [
    ['fallback da fila', { queueInit: store => { store.degraded = true; return { storage: 'localStorage', degraded: true }; } }],
    ['erro ao hidratar a fila', { queueLoad: () => { throw new Error('fixture IDB read failure'); } }],
    ['fotos que resolvem sem abrir IDB', { photoInit: () => undefined }],
    ['fotos que rejeitam', { photoInit: () => Promise.reject(new Error('fixture photos failure')) }]
  ]) {
    test('#2065 OTA: ' + label + ' não confirma versão saudável', async () => {
      const f = fixture(options); f.boot();
      const result = await f.context.cdbSync.localReady;
      assert.equal(result.ready, false);
      assert.equal(result.reason, 'local-storage-unavailable');
    });
  }

  test('#2065 OTA: falha antes das stores resolve confirmação negativa sem esconder o erro original', async () => {
    const f = fixture();
    f.context.cdbApp.getState = () => { throw new Error('fixture boot failure'); };
    assert.throws(f.boot, /fixture boot failure/);
    assert.equal((await f.context.cdbSync.localReady).ready, false);
  });

  test('#2065 OTA: comando manual no piloto só prepara versão, sem limpar dados, drenar ou recarregar', async () => {
    const f = fixture();
    const original = f.data.get('cdb-sync-queue');
    const result = await f.context.cdbSync.forceUpdate();
    assert.equal(result.pendingVersion, 'fixture-next');
    assert.deepStrictEqual(f.calls, [['check']]);
    assert.equal(f.data.get('cdb-sync-queue'), original);
    assert.equal(f.data.has('cdb-update-in-progress'), false);
    assert.equal(f.data.has('cdb-mutations-blocked'), false);
  });

  test('#2065 OTA: pwa_update remoto no piloto não envia fila pendente antes de preparar', async () => {
    const f = fixture();
    f.context.navigator.onLine = true;
    f.context.localStorage.setItem('cdb-pairing', JSON.stringify({ apiKey: 'fixture', empresaId: 'fixture', lojaId: 'fixture' }));
    f.context.fetch = async url => {
      f.calls.push(['fetch', String(url)]);
      return { ok: true, status: 200, json: async () => [{ commandType: 'pwa_update' }] };
    };
    await f.context.cdbSync.fetchCommands();
    await tick();
    assert.equal(f.calls.filter(call => call[0] === 'fetch').length, 1, 'only command GET, no sync POST');
    assert.equal(f.calls.filter(call => call[0] === 'check').length, 1);
    assert.equal(f.calls.some(call => call[0] === 'reload'), false);
    assert.equal(f.data.has('cdb-update-in-progress'), false);
    assert.equal(JSON.parse(f.data.get('cdb-sync-queue')).length, 1);
  });

  test('#2065 OTA: ausência do plugin no piloto não usa fallback destrutivo de PWA', async () => {
    const f = fixture(); delete f.context.Capacitor;
    assert.equal((await f.context.cdbSync.forceUpdate()).reason, 'updater-unavailable');
    assert.deepStrictEqual(f.calls, []);
  });
};
