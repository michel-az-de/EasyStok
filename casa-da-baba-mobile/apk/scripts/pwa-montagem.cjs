'use strict';
// #2892 fase 2: a PWA serve o CSS e o JS do app em arquivos (css/app.css, js/app-*.js).
// O APK e os testes que leem a PWA como texto precisam do HTML único de antes:
// esta função devolve cada arquivo ao seu ponto, byte a byte.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

const CSS = /<link rel="stylesheet" href="(css\/app\.css)(?:\?v=[0-9a-f]+)?">/g;
const JS = /<script src="(js\/app-[a-z-]+\.js)(?:\?v=[0-9a-f]+)?"><\/script>/g;

// Hash do conteúdo com fim de linha LF, igual no Windows e no Linux.
function versaoDoArquivo(texto) {
  return crypto.createHash('sha256').update(texto.replace(/\r\n/g, '\n')).digest('hex').slice(0, 8);
}

function arquivosDoApp(html) {
  return [...html.matchAll(CSS), ...html.matchAll(JS)].map(m => m[1]);
}

function montarHtmlUnico(html, pastaPwa) {
  const ler = arquivo => fs.readFileSync(path.join(pastaPwa, arquivo), 'utf8');
  return html
    .replace(CSS, (_, arquivo) => '<style>' + ler(arquivo) + '</style>')
    .replace(JS, (_, arquivo) => '<script>' + ler(arquivo) + '</script>');
}

function lerPwaMontada(caminhoIndex) {
  return montarHtmlUnico(fs.readFileSync(caminhoIndex, 'utf8'), path.dirname(caminhoIndex));
}

module.exports = { arquivosDoApp, montarHtmlUnico, lerPwaMontada, versaoDoArquivo };
