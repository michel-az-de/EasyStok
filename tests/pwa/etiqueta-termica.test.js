'use strict';
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// #2141: a etiqueta de produção da térmica de 58 mm sai como imagem (GS v 0),
// a mesma que aparece na prévia. O canvas falso pinta pixels de verdade para
// que o raster conferido seja o que a impressora receberia.
const PWA = process.env.PWA_HTML ? path.dirname(process.env.PWA_HTML) : path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa');

function fakeCanvas(width, height) {
  const data = new Uint8ClampedArray(width * height * 4).fill(255);
  const texts = [];
  const ctx = {
    fillStyle: '#000', font: '16px sans-serif', textAlign: 'left', textBaseline: 'top',
    measureText(t) { return { width: String(t).length * px(ctx.font) * 0.56 }; },
    fillRect(x, y, w, h) {
      const dark = ctx.fillStyle === '#000' || ctx.fillStyle === '#000000' ? 0 : 255;
      for (let yy = Math.max(0, Math.round(y)); yy < Math.min(height, Math.round(y + h)); yy++)
        for (let xx = Math.max(0, Math.round(x)); xx < Math.min(width, Math.round(x + w)); xx++) {
          const i = (yy * width + xx) * 4; data[i] = data[i + 1] = data[i + 2] = dark;
        }
    },
    fillText(t, x, y) {
      const w = ctx.measureText(t).width;
      const left = ctx.textAlign === 'center' ? x - w / 2 : ctx.textAlign === 'right' ? x - w : x;
      texts.push({ t: String(t), left, right: left + w, y, size: px(ctx.font) });
    },
    getImageData(x, y, w, h) {
      const out = new Uint8ClampedArray(w * h * 4);
      for (let yy = 0; yy < h; yy++) out.set(data.subarray(((y + yy) * width + x) * 4, ((y + yy) * width + x + w) * 4), yy * w * 4);
      return { data: out, width: w, height: h };
    },
  };
  return { width, height, ctx, texts, getContext: () => ctx };
}
function px(font) { const m = /(\d+)px/.exec(font); return m ? Number(m[1]) : 16; }

function load() {
  const context = { console, Uint8Array, Uint8ClampedArray, Math, String, Number, Array, Object, Error };
  context.window = context;
  vm.createContext(context);
  vm.runInContext(fs.readFileSync(path.join(PWA, 'qrcode.min.js'), 'utf8'), context);
  vm.runInContext(fs.readFileSync(path.join(PWA, 'etiqueta-termica.js'), 'utf8'), context);
  return context.cdbEtiquetaTermica;
}

const ET = {
  empresa: 'Casa da Baba', productName: 'Lasanha à bolonhesa', unit: 'pote 1 kg', weightG: 1000,
  productionDate: Date.UTC(2026, 9, 28, 15), expiresAt: Date.UTC(2026, 9, 31, 15),
  lote: 'LOT-261028-066839', codigoBarras: 'P1-LOT-261028-066839-001',
};
const fmt = { data: ms => new Date(ms).toISOString().slice(0, 10).split('-').reverse().join('/'), peso: g => g >= 1000 ? (g / 1000).toFixed(2).replace('.', ',') + ' kg' : g + ' g' };

function render(T, et, opts = {}) {
  const canvas = fakeCanvas(T.LARGURA, 1600);
  const layout = T.desenhar(canvas.ctx, et, Object.assign({ formatar: fmt, codigos: 'qr' }, opts));
  return { canvas, layout };
}

module.exports = function ({ test, assert }) {
  test('Etiqueta térmica: raster GS v 0 empacota 8 pixels por byte, preto = 1', () => {
    const T = load();
    const rgba = new Uint8ClampedArray(16 * 2 * 4).fill(255);
    rgba[0] = rgba[1] = rgba[2] = 0;                 // (0,0) preto
    const p = (1 * 16 + 15) * 4; rgba[p] = rgba[p + 1] = rgba[p + 2] = 0; // (15,1) preto
    const bytes = Array.from(T.raster(rgba, 16, 2));
    assert.deepEqual(bytes.slice(0, 8), [0x1D, 0x76, 0x30, 0x00, 2, 0, 2, 0]);
    assert.deepEqual(bytes.slice(8), [0x80, 0x00, 0x00, 0x01]);
  });

  test('Etiqueta térmica: raster alto emite um GS v 0 por faixa de linhas', () => {
    const T = load();
    const h = T.FAIXA * 2 + 5;
    const bytes = Array.from(T.raster(new Uint8ClampedArray(8 * h * 4).fill(255), 8, h));
    let faixas = 0;
    for (let i = 0; i + 2 < bytes.length; i++) if (bytes[i] === 0x1D && bytes[i + 1] === 0x76 && bytes[i + 2] === 0x30) faixas++;
    assert.equal(faixas, 3);
  });

  test('Etiqueta térmica: marca Casa da Baba, acentos e nada passa da largura de 58 mm', () => {
    const T = load();
    const { canvas, layout } = render(T, ET);
    const textos = canvas.texts.map(x => x.t);
    assert.ok(textos.includes('Casa da Baba'), 'marca no topo');
    assert.ok(!textos.some(t => /easystok/i.test(t)), 'sem marca EasyStok');
    assert.ok(textos.some(t => t.includes('Lasanha à bolonhesa')), 'nome com acento: ' + textos.join(' | '));
    assert.ok(textos.some(t => t === '31/10/2026'), 'validade separada do rótulo');
    for (const x of canvas.texts) assert.ok(x.left >= 0 && x.right <= T.LARGURA, `"${x.t}" fora da largura (${x.left}..${x.right})`);
    assert.ok(layout.altura > 200 && layout.altura < 1600);
  });

  test('Etiqueta térmica: nome longo quebra em palavras sem perder letra', () => {
    const T = load();
    const nome = 'Ravioli de ricota com espinafre e nozes ao molho branco';
    const { canvas } = render(T, Object.assign({}, ET, { productName: nome }));
    const tamanhoNome = Math.max(...canvas.texts.filter(x => nome.includes(x.t)).map(x => x.size));
    const linhas = canvas.texts.filter(x => x.size === tamanhoNome && nome.includes(x.t)).map(x => x.t);
    assert.ok(linhas.length >= 2 && linhas.length <= 3, 'linhas: ' + linhas.join(' / '));
    assert.equal(linhas.join(' '), nome);
  });

  test('Etiqueta térmica: QR com módulo inteiro e zona branca de 4 módulos', () => {
    const T = load();
    const { canvas, layout } = render(T, ET);
    const { x, y, modulo, modulos } = layout.qr;
    assert.ok(Number.isInteger(modulo) && modulo >= 3, 'módulo ' + modulo);
    const img = canvas.ctx.getImageData(0, 0, T.LARGURA, layout.altura).data;
    const preto = (px_, py) => img[(py * T.LARGURA + px_) * 4] < 128;
    const q = 4 * modulo, lado = modulos * modulo;
    for (let py = y - q; py < y + lado + q; py++)
      for (const px_ of [x - q, x - 1, x + lado, x + lado + q - 1]) assert.ok(!preto(px_, py), `quiet zone suja em ${px_},${py}`);
    assert.ok(preto(x, y), 'canto do localizador do QR é preto');
  });

  test('Etiqueta térmica: bytes da impressão são imagem, não texto ESC/POS', () => {
    const T = load();
    const { canvas, layout } = render(T, ET);
    const bytes = Array.from(T.bytes(canvas.ctx, layout.altura));
    assert.deepEqual(bytes.slice(0, 2), [0x1B, 0x40], 'init');
    const i = bytes.findIndex((b, k) => b === 0x1D && bytes[k + 1] === 0x76 && bytes[k + 2] === 0x30);
    assert.ok(i > 0, 'tem GS v 0');
    assert.deepEqual(bytes.slice(i + 4, i + 6), [48, 0], '384 px = 48 bytes por linha');
    assert.ok(!Buffer.from(bytes).toString('latin1').includes('Lasanha'), 'nome não vai como texto');
  });
  function fromIndex(names, context) {
    const html = fs.readFileSync(process.env.PWA_HTML || path.join(PWA, 'index.html'), 'utf8');
    vm.createContext(context);
    for (const name of names) {
      const start = html.indexOf('function ' + name + '(');
      const end = html.indexOf('\nfunction ', start + 1);
      assert.ok(start >= 0 && end > start, name + ' existe no index.html');
      vm.runInContext(html.slice(start, end), context);
    }
    return { html, context };
  }

  test('Etiqueta térmica: index.html carrega o gerador e a impressão de produção usa a imagem', () => {
    const gerado = [];
    const { html, context } = fromIndex(['etiquetaTermica', 'code128TermicaLegivel', 'escPosEtiquetaProducao'], {
      console, document: {}, fmtDateBR: String, fmtPesoG: String, getProdScanMode: () => 'qr', drawCode128OnCanvas() {},
      getEmpresaName: () => 'Casa da Baba', escPosLabelProducao: () => Uint8Array.of(0x54),
      window: { cdbEtiquetaTermica: { gerar: (et, opts) => { gerado.push({ et, opts }); return { bytes: () => Uint8Array.of(0x1D, 0x76, 0x30) }; } } },
    });
    assert.match(html, /<script src="etiqueta-termica\.js[^"]*"><\/script>/);
    assert.match(html.slice(html.indexOf('async function etqAction'), html.indexOf('\nfunction ', html.indexOf('async function etqAction'))), /escPosEtiquetaProducao\(et\)/);
    const bytes = Array.from(context.escPosEtiquetaProducao({ productName: 'Lasanha à bolonhesa', _nota: 'sem lactose' }));
    assert.deepEqual(bytes, [0x1D, 0x76, 0x30]);
    assert.equal(gerado[0].et.empresa, 'Casa da Baba');
    assert.equal(gerado[0].opts.nota, 'sem lactose');
    assert.equal(gerado[0].opts.codigos, 'qr');
  });

  test('Etiqueta térmica: se a imagem falhar, imprime o texto antigo em vez de não imprimir', () => {
    const { context } = fromIndex(['etiquetaTermica', 'code128TermicaLegivel', 'escPosEtiquetaProducao'], {
      console: { warn() {} }, document: {}, fmtDateBR: String, fmtPesoG: String, getProdScanMode: () => 'both', drawCode128OnCanvas() {},
      getEmpresaName: () => 'Casa da Baba', escPosLabelProducao: () => Uint8Array.of(0x54),
      window: { cdbEtiquetaTermica: { gerar() { throw new Error('canvas indisponível'); } } },
    });
    assert.deepEqual(Array.from(context.escPosEtiquetaProducao({ productName: 'X' })), [0x54]);
  });
  test('Etiqueta térmica: barras que não cabem com 2 pontos dão lugar ao QR', () => {
    const T = load();
    const { layout } = render(T, ET, { codigos: 'barcode', code128: () => false });
    assert.ok(layout.qr && layout.qr.modulo >= 3, 'QR assume quando as barras ficariam ilegíveis');
    const desenhadas = render(T, ET, { codigos: 'barcode', code128: () => true }).layout;
    assert.equal(desenhadas.qr, null, 'com barras legíveis não duplica com QR');
  });

  test('Etiqueta térmica: prazo do envio cresce com o tamanho da impressão', () => {
    const { context } = fromIndex(['printWriteTimeoutMs'], {});
    assert.equal(context.printWriteTimeoutMs(300), 16000);
    assert.ok(context.printWriteTimeoutMs(1000000) >= 140000, 'lote de 1 MB não cai no prazo de 15 s');
  });
};
