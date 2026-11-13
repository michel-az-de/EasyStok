'use strict';
// #2247 (v3, G1 item 6): gera docs/contracts/papeis.v1.json a partir do
// catálogo do app (pwa/impressao-papeis.js). Rode depois de mudar um papel:
//   node tools/contratos/gerar-papeis.js
// O teste do app (tests/pwa) e o do servidor (PapeisContratoTests) leem o arquivo.
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const raiz = path.join(__dirname, '..', '..');
const fonte = path.join(raiz, 'EasyStock.Api', 'wwwroot', 'pwa', 'impressao-papeis.js');
const destino = path.join(raiz, 'docs', 'contracts', 'papeis.v1.json');

const sb = {};
sb.globalThis = sb;
vm.createContext(sb);
vm.runInContext(fs.readFileSync(fonte, 'utf8'), sb, { filename: 'impressao-papeis.js' });

fs.writeFileSync(destino, JSON.stringify(sb.cdbPapeis.contrato(), null, 2) + '\n', 'utf8');
console.log('gerado ' + path.relative(raiz, destino));
