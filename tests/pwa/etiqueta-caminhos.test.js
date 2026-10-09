// Testes — #1509 caminhos do modulo de etiquetas
//
// O PWA e servido em /pwa/ e os arquivos de etiqueta moram em wwwroot/pwa/etiqueta.
// Caminho absoluto '/etiqueta/...' dava 404 no navegador (nao existe wwwroot/etiqueta);
// so funcionava no APK, que serve o bundle na raiz. Caminho relativo vale nos dois.
'use strict';

const fs = require('fs');
const path = require('path');

module.exports = function ({ test, assert }) {
  const PWA_DIR = path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa');

  function arquivos() {
    const etq = path.join(PWA_DIR, 'etiqueta');
    const lista = [path.join(PWA_DIR, 'index.html')];
    (function andar(dir) {
      for (const nome of fs.readdirSync(dir)) {
        const p = path.join(dir, nome);
        if (fs.statSync(p).isDirectory()) { if (nome !== 'vendor') andar(p); continue; }
        if (/\.(js|css|html)$/.test(nome)) lista.push(p);
      }
    })(etq);
    return lista;
  }

  test('#1509: nenhum caminho absoluto /etiqueta/ no PWA', () => {
    const ruins = [];
    for (const arq of arquivos()) {
      const linhas = fs.readFileSync(arq, 'utf8').split(/\r?\n/);
      linhas.forEach((l, i) => {
        if (/["'`(]\/etiqueta\//.test(l)) ruins.push(path.relative(PWA_DIR, arq) + ':' + (i + 1));
      });
    }
    assert.deepStrictEqual(ruins, [], 'caminhos absolutos: ' + ruins.join(', '));
  });

  test('#1509: caminhos de etiqueta apontam para arquivos que existem', () => {
    const html = fs.readFileSync(path.join(PWA_DIR, 'index.html'), 'utf8');
    const re = /(?:src|href)=["'](?:\.\/)?(etiqueta\/[^"'?]+)|from\s+['"]\.\/(etiqueta\/[^'"]+)['"]/g;
    const vistos = [];
    let m;
    while ((m = re.exec(html)) !== null) {
      const rel = m[1] || m[2];
      vistos.push(rel);
      assert.ok(fs.existsSync(path.join(PWA_DIR, rel)), 'nao existe: ' + rel);
    }
    assert.ok(vistos.length >= 5, 'index.html referencia o modulo de etiquetas: ' + vistos.join(', '));
  });
};
