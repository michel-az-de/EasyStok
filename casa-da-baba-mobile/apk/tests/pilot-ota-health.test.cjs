'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { confirmOtaHealth, otaStatusText } = require('../pilot.js');
function fixture() {
  const data = new Map([['cdb-schema-version', '2'], ['cdb-orders', '[{"id":"pedido-preservado"}]'], ['cdb-sync-queue', '[{"id":"pendente"}]']]);
  const calls = [];
  const appConfig = { bundleVersion: '0.3.1', dataSchemaVersion: 2 };
  const environment = { etqAbrirImprimir() {}, etqImpImprimir() {}, etqEditorAbrir() {}, initEditor() {},
    cdbPilotUiReady: true, cdbPilotSettingsReady: true,
    cdbSync: { localReady: Promise.resolve({ ready: true }) },
    cdbApp: { getState: () => ({ products: [], clients: [], orders: [{ id: 'pedido-preservado' }], cashEntries: [], batches: [], cashClosings: [] }) },
    localStorage: { getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value), removeItem: key => data.delete(key) },
  };
  const updater = { ready: async value => calls.push(value) };
  return { data, calls, environment, appConfig, updater };
}
test('ACK espera bootstrap local e preserva pedido e fila pendente mesmo offline', async () => {
  const f = fixture();
  const before = [...f.data];
  let release;
  f.environment.cdbSync.localReady = new Promise(resolve => { release = resolve; });
  f.environment.navigator = { onLine: false };
  const task = confirmOtaHealth(f.environment, f.appConfig, f.updater);
  await Promise.resolve();
  assert.equal(f.calls.length, 0);
  release({ ready: true });
  await task;
  assert.deepEqual(f.calls, [{ bundleVersion: '0.3.1' }]);
  assert.deepEqual([...f.data], before);
});
test('não confirma versão quando stores degradam ou bootstrap falha', async () => {
  for (const state of [null, { ready: false }, { ready: true }]) {
    const f = fixture();
    f.environment.cdbSync.localReady = Promise.resolve(state);
    if (state?.ready) f.environment.cdbApp.getState = () => ({ orders: [] });
    await assert.rejects(confirmOtaHealth(f.environment, f.appConfig, f.updater));
    assert.equal(f.calls.length, 0);
  }
});
test('bloqueia ACK de schema incompatível e JSON corrompido sem sobrescrever dados', async () => {
  for (const [key, value] of [['cdb-schema-version', '3'], ['cdb-orders', '{bad'], ['cdb-products', '{}']]) {
    const f = fixture(); f.data.set(key, value);
    const before = [...f.data];
    await assert.rejects(confirmOtaHealth(f.environment, f.appConfig, f.updater));
    assert.equal(f.calls.length, 0); assert.deepEqual([...f.data], before);
  }
});
test('armazenamento cheio impede ACK; não limpa pedidos nem fila para abrir espaço', async () => {
  const f = fixture(); const before = [...f.data];
  f.environment.localStorage.setItem = () => { throw new Error('QuotaExceededError'); };
  await assert.rejects(confirmOtaHealth(f.environment, f.appConfig, f.updater));
  assert.equal(f.calls.length, 0); assert.deepEqual([...f.data], before);
});
test('chave de diagnóstico preexistente também é preservada', async () => {
  const f = fixture(); f.data.set('cdb-ota-health-probe', 'anterior');
  const before = [...f.data];
  await confirmOtaHealth(f.environment, f.appConfig, f.updater);
  assert.deepEqual([...f.data], before);
});
test('status distingue download preparado, recuperação e falha sem prometer push', () => {
  assert.match(otaStatusText({ activeVersion: '0.3.0', pendingVersion: '0.3.1' }), /próxima abertura/);
  assert.match(otaStatusText({ rolledBack: true }), /recuperada/);
  assert.match(otaStatusText({ lastError: 'network' }), /mantida/);
  assert.match(otaStatusText({ activeVersion: '0.3.0' }), /abrir ou retomar/);
  assert.doesNotMatch(otaStatusText({ activeVersion: '0.3.0' }), /push/i);
});

test('falha de módulo de etiquetas impede ACK mesmo com armazenamento saudável', async () => {
  const f = fixture(); delete f.environment.etqImpImprimir;
  await assert.rejects(confirmOtaHealth(f.environment, f.appConfig, f.updater), /Módulos/);
  assert.equal(f.calls.length, 0);
});
test('script do piloto que falhou ao instalar impede ACK e deixa o rollback nativo agir', async () => {
  for (const flag of ['cdbPilotUiReady', 'cdbPilotSettingsReady']) {
    const f = fixture(); delete f.environment[flag];
    await assert.rejects(confirmOtaHealth(f.environment, f.appConfig, f.updater), /piloto/);
    assert.equal(f.calls.length, 0, flag);
  }
});
test('aguarda load para confirmar módulos defer, sem recarregar ou interromper o app', async () => {
  const f = fixture(); let loaded;
  f.environment.document = { readyState: 'loading' };
  f.environment.addEventListener = (name, resolve) => { assert.equal(name, 'load'); loaded = resolve; };
  const pending = confirmOtaHealth(f.environment, f.appConfig, f.updater);
  await Promise.resolve(); assert.equal(f.calls.length, 0);
  loaded(); await pending; assert.equal(f.calls.length, 1);
});