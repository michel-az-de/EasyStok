'use strict';
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// #2329: pedido, teste e recibo em TSPL saem pelo mesmo modelo de cabeça da
// produção (v3, #2265): SIZE da cabeça, SHIFT sempre (a Oasis guarda o último)
// e o desenho onde o papel está mais o ajuste fino. E o resolvedor mede cada
// texto uma vez por peso: a largura é linear no corpo.
const PWA = process.env.PWA_HTML ? path.dirname(process.env.PWA_HTML) : path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa');

function isolado(arquivos) {
  const ctx = {};
  ctx.globalThis = ctx;
  vm.createContext(ctx);
  for (const a of arquivos) vm.runInContext(fs.readFileSync(path.join(PWA, a), 'utf8') + (a === 'qrcode.min.js' ? ';globalThis.qrcode=qrcode;' : ''), ctx);
  return ctx;
}

function cabecalho(bytes) { return Buffer.from(bytes).toString('latin1').split('BITMAP ')[0]; }
function bitmapXY(bytes) { return Buffer.from(bytes).toString('latin1').split('BITMAP ')[1].split(',').slice(0, 2).map(Number); }

const OASIS = { address: 'DC:0D:30:83:81:0B', midia: { preset: 'continuo-80', tipo: 'continuo', larguraMm: 80 }, offsetMm: { x: 1, y: 2 } };

module.exports = function ({ test, runInSandbox, assert }) {
  const run = body => JSON.parse(runInSandbox(`(function(){ ${body} })()`));

  test('#2329 posicaoNaCabeca: cabeça do catálogo, papel centralizado e ajuste fino da impressora', () => {
    const ctx = isolado(['impressao-papeis.js', 'impressora-tspl.js']);
    const p = JSON.parse(JSON.stringify(ctx.cdbTspl.posicaoNaCabeca(OASIS, ctx.cdbPapeis.daMidia(OASIS.midia))));
    assert.deepStrictEqual(p, { cabecaMm: 104, posicao: 'centro', ajusteMm: { x: 1, y: 2 } });
    const sem = JSON.parse(JSON.stringify(ctx.cdbTspl.posicaoNaCabeca({}, null)));
    assert.deepStrictEqual(sem, { cabecaMm: 104, posicao: 'centro', ajusteMm: { x: 0, y: 0 } });
  });

  test('#2329 pedido TSPL sai na cabeça, com SHIFT do ajuste e o x do papel', () => {
    const r = run(`
      const old = window.cdbTspl.paraEtiqueta;
      let cfg = null;
      try {
        window.cdbTspl.paraEtiqueta = (c, o) => { cfg = o; return new Uint8Array([1]); };
        tsplBytesDoCanvas({ width: 384, height: 100 }, ${JSON.stringify(OASIS)}, 2);
        return JSON.stringify({ cabecaMm: cfg.cabecaMm, posicao: cfg.posicao, ajusteMm: cfg.ajusteMm, copias: cfg.copias, larguraMm: cfg.larguraMm });
      } finally { window.cdbTspl.paraEtiqueta = old; }
    `);
    assert.deepStrictEqual(r, { cabecaMm: 104, posicao: 'centro', ajusteMm: { x: 1, y: 2 }, copias: 2, larguraMm: 80 });
  });

  test('#2329 com a cabeça, SHIFT vai mesmo zerado e o BITMAP começa no papel', () => {
    const ctx = isolado(['impressao-papeis.js', 'impressora-tspl.js']);
    const T = ctx.cdbTspl;
    const rgba = new Uint8ClampedArray(384 * 8 * 4).fill(255);
    const zero = T.etiqueta(rgba, 384, 8, Object.assign({ larguraMm: 80, alturaMm: 5, continuo: true }, T.posicaoNaCabeca({}, null)));
    assert.match(cabecalho(zero), /^SIZE 104 mm,5 mm\r\n[\s\S]*SHIFT 0\r\n/);
    // Papel de 80 mm centralizado na cabeça de 104: começa em 12 mm (96 pontos); desenho de 384 px centralizado no papel (+128).
    assert.deepStrictEqual(bitmapXY(zero), [96 + 128, 0]);
  });

  test('#2329 resolvedor mede cada texto uma vez por peso, sem mudar o layout', () => {
    const ctx = isolado(['qrcode.min.js', 'impressao-papeis.js', 'etiqueta-resolver.js', 'etiqueta-canvas.js']);
    const largura = t => String(t).length * 55; // a 100 px
    let chamadas = 0;
    const chaves = new Set();
    const doc = { createElement: () => ({ getContext: () => {
      let fonte = '';
      return { set font(f) { fonte = f; }, get font() { return fonte; }, measureText: t => { chamadas++; chaves.add(fonte.split(' ')[0] + '|' + t); return { width: largura(t) }; } };
    } }) };
    const PT_MM = 25.4 / 72;
    const medirSemCache = (t, pt) => largura(t) / 100 * pt * PT_MM;
    const campos = { empresa: 'Casa da Baba', nome: 'Bolo de cenoura com cobertura de chocolate meio amargo', validade: '20/11/2026', fabricacao: '12/11/2026', lote: 'L-000123', peso: 'PESO 1,2 kg', nota: 'Manter refrigerado entre 0 e 5 graus', qr: 'https://x.y/l/0123456789abcdef', barras: '0123456789' };
    for (const [papel, ori, codigos] of [['continuo-80', 'vertical', ['qr']], ['continuo-52', 'vertical', ['qr', 'barras']], ['etiqueta-50x30', 'horizontal', ['qr', 'barras']]]) {
      chamadas = 0; chaves.clear();
      const area = ctx.cdbEtiquetaCanvas.areaDoPapel(ctx.cdbPapeis.porId(papel), ori);
      const comCache = ctx.cdbResolverEtiqueta.resolver(campos, area, { codigos, document: doc, orientacao: ori });
      const referencia = ctx.cdbResolverEtiqueta.resolver(campos, area, { codigos, medir: medirSemCache, orientacao: ori });
      assert.deepStrictEqual(JSON.parse(JSON.stringify(comCache)), JSON.parse(JSON.stringify(referencia)), papel);
      assert.equal(chamadas, chaves.size, `${papel}: ${chamadas} medidas para ${chaves.size} textos`);
    }
  });
};
