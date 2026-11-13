// Testes — #2401 achados da revisão geral de 12/11 no PWA
//
// 1. Handler inline com '${x}': o navegador decodifica entidades HTML antes de
//    rodar o JS, então escapeHtml não protege string JS. Use jsString(x).
// 2. Foto da conferência montada em innerHTML sem escape nem filtro de esquema.
// 3. imprimir.js: fetch sem checar res.ok mostra "salvo" quando a API recusou.
// 4. Cache de render de etiqueta: LOGOUT_CLEAR nunca era enviado e filtrava por
//    userId=, que a URL não tem. Ao desparear, o cache inteiro tem de sair.
'use strict';

const fs = require('fs');
const path = require('path');

module.exports = function ({ test, assert }) {
  const PWA_DIR = path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa');
  const ler = (rel) => fs.readFileSync(path.join(PWA_DIR, rel), 'utf8');

  test('#2401: nenhum handler inline interpola ${...} entre aspas simples', () => {
    const linhas = ler('index.html').split(/\r?\n/);
    const ruins = [];
    linhas.forEach((l, i) => {
      const handlers = l.match(/\son[a-z]+="[^"]*"/g) || [];
      for (const h of handlers) {
        if (/'\$\{[^}]*\}'/.test(h)) ruins.push(`${i + 1}: ${h.trim().slice(0, 90)}`);
      }
    });
    assert.deepStrictEqual(ruins, [], 'use ${jsString(x)} no lugar de \'${x}\':\n' + ruins.join('\n'));
  });

  test('#2401: foto da conferência não é concatenada em innerHTML', () => {
    assert.ok(!/innerHTML\s*=\s*'<img src="'\s*\+\s*et\.photo/.test(ler('index.html')),
      'et.photo vai direto para innerHTML');
  });

  test('#2401: imprimir.js não descarta a resposta de escrita (await fetch solto)', () => {
    const linhas = ler('etiqueta/imprimir.js').split(/\r?\n/);
    const soltos = linhas
      .map((l, i) => [i + 1, l])
      .filter(([, l]) => /^\s*await\s+fetch\(/.test(l))
      .map(([n, l]) => `${n}: ${l.trim()}`);
    assert.deepStrictEqual(soltos, [], 'fetch sem checar res.ok:\n' + soltos.join('\n'));
  });

  test('#2401: cancelar o "Descartar?" das Configurações não abre outra tela por cima', () => {
    // pilot-settings.js troca closeAjustes por uma versão que devolve false quando a
    // pessoa mantém o rascunho; quem chama tem de respeitar.
    const ruins = ler('index.html').split(/\r?\n/)
      .map((l, i) => [i + 1, l])
      .filter(([, l]) => /closeAjustes\(\);\s*open\w*\(/.test(l))
      .map(([n, l]) => `${n}: ${l.trim().slice(0, 90)}`);
    assert.deepStrictEqual(ruins, [], 'ignora o retorno de closeAjustes():\n' + ruins.join('\n'));
  });

  test('#2401: desparear limpa o cache de render de etiquetas', () => {
    const html = ler('index.html');
    const ini = html.indexOf('function unpairDevice()');
    assert.ok(ini > 0, 'unpairDevice não encontrado');
    const corpo = html.slice(ini, html.indexOf('\n}\n', ini));
    assert.ok(/LOGOUT_CLEAR/.test(corpo), 'unpairDevice não avisa o service worker');

    const sw = ler('sw.js');
    const h = sw.slice(sw.indexOf("type === 'LOGOUT_CLEAR'"), sw.indexOf("type === 'LOGOUT_CLEAR'") + 600);
    assert.ok(!/userId=/.test(h), 'LOGOUT_CLEAR ainda filtra por userId=, que a URL não tem');
  });
};
