// #2892 fase 2: o CSS e o JS do app saíram do index.html para arquivos.
// Garante que nada voltou inline, que cada arquivo tem ?v= do próprio conteúdo
// (o service worker guarda a URL com a query; sem isso o HTML novo rodaria JS velho)
// e que o APK remonta o HTML único.
'use strict';
const fs = require('fs');
const path = require('path');

module.exports = function ({ test, assert }) {
  const raiz = path.resolve(__dirname, '../..');
  const PWA_DIR = path.join(raiz, 'EasyStock.Api/wwwroot/pwa');
  const montagem = () => require(path.join(raiz, 'casa-da-baba-mobile/apk/scripts/pwa-montagem.cjs'));
  const html = () => fs.readFileSync(path.join(PWA_DIR, 'index.html'), 'utf8');

  test('#2892 index.html não tem <style> nem <script> clássico inline', () => {
    assert.ok(!/<style\b/i.test(html()), 'há <style> inline no index.html');
    const inline = [...html().matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)]
      .filter(m => !/\bsrc=/.test(m[1]) && !/type=["']module["']/.test(m[1]));
    assert.deepStrictEqual(inline.map(m => m[2].slice(0, 60)), []);
  });

  test('#2892 cada arquivo do app tem ?v= igual ao hash do conteúdo', () => {
    const { arquivosDoApp, versaoDoArquivo } = montagem();
    const arquivos = arquivosDoApp(html());
    assert.deepStrictEqual(arquivos, ['css/app.css', 'js/app-nucleo.js', 'js/app-fonte-etiqueta.js', 'js/app-arranque.js']);
    const errados = arquivos.filter(a => {
      const esperado = versaoDoArquivo(fs.readFileSync(path.join(PWA_DIR, a), 'utf8'));
      return !html().includes(a + '?v=' + esperado + '"');
    }).map(a => a + ' (use ?v=' + versaoDoArquivo(fs.readFileSync(path.join(PWA_DIR, a), 'utf8')) + ')');
    assert.deepStrictEqual(errados, [], 'atualize o ?v= no index.html');
  });

  test('#2892 HTML remontado devolve CSS e JS ao ponto original', () => {
    const { lerPwaMontada } = montagem();
    const unico = lerPwaMontada(path.join(PWA_DIR, 'index.html'));
    assert.strictEqual((unico.match(/<style>/g) || []).length, 1);
    assert.ok(!/css\/app\.css|js\/app-/.test(unico), 'sobrou referência a arquivo do app');
    assert.ok(unico.indexOf('<style>') < unico.indexOf('harmonia.css'), 'o CSS do app vem antes do harmonia.css');
  });

  test('#2892 copy-web.cjs remonta o HTML único antes das adaptações do APK', () => {
    const codigo = fs.readFileSync(path.join(raiz, 'casa-da-baba-mobile/apk/scripts/copy-web.cjs'), 'utf8');
    assert.match(codigo, /adaptAppMarkup\(montarHtmlUnico\(/);
  });
};
