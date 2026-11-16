'use strict';
// #2804: acabamento visual da revisão do APK 0.8.15, medido no CSS que o APK aplica sobre o PWA.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const css = fs.readFileSync(path.join(__dirname, '..', 'pilot.css'), 'utf8');
const rules = selector => [...css.matchAll(/([^{}]+)\{([^{}]*)\}/g)].filter(([, s]) => s.includes(selector)).map(([, , body]) => body);
const px = (body, prop) => Number(body.match(new RegExp('(?:^|;|\\s)' + prop + ':\\s*(\\d+)px'))?.[1] ?? NaN);

test('Tentar de novo da etiqueta usa o preenchimento de contraste e vence o estilo inline', () => {
  assert.ok(rules('#etqRetryBtn').some(body => /background:\s*var\(--fill-accent\)\s*!important/.test(body)));
});

test('editar e arquivar do catálogo de etiquetas têm alvo de 48 px', () => {
  assert.ok(rules('.catalog-list .cat-btn').some(body => px(body, 'width') >= 48 && px(body, 'height') >= 48));
});

test('rótulos, dicas, metadados e contadores pequenos ficam com 12 px ou mais', () => {
  for (const selector of ['.ajustes-section label', '.ajustes-section .hint', '.catalog-list .cat-meta', '.logo-picker-actions .hint', '.pilot-notice-list-heading span'])
    assert.ok(rules(selector).some(body => px(body, 'font-size') >= 12), selector);
  assert.ok(rules('.pilot-bell-count').some(body => /font:\s*700 12px/.test(body)), 'número do sino');
  for (const body of rules('.pilot-topline')) assert.ok(!(px(body, 'font-size') < 12), 'marca dos Avisos');
});

test('ponto de aviso novo usa a cor de tinta, legível nos dois temas', () => {
  for (const body of rules('.pilot-notice-item[data-unread=true]::before').filter(body => body.includes('background')))
    assert.match(body, /background:\s*var\(--accent-ink\)/);
});

test('divisores, anéis e sombras derivam da tinta do tema, sem marrom fixo que some no escuro', () => {
  assert.doesNotMatch(css, /#352d25[0-9a-f]{2}/);
});
