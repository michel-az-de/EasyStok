'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const html = fs.readFileSync(path.resolve(__dirname, '../../../EasyStock.Api/wwwroot/pwa/index.html'), 'utf8');
const backSource = html.slice(html.indexOf('(function setupBackButton() {'), html.indexOf('// Forca checagem manual de update'));

function element(id, open) {
  const classes = new Set(open ? ['show'] : []);
  return {
    id,
    // Overlays do app usam position: fixed, então offsetParent é sempre null.
    offsetParent: null,
    classList: { contains: name => classes.has(name), remove: name => classes.delete(name), add: name => classes.add(name) },
    getClientRects: () => (classes.has('show') ? [{}] : []),
    get isOpen() { return classes.has('show'); }
  };
}

function loadBack({ open = [], screen = 'producao', pilot = true } = {}) {
  const nodes = new Map(open.map(id => [id, element(id, true)]));
  const calls = [];
  const context = {
    console,
    isPrinting: false,
    currentScreen: screen,
    document: {
      getElementById: id => nodes.get(id) || null,
      querySelectorAll: selector => [...nodes.values()].filter(node => node.isOpen && selector.includes('.np-overlay.show')),
      querySelector: () => null,
      addEventListener() {}
    },
    history: { pushState() {} },
    location: { href: 'https://localhost/' },
    setTimeout() {},
    goBack: () => calls.push('goBack'),
    go: target => calls.push('go:' + target),
    closeMoreMenu() {},
    closePrinterPicker: () => { calls.push('closePrinterPicker'); nodes.get('printerPickerModal')?.classList.remove('show'); }
  };
  context.window = context;
  context.window.CDB_CONFIG = { isPilot: pilot };
  context.window.addEventListener = () => {};
  vm.createContext(context);
  vm.runInContext(backSource, context);
  return { back: context.cdbHandleBack, calls, nodes };
}

test('Voltar fecha o seletor de impressoras pelo fechador próprio', () => {
  const { back, calls, nodes } = loadBack({ open: ['printerPickerModal'] });
  assert.equal(back(), true);
  assert.deepEqual(calls, ['closePrinterPicker']);
  assert.equal(nodes.get('printerPickerModal').isOpen, false);
});

test('Voltar fecha o seletor de impressoras antes de Configurações aberta por baixo', () => {
  const { back, calls } = loadBack({ open: ['ajustesModal', 'printerPickerModal'] });
  back();
  assert.deepEqual(calls, ['closePrinterPicker']);
});

test('overlay genérico com posição fixa também fecha no Voltar', () => {
  const { back, calls, nodes } = loadBack({ open: ['overlayDesconhecido'] });
  assert.equal(back(), true);
  assert.equal(nodes.get('overlayDesconhecido').isOpen, false);
  assert.deepEqual(calls, []);
});

test('no APK, Voltar em Produção ou Pedidos volta sempre ao Início', () => {
  for (const screen of ['producao', 'pedidos']) {
    const { back, calls } = loadBack({ screen });
    assert.equal(back(), true);
    assert.deepEqual(calls, ['go:home'], screen);
  }
});

test('no APK, subtela continua voltando pela história', () => {
  const { back, calls } = loadBack({ screen: 'finalizados' });
  back();
  assert.deepEqual(calls, ['goBack']);
});

test('fora do APK, a navegação pela história permanece', () => {
  const { back, calls } = loadBack({ screen: 'pedidos', pilot: false });
  back();
  assert.deepEqual(calls, ['goBack']);
});

const pilot = fs.readFileSync(path.join(__dirname, '../pilot.js'), 'utf8');
const controls = pilot.slice(pilot.indexOf('<div class="pilot-home-controls">'), pilot.indexOf('<div class="pilot-home-support"'));

test('Início não duplica entradas que já moram em Configurações', () => {
  assert.doesNotMatch(controls, /migration-guide/, 'Sistema Antigo fica só em Configurações');
  assert.match(controls, /tileButton\('printers', 'Impressoras'/);
});

test('atalho Impressoras abre o mesmo lugar de Configurações > Etiquetas', () => {
  const action = pilot.match(/if \(action === 'printers'\) \{([^}]*)\}/)[1];
  assert.doesNotMatch(action, /openPrinterPicker/);
  assert.match(action, /openPrinterSettings\(\)/);
  const helper = pilot.slice(pilot.indexOf('function openPrinterSettings()'), pilot.indexOf('\n  }', pilot.indexOf('function openPrinterSettings()')));
  assert.match(helper, /root\.cdbOpenAjustesAt\('eq'\)/);
  assert.match(helper, /impressorasLista/);
});
