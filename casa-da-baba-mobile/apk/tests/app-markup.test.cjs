'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { adaptAppMarkup } = require('../scripts/app-markup.cjs');

const source = fs.readFileSync(path.resolve(__dirname, '../../../EasyStock.Api/wwwroot/pwa/index.html'), 'utf8');
const start = '<!-- Modal novo produto -->';
const end = '<!-- Modal novo cliente -->';
function modal(html) { return html.slice(html.indexOf(start), html.indexOf(end)); }
function ids(html) { return [...html.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]).sort(); }
function tags(html, pattern) { return (html.match(pattern) || []).sort(); }
const original = modal(source);
const result = adaptAppMarkup(source);
const adapted = modal(result);

test('adapta o formulário real preservando todos os IDs uma única vez', () => {
  assert.deepEqual(ids(adapted), ids(original));
  assert.equal(new Set(ids(adapted)).size, ids(adapted).length);
  assert.match(adapted, /Dados do produto e validade\./);
  assert.match(adapted, /<summary>Mais opções do produto<\/summary>/);
});

test('preserva controles completos, handlers, validação, acessibilidade e estado inicial', () => {
  // Igualdade do elemento de abertura cobre required/min/max/step, bindings,
  // disabled, tipos, valores, ARIA e seleções iniciais, sem reescrevê-los.
  assert.deepEqual(tags(adapted, /<(?:input|button|select|textarea)\b[^>]*>/g),
    tags(original, /<(?:input|button|select|textarea)\b[^>]*>/g));
  assert.deepEqual(tags(adapted, /\bon[a-z]+="[^"]*"/g), tags(original, /\bon[a-z]+="[^"]*"/g));
  assert.deepEqual(tags(adapted, /<[^>]*\bid="[^"]+"[^>]*>/g), tags(original, /<[^>]*\bid="[^"]+"[^>]*>/g));
  assert.match(adapted, /<div id="newProdBatchFields" hidden>/);
});

test('essenciais e preço ficam fora de details, opções sem obrigatórios ficam dentro', () => {
  const details = adapted.match(/<details class="pilot-product-options">([\s\S]*?)<\/details>/)[1];
  const essentials = adapted.slice(adapted.indexOf('<div class="pilot-product-section">'), adapted.indexOf('<details'));
  for (const id of ['newProdName', 'catPills', 'newProdUnit', 'newProdValidity', 'newProdBatchFields',
    'newProdQuantity', 'newProdManufacturedDate', 'newProdWeight', 'newProdExpiry', 'newProdPrice']) {
    assert.ok(essentials.includes('id="' + id + '"'), id + ' deve estar na seção aberta');
    assert.ok(!details.includes('id="' + id + '"'), id + ' não pode estar recolhido');
  }
  for (const id of ['newProdCost', 'newProdMinStock', 'marginPreview', 'mpProfit', 'mpMargin', 'mpHint', 'emojiGrid']) {
    assert.ok(details.includes('id="' + id + '"'), id + ' deve estar nas opções');
  }
  assert.doesNotMatch(details, /\brequired(?:\s|>|=)/);
  assert.ok(adapted.indexOf('id="saveProductBtn"') > adapted.indexOf('</details>'));
});

test('preserva integralmente todo o documento fora do modal, inclusive scripts', () => {
  assert.equal(result.slice(0, result.indexOf(start)), source.slice(0, source.indexOf(start)));
  assert.equal(result.slice(result.indexOf(end)), source.slice(source.indexOf(end)));
});

test('rejeita segunda transformação explicitamente', () => {
  assert.throws(() => adaptAppMarkup(result), /já adaptado/);
});

test('falha antes de retornar HTML se delimitadores desaparecem ou ficam ambíguos', () => {
  for (const marker of [start, end, 'id="newProductModal"',
    '<label class="modal-label" for="newProdName">', '<label class="modal-label" id="emojiGridLabel">',
    '<label class="modal-label" for="newProdPrice">', '<label class="modal-label" for="newProdCost">',
    '<div class="modal-sub">fica disponível na hora</div>']) {
    assert.throws(() => adaptAppMarkup(source.replace(marker, '')), /incompatível/);
    assert.throws(() => adaptAppMarkup(source.replace(marker, marker + marker)), /incompatível/);
  }
  const brokenModal = original.replace('<div class="modal-ctas">', '');
  assert.throws(() => adaptAppMarkup(source.replace(original, brokenModal)), /incompatível/);
  const reversed = source.replace(start, '<!-- reversed-start -->').replace(end, start).replace('<!-- reversed-start -->', end);
  assert.throws(() => adaptAppMarkup(reversed), /fora de ordem/);
  const name = '<label class="modal-label" for="newProdName">';
  const price = '<label class="modal-label" for="newProdPrice">';
  const reversedFields = source.replace(name, '<!-- reversed-field -->').replace(price, name).replace('<!-- reversed-field -->', price);
  assert.throws(() => adaptAppMarkup(reversedFields), /fora de ordem/);
});

test('mantém aninhamento válido no modal reorganizado', () => {
  const stack = [];
  for (const match of adapted.matchAll(/<(\/?)([a-z][a-z0-9-]*)\b[^>]*>/gi)) {
    const [, closing, rawName] = match;
    const name = rawName.toLowerCase();
    if (['input', 'br', 'img', 'hr'].includes(name)) continue;
    if (closing) assert.equal(stack.pop(), name, 'Fechamento incompatível: ' + match[0]);
    else stack.push(name);
  }
  assert.deepEqual(stack, []);
});

test('aceita LF e CRLF mantendo o restante do documento intacto', () => {
  for (const newline of ['\n', '\r\n']) {
    const input = source.replace(/\r?\n/g, newline);
    const output = adaptAppMarkup(input);
    assert.deepEqual(ids(modal(output)), ids(modal(input)));
    assert.equal(output.slice(output.indexOf(end)), input.slice(input.indexOf(end)));
  }
});
