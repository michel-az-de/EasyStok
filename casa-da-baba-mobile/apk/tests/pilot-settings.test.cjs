'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { installSettings, STEPS } = require('../pilot-settings.js');
const { pilotIcon } = require('../pilot.js');
const { commandFor } = require('../pilot-ui.js');

test('importar e exportar mantêm direções visuais distintas', () => {
  const incoming = commandFor('Importar backup'), outgoing = commandFor('Exportar backup');
  assert.notEqual(incoming.icon, outgoing.icon);
  assert.notEqual(pilotIcon(incoming.icon), pilotIcon(outgoing.icon));
});

function settings() {
  const nodes = new Map(), handlers = new Map(); let nested = false;
  const title = {}, sheet = { scrollTop: 100 }, head = { prepend(node) { nodes.set('back', node); } };
  const tabs = { querySelectorAll: () => [], after() {} };
  const modal = { dataset: {}, classList: { contains: () => true }, addEventListener(k, fn) { handlers.set(k, fn); },
    querySelector: selector => ({ '.ajustes-head h3': title, '.ajustes-tabs': tabs, '.ajustes-head': head, '.ajustes-sheet': sheet })[selector], querySelectorAll: () => [] };
  const calls = [];
  const environment = { cdbPilotIcon: pilotIcon, cdbMenuButton: () => '',
    document: { getElementById: id => id === 'ajustesModal' ? modal : null,
      querySelector: () => nested, createElement: () => ({ setAttribute() {}, addEventListener(k, fn) { this[k] = fn; } }) },
    openAjustes() { calls.push('open'); }, closeAjustes() { calls.push('close'); },
    setAjustesTab(group) { calls.push(group); }, cdbHandleNativeBack() { calls.push('native'); return false; } };
  installSettings(environment);
  return { environment, modal, title, calls, nodes, handlers, nest(value) { nested = value; } };
}
test('Configurações abre na lista; Voltar do Android retorna à lista e preserva o formulário', () => {
  const s = settings(); s.environment.openAjustes();
  assert.equal(s.modal.dataset.view, 'root'); assert.match(s.title.innerHTML, /Configurações/);
  s.environment.setAjustesTab('loja');
  assert.equal(s.modal.dataset.view, 'loja'); assert.match(s.title.innerHTML, /Geral/);
  s.handlers.get('input')(); assert.equal(s.modal.dataset.dirty, 'true');
  assert.equal(s.environment.cdbHandleNativeBack(), true);
  assert.equal(s.modal.dataset.view, 'root'); assert.equal(s.modal.dataset.dirty, 'true');
  assert.deepEqual(s.calls, ['open', 'loja']);
  assert.equal(s.environment.cdbHandleNativeBack(), false); assert.equal(s.calls.at(-1), 'native');
});
test('Voltar não atravessa um diagnóstico, uma impressora ou uma prévia aberta', () => {
  const s = settings(); s.environment.setAjustesTab('eq'); s.nest(true);
  assert.equal(s.environment.cdbHandleNativeBack(), false);
  assert.equal(s.modal.dataset.view, 'eq'); assert.equal(s.calls.at(-1), 'native');
});
const source = fs.readFileSync(path.resolve(__dirname, '../../../EasyStock.Api/wwwroot/pwa/index.html'), 'utf8');
test('Diagnóstico sem pareamento não anuncia envio nem usa o flush legado', async () => {
  const start = source.indexOf('function diagForceFlush()'), end = source.indexOf('function diagForcePull()', start);
  let refreshed = 0, legacy = 0;
  const context = { window: { CDB_CONFIG: { isPilot: true }, cdbSync: { flush() { legacy++; } } },
    syncNow: async () => 'auth', openDiagnostics: () => refreshed++, showToast: () => assert.fail('Não pode afirmar sucesso') };
  vm.createContext(context); vm.runInContext(source.slice(start, end), context);
  assert.equal(await context.diagForceFlush(), 'auth');
  assert.equal(refreshed, 1); assert.equal(legacy, 0);
});
test('atalho da impressora fecha Configurações antes de abrir Diagnóstico no piloto', () => {
  const code = source.match(/function printerDiagnostics\(\) \{[^\n]+\}/)[0];
  const calls = [], context = { window: { CDB_CONFIG: { isPilot: true } }, closeAjustes: () => calls.push('close'), openDiagnostics: () => calls.push('diagnostics') };
  vm.createContext(context); vm.runInContext(code, context); context.printerDiagnostics();
  assert.deepEqual(calls, ['close', 'diagnostics']);
});
test('todos os passos do guia têm captura local disponível para uso offline', () => {
  for (const step of STEPS) {
    assert.ok(step.alt); assert.ok(!/^https?:/.test(step.image));
    const file = path.resolve(__dirname, '../assets/guia', step.image);
    assert.ok(fs.existsSync(file), 'Captura ausente: ' + step.image);
    assert.ok(fs.statSync(file).size > 1000, 'Captura vazia: ' + step.image);
  }
});
