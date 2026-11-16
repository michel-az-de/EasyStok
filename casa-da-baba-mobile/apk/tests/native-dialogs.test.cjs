'use strict';
// Contrato da casca nativa: alert/confirm/prompt do JavaScript aparecem em português.
// O BridgeWebChromeClient do Capacitor fixa "OK" e "Cancel"; PilotChromeClient troca os rótulos
// e a MainActivity o instala logo após criar a Bridge (antes do estado STARTED).
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const main = path.join(__dirname, '..', 'android', 'app', 'src', 'main');
const read = (...parts) => fs.readFileSync(path.join(main, ...parts), 'utf8');
const clientPath = ['java', 'com', 'casadababa', 'app', 'PilotChromeClient.java'];

test('rótulos em português ficam em strings.xml', () => {
  const strings = read('res', 'values', 'strings.xml');
  assert.match(strings, /<string name="dialog_ok">OK<\/string>/);
  assert.match(strings, /<string name="dialog_cancel">Cancelar<\/string>/);
});

test('PilotChromeClient estende o cliente do Capacitor e sobrescreve os três diálogos', () => {
  const source = read(...clientPath);
  assert.match(source, /class PilotChromeClient extends BridgeWebChromeClient/);
  for (const method of ['onJsAlert', 'onJsConfirm', 'onJsPrompt'])
    assert.match(source, new RegExp(`public boolean ${method}\\(`), `${method} sobrescrito`);
  assert.match(source, /R\.string\.dialog_ok/);
  assert.match(source, /R\.string\.dialog_cancel/);
  assert.doesNotMatch(source, /"Cancel"/, 'sem rótulo em inglês fixo');
});

test('PilotChromeClient preserva o comportamento do original', () => {
  const source = read(...clientPath);
  assert.equal((source.match(/isFinishing\(\)/g) || []).length, 3, 'guarda de isFinishing nos três');
  assert.equal((source.match(/setOnCancelListener/g) || []).length, 3, 'tocar fora cancela nos três');
  assert.match(source, /setNegativeButton/);
  assert.match(source, /input\.setText\(defaultValue\)/, 'prompt mostra o valor padrão');
  assert.match(source, /result\.confirm\(input\.getText\(\)\.toString\(\)\.trim\(\)\)/);
});

test('MainActivity instala o cliente após criar a Bridge e não troca o cliente depois', () => {
  const source = read('java', 'com', 'casadababa', 'app', 'MainActivity.java');
  const load = source.slice(source.indexOf('protected void load()'));
  const created = load.indexOf('super.load();');
  const installed = load.indexOf('setWebChromeClient(new PilotChromeClient(bridge))');
  assert.ok(created > 0 && installed > created, 'instalado depois de super.load(), em onCreate');
  assert.equal((source.match(/setWebChromeClient/g) || []).length, 1);
});
