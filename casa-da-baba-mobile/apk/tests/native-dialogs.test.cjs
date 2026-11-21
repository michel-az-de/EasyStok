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

// #2886: prompt de quantidade abria teclado de letras, sem foco, campo colado na borda e teal padrão.
test('prompt: valor padrão numérico curto abre teclado numérico com foco e tudo selecionado', () => {
  const source = read(...clientPath);
  assert.match(source, /defaultValue\.matches\("\\\\d\{1,4\}"\)/, 'só número de até 4 dígitos vira teclado numérico');
  assert.match(source, /InputType\.TYPE_CLASS_NUMBER/);
  assert.match(source, /setSelectAllOnFocus\(true\)/);
  assert.match(source, /input\.requestFocus\(\)/);
  assert.match(source, /SOFT_INPUT_STATE_ALWAYS_VISIBLE/);
  assert.match(source, /EditorInfo\.IME_ACTION_DONE/);
  assert.match(source, /BUTTON_POSITIVE\)\.performClick\(\)/, 'Concluir do teclado confirma pelo mesmo caminho do OK');
  assert.match(source, /R\.dimen\.dialog_input_inset/, 'campo com margem da caixa');
  // No emulador o campo criado com o contexto da Activity ignorava o PilotDialog e ficava teal.
  assert.match(source, /new ContextThemeWrapper\(view\.getContext\(\), R\.style\.PilotDialog\)/);
  assert.match(source, /new EditText\(themed\)/);
  assert.doesNotMatch(source, /new EditText\(view\.getContext\(\)\)/);
});

test('diálogos usam o tema da marca no claro e no escuro', () => {
  const source = read(...clientPath);
  assert.equal((source.match(/new AlertDialog\.Builder\(view\.getContext\(\), R\.style\.PilotDialog\)/g) || []).length, 3);
  const light = read('res', 'values', 'styles.xml');
  const night = read('res', 'values-night', 'styles.xml');
  assert.match(light, /<style name="PilotDialog" parent="android:Theme\.DeviceDefault\.Light\.Dialog\.Alert">[\s\S]*?<item name="android:colorAccent">#496B56<\/item>/);
  assert.match(night, /<style name="PilotDialog" parent="android:Theme\.DeviceDefault\.Dialog\.Alert">[\s\S]*?<item name="android:colorAccent">#A5C3AD<\/item>/);
  // No emulador o sublinhado e a seleção do campo seguiam o teal do sistema: só colorAccent não basta.
  const pilotStyle = xml => xml.slice(xml.indexOf('<style name="PilotDialog"'), xml.indexOf('</style>', xml.indexOf('<style name="PilotDialog"')));
  assert.match(pilotStyle(light), /<item name="android:colorControlActivated">#496B56<\/item>/);
  assert.match(pilotStyle(light), /<item name="android:textColorHighlight">#[0-9A-F]{2}496B56<\/item>/);
  assert.match(pilotStyle(night), /<item name="android:colorControlActivated">#A5C3AD<\/item>/);
  assert.match(pilotStyle(night), /<item name="android:textColorHighlight">#[0-9A-F]{2}A5C3AD<\/item>/);
  assert.match(read('res', 'values', 'dimens.xml'), /<dimen name="dialog_input_inset">24dp<\/dimen>/);
});
