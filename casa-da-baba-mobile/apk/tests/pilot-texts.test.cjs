'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { commandFor } = require('../pilot-ui.js');

const html = fs.readFileSync(path.join(__dirname, '..', '..', '..', 'EasyStock.Api', 'wwwroot', 'pwa', 'index.html'), 'utf8');
const pilot = fs.readFileSync(path.join(__dirname, '..', 'pilot.js'), 'utf8');

test('confirmações que a operadora lê antes de cancelar estão acentuadas', () => {
  for (const wrong of ['JA ENTREGUE', 'Estoque sera devolvido', 'Já fazem alguns dias']) assert.ok(!html.includes(wrong), wrong);
  assert.ok(html.includes('Cancelar pedido JÁ ENTREGUE'));
  assert.ok(html.includes('Estoque será devolvido'));
});

test('diagnóstico sem pareamento manda conectar em vez de sincronizar', () => {
  assert.ok(html.includes('Conecte o aparelho à loja para enviar.'));
});

test('ações do diagnóstico seguem o mesmo padrão de comando dos vizinhos', () => {
  assert.deepEqual(commandFor('Compactar audit'), { label: 'Compactar', icon: 'archive' });
  assert.deepEqual(commandFor('Salvar agora'), { label: 'Salvar', icon: 'check' });
});

test('falha ao conferir atualização usa uma única frase', () => {
  const phrases = new Set(pilot.match(/Não foi possível (?:consultar|conferir) a atualização[^'"]*/g));
  assert.deepEqual([...phrases], ['Não foi possível conferir a atualização. Tente novamente com conexão.']);
});
