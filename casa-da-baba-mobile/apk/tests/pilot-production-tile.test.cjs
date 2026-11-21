'use strict';
const { lerPwaMontada } = require('../scripts/pwa-montagem.cjs');
// #2886: no card selecionado da Produção o botão Foto (48dp no piloto) cobria a metade de cima do "+".
// O Foto sai do canto e entra na linha de ações; a região viva única existe no markup.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const css = fs.readFileSync(path.join(__dirname, '..', 'pilot.css'), 'utf8');
const html = lerPwaMontada(path.join(__dirname, '..', '..', '..', 'EasyStock.Api', 'wwwroot', 'pwa', 'index.html'));
const rule = selector => {
  const start = css.indexOf(selector + ' {');
  assert.ok(start >= 0, selector + ' ausente');
  return css.slice(start, css.indexOf('}', start));
};
const tile = 'body.pilot-app .phone [data-screen=producao] .product-tile';

test('Foto sai do canto absoluto e divide a linha de ações, longe do "+"', () => {
  const camera = rule(tile + ' .camera-btn');
  assert.match(camera, /position:\s*static/);
  assert.match(camera, /grid-row:\s*2/);
  assert.match(camera, /grid-column:\s*1/);
  assert.match(camera, /justify-self:\s*start/);
  assert.match(rule(tile + ' .prod-tile-actions'), /grid-row:\s*2/, 'ações na mesma linha explícita');
});

test('Foto mostra o texto como Editar e Excluir', () => {
  assert.match(rule(tile + ' .camera-btn::after'), /content:\s*'Foto'/);
});

test('região viva única para leitor de tela', () => {
  assert.match(html, /<div id="srStatus" class="sr-only" role="status" aria-live="polite" aria-atomic="true"><\/div>/);
  assert.match(html, /\.sr-only\s*\{[^}]*clip:\s*rect\(0,\s*0,\s*0,\s*0\)/);
});
