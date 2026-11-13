'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const html = fs.readFileSync(path.resolve(__dirname, '../../../EasyStock.Api/wwwroot/pwa/index.html'), 'utf8');

test('título da tela de movimentos tem acento', () => {
  assert.doesNotMatch(html, /<h2 class="screen-title">Historico<\/h2>/);
  assert.match(html, /<h2 class="screen-title">Histórico<\/h2>/);
});

// Escolha única (código impresso, orientação) é rádio, não botão de alternância.
test('cartões de escolha única anunciam rádio marcado', () => {
  for (const group of ['scanModeOptions', 'orientModeOptions']) {
    assert.match(html, new RegExp('id="' + group + '"[^>]*role="radiogroup"'), group);
  }
  const cards = html.match(/<button type="button" class="scan-mode-card"[^>]*>/g);
  assert.equal(cards.length, 6);
  for (const card of cards) {
    assert.match(card, /role="radio"/);
    assert.match(card, /aria-checked="false"/);
    assert.doesNotMatch(card, /aria-pressed/);
  }
  for (const fn of ['function setScanModeChoice', 'function setOrientChoice']) {
    const body = html.slice(html.indexOf(fn), html.indexOf('\n}', html.indexOf(fn)));
    assert.match(body, /setAttribute\('aria-checked'/, fn);
    assert.doesNotMatch(body, /aria-pressed/, fn);
  }
});

test('diagnóstico mostra o tema em português', () => {
  assert.doesNotMatch(html, /_diagRow\('Tema', escapeHtml\(d\.app\.theme\)\)/);
  assert.match(html, /_diagRow\('Tema', d\.app\.theme === 'light' \? 'Claro' : 'Escuro'\)/);
});

test('detalhes técnicos do seletor de impressoras têm alvo de toque de 48px', () => {
  const css = fs.readFileSync(path.join(__dirname, '../pilot.css'), 'utf8');
  assert.match(css, /body\.pilot-app #printerPickerModal details > summary \{ min-height: 48px;/);
});

test('relatório copiado do diagnóstico também diz Claro/Escuro', () => {
  assert.doesNotMatch(html, /' · Tema: ' \+ d\.app\.theme\)/);
  assert.match(html, /' · Tema: ' \+ \(d\.app\.theme === 'light' \? 'Claro' : 'Escuro'\)\)/);
});

// A folha de Configurações tem 18px de respiro no topo; sem compensar, o conteúdo rolado aparece acima do cabeçalho fixo.
test('cabeçalho fixo de Configurações cobre o respiro do topo da folha', () => {
  const css = fs.readFileSync(path.join(__dirname, '../pilot.css'), 'utf8');
  assert.match(css, /body\.pilot-app #ajustesModal \.ajustes-head \{ top: -18px; margin-top: -18px; padding-top: 20px; \}/);
});
