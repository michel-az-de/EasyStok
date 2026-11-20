// #2892 fase 1: toda troca de texto que o empacotamento do APK faz na PWA
// precisa casar. Troca que não casa deixava o APK com o texto antigo, em silêncio.
'use strict';
const fs = require('fs');
const path = require('path');

module.exports = function ({ test, assert }) {
  const raiz = path.resolve(__dirname, '../..');
  const scripts = path.join(raiz, 'casa-da-baba-mobile/apk/scripts');
  const carregar = () => require(path.join(scripts, 'pwa-trocas.cjs'));
  const fontePwa = () => {
    const { adaptAppMarkup } = require(path.join(scripts, 'app-markup.cjs'));
    return adaptAppMarkup(fs.readFileSync(path.join(raiz, 'EasyStock.Api/wwwroot/pwa/index.html'), 'utf8'));
  };

  test('#2892 toda troca do APK casa com a PWA atual', () => {
    const { TROCAS, trocasSemAlvo } = carregar();
    assert.ok(TROCAS.length >= 12, 'lista de trocas incompleta: ' + TROCAS.length);
    assert.deepStrictEqual(trocasSemAlvo(fontePwa(), TROCAS), []);
  });

  test('#2892 troca sem alvo falha alto com o texto procurado', () => {
    const { aplicarTrocas } = carregar();
    assert.throws(() => aplicarTrocas('<html></html>', [{ de: 'Forçar flush', para: 'Sincronizar' }]), /Forçar flush/);
  });

  test('#2892 troca simples muda só a primeira ocorrência; troca "todas" muda todas', () => {
    const { aplicarTrocas } = carregar();
    assert.strictEqual(aplicarTrocas('a a', [{ de: 'a', para: 'b' }]), 'b a');
    assert.strictEqual(aplicarTrocas('a a', [{ de: 'a', para: 'b', todas: true }]), 'b b');
  });

  test('#2892 copy-web.cjs não faz troca de texto literal fora da lista', () => {
    const codigo = fs.readFileSync(path.join(scripts, 'copy-web.cjs'), 'utf8');
    assert.ok(/aplicarTrocas\(/.test(codigo), 'copy-web.cjs não usa aplicarTrocas');
    assert.deepStrictEqual(codigo.match(/\.replace(?:All)?\(\s*['"]/g) || [], []);
  });
};
