'use strict';
// #3251 (P2 da #3114): o conteúdo web da homologação declara o ambiente em todo fetch à API,
// e a API da produção recusa. O oficial não muda: fetch original, sem cabeçalho.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const { instalarAmbiente } = require('../pilot-ambiente.js');
const API = 'https://api-teste.example';

function alvo() {
  const chamadas = [];
  const original = (input, init) => { chamadas.push({ input, init }); return Promise.resolve(new Response('{}')); };
  return { fetch: original, original, chamadas };
}

test('oficial e config sem ambiente ficam com o fetch original', () => {
  for (const config of [{ ambiente: 'oficial', apiBaseUrl: 'https://api.oficial.example' }, { apiBaseUrl: API }, undefined]) {
    const janela = alvo();
    assert.equal(instalarAmbiente(janela, config), false);
    assert.equal(janela.fetch, janela.original);
  }
});

test('homologação declara o ambiente em todo pedido à API, preservando corpo, método e sinal', async () => {
  const janela = alvo();
  assert.equal(instalarAmbiente(janela, { ambiente: 'homologacao', apiBaseUrl: API }), true);
  const controle = new AbortController();
  await janela.fetch(API + '/api/mobile/sync', { method: 'POST', body: '{"a":1}', headers: { 'X-Mobile-Api-Key': 'k' }, signal: controle.signal });
  const pedido = janela.chamadas[0].input;
  assert.ok(pedido instanceof Request);
  assert.equal(pedido.headers.get('X-Cdb-Ambiente'), 'homologacao');
  assert.equal(pedido.headers.get('X-Mobile-Api-Key'), 'k');
  assert.equal(pedido.method, 'POST');
  assert.equal(await pedido.text(), '{"a":1}');
  controle.abort();
  assert.equal(pedido.signal.aborted, true, 'cancelamento do chamador chega ao pedido');

  await janela.fetch(new Request(API + '/api/mobile/sync/pull', { headers: { Accept: 'application/json' } }));
  const doRequest = janela.chamadas[1].input;
  assert.equal(doRequest.headers.get('X-Cdb-Ambiente'), 'homologacao');
  assert.equal(doRequest.headers.get('Accept'), 'application/json');
});

test('pedido para outra origem sai intocado', async () => {
  const janela = alvo();
  instalarAmbiente(janela, { ambiente: 'homologacao', apiBaseUrl: API });
  const init = { method: 'GET' };
  await janela.fetch('https://outro.example/fonte.woff2', init);
  assert.equal(janela.chamadas[0].input, 'https://outro.example/fonte.woff2');
  assert.equal(janela.chamadas[0].init, init);
});

test('o APK carrega a declaração logo depois do config.js, antes do conteúdo do PWA', () => {
  const { TROCAS } = require('../scripts/pwa-trocas.cjs');
  const cabeca = TROCAS.find(troca => troca.de === '<head>').para;
  assert.match(cabeca, /<script src="config\.js"><\/script>\s*<script src="pilot-ambiente\.js"><\/script>/);
  const copia = fs.readFileSync(path.join(root, 'scripts/copy-web.cjs'), 'utf8');
  for (const lista of copia.match(/\[(?:'pilot[^\]]*)\]/g)) assert.ok(lista.includes("'pilot-ambiente.js'"), 'copy-web copia ' + lista);
});
