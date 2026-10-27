'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { noticePresentation } = require('../pilot.js');
const settle = () => new Promise(resolve => setImmediate(resolve));
const normal = () => ({ supported: true, permission: 'granted', registered: true, installationId: 'a'.repeat(64), apkVersion: '0.6.0', unread: 1, lastPushAt: 0, lastSyncAt: 0, pendingReceipts: 0, lastError: '', items: [{ id: 'notice-one', title: 'Traga seus dados', body: 'Preserve o backup do PWA.', receivedAt: 1793050000000, openedAt: 0, source: 'push', screen: 'migration-guide' }] });
function harness(state = normal(), nativeEnabled = true) {
  const nodes = new Map(), events = new Map(), listeners = new Map(), calls = { open: [], refresh: 0, permission: 0, copied: [] };
  const on = (name, callback) => { if (!events.has(name)) events.set(name, []); events.get(name).push(callback); };
  const emit = name => Promise.all((events.get(name) || []).map(callback => callback()));
  const doc = { hidden: false, activeElement: null, addEventListener: on, getElementById: id => nodes.get(id) };
  const element = tag => {
    const handlers = new Map(); let text = '';
    const el = { tag, dataset: {}, attributes: {}, children: [], open: false, hidden: false, disabled: false, isConnected: true,
      classList: { add() {} }, setAttribute(name, value) { this.attributes[name] = value; },
      getClientRects: () => [1], scrollIntoView() {}, focus() { doc.activeElement = el; },
      showModal() { this.open = true; }, close() { this.open = false; handlers.get('close')?.({}); },
      append(...children) { this.children.push(...children); },
      addEventListener(name, cb) { handlers.set(name, cb); },
      async click(action, id) { await handlers.get('click')?.({ target: { closest: () => ({ dataset: { action, noticeAction: action, noticeId: id } }) } }); },
      cancel() { let prevented = false; handlers.get('cancel')?.({ preventDefault() { prevented = true; } }); if (!prevented) this.close(); return prevented; }
    };
    Object.defineProperty(el, 'textContent', { get() { return text; }, set(value) { text = value; el.children = []; } });
    Object.defineProperty(el, 'innerHTML', { set(html) { for (const match of html.matchAll(/id="([^"]+)"/g)) nodes.set(match[1], element('element')); } });
    return el;
  };
  doc.createElement = element;
  doc.body = { classList: { add() {} }, prepend() {}, append(el) { nodes.set(el.id, el); } };
  const plugin = {
    noticeStatus: async () => structuredClone(state),
    noticeRefresh: async () => { calls.refresh++; return structuredClone(state); },
    noticeOpen: async ({ id }) => {
      assert.equal(nodes.get('pilotNoticeDetail').hidden, false, 'detalhe deve estar visível antes do recibo');
      assert.equal(doc.activeElement, nodes.get('pilotNoticeDetailTitle'));
      calls.open.push(id); const item = state.items.find(item => item.id === id); item.openedAt = 1793051000000;
      state.unread = state.items.filter(item => !item.openedAt).length; state.pendingOpenId = '';
      return structuredClone(state);
    },
    notifications: async () => { calls.permission++; }, migrationGuide: async () => {},
    addListener: async (name, callback) => { listeners.set(name, callback); }
  };
  const root = { document: doc, navigator: { onLine: true, clipboard: { writeText: async text => calls.copied.push(text) } },
    CDB_CONFIG: { isPilot: true, appId: 'com.casadababa.piloto', version: '0.6.0', bundleVersion: '0.6.0', versionCode: 6, dataSchemaVersion: 2, apiBaseUrl: 'https://api.easystok.online', updateManifestUrl: 'https://easystok.online/downloads/casa-da-baba/latest.json' },
    cdbApp: { getOperator: () => '' }, cdbSync: { getPairing: () => null, isPairingInvalid: () => false, queueSize: () => 0 }, addEventListener: on,
    Capacitor: nativeEnabled ? { isNativePlatform: () => true, Plugins: { PilotApp: plugin } } : undefined
  };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../pilot.js'), 'utf8'), { window: root, URL, AbortSignal, fetch: async () => ({ ok: true, json: async () => ({}) }) });
  return { root, doc, nodes, calls, state, plugin, emit, listeners };
}

test('sino mostra não abertos sem marcar leitura e restaura foco ao fechar', async () => {
  const { nodes, calls, doc } = harness(); await settle();
  const bell = nodes.get('pilotNoticeBell'); bell.focus(); await bell.click(); await settle();
  assert.equal(nodes.get('pilotNotices').open, true); assert.equal(doc.activeElement, nodes.get('pilotNoticesTitle'));
  assert.deepEqual(calls.open, []); assert.equal(nodes.get('pilotNoticeCount').textContent, '1');
  assert.match(bell.attributes['aria-label'], /1 não aberto/);
  await nodes.get('pilotNotices').click('close'); assert.equal(doc.activeElement, bell);
});

test('detalhe registra somente aviso selecionado depois de estar visível', async () => {
  const s = normal(); s.items.push({ ...s.items[0], id: 'notice-two' }); s.unread = 2;
  const { nodes, calls } = harness(s); await settle(); await nodes.get('pilotNoticeBell').click(); await settle();
  await nodes.get('pilotNotices').click('open', 'notice-one');
  assert.deepEqual(calls.open, ['notice-one']); assert.equal(nodes.get('pilotNoticeCount').textContent, '1');
  assert.equal(s.items[1].openedAt, 0); assert.equal(nodes.get('pilotNoticeGuide').hidden, false);
});

test('Escape do detalhe volta ao histórico e depois fecha, sem abrir outros avisos', async () => {
  const { nodes, calls } = harness(); await settle(); await nodes.get('pilotNoticeBell').click(); await settle();
  await nodes.get('pilotNotices').click('open', 'notice-one');
  assert.equal(nodes.get('pilotNotices').cancel(), true); assert.equal(nodes.get('pilotNotices').open, true);
  assert.equal(nodes.get('pilotNoticeDetail').hidden, true); assert.equal(nodes.get('pilotNotices').cancel(), false);
  assert.equal(nodes.get('pilotNotices').open, false); assert.deepEqual(calls.open, ['notice-one']);
});

test('permissão negada tem ação clara e não é descrita como entrega pronta', async () => {
  const s = normal(); s.permission = 'denied'; s.registered = false;
  const { nodes, calls } = harness(s); await settle();
  assert.match(nodes.get('pilotNoticeHealthTitle').textContent, /Permita/);
  assert.equal(nodes.get('pilotNoticePermission').textContent, 'Ativar notificações');
  await nodes.get('pilotNotices').click('permission'); assert.equal(calls.permission, 1);
  assert.equal(noticePresentation({ ...s, permission: 'blocked' }).tone, 'attention');
});

test('histórico recuperado não vira prova de push e confirmações pendentes ficam explícitas', async () => {
  const s = normal(); s.items[0].source = 'sync'; s.pendingReceipts = 2; s.lastSyncAt = 1793051000000;
  const { nodes } = harness(s); await settle();
  assert.match(nodes.get('pilotNoticeList').children[0].children[1].textContent, /Recuperado pelo histórico/);
  assert.match(nodes.get('pilotNoticePush').textContent, /Ainda não confirmado/);
  assert.match(nodes.get('pilotNoticeReceipts').textContent, /2 confirmações aguardam envio/);
  assert.match(nodes.get('pilotNoticeHealthBody').textContent, /ainda não confirma a entrega/);
});

test('falha de rede preserva histórico e permite nova tentativa sem alegar sucesso', async () => {
  const { nodes, plugin, state } = harness(); await settle();
  plugin.noticeRefresh = async () => { throw new Error('segredo que não deve ir para a UI'); };
  await nodes.get('pilotNotices').click('refresh');
  assert.match(nodes.get('pilotNoticeHealthTitle').textContent, /conferir a conexão/);
  assert.doesNotMatch(nodes.get('pilotNoticeError').textContent, /segredo/);
  assert.equal(nodes.get('pilotNoticeRefresh').disabled, false); assert.equal(nodes.get('pilotNoticeList').children.length, 1);
  plugin.noticeRefresh = async () => structuredClone(state); await nodes.get('pilotNotices').click('refresh');
  assert.match(nodes.get('pilotNoticeHealthTitle').textContent, /registrado/);
});

test('prévia não inventa registro, notificações nem identificador Android', async () => {
  const { nodes } = harness(normal(), false); await settle();
  assert.match(nodes.get('pilotNoticeHealthBody').textContent, /prévia no navegador/);
  assert.equal(nodes.get('pilotNoticePermission').hidden, true); assert.equal(nodes.get('pilotNoticeCopy').hidden, true);
  assert.equal(nodes.get('pilotNoticeList').children.length, 0); assert.equal(nodes.get('pilotNoticeCount').hidden, true);
});

test('identificação copiada contém somente aparelho e versão, sem token', async () => {
  const { nodes, calls } = harness(); await settle(); await nodes.get('pilotNotices').click('copy');
  assert.equal(calls.copied.length, 1); assert.match(calls.copied[0], new RegExp('a{64}'));
  assert.match(calls.copied[0], /APK 0.6.0/); assert.doesNotMatch(calls.copied[0], /token|key|nonce/);
  assert.match(nodes.get('pilotNoticeDevice').textContent, /Aparelho a{12} · APK/);
});

test('toque nativo pendente só confirma abertura quando a página está visível', async () => {
  const s = normal(); s.pendingOpenId = 'notice-one';
  const { nodes, doc, calls, emit } = harness(s); doc.hidden = true; await settle();
  assert.deepEqual(calls.open, []); assert.equal(nodes.get('pilotNotices').open, false);
  doc.hidden = false; await emit('visibilitychange'); await settle();
  assert.equal(nodes.get('pilotNotices').open, true); assert.deepEqual(calls.open, ['notice-one']);
});

test('conteúdo do aviso é exibido como texto e nunca interpretado como HTML', async () => {
  const s = normal(); s.items[0].title = '<img src=x onerror=alert(1)>'; s.items[0].body = '<script>bad()</script>';
  const { nodes } = harness(s); await settle(); await nodes.get('pilotNoticeBell').click(); await settle();
  await nodes.get('pilotNotices').click('open', 'notice-one');
  assert.equal(nodes.get('pilotNoticeDetailTitle').textContent, s.items[0].title);
  assert.equal(nodes.get('pilotNoticeDetailBody').textContent, s.items[0].body);
  assert.equal(nodes.get('pilotNoticeList').children[0].children[0].textContent, s.items[0].title);
});

test('offline e registro pendente têm estados distintos e não afirmam entrega', () => {
  assert.equal(noticePresentation(normal(), false).title, 'Sem conexão agora');
  assert.match(noticePresentation({ ...normal(), registered: false }).title, /ainda não confirmado/);
  assert.match(noticePresentation({ supported: false, native: true }).title, /Atualize o app/);
});