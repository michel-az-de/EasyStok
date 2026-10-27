'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { validateManifest } = require('../pilot.js');
const config = { appId: 'com.casadababa.piloto', versionCode: 1 };
const manifest = {
  schemaVersion: 1, appId: config.appId, versionCode: 2, version: '0.1.1',
  url: 'https://easystok.online/downloads/casa-da-baba/casa-da-baba-2.apk',
  sha256: 'a'.repeat(64),
};
test('oferece versão superior e não oferece downgrade ou reinstalação', () => {
  assert.equal(validateManifest(manifest, config).available, true);
  assert.equal(validateManifest({ ...manifest, versionCode: 1 }, config).available, false);
  assert.equal(validateManifest(manifest, { ...config, versionCode: 3 }).available, false);
});
test('rejeita pacote de outro aplicativo e metadados corrompidos', () => {
  for (const extra of [{ appId: 'com.casadababa.app' }, { versionCode: 2.5 }, { versionCode: -1 }, { sha256: '' }, { schemaVersion: 0 }, { version: 2 }]) {
    assert.throws(() => validateManifest({ ...manifest, ...extra }, config));
  }
});
test('não abre links de atualização fora da distribuição aprovada', () => {
  for (const url of ['http://easystok.online/downloads/casa-da-baba/a.apk', 'https://evil.example/a.apk', 'https://easystok.online.evil.example/downloads/casa-da-baba/a.apk', 'https://user@easystok.online/downloads/casa-da-baba/a.apk', 'https://easystok.online/downloads/a.apk', 'https://easystok.online/downloads/casa-da-baba/a.apk?redirect=evil', 'https://easystok.online/downloads/casa-da-baba/a.apk#fragment', 'https://easystok.online/downloads/casa-da-baba/../../a.apk', 'https://easystok.online/downloads/casa-da-baba/a%2Ffile.apk']) {
    assert.throws(() => validateManifest({ ...manifest, url }, config));
  }
});

const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const { connectionStatus } = require('../pilot.js');

test('diferencia servidor acessível de aparelho pareado e envio pendente', () => {
  const local = connectionStatus({ health: 'online', paired: false, invalid: false, pending: 2 });
  assert.match(local.status, /acessível/);
  assert.match(local.pairing, /só neste aparelho/);
  assert.equal(local.queue, '2 alterações aguardando envio.');
  const offline = connectionStatus({ health: 'offline', paired: true, invalid: false, pending: 1 });
  assert.match(offline.status, /Sem conexão confirmada/);
  assert.match(offline.pairing, /pareada/);
  assert.equal(offline.queue, '1 alteração aguardando envio.');
  const revoked = connectionStatus({ health: 'online', paired: true, invalid: true, pending: 0 });
  assert.match(revoked.pairing, /inválido/);
  assert.doesNotMatch(revoked.queue, /sincronizad|confirmad/);
});

function pilotHarness({ operator = 'Operadora', connected = true, manifestError = false } = {}) {
  const nodes = new Map();
  const events = new Map();
  const doc = { activeElement: null, getElementById: id => nodes.get(id) };
  const makeElement = tag => {
    const handlers = new Map();
    const el = { tag, dataset: {}, open: false, hidden: false, disabled: false, textContent: '', isConnected: true,
      classList: { add() {} }, setAttribute() {}, getClientRects: () => [1], scrollIntoView() {},
      focus() { doc.activeElement = el; },
      showModal() { el.open = true; },
      close() { el.open = false; handlers.get('close')?.(); },
      addEventListener(name, callback) { handlers.set(name, callback); },
      async click(action) { await handlers.get('click')?.({ target: { closest: () => ({ dataset: { action } }) } }); }
    };
    Object.defineProperty(el, 'innerHTML', { set(html) {
      for (const match of html.matchAll(/id="([^"]+)"/g)) nodes.set(match[1], makeElement('element'));
    } });
    return el;
  };
  doc.createElement = makeElement;
  doc.body = { classList: { add() {} }, prepend() {}, append(el) { nodes.set(el.id, el); } };
  const calls = [];
  const root = { document: doc, navigator: { onLine: connected },
    CDB_CONFIG: { ...config, isPilot: true, version: '0.1.0', apiBaseUrl: 'https://api.easystok.online', updateManifestUrl: 'https://easystok.online/downloads/casa-da-baba/latest.json' },
    cdbApp: { getOperator: () => operator },
    cdbSync: { getPairing: () => null, isPairingInvalid: () => false, queueSize: () => 0 },
    addEventListener: (name, callback) => events.set(name, callback),
    open: url => calls.push(url), go: screen => calls.push(screen),
  };
  const fetch = async url => {
    if (url.endsWith('/api/mobile/version')) return { ok: true, json: async () => ({ databaseOk: true }) };
    if (manifestError) throw new Error('Network failure');
    return { ok: true, json: async () => manifest };
  };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '..', 'pilot.js'), 'utf8'), { window: root, fetch, URL, AbortSignal });
  return { root, doc, nodes, events, calls };
}

test('atualização acionada fora do início abre diálogo, informa resultado e restaura foco', async () => {
  const { root, nodes, doc } = pilotHarness();
  const home = nodes.get('pilotHome');
  home.close();
  const origin = { isConnected: true, getClientRects: () => [1], focus() { doc.activeElement = origin; } };
  doc.activeElement = origin;
  await root.forceCheckUpdate();
  assert.equal(home.open, true);
  assert.match(nodes.get('pilotUpdateStatus').textContent, /0\.1\.1 disponível/);
  assert.equal(nodes.get('pilotDownload').hidden, false);
  assert.equal(nodes.get('pilotUpdateButton').disabled, false);
  assert.equal(doc.activeElement, nodes.get('pilotUpdateStatus'));
  home.close();
  assert.equal(doc.activeElement, origin);
});

test('falha na consulta de atualização mantém retorno visível e permite tentar novamente', async () => {
  const { root, nodes } = pilotHarness({ manifestError: true });
  nodes.get('pilotHome').close();
  await root.forceCheckUpdate();
  assert.equal(nodes.get('pilotHome').open, true);
  assert.match(nodes.get('pilotUpdateStatus').textContent, /Não foi possível conferir/);
  assert.equal(nodes.get('pilotDownload').hidden, true);
  assert.equal(nodes.get('pilotUpdateButton').disabled, false);
});

test('início aguarda identificação do operador, sem sobrepor boas-vindas', () => {
  const { nodes, events } = pilotHarness({ operator: '' });
  assert.equal(nodes.get('pilotHome').open, false);
  events.get('cdb-operator-ready')();
  assert.equal(nodes.get('pilotHome').open, true);
});

test('offline mantém registro local disponível e explica por que atendimento não abre', async () => {
  const { nodes, doc, calls } = pilotHarness({ connected: false });
  await nodes.get('pilotHome').click('workspace');
  assert.equal(calls.length, 0);
  assert.match(nodes.get('pilotError').textContent, /sem conexão/);
  assert.equal(doc.activeElement, nodes.get('pilotError'));
  await nodes.get('pilotHome').click('local-orders');
  assert.deepEqual(calls, ['pedidos']);
});

test('preview online abre o balcão real do atendimento', async () => {
  const { nodes, calls } = pilotHarness();
  await nodes.get('pilotHome').click('workspace');
  assert.deepEqual(calls, ['https://app.easystok.online/#/m/atendimento/balcao']);
});

test('textos pequenos do início mantêm contraste mínimo de 4,5:1', () => {
  const css = fs.readFileSync(path.join(__dirname, '..', 'pilot.css'), 'utf8');
  const style = selector => {
    const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const body = css.match(new RegExp(escaped + '\\s*\\{([^}]+)\\}'))?.[1];
    assert.ok(body, selector);
    return Object.fromEntries(body.split(';').filter(part => part.includes(':')).map(part => part.split(':').map(value => value.trim())));
  };
  const luminance = hex => {
    const rgb = hex.slice(1).match(/.{2}/g).map(value => parseInt(value, 16) / 255);
    return rgb.map(value => value <= .04045 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4)
      .reduce((sum, value, index) => sum + value * [.2126, .7152, .0722][index], 0);
  };
  for (const [text, background] of [['.pilot-topline', '.pilot-home'], ['.pilot-update', '.pilot-home'], ['.pilot-production', '.pilot-production'], ['.pilot-orders', '.pilot-orders'], ['.pilot-rail button span', '.pilot-rail']]) {
    const levels = [luminance(style(text).color), luminance(style(background).background)].sort((a, b) => a - b);
    assert.ok((levels[1] + .05) / (levels[0] + .05) >= 4.5, text);
  }
  assert.equal(style('.pilot-card .pilot-step').opacity, undefined);
});

test('cancelar pareamento conserva início e devolve foco ao atalho', async () => {
  const { root, nodes, doc } = pilotHarness();
  root.pairDevicePrompt = async () => ({ cancelled: true });
  await nodes.get('pilotHome').click('pair');
  assert.equal(nodes.get('pilotHome').open, true);
  assert.equal(doc.activeElement, nodes.get('pilotPairButton'));
  assert.equal(nodes.get('pilotPairButton').disabled, false);
});

test('sucesso e falha de pareamento ficam visíveis sem abrir diagnóstico', async () => {
  const { root, nodes, doc } = pilotHarness();
  root.pairDevicePrompt = async () => {
    root.cdbSync.getPairing = () => ({ empresaId: 'empresa', lojaId: 'loja' });
    return { paired: true };
  };
  await nodes.get('pilotHome').click('pair');
  assert.equal(nodes.get('pilotHome').open, true);
  assert.equal(doc.activeElement, nodes.get('pilotConnection'));
  assert.match(nodes.get('pilotConnection').textContent, /Produção pareada/);
  root.pairDevicePrompt = async () => { throw new Error('Código inválido'); };
  await nodes.get('pilotHome').click('pair');
  assert.equal(doc.activeElement, nodes.get('pilotError'));
  assert.match(nodes.get('pilotError').textContent, /Código inválido/);
  assert.equal(nodes.get('pilotPairButton').disabled, false);
});