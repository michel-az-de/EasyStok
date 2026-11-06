// #2247 (v3, G1 item 5): uma fonte só para o motor da etiqueta. A Nunito Sans
// (woff2 local, a mesma do Console) é carregada pelo motor com nome próprio;
// sem ela o motor recusa com motivo, em vez de medir com uma fonte e o
// aparelho desenhar com outra.
'use strict';

module.exports = function ({ test, sandbox, assert }) {
  const R = () => sandbox.window.cdbResolverEtiqueta;
  const C = () => sandbox.window.cdbEtiquetaCanvas;
  const CAMPOS = { empresa: 'Minha empresa', nome: 'Bolo de Cenoura', validade: '12/11/2026', lote: 'L1', qr: 'https://x/l/1' };

  // FontFace falso: guarda o que foi pedido e resolve ou falha no load().
  function fontFaceFalsa(falhar) {
    const criadas = [];
    function FF(familia, fonte, desc) { this.family = familia; this.src = fonte; this.weight = desc && desc.weight; criadas.push(this); }
    FF.prototype.load = function () { return falhar ? Promise.reject(new Error('404')) : Promise.resolve(this); };
    return { FF, criadas };
  }
  const docCom = adicionadas => ({ fonts: { add: f => adicionadas.push(f) } });

  test('v3 fonte: o motor mede e desenha só com a Nunito Sans própria, sem reserva', () => {
    assert.strictEqual(R().FONTE, "'Nunito Sans Etiqueta'");
    assert.strictEqual(R().FAMILIA_FONTE, 'Nunito Sans Etiqueta');
  });

  test('v3 fonte: sem a fonte carregada, desenhar recusa com motivo', () => {
    const antes = R().fontePronta();
    R().usarFonteRegistrada(false);
    try {
      const r = C().desenhar({ campos: CAMPOS, papel: 'etiqueta-52x30', orientacao: 'horizontal', medir: () => 1 });
      assert.strictEqual(r.canvas, null);
      assert.match(r.recusa, /fonte da etiqueta/);
    } finally { R().usarFonteRegistrada(antes); }
  });

  test('v3 fonte: carregarFonte registra 400 e 700 dos woff2 locais e libera o motor', async () => {
    const antes = R().fontePronta();
    R().usarFonteRegistrada(false);
    const { FF, criadas } = fontFaceFalsa(false);
    const adicionadas = [];
    try {
      const ok = await R().carregarFonte({ document: docCom(adicionadas), base: 'fontes/', FontFace: FF });
      assert.strictEqual(ok, true);
      assert.deepStrictEqual(criadas.map(f => [f.family, f.src, f.weight]), [
        ['Nunito Sans Etiqueta', "url('fontes/NunitoSans-400.woff2') format('woff2')", '400'],
        ['Nunito Sans Etiqueta', "url('fontes/NunitoSans-700.woff2') format('woff2')", '700']
      ]);
      assert.strictEqual(adicionadas.length, 2);
      assert.strictEqual(R().fontePronta(), true);
    } finally { R().usarFonteRegistrada(antes); }
  });

  // O Console (Vite) serve os woff2 com hash no nome: ele passa a URL de cada peso.
  test('v3 fonte: carregarFonte aceita a URL de cada peso', async () => {
    const antes = R().fontePronta();
    R().usarFonteRegistrada(false);
    const { FF, criadas } = fontFaceFalsa(false);
    try {
      const ok = await R().carregarFonte({ document: docCom([]), FontFace: FF, urls: { 400: '/assets/a-1.woff2', 700: '/assets/b-2.woff2' } });
      assert.strictEqual(ok, true);
      assert.deepStrictEqual(criadas.map(f => f.src), [
        "url('/assets/a-1.woff2') format('woff2')",
        "url('/assets/b-2.woff2') format('woff2')"
      ]);
    } finally { R().usarFonteRegistrada(antes); }
  });

  test('v3 fonte: falha ao carregar deixa o motor travado e diz o porquê', async () => {
    const antes = R().fontePronta();
    R().usarFonteRegistrada(false);
    const { FF } = fontFaceFalsa(true);
    try {
      const ok = await R().carregarFonte({ document: docCom([]), base: 'fontes/', FontFace: FF });
      assert.strictEqual(ok, false);
      assert.strictEqual(R().fontePronta(), false);
      assert.match(R().motivoFonte(), /404/);
    } finally { R().usarFonteRegistrada(antes); }
  });
};
