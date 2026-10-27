'use strict';
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

module.exports = function ({ test, assert }) {
  const html = fs.readFileSync(process.env.PWA_HTML || path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa/index.html'), 'utf8');
  const start = html.indexOf('function updateSync() {');
  const end = html.indexOf("window.addEventListener('online', updateSync);", start);
  assert.ok(start >= 0 && end > start);
  function render({ paired = true, invalid = false, online = true, pending = 0 } = {}) {
    const nodes = new Map();
    const document = { getElementById(id) {
      if (!nodes.has(id)) nodes.set(id, { textContent: '', hidden: false, classList: { toggle() {}, add() {}, remove() {} } });
      return nodes.get(id);
    } };
    const context = { document, navigator: { onLine: online }, pendingSync: pending,
      window: { CDB_CONFIG: { isPilot: true }, cdbSync: { getPairing: () => paired ? { deviceId: 'test' } : null, isPairingInvalid: () => invalid } } };
    vm.runInNewContext(html.slice(start, end) + '\nupdateSync();', context);
    return nodes.get('syncText').textContent;
  }
  test('fila vazia com aparelho vinculado não afirma sincronização sem recibo do servidor', () => {
    assert.match(html, /id="syncText">Conferindo fila de envio</);
    assert.equal(render(), 'Nenhuma alteração na fila de envio');
    assert.doesNotMatch(render(), /sincronizado|confirmado/i);
  });
  test('estado da fila preserva falta de pareamento, conexão inválida, pendências e offline', () => {
    assert.equal(render({ paired: false }), 'Neste aparelho · não conectado');
    assert.equal(render({ invalid: true }), 'Neste aparelho · não conectado');
    assert.equal(render({ pending: 2 }), '2 itens pra sincronizar');
    assert.equal(render({ online: false }), 'Offline');
    assert.equal(render({ online: false, pending: 1 }), '1 pendente offline');
  });

  function manualSync({ online = true, paired = true, review = false, status = 200, body = {}, queue = [{ id: 'mutation-1' }], fetchError = false } = {}) {
    const syncSource = fs.readFileSync(path.resolve(path.dirname(process.env.PWA_HTML || path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa/index.html')), 'sync.js'), 'utf8');
    const flushStart = syncSource.indexOf('  async function flush() {');
    const flushEnd = syncSource.indexOf('\n  async function _guardarConflitos(', flushStart);
    const actionStart = html.indexOf('async function syncNow() {');
    const actionEnd = html.indexOf('\nfunction updateSync()', actionStart);
    assert.ok(flushStart >= 0 && flushEnd > flushStart && actionStart >= 0 && actionEnd > actionStart);
    const toasts = [], writes = [], events = [], refused = []; let requests = 0;
    const context = {
      navigator: { onLine: online }, pendingSync: queue.length,
      flushPromise: null, _pairingInvalid: false, _consecutiveFlushFailures: 0,
      syncHeldForImport: () => review, getApiKey: () => paired ? 'fixture' : null,
      loadQueue: () => queue.slice(), saveQueue: items => { queue = items; },
      API_BASE_URL: 'https://example.invalid', API_PREFIX: '/api/mobile', deviceId: 'fixture',
      baseHeaders: headers => headers, updateServerTimeFromResponse() {},
      localStorage: { getItem: () => null, setItem: (key, value) => writes.push({ key, value }) },
      LAST_FULL_SYNC_KEY: 'cdb-last-sync', AbortController,
      setTimeout() { return 1; }, clearTimeout() {}, console: { warn() {} },
      CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options.detail; } },
      fetch: async () => { requests++; if (fetchError) throw new Error('fixture offline'); return { status, ok: status >= 200 && status < 300, json: async () => body }; },
      updatePendingCount() { context.pendingSync = queue.length; }, updateSync() {},
      showToast: text => toasts.push(text),
      _moverRecusadasParaDeadletter: async rejected => { refused.push(...rejected); queue = queue.filter(m => !rejected.some(r => r.mutationId === m.id)); }
    };
    context.window = { CDB_CONFIG: { isPilot: true }, dispatchEvent: event => events.push(event) };
    vm.createContext(context);
    vm.runInContext(syncSource.slice(flushStart, flushEnd), context);
    context.window.cdbSync = { flush: context.flush };
    vm.runInContext(html.slice(actionStart, actionEnd), context);
    return { context, toasts, writes, events, refused, requests: () => requests, queue: () => queue };
  }

  test('sincronização manual offline, sem vínculo ou pausada não inventa envio nem altera fila/data', async () => {
    for (const [options, expected] of [[{ online: false }, 'offline'], [{ paired: false }, 'auth'], [{ review: true }, 'review']]) {
      const state = manualSync(options);
      assert.equal(await state.context.syncNow(), expected);
      assert.equal(state.requests(), 0);
      assert.equal(state.context.pendingSync, 1);
      assert.equal(state.queue().length, 1);
      assert.equal(state.writes.length, 0);
      assert.doesNotMatch(state.toasts.join(' '), /sincronizado|enviado|recebido/i);
    }
  });

  test('sincronização manual mantém pendências e data quando API rejeita autenticação ou falha', async () => {
    for (const [options, expected] of [[{ status: 401 }, 'auth'], [{ status: 503 }, 'retry'], [{ fetchError: true }, 'retry']]) {
      const state = manualSync(options);
      assert.equal(await state.context.syncNow(), expected);
      assert.equal(state.requests(), 1);
      assert.equal(state.context.pendingSync, 1);
      assert.equal(state.queue().length, 1);
      assert.equal(state.writes.length, 0);
      assert.equal(state.events.length, 0);
      assert.doesNotMatch(state.toasts.join(' '), /sincronizado|enviado|recebido/i);
    }
  });

  test('HTTP 200 sem aceite ou com recusa não vira confirmação de sincronização manual', async () => {
    for (const body of [{}, { acceptedIds: [], rejected: [{ mutationId: 'mutation-1', reason: 'validation: fixture' }] }]) {
      const state = manualSync({ body });
      assert.equal(await state.context.syncNow(), 'ok');
      assert.equal(state.events.length, 0);
      assert.equal(state.toasts.length, 0, 'a UI não transforma HTTP 200 em comprovante de aceite');
      if (body.rejected) assert.equal(state.refused.length, 1, 'recusa segue o tratamento real de dead-letter');
      else assert.equal(state.queue().length, 1, 'sem acceptedIds a fila permanece');
    }
  });

  test('sincronização manual usa aceite real do flush e deixa a confirmação para o evento dos IDs aceitos', async () => {
    const state = manualSync({ body: { acceptedIds: ['mutation-1'] } });
    assert.equal(await state.context.syncNow(), 'ok');
    assert.equal(state.requests(), 1);
    assert.equal(state.queue().length, 0);
    assert.equal(state.context.pendingSync, 0);
    assert.equal(state.events.length, 1);
    assert.equal(state.events[0].type, 'cdb-sync-success');
    assert.equal(state.events[0].detail.count, 1);
    assert.equal(state.writes.length, 1, 'apenas flush registra a data após resposta');
    assert.equal(state.toasts.length, 0, 'sem segundo toast que afirme sincronização total');
  });
};
