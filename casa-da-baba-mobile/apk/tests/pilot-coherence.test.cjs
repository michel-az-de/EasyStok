'use strict';
// Coerência visual e de navegação do piloto (#2212): uma fonte, uma escala de raios,
// um ritmo de movimento e um Voltar que sempre devolve ao lugar de onde se veio.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { installSettings } = require('../pilot-settings.js');
const { pilotIcon } = require('../pilot.js');

const root = path.resolve(__dirname, '..');
const css = fs.readFileSync(path.join(root, 'pilot.css'), 'utf8');
const pilot = fs.readFileSync(path.join(root, 'pilot.js'), 'utf8');
const java = fs.readFileSync(path.join(root, 'android/app/src/main/java/com/casadababa/app/WorkspaceActivity.java'), 'utf8');

test('piloto usa só Manrope, preservando códigos em monoespaçada', () => {
  assert.doesNotMatch(css, /font-family:\s*system-ui|font:[^;]*\bsystem-ui/);
  const rule = css.match(/body\.pilot-app :is\(\*\):not\(([^)]*)\)\s*\{[^}]*\}/);
  assert.ok(rule, 'regra global de fonte do piloto');
  assert.match(rule[0], /font-family:\s*'Manrope'[^;]*!important/);
  for (const keep of ['code', 'pre', '.pix-value', '.cr-code']) assert.ok(rule[1].includes(keep), keep);
});

test('raios seguem a escala de três tokens', () => {
  assert.match(css, /--pilot-r-sm:\s*8px;\s*--pilot-r-md:\s*12px;\s*--pilot-r-lg:\s*20px/);
  const literal = [...css.matchAll(/border-radius:\s*(\d+)px\s*[;}!]/g)].map(m => m[1]).filter(v => v !== '0');
  assert.deepEqual([...new Set(literal)], [], 'raio literal fora da escala');
});

test('movimento usa os tokens de duração e curva', () => {
  const body = css.replace(/--pilot-(fast|enter):\s*\d+ms/g, '');
  assert.deepEqual(body.match(/(?<![.\d])\d{2,4}ms\b/g) || [], []);
  assert.match(css, /@media \(prefers-reduced-motion: reduce\)\s*\{\s*body\.pilot-app \*[^}]*transition-duration: 0\.01ms !important/);
});

test('histórico chama de Configurações o filtro de configurações', () => {
  assert.match(pilot, /\[data-f="settings"\][^\n]*textContent = 'Configurações'/);
});

function settings() {
  const handlers = new Map(); const title = {}, sheet = { scrollTop: 0 };
  const modal = { dataset: {}, classList: { contains: () => true }, addEventListener(k, fn) { handlers.set(k, fn); },
    querySelector: s => ({ '.ajustes-head h3': title, '.ajustes-tabs': { querySelectorAll: () => [], after() {} }, '.ajustes-head': { prepend() {} }, '.ajustes-sheet': sheet })[s], querySelectorAll: () => [] };
  const calls = [];
  const environment = { cdbPilotIcon: pilotIcon, cdbMenuButton: () => '',
    document: { getElementById: id => id === 'ajustesModal' ? modal : null, querySelector: () => null,
      createElement: () => ({ setAttribute() {}, addEventListener(k, fn) { this[k] = fn; } }) },
    openAjustes() { calls.push('open'); }, closeAjustes() { calls.push('close'); },
    setAjustesTab(g) { calls.push(g); }, cdbHandleNativeBack() { calls.push('native'); return false; } };
  installSettings(environment);
  return { environment, modal, calls };
}

test('atalho direto para uma seção volta para quem o abriu, não para a lista', () => {
  const s = settings();
  s.environment.cdbOpenAjustesAt('eq');
  assert.equal(s.modal.dataset.view, 'eq');
  assert.equal(s.environment.cdbHandleNativeBack(), true);
  assert.equal(s.calls.at(-1), 'close');
  s.environment.openAjustes(); s.environment.setAjustesTab('eq');
  assert.equal(s.environment.cdbHandleNativeBack(), true);
  assert.equal(s.modal.dataset.view, 'root', 'entrada pela lista continua voltando à lista');
  assert.match(pilot, /function openPrinterSettings\(\) \{\s*root\.cdbOpenAjustesAt\('eq'\)/);
});

test('Atendimento: Voltar sem página anterior real fecha e devolve ao app', () => {
  assert.match(java, /copyBackForwardList\(\)/);
  assert.match(java, /about:blank/);
  assert.match(java, /private void returnToApp\(\) \{\s*finish\(\);/);
  assert.doesNotMatch(java, /import android\.widget\.Button;/, 'barra própria, sem botão Material cinza');
  assert.match(java, /UI_MODE_NIGHT_YES/);
  assert.match(java, /Início", "Voltar ao início"/);
});
