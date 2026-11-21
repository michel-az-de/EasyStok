const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');

// Executa o script de produção; somente DOM, Capacitor e sync são portas simuladas.
// O override é exclusivo do registro Red, antes da correção, em .build/2184.
const sourcePath = process.env.EASYSTOK_2184_RED_DIR
  ? path.join(process.env.EASYSTOK_2184_RED_DIR, 'pilot-auth.js')
  : path.join(__dirname, '..', 'pilot-auth.js');
const source = fs.readFileSync(sourcePath, 'utf8');
const deferred = () => {
  let resolve, reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
};
const settle = () => new Promise(resolve => setImmediate(resolve));
const account = (name = 'A', versao = 1) => ({
  autenticado: true, versao, sessaoId: `session-${name}`, dispositivoId: 'device-qa',
  empresaId: 'company-qa', usuario: { id: `person-${name}`, nome: `Pessoa QA ${name}` },
});
const connection = profile => ({
  versao: profile.versao, sessaoId: profile.sessaoId, usuario: profile.usuario,
  apiKey: `fixture-${profile.sessaoId}`, deviceId: 'device-qa', empresaId: 'company-qa',
  lojaId: 'shop-qa', empresaNome: 'Empresa QA', lojaNome: 'Loja QA',
});

function fixture({ review = false, initial = account(), status, pair } = {}) {
  const elements = new Map(), documentEvents = new Map(), windowEvents = new Map();
  const calls = { pair: [], apply: [], flush: 0, pull: 0, clear: 0 };
  const state = { current: initial, status, pair, logout: null, pairing: null };
  let sessionChanged;
  function element(key) {
    if (!elements.has(key)) elements.set(key, {
      hidden: false, disabled: false, textContent: '', value: '', open: false,
      setAttribute() {}, append() {}, replaceChildren() {},
      addEventListener(name, callback) { this[name] = callback; },
      querySelector: element, showModal() { this.open = true; }, close() { this.open = false; },
    });
    return elements.get(key);
  }
  const native = {
    authStatus: () => state.status ? state.status() : Promise.resolve(state.current),
    authLogin: () => Promise.resolve(state.current),
    authPair: data => {
      calls.pair.push(data);
      return state.pair ? state.pair(data) : Promise.resolve(connection(state.current));
    },
    authLogout: () => state.logout ? state.logout() : Promise.resolve({ autenticado: false, versao: state.current.versao + 1 }),
    addListener: async (name, callback) => { assert.equal(name, 'sessionChanged'); sessionChanged = callback; },
  };
  const sync = {
    localReady: Promise.resolve(), getDeviceId: () => 'device-qa', getDataContext: () => null,
    getPairing: () => state.pairing, queueSize: () => review ? 8 : 0,
    isPairingInvalid: () => false,
    applySessionPairing(data, options) {
      if (review && options.confirmLocalContext !== true)
        throw Object.assign(new Error('Revisão necessária'), { code: 'LOCAL_CONTEXT_REQUIRED' });
      calls.apply.push(data); state.pairing = data;
    },
    flush() { calls.flush++; }, pull() { calls.pull++; }, startRealtime() {}, stopRealtime() {},
    clearPairing() { calls.clear++; state.pairing = null; },
  };
  const document = {
    readyState: 'loading', body: { append() {} }, createElement: () => element('dialog'),
    addEventListener: (name, callback) => documentEvents.set(name, callback),
  };
  const window = {
    document, cdbSync: sync, Capacitor: { Plugins: { PilotApp: native } },
    dispatchEvent() {}, addEventListener: (name, callback) => windowEvents.set(name, callback),
  };
  vm.runInNewContext(source, { window, console, CustomEvent: class {
    constructor(type, options) { this.type = type; this.detail = options?.detail; }
  } }, { filename: sourcePath });
  return {
    calls, state, element, window,
    start: () => documentEvents.get('DOMContentLoaded')(),
    change: event => sessionChanged(event),
    online: () => windowEvents.get('online')(),
    click: action => element('dialog').click({ target: { closest: () => ({ dataset: { account: action } }) } }),
  };
}

test('Vincular não aplica dados nem inicia sync enquanto Sair está pendente', async () => {
  const f = fixture({ review: true });
  await f.start();
  assert.equal(f.element('[data-account="confirm"]').hidden, false);
  const pending = deferred();
  f.state.logout = () => pending.promise;
  const leaving = f.click('logout');
  try {
    await f.click('confirm'); // Também cobre ativação já enfileirada, além do disabled visual.
    assert.equal(f.calls.apply.length, 0, 'Sair invalida imediatamente a revisão');
    assert.equal(f.calls.flush, 0, 'nenhum envio começa durante a saída');
    assert.ok(f.element('[data-account="confirm"]').disabled || f.element('[data-account="confirm"]').hidden);
  } finally { pending.resolve({ autenticado: false, versao: 2 }); await leaving; }
});

test('Selecionar loja não solicita novo vínculo enquanto Sair está pendente', async () => {
  const f = fixture({ pair: async () => { throw Object.assign(new Error('Escolha loja'), {
    code: 'LOJA_REQUIRED', data: { lojas: [{ id: 'shop-qa', nome: 'Loja QA' }] },
  }); } });
  await f.start();
  const select = f.element('select'); select.value = 'shop-qa'; select.change();
  const before = f.calls.pair.length, pending = deferred();
  f.state.logout = () => pending.promise;
  const leaving = f.click('logout');
  try {
    await f.click('shop');
    assert.equal(f.calls.pair.length, before, 'troca de loja não atravessa a saída');
    assert.equal(f.calls.apply.length, 0);
    assert.equal(f.calls.flush, 0);
  } finally { pending.resolve({ autenticado: false, versao: 2 }); await leaving; }
});

test('authPair recebido depois de Sair não reconecta nem envia a fila', async () => {
  const pending = deferred(), f = fixture({ pair: () => pending.promise });
  const starting = f.start(); await settle();
  assert.equal(f.calls.pair.length, 1);
  await f.click('logout');
  pending.resolve(connection(account())); await starting;
  assert.equal(f.window.cdbAuth.current()?.autenticado, false);
  assert.equal(f.calls.apply.length, 0, 'vínculo da sessão encerrada é descartado');
  assert.equal(f.calls.flush, 0);
});

test('authStatus anterior ao Sair no Workspace não restaura perfil nem conecta', async () => {
  const pending = deferred(), f = fixture({ status: () => pending.promise });
  const starting = f.start(); await settle();
  await f.change({ autenticado: false, versao: 2 });
  pending.resolve(account('A', 1)); await starting;
  assert.equal(f.window.cdbAuth.current()?.autenticado, false, 'a versão encerrada não volta ao perfil');
  assert.equal(f.calls.pair.length, 0);
  assert.equal(f.calls.apply.length, 0);
});

test('authStatus tardio de A não substitui o usuário B nem reaproveita sua restauração', async () => {
  const f = fixture(); await f.start();
  const pending = deferred(); let statusCalls = 0;
  f.state.status = () => ++statusCalls === 1 ? pending.promise : Promise.resolve(account('B', 3));
  const oldRestore = f.window.cdbAuth.restore();
  await f.change({ autenticado: false, versao: 2 });
  f.state.current = account('B', 3);
  const newSession = f.change({ autenticado: true, versao: 3, sessaoId: 'session-B', usuario: { id: 'person-B' } });
  await settle(); pending.resolve(account('A', 1));
  await Promise.allSettled([oldRestore, newSession]); await settle();
  assert.equal(f.window.cdbAuth.current()?.usuario?.id, 'person-B');
  assert.equal(f.window.cdbAuth.current()?.versao, 3);
});

test('authPair tardio de A não instala credencial depois da entrada de B', async () => {
  const pending = deferred(), f = fixture({ pair: () => pending.promise });
  const starting = f.start(); await settle();
  await f.change({ autenticado: false, versao: 2 });
  f.state.current = account('B', 3);
  f.state.pair = async () => connection(f.state.current);
  const newSession = f.change({ autenticado: true, versao: 3, sessaoId: 'session-B', usuario: { id: 'person-B' } });
  await settle(); pending.resolve(connection(account('A', 1)));
  await Promise.allSettled([starting, newSession]); await settle();
  assert.equal(f.window.cdbAuth.current()?.usuario?.id, 'person-B');
  assert.ok(f.calls.apply.every(data => data.versao === 3), 'somente B pode instalar uma conexão');
});

test('API key recusada é comunicada ao nativo sem ser reapresentada como apiKeyAtual', async () => {
  const f = fixture(), rejected = connection(account());
  f.state.pairing = rejected;
  f.window.cdbSync.isPairingInvalid = () => true;
  f.state.pair = async () => ({ ...rejected, apiKey: 'fixture-renovada' });
  await f.start();
  assert.equal(f.calls.pair.length, 1);
  assert.equal(f.calls.pair[0].apiKeyRejeitada, rejected.apiKey);
  assert.equal(Object.hasOwn(f.calls.pair[0], 'apiKeyAtual'), false, 'credencial recusada não é apresentada como válida');
  assert.equal(f.calls.apply.length, 1);
  assert.equal(f.calls.apply[0].apiKey, 'fixture-renovada', 'só a resposta revalidada é aplicada');
});

test('evento de saída antes de authLogout resolver mantém bloqueio e conclui feedback sem reconectar', async () => {
  const f = fixture(); await f.start();
  const before = { apply: f.calls.apply.length, flush: f.calls.flush, pair: f.calls.pair.length };
  const pending = deferred();
  f.state.logout = async () => {
    // NativeSession publica changed(false) antes de entregar o callback de authLogout.
    await f.change({ autenticado: false, versao: 2 });
    return pending.promise;
  };
  const leaving = f.click('logout'); await settle();
  try {
    assert.equal(f.window.cdbAuth.current()?.autenticado, false);
    assert.equal(f.element('[data-account="login"]').disabled, true, 'a própria saída ainda aguarda seu callback');
    await f.click('confirm');
    assert.equal(f.calls.apply.length, before.apply);
    assert.equal(f.calls.flush, before.flush);
  } finally { pending.resolve({ autenticado: false, versao: 2 }); await leaving; }
  assert.equal(f.element('#pilotAccountNotice').textContent, 'Você saiu deste aparelho. Os dados e envios pendentes foram mantidos.');
  assert.equal(f.element('[data-account="login"]').disabled, false);
  assert.equal(f.calls.pair.length, before.pair);
  assert.equal(f.calls.apply.length, before.apply);
  assert.equal(f.calls.flush, before.flush);
});

test('voltar online retoma vínculo conhecido que falhou por rede, sem reconfirmar origem', async () => {
  const f = fixture(), saved = connection(account());
  f.state.pairing = saved;
  f.window.cdbSync.getDataContext = () => ({ empresaId: saved.empresaId, lojaId: saved.lojaId });
  let attempts = 0;
  f.state.pair = async () => {
    if (++attempts === 1) throw new Error('Sem rede para renovar a sessão vencida');
    return { ...saved, apiKey: 'fixture-retomada-online' };
  };
  const apply = f.window.cdbSync.applySessionPairing;
  f.window.cdbSync.applySessionPairing = (data, options) => {
    assert.equal(options.confirmLocalContext, false, 'origem conhecida não exige nem simula nova confirmação');
    return apply(data, options);
  };
  await f.start();
  assert.equal(f.calls.pair.length, 1);
  assert.equal(f.window.cdbAuth.canSync(), false);
  assert.equal(f.state.pairing, saved, 'falha de rede conserva o vínculo local');
  assert.equal(f.calls.clear, 0);
  assert.equal(f.calls.apply.length, 0);
  assert.equal(f.calls.flush, 0, 'a fila não é enviada antes da revalidação');

  f.online(); await settle();
  assert.equal(f.calls.pair.length, 2, 'online tenta outra authPair mesmo com vínculo local existente');
  assert.equal(f.calls.pair[1].apiKeyAtual, saved.apiKey);
  assert.equal(f.calls.pair[1].empresaId, saved.empresaId);
  assert.equal(f.calls.pair[1].lojaId, saved.lojaId);
  assert.equal(f.window.cdbAuth.canSync(), true);
  assert.equal(f.state.pairing.apiKey, 'fixture-retomada-online');
  assert.equal(f.calls.clear, 0, 'retomada não apaga vínculo nem pede limpeza dos dados');
  assert.equal(f.calls.apply.length, 1);
  assert.equal(f.calls.flush, 1);
  assert.equal(f.element('[data-account="confirm"]').hidden, true);

  f.online(); await settle();
  assert.equal(f.calls.pair.length, 2, 'nova notificação online não refaz um vínculo já pronto');

  const unknown = fixture({ review: true }); await unknown.start();
  assert.equal(unknown.element('[data-account="confirm"]').hidden, false);
  unknown.online(); await settle();
  assert.equal(unknown.calls.pair.length, 1, 'online não repete uma revisão de origem já pendente');
  assert.equal(unknown.calls.apply.length, 0, 'origem desconhecida continua aguardando a pessoa');
  assert.equal(unknown.calls.flush, 0);
});

test('ensureSignedIn rejeita authLogin sem autenticação e não inicia pareamento nem sync', async () => {
  const f = fixture({ initial: { autenticado: false, versao: 0 } });
  await f.start();
  await assert.rejects(f.window.cdbAuth.ensureSignedIn(), error => /Google/i.test(error.message));
  assert.equal(f.window.cdbAuth.current()?.autenticado, false);
  assert.equal(f.calls.pair.length, 0);
  assert.equal(f.calls.apply.length, 0);
  assert.equal(f.calls.flush, 0);
  assert.equal(f.calls.pull, 0);
});
