// Testes — #2431 achados da revisão do dia 12/11 no PWA
//
// 1. printerDiagnostics: fora do piloto o botão Diagnóstico não abria nada.
// 2. Handler inline com id cru ('${x}') que o #2401 deixou passar.
'use strict';

const fs = require('fs');
const path = require('path');

module.exports = function ({ test, runInSandbox, assert }) {
  const html = fs.readFileSync(path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa/index.html'), 'utf8');

  const chamar = (pilot, retornoClose) => JSON.parse(runInSandbox(`
    (function () {
      const orig = { cfg: window.CDB_CONFIG, close: closeAjustes, open: openDiagnostics };
      const chamadas = [];
      window.CDB_CONFIG = { isPilot: ${pilot} };
      closeAjustes = function () { chamadas.push('close'); return ${retornoClose}; };
      openDiagnostics = function () { chamadas.push('open'); };
      try { printerDiagnostics(); } finally {
        window.CDB_CONFIG = orig.cfg; closeAjustes = orig.close; openDiagnostics = orig.open;
      }
      return JSON.stringify(chamadas);
    })()
  `));

  test('#2431: printerDiagnostics fora do piloto abre o diagnóstico', () => {
    assert.deepStrictEqual(chamar(false, 'undefined'), ['open']);
  });

  test('#2431: printerDiagnostics no piloto fecha Ajustes e abre o diagnóstico', () => {
    assert.deepStrictEqual(chamar(true, 'undefined'), ['close', 'open']);
  });

  test('#2431: printerDiagnostics no piloto com "Descartar?" cancelado não abre', () => {
    assert.deepStrictEqual(chamar(true, 'false'), ['close']);
  });

  test('#2431: handler inline não interpola id cru entre aspas simples', () => {
    const ruins = html.split(/\r?\n/)
      .map((l, i) => [i + 1, l])
      .filter(([, l]) => /(runOrderAction|advanceOrder|prefillFromDemand)\('\$\{/.test(l)
        || /getElementById\('[\w-]*\$\{/.test(l))
      .map(([n, l]) => `${n}: ${l.trim().slice(0, 90)}`);
    assert.deepStrictEqual(ruins, [], 'use ${jsString(x)}:\n' + ruins.join('\n'));
  });
};
