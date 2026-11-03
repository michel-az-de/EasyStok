'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { installAppChrome, refreshOperatorReview } = require('../pilot.js');

test('cabeçalho único move os controles originais, preservando identidade e handlers', () => {
  const ids = ['globalSearchBtn', 'ajustesBtn', 'opBadge', 'syncChip'];
  const nodes = new Map(ids.map(id => [id, { id, onclick() { return id; } }]));
  for (const id of ['pilotRailActions', 'pilotOperator', 'pilotSyncDetails']) nodes.set(id, {
    children: [], append(node) { this.children.push(node); }
  });
  const header = { hidden: false }, label = { textContent: 'Hoje' };
  const document = { getElementById: id => nodes.get(id), querySelector: selector => selector === '.phone > .header' ? header : label };
  const calls = [], open = [], environment = { document, go(screen, options) { calls.push([this, screen, options]); return 'preserved'; } };
  installAppChrome(environment, () => open.push('home'));
  assert.equal(header.hidden, true);
  assert.equal(label.textContent, 'Início');
  assert.deepEqual(nodes.get('pilotRailActions').children, [nodes.get('globalSearchBtn'), nodes.get('ajustesBtn')]);
  assert.equal(nodes.get('pilotOperator').children[0], nodes.get('opBadge'));
  assert.equal(nodes.get('pilotSyncDetails').children[0], nodes.get('syncChip'));
  for (const id of ids) assert.equal(nodes.get(id).onclick(), id);
  const options = { preserveHistory: true };
  assert.equal(environment.go('producao', options), 'preserved');
  assert.equal(open.length, 0);
  assert.equal(environment.go('home', options), 'preserved');
  assert.deepEqual(open, ['home']);
  assert.deepEqual(calls.map(call => call.slice(1)), [['producao', options], ['home', options]]);
  assert.ok(calls.every(call => call[0] === environment));
});

test('prévia sem cabeçalho legado não perde a navegação existente', () => {
  const go = () => 'unchanged';
  const environment = { document: { querySelector: () => null }, go };
  installAppChrome(environment, () => assert.fail('Não deve abrir nada durante instalação'));
  assert.equal(environment.go, go);
});

test('autoria do lote é atualizada em ambos os cadastros e nomes nunca viram HTML', () => {
  const labels = [{}, {}];
  const document = { querySelectorAll: () => labels };
  refreshOperatorReview(document, ' Thatiane ');
  assert.ok(labels.every(label => label.textContent === 'Thatiane'));
  refreshOperatorReview(document, '<img src=x onerror=alert(1)>');
  assert.ok(labels.every(label => label.textContent === '<img src=x onerror=alert(1)>' && label.innerHTML === undefined));
  refreshOperatorReview(document, '');
  assert.ok(labels.every(label => label.textContent === 'Não identificado'));
});
