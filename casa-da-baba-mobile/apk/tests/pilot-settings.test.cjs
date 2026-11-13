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

function settings({ native = false } = {}) {
  const nodes = new Map(), handlers = new Map(); let nested = false;
  let environment;
  const node = () => ({ attributes: {}, isConnected: true,
    setAttribute(k, v) { this.attributes[k] = v; },
    focus() { environment.document.activeElement = this; },
    addEventListener(k, fn) { this[k] = fn; } });
  const title = node(), opener = node(), sheet = { scrollTop: 100 }, head = { prepend(value) { nodes.set('back', value); } };
  const saved = { ajusteOperator: 'Operador', ajusteEmpresa: 'Empresa', ajustePixType: 'telefone', ajustePixKey: '', ajustePixHolder: '' };
  const fields = Object.fromEntries(Object.entries(saved).map(([id, value]) => {
    const label = node(), field = { ...node(), id, value, closest: () => ({ querySelector: () => label }), label };
    return [id, field];
  }));
  const tabs = { querySelectorAll: () => [], after() {} };
  const update = node(), printerDiagnostics = node();
  let shown = false, answer = false;
  const modal = { ...node(), id: 'ajustesModal', dataset: {}, getClientRects: () => shown ? [{}] : [], classList: { contains: () => shown }, addEventListener(k, fn) { handlers.set(k, fn); },
    querySelector: selector => ({ '.ajustes-head h3': title, '.ajustes-tabs': tabs, '.ajustes-head': head, '.ajustes-sheet': sheet,
      '[onclick="forceCheckUpdate()"]': update, '[onclick="printerDiagnostics()"]': printerDiagnostics })[selector], querySelectorAll: () => [] };
  const calls = [];
  environment = { cdbPilotIcon: pilotIcon, cdbMenuButton: () => '',
    document: { activeElement: opener, getElementById: id => id === 'ajustesModal' ? modal : fields[id] || null,
      querySelector: selector => selector === '.screen.active' ? { dataset: { screen: 'home' } } : nested,
      querySelectorAll: () => [], createElement: node, addEventListener() {} },
    confirm(message) { calls.push('confirm'); assert.match(message, /Descartar.*não salv/); return answer; },
    openAjustes() { calls.push('open'); shown = true; for (const id in saved) fields[id].value = saved[id]; },
    closeAjustes() { calls.push('close'); shown = false; },
    openDiagnostics() { calls.push('diagnostics'); },
    go(target) { calls.push('go:' + target); }, forceCheckUpdate() { calls.push('update'); },
    operatorName: saved.ajusteOperator,
    getEmpresaName: () => saved.ajusteEmpresa, getPixType: () => saved.ajustePixType,
    getPixKey: () => saved.ajustePixKey, getPixHolder: () => saved.ajustePixHolder,
    validatePixKey: key => key !== 'invalida', showToast: text => calls.push(text),
    saveOperatorName(value) { saved.ajusteOperator = value; environment.operatorName = value; },
    cdbSetPix(key, holder, type, empresa) { Object.assign(saved, { ajustePixKey: key || '', ajustePixHolder: holder || '', ajustePixType: type, ajusteEmpresa: empresa || '' }); },
    renderBrandName() {}, diffObjects: () => null,
    setAjustesTab(group) { calls.push(group); }, cdbHandleNativeBack() { calls.push('native'); return false; } };
  const start = source.indexOf('function saveAjustes()');
  vm.runInNewContext(source.slice(start, source.indexOf('// THERMAL PRINTER NATIVE SHIM', start)), environment);
  if (native) {
    Object.assign(environment, { window: environment, console, isPrinting: false, currentScreen: 'home', CDB_CONFIG: { isPilot: true },
      cdbApp: { getOperator: () => saved.ajusteOperator }, history: { pushState() {} }, location: { href: 'https://localhost/' },
      setTimeout() {}, addEventListener() {} });
    vm.runInNewContext(source.slice(source.indexOf('(function setupBackButton() {'), source.indexOf('// Forca checagem manual de update')), environment);
    const pilot = fs.readFileSync(path.resolve(__dirname, '../pilot.js'), 'utf8');
    vm.runInNewContext(pilot.slice(pilot.indexOf('  root.cdbHandleNativeBack = function () {'), pilot.indexOf('  function showError(message)')),
      { root: environment, doc: environment.document, notices: { open: false }, home: { open: false }, openHome: () => calls.push('home') });
  }
  installSettings(environment);
  return { environment, modal, title, calls, nodes, handlers, fields, saved, opener, update, printerDiagnostics,
    answer(value) { answer = value; }, nest(value) { nested = value; } };
}
test('Configurações abre na lista; Voltar do Android retorna à lista e preserva o formulário', () => {
  const s = settings(); s.environment.openAjustes();
  assert.equal(s.modal.dataset.view, 'root'); assert.match(s.title.innerHTML, /Configurações/);
  s.environment.setAjustesTab('loja');
  assert.equal(s.modal.dataset.view, 'loja'); assert.match(s.title.innerHTML, /Geral/);
  s.fields.ajusteOperator.value = 'Rascunho'; s.handlers.get('input')(); assert.equal(s.modal.dataset.dirty, 'true');
  assert.equal(s.environment.cdbHandleNativeBack(), true);
  assert.equal(s.modal.dataset.view, 'root'); assert.equal(s.modal.dataset.dirty, 'true');
  assert.deepEqual(s.calls, ['open', 'loja']);
  assert.equal(s.environment.cdbHandleNativeBack(), true); assert.equal(s.calls.at(-1), 'confirm');
});
test('Voltar não atravessa um diagnóstico, uma impressora ou uma prévia aberta', () => {
  const s = settings(); s.environment.openAjustes(); s.environment.setAjustesTab('eq'); s.nest(true);
  assert.equal(s.environment.cdbHandleNativeBack(), false);
  assert.equal(s.modal.dataset.view, 'eq'); assert.equal(s.calls.at(-1), 'native');
});
const source = fs.readFileSync(path.resolve(__dirname, '../../../EasyStock.Api/wwwroot/pwa/index.html'), 'utf8');

test('Voltar preserva o rascunho quando o descarte é cancelado; confirmar restaura o salvo ao reabrir', () => {
  const s = settings(); s.environment.openAjustes(); s.environment.setAjustesTab('loja');
  s.fields.ajusteOperator.value = 'Rascunho'; s.handlers.get('input')();
  s.nodes.get('back').click();
  assert.equal(s.modal.dataset.view, 'root');
  assert.equal(s.calls.includes('confirm'), false, 'voltar à lista não descarta o formulário');
  s.nodes.get('back').click();
  assert.equal(s.modal.classList.contains('show'), true);
  assert.equal(s.fields.ajusteOperator.value, 'Rascunho');
  assert.equal(s.saved.ajusteOperator, 'Operador');
  s.answer(true); s.nodes.get('back').click();
  assert.equal(s.modal.classList.contains('show'), false);
  assert.equal(s.environment.document.activeElement, s.opener);
  s.environment.openAjustes();
  assert.equal(s.fields.ajusteOperator.value, 'Operador');
});

test('os cinco campos salvos protegem a saída; edição revertida e controles imediatos não pedem descarte', () => {
  const s = settings();
  for (const id of Object.keys(s.fields)) {
    s.environment.openAjustes(); s.fields[id].value += ' editado'; s.handlers.get('change')();
    assert.equal(s.environment.closeAjustes(), false, id);
    assert.equal(s.modal.classList.contains('show'), true, id);
    s.fields[id].value = s.saved[id]; s.handlers.get('input')();
    const prompts = s.calls.filter(x => x === 'confirm').length;
    s.environment.closeAjustes();
    assert.equal(s.calls.filter(x => x === 'confirm').length, prompts, id);
  }
  s.environment.openAjustes(); s.handlers.get('change')();
  assert.equal(s.modal.dataset.dirty, 'false', 'evento de controle imediato não é rascunho dos campos');
});

test('Salvar real fecha sem confirmação; PIX inválido mantém formulário protegido', () => {
  const s = settings(); s.environment.openAjustes();
  s.fields.ajusteOperator.value = 'Novo operador'; s.handlers.get('input')();
  s.environment.saveAjustes();
  assert.equal(s.saved.ajusteOperator, 'Novo operador');
  assert.equal(s.modal.classList.contains('show'), false);
  assert.equal(s.calls.includes('confirm'), false);
  s.environment.openAjustes(); s.fields.ajustePixKey.value = 'invalida'; s.handlers.get('input')();
  s.environment.saveAjustes();
  assert.equal(s.modal.classList.contains('show'), true);
  assert.equal(s.fields.ajustePixKey.value, 'invalida');
  assert.ok(s.calls.includes('Chave PIX inválida pra esse tipo'));
  assert.equal(s.environment.closeAjustes(), false);
});

test('atalho Impressoras, foco de entrada e rótulos existentes continuam operáveis', () => {
  const s = settings();
  assert.equal(s.environment.document.activeElement, s.opener, 'instalação não rouba foco');
  s.environment.cdbOpenAjustesAt('eq');
  assert.equal(s.environment.document.activeElement, s.title);
  assert.equal(s.modal.dataset.view, 'eq');
  s.nodes.get('back').click();
  assert.equal(s.modal.classList.contains('show'), false);
  assert.equal(s.environment.document.activeElement, s.opener);
  assert.equal(s.calls.includes('confirm'), false);
  for (const [id, field] of Object.entries(s.fields)) assert.equal(field.label.attributes.for, id);
  assert.equal(s.modal.attributes['aria-labelledby'], s.title.id);
  assert.equal(s.title.attributes.tabindex, '-1');
});

test('Diagnóstico não avança quando o descarte dos campos é cancelado', () => {
  const s = settings(); s.environment.openAjustes();
  s.fields.ajusteEmpresa.value = 'Rascunho'; s.handlers.get('input')();
  s.environment.openDiagnostics();
  assert.equal(s.calls.includes('diagnostics'), false);
  assert.equal(s.calls.filter(x => x === 'confirm').length, 1);
  s.answer(true); s.environment.openDiagnostics();
  assert.equal(s.calls.at(-1), 'diagnostics');
});

test('Voltar nativo real não abre Início sobre o rascunho quando o descarte é cancelado', () => {
  const s = settings({ native: true }); s.environment.openAjustes();
  s.fields.ajusteOperator.value = 'Rascunho'; s.handlers.get('input')();
  assert.equal(s.environment.cdbHandleNativeBack(), true);
  assert.equal(s.modal.classList.contains('show'), true);
  assert.equal(s.calls.includes('home'), false);
  assert.equal(s.calls.filter(x => x === 'confirm').length, 1);
  s.answer(true); s.environment.cdbHandleNativeBack();
  assert.equal(s.modal.classList.contains('show'), false);
  assert.equal(s.calls.at(-1), 'home');
  assert.equal(s.calls.filter(x => x === 'confirm').length, 2, 'uma pergunta por tentativa');
});

test('falha ao salvar mantém a proteção; Atualizar e Diagnóstico da impressora respeitam cancelar', () => {
  const s = settings(); s.environment.openAjustes();
  s.fields.ajustePixHolder.value = 'Rascunho'; s.handlers.get('input')();
  s.environment.cdbSetPix = () => { throw new Error('Falha local'); };
  assert.throws(() => s.environment.saveAjustes(), /Falha local/);
  assert.equal(s.environment.closeAjustes(), false);
  s.update.onclick(); s.printerDiagnostics.onclick();
  assert.equal(s.calls.includes('update'), false);
  assert.equal(s.calls.includes('diagnostics'), false);
  assert.equal(s.modal.classList.contains('show'), true);
  assert.equal(s.fields.ajustePixHolder.value, 'Rascunho');
});
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
