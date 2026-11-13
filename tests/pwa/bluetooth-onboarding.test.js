'use strict';
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

module.exports = function ({ test, assert }) {
  const html = fs.readFileSync(process.env.PWA_HTML || path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa/index.html'), 'utf8');
  const start = html.indexOf('let initialBluetoothOfferStarted = false;');
  const end = html.indexOf('// Hooks globais de erro', start);
  assert.ok(start >= 0 && end > start, 'bloco real de onboarding Bluetooth precisa existir');
  const source = html.slice(start, end);
  const offeredKey = 'cdb-bluetooth-initial-offer-v1';

  function boot(store = new Map([['cdb-onboarding-done', '1']]), nativeReady = true, pilot = false) {
    const listeners = {}, timers = [], callbacks = [];
    let requests = 0;
    const context = {
      document: { addEventListener: (name, fn) => { listeners[name] = fn; } },
      window: { addEventListener: (name, fn) => { listeners[name] = fn; } },
      localStorage: { getItem: key => store.get(key) || null, setItem: (key, value) => store.set(key, value) },
      setTimeout: fn => { timers.push(fn); },
      ensureBluetoothPermission: cb => { requests++; callbacks.push(cb); },
    };
    const ready = () => { context.window.Capacitor = { Plugins: { ThermalPrinter: { requestPermissions() {} } } }; };
    if (pilot) context.window.CDB_CONFIG = { isPilot: true };
    if (nativeReady) ready();
    vm.runInNewContext(source, context);
    return { store, ready, context, callbacks, get requests() { return requests; },
      deviceReady: () => listeners.deviceready(),
      operatorReady: () => listeners['cdb-operator-ready'](),
      load: () => { listeners.load(); timers.splice(0).forEach(fn => fn()); },
    };
  }

  test('Bluetooth: deviceready e fallback oferecem uma vez, inclusive após negar', () => {
    for (const first of ['deviceReady', 'load']) {
      const app = boot();
      app[first]();
      assert.equal(app.requests, 1);
      assert.equal(app.store.get(offeredKey), '1', 'oferta persiste antes de o usuário responder');
      app[first === 'deviceReady' ? 'load' : 'deviceReady']();
      assert.equal(app.requests, 1, 'diálogo pendente não pode receber outra solicitação');
      app.callbacks[0](false);
      app.deviceReady(); app.load();
      assert.equal(app.requests, 1, 'recusa não reativa a oferta automática');
    }
  });

  test('Bluetooth: sem onboarding concluído, nada é pedido nem gravado', () => {
    const app = boot(new Map([]));
    app.deviceReady(); app.load();
    assert.equal(app.requests, 0);
    assert.equal(app.store.has(offeredKey), false);
  });

  test('Bluetooth: no piloto (sem tour) a oferta sai depois da boas-vindas, não antes', () => {
    const store = new Map([]);
    const app = boot(store, true, true);
    app.deviceReady(); app.load();
    assert.equal(app.requests, 0, 'sem nome a boas-vindas ainda está na tela');
    store.set('cdb-operator-name', 'Ana');
    app.operatorReady();
    assert.equal(app.requests, 1, 'o piloto nunca grava cdb-onboarding-done: a oferta segue o fim da boas-vindas');
    const reaberto = boot(new Map([['cdb-operator-name', 'Ana']]), true, true);
    reaberto.deviceReady();
    assert.equal(reaberto.requests, 1, 'aparelho já identificado recebe a oferta ao abrir');
  });

  test('Bluetooth: reiniciar após recusa não repete oferta, mas ação explícita continua disponível', () => {
    const first = boot();
    first.deviceReady(); first.callbacks[0](false);
    const restarted = boot(first.store);
    restarted.deviceReady(); restarted.load();
    assert.equal(restarted.requests, 0);
    restarted.context.ensureBluetoothPermission(() => {});
    assert.equal(restarted.requests, 1, 'guarda inicial não bloqueia Impressoras/Imprimir');
  });

  test('Bluetooth: bridge ausente não consome a oferta e fallback pode atender bridge tardio', () => {
    const app = boot(new Map([['cdb-onboarding-done', '1']]), false);
    app.deviceReady();
    assert.equal(app.requests, 0);
    assert.equal(app.store.has(offeredKey), false);
    app.ready(); app.load();
    assert.equal(app.requests, 1);
  });

  test('Bluetooth: erro da solicitação inicial não dispara nova oferta no fallback ou reinício', () => {
    const app = boot();
    let errors = 0;
    app.context.ensureBluetoothPermission = () => { errors++; throw new Error('bridge indisponível'); };
    app.deviceReady(); app.load();
    assert.equal(errors, 1);
    const restarted = boot(app.store);
    restarted.deviceReady(); restarted.load();
    assert.equal(restarted.requests, 0);
  });
};
