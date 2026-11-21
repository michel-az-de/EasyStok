'use strict';
// #2247 (v3, G1 item 7): copia o motor da etiqueta da PWA para o Console.
// O deploy do Console empacota só EasyStock.Console, então ele não alcança
// a PWA; a cópia é gerada e o teste tests/pwa/impressao-v3-motor-console
// reprova se ela ficar diferente. Rode depois de mudar o motor:
//   node tools/contratos/sincronizar-motor-console.js
const fs = require('fs');
const path = require('path');

const raiz = path.join(__dirname, '..', '..');
const pwa = path.join(raiz, 'EasyStock.Api', 'wwwroot', 'pwa');
const motor = path.join(raiz, 'EasyStock.Console', 'src', 'infra', 'motor');
const ARQUIVOS = [
  'impressao-papeis.js',
  'etiqueta-resolver.js',
  'etiqueta-canvas.js',
  'fontes/NunitoSans-400.woff2',
  'fontes/NunitoSans-700.woff2',
  'fontes/NunitoSans-OFL.txt'
];

for (const f of ARQUIVOS) {
  const destino = path.join(motor, f);
  fs.mkdirSync(path.dirname(destino), { recursive: true });
  fs.copyFileSync(path.join(pwa, f), destino);
  console.log('copiado ' + path.relative(raiz, destino));
}
