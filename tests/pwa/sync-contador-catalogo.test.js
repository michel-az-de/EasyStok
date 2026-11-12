// #2316 — SYNC-4 (catalogo semente nao entra em aparelho pareado) e SYNC-7 (contador de
// pendentes deriva so da fila, sem ++ manual que somava duas vezes o mesmo item).
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

module.exports = function ({ test, assert }) {
  const html = fs.readFileSync(process.env.PWA_HTML || path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa/index.html'), 'utf8');

  function extrairFuncao(nome) {
    const ini = html.indexOf('function ' + nome + '(');
    assert.ok(ini >= 0, 'index.html precisa declarar ' + nome);
    let i = html.indexOf('{', ini), nivel = 0;
    for (; i < html.length; i++) {
      if (html[i] === '{') nivel++;
      else if (html[i] === '}' && --nivel === 0) break;
    }
    return html.slice(ini, i + 1);
  }

  const semente = [{ id: 'talharim', name: 'Talharim' }, { id: 'lasanha', name: 'Lasanha' }];
  function catalogo(armazenado, ctx) {
    const sandbox = { out: null };
    vm.runInNewContext(extrairFuncao('catalogoLocalInicial') + '\nout = catalogoLocalInicial(' +
      JSON.stringify(armazenado) + ',' + JSON.stringify(semente) + ',' + JSON.stringify(ctx) + ');', sandbox);
    return JSON.parse(JSON.stringify(sandbox.out));
  }

  test('SYNC-4: aparelho pareado com o ERP nao semeia o catalogo local', () => {
    assert.deepEqual(catalogo(null, { isPilot: false, paired: true }), []);
  });
  test('SYNC-4: pareado nao reinjeta semente em catalogo ja salvo', () => {
    const salvo = [{ id: 'erp-1', name: 'Nhoque' }];
    assert.deepEqual(catalogo(salvo, { isPilot: false, paired: true }), salvo);
  });
  test('SYNC-4: aparelho solto (sem pareamento) segue semeando', () => {
    assert.deepEqual(catalogo(null, { isPilot: false, paired: false }).map(p => p.id), ['talharim', 'lasanha']);
  });
  test('SYNC-4: piloto segue vazio', () => {
    assert.deepEqual(catalogo(null, { isPilot: true, paired: false }), []);
  });

  test('SYNC-7: pendingSync nao e incrementado a mao, so vem da fila', () => {
    assert.doesNotMatch(html, /pendingSync\s*(\+\+|\+=)/);
  });
};
