// #2247 (v3, G1 item 7): o Console desenha a etiqueta com o mesmo motor do app.
// O deploy do Console empacota só EasyStock.Console, então o motor entra lá
// como cópia gerada (`node tools/contratos/sincronizar-motor-console.js`).
// Cópia diferente da PWA reprova aqui: a tela do Console mostraria uma
// etiqueta e o app imprimiria outra.
'use strict';

const fs = require('fs');
const path = require('path');

module.exports = function ({ test, assert }) {
  const raiz = path.join(__dirname, '..', '..');
  const pwa = path.join(raiz, 'EasyStock.Api', 'wwwroot', 'pwa');
  const motor = path.join(raiz, 'EasyStock.Console', 'src', 'infra', 'motor');
  const TEXTO = ['impressao-papeis.js', 'etiqueta-resolver.js', 'etiqueta-canvas.js'];
  const BINARIO = ['fontes/NunitoSans-400.woff2', 'fontes/NunitoSans-700.woff2'];
  const semCr = b => b.toString('utf8').replace(/\r\n/g, '\n');

  test('v3 motor no Console: os arquivos do motor são cópia exata dos da PWA', () => {
    for (const f of TEXTO.concat(BINARIO)) {
      const destino = path.join(motor, f);
      assert.ok(fs.existsSync(destino), 'falta ' + f + ' no Console; rode node tools/contratos/sincronizar-motor-console.js');
      const a = fs.readFileSync(path.join(pwa, f)), b = fs.readFileSync(destino);
      if (TEXTO.includes(f)) assert.strictEqual(semCr(b), semCr(a), f + ' desatualizado; rode node tools/contratos/sincronizar-motor-console.js');
      else assert.ok(a.equals(b), f + ' desatualizado; rode node tools/contratos/sincronizar-motor-console.js');
    }
  });
};
