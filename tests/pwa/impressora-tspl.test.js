'use strict';
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');

// #2193: a Oasis OIA-8381-B só fala TSPL. O codificador transforma o bitmap
// da etiqueta em SIZE/GAP/CLS/BITMAP/PRINT, com 0 = ponto preto.
const PWA = process.env.PWA_HTML ? path.dirname(process.env.PWA_HTML) : path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa');

function carregar() {
  const ctx = { globalThis: {} };
  ctx.globalThis = ctx;
  vm.createContext(ctx);
  vm.runInContext(fs.readFileSync(path.join(PWA, 'impressora-tspl.js'), 'utf8'), ctx);
  return ctx.cdbTspl;
}

function rgbaBranco(w, h) { return new Uint8ClampedArray(w * h * 4).fill(255); }
function pintar(rgba, w, x, y) { const i = (y * w + x) * 4; rgba[i] = rgba[i + 1] = rgba[i + 2] = 0; rgba[i + 3] = 255; }

function separar(bytes) {
  const texto = Buffer.from(bytes).toString('latin1');
  const marca = texto.indexOf('BITMAP ');
  const virgulas = [];
  for (let i = marca; virgulas.length < 5; i++) if (texto[i] === ',') virgulas.push(i);
  const inicioDados = virgulas[4] + 1;
  const fim = texto.lastIndexOf('\r\nPRINT');
  return { cabeca: texto.slice(0, inicioDados), dados: bytes.slice(inicioDados, fim), rodape: texto.slice(fim) };
}

module.exports = function ({ test }) {
  // #2202 (D1): sem mídia informada o padrão é o rolo contínuo (GAP 0,0).
  // Antes era GAP 2 mm fixo, que no rolo contínuo gastava uns 25 cm (P2).
  test('cabeçalho TSPL com SIZE, GAP, CLS e PRINT (contínuo por padrão)', () => {
    const T = carregar();
    const { cabeca, rodape } = separar(T.etiqueta(rgbaBranco(384, 80), 384, 80, {}));
    const linhas = cabeca.split('\r\n');
    assert.deepEqual(linhas.slice(0, 8), ['SIZE 104 mm,12 mm', 'GAP 0,0', 'SET TEAR ON', 'DIRECTION 1', 'REFERENCE 0,0', 'DENSITY 8', 'SPEED 4', 'CLS']);
    assert.match(linhas[8], /^BITMAP \d+,0,48,80,0,$/);
    assert.equal(rodape, '\r\nPRINT 1,1\r\n');
    assert.equal(T.DPI, 203);
    assert.equal(T.PONTOS_MM, 8);
  });

  test('papel contínuo usa GAP 0,0 e tamanho informado', () => {
    const T = carregar();
    const { cabeca } = separar(T.etiqueta(rgbaBranco(16, 8), 16, 8, { continuo: true, larguraMm: 58, alturaMm: 40 }));
    assert.ok(cabeca.startsWith('SIZE 58 mm,40 mm\r\nGAP 0,0\r\n'));
  });

  test('recortada manda o gap da mídia; as duas mandam SET TEAR ON para parar na serrilha', () => {
    const T = carregar();
    const rec = separar(T.etiqueta(rgbaBranco(16, 8), 16, 8, { larguraMm: 52, alturaMm: 30, gapMm: 3, densidade: 10, velocidade: 3 })).cabeca;
    assert.ok(rec.startsWith('SIZE 52 mm,30 mm\r\nGAP 3 mm,0 mm\r\nSET TEAR ON\r\nDIRECTION 1\r\nREFERENCE 0,0\r\nDENSITY 10\r\nSPEED 3\r\nCLS\r\n'), rec);
    const forcada = separar(T.etiqueta(rgbaBranco(16, 8), 16, 8, { continuo: false, larguraMm: 80, alturaMm: 40 })).cabeca;
    assert.match(forcada, /GAP 2 mm,0 mm\r\nSET TEAR ON/);
    const cont = separar(T.etiqueta(rgbaBranco(16, 8), 16, 8, { continuo: true, gapMm: 2, larguraMm: 80, alturaMm: 40 })).cabeca;
    assert.ok(/GAP 0,0\r\nSET TEAR ON\r\n/.test(cont) && !/GAP 2/.test(cont), cont);
  });

  test('opts.x fixa a coluna do BITMAP', () => {
    const T = carregar();
    const { cabeca } = separar(T.etiqueta(rgbaBranco(416, 8), 416, 8, { larguraMm: 52, x: 0 }));
    assert.match(cabeca, /BITMAP 0,0,52,8,0,$/);
  });

  // #2247 (v3): com a cabeça informada, SIZE ocupa a cabeça e o BITMAP começa
  // onde as guias põem o papel (centro), mais o ajuste fino (y em SHIFT).
  test('cabeça: papel de 52 mm centralizado na de 104 mm, com ajuste fino', () => {
    const T = carregar();
    const centro = separar(T.etiqueta(rgbaBranco(416, 8), 416, 8, { larguraMm: 52, alturaMm: 30, gapMm: 2, cabecaMm: 104, x: 0 })).cabeca;
    assert.ok(centro.startsWith('SIZE 104 mm,30 mm\r\n'), centro);
    assert.match(centro, /BITMAP 208,0,52,8,0,$/);
    assert.ok(!/SHIFT/.test(centro), centro);
    const ajustado = separar(T.etiqueta(rgbaBranco(416, 8), 416, 8, { larguraMm: 52, alturaMm: 30, cabecaMm: 104, x: 0, ajusteMm: { x: -1.5, y: 1 } })).cabeca;
    assert.match(ajustado, /REFERENCE 0,0\r\nSHIFT 8\r\n/);
    assert.match(ajustado, /BITMAP 196,0,52,8,0,$/);
    const borda = separar(T.etiqueta(rgbaBranco(416, 8), 416, 8, { larguraMm: 52, cabecaMm: 104, posicao: 'borda', x: 0 })).cabeca;
    assert.match(borda, /BITMAP 0,0,52,8,0,$/);
  });

  test('calibração: contínua só grava GAP 0,0; recortada mede com GAPDETECT', () => {
    const T = carregar();
    const cont = Buffer.from(T.calibracao({ tipo: 'continuo', larguraMm: 80, continuo: true })).toString('latin1');
    assert.equal(cont, 'SIZE 80 mm,30 mm\r\nGAP 0,0\r\n');
    const rec = Buffer.from(T.calibracao({ larguraMm: 100, alturaMm: 150, gapMm: 3, continuo: false })).toString('latin1');
    assert.equal(rec, 'SIZE 100 mm,150 mm\r\nGAP 3 mm,0 mm\r\nSET TEAR ON\r\nGAPDETECT\r\n');
    assert.ok(!/PRINT/.test(cont + rec));
  });

  // #2247 (v3): o teste de alinhamento vai para onde o papel está na cabeça,
  // com o ajuste fino salvo, como a etiqueta.
  test('alinhamento na cabeça: moldura no papel de 52 mm e ajuste fino', () => {
    const T = carregar();
    const midia = { larguraMm: 52, alturaMm: 30, gapMm: 2, continuo: false };
    const centro = Buffer.from(T.alinhamento(midia, { cabecaMm: 104 })).toString('latin1');
    assert.ok(centro.startsWith('SIZE 104 mm,30 mm\r\n'), centro);
    assert.match(centro, /\r\nBOX 216,8,615,231,2\r\n/);
    assert.match(centro, /\r\nTEXT 224,16,"2",0,1,1,"52 x 30 mm"\r\n/);
    assert.ok(!/SHIFT/.test(centro), centro);
    const ajustado = Buffer.from(T.alinhamento(midia, { cabecaMm: 104, ajusteMm: { x: 1, y: -0.5 } })).toString('latin1');
    assert.match(ajustado, /REFERENCE 0,0\r\nSHIFT -4\r\n/);
    assert.match(ajustado, /\r\nBOX 224,8,623,231,2\r\n/);
    const borda = Buffer.from(T.alinhamento(midia, { cabecaMm: 104, posicao: 'borda' })).toString('latin1');
    assert.match(borda, /\r\nBOX 8,8,407,231,2\r\n/);
  });

  test('alinhamento: moldura, cruz, régua de 10 mm e texto, sem bitmap', () => {
    const T = carregar();
    const txt = Buffer.from(T.alinhamento({ larguraMm: 52, alturaMm: 30, gapMm: 2, continuo: false })).toString('latin1');
    const linhas = txt.split('\r\n');
    assert.equal(linhas[0], 'SIZE 52 mm,30 mm');
    assert.ok(linhas.includes('GAP 2 mm,0 mm') && linhas.includes('SET TEAR ON'));
    assert.ok(linhas.includes('BOX 8,8,407,231,2'), 'moldura a 1 mm da borda');
    const ticks = linhas.filter(l => /^BAR \d+,\d+,1,\d+$/.test(l)).map(l => Number(l.split(/[ ,]/)[1]));
    assert.equal(ticks.length, 11);
    assert.equal(ticks[10] - ticks[0], 80, 'régua de 10 mm = 80 pontos');
    assert.ok(linhas.some(l => l.startsWith('TEXT 16,16,"2",0,1,1,"52 x 30 mm"')));
    assert.ok(!/BITMAP/.test(txt));
    assert.equal(linhas[linhas.length - 2], 'PRINT 1,1');
    const cont = Buffer.from(T.alinhamento({ larguraMm: 80, continuo: true })).toString('latin1');
    assert.ok(cont.startsWith('SIZE 80 mm,30 mm\r\nGAP 0,0\r\n'));
    assert.ok(cont.split('\r\n').some(l => l === 'TEXT 16,16,"2",0,1,1,"ROLO 80 mm"'));
  });

  test('largura em bytes arredonda para cima', () => {
    const T = carregar();
    const { cabeca, dados } = separar(T.etiqueta(rgbaBranco(10, 3), 10, 3, {}));
    assert.match(cabeca, /BITMAP \d+,0,2,3,0,$/);
    assert.equal(dados.length, 2 * 3);
  });

  test('polaridade: preto vira bit 0, branco fica 1', () => {
    const T = carregar();
    const rgba = rgbaBranco(8, 2);
    pintar(rgba, 8, 0, 0);
    const { dados } = separar(T.etiqueta(rgba, 8, 2, {}));
    assert.equal(dados[0], 0x7F);
    assert.equal(dados[1], 0xFF);
  });

  test('x centraliza o bitmap na largura da etiqueta', () => {
    const T = carregar();
    const { cabeca } = separar(T.etiqueta(rgbaBranco(384, 8), 384, 8, { larguraMm: 104 }));
    assert.match(cabeca, /BITMAP 224,0,/);
    const estreita = separar(T.etiqueta(rgbaBranco(384, 8), 384, 8, { larguraMm: 40 })).cabeca;
    assert.match(estreita, /BITMAP 0,0,/);
  });

  test('tamanho total do BITMAP e cópias', () => {
    const T = carregar();
    const bytes = T.etiqueta(rgbaBranco(384, 100), 384, 100, { copias: 3 });
    const { cabeca, dados, rodape } = separar(bytes);
    assert.equal(dados.length, 48 * 100);
    assert.equal(bytes.length, cabeca.length + dados.length + rodape.length);
    assert.equal(rodape, '\r\nPRINT 1,3\r\n');
  });

  test('doCanvas lê o canvas e chama etiqueta', () => {
    const T = carregar();
    const rgba = rgbaBranco(8, 1);
    pintar(rgba, 8, 7, 0);
    const canvas = { width: 8, height: 1, getContext: () => ({ getImageData: () => ({ data: rgba }) }) };
    const { dados } = separar(T.doCanvas(canvas, {}));
    assert.equal(dados[0], 0xFE);
  });

  // Canvas falso: devolve pixels brancos do tamanho pedido e guarda o drawImage.
  function documentoFalso() {
    const criados = [];
    return {
      criados,
      createElement() {
        const c = { width: 0, height: 0, desenhos: [] };
        c.getContext = () => ({
          fillRect() {}, drawImage(...a) { c.desenhos.push(a); },
          getImageData: () => ({ data: rgbaBranco(c.width, c.height) })
        });
        criados.push(c);
        return c;
      }
    };
  }
  const origem = (w, h) => ({ width: w, height: h });

  test('paraEtiqueta encaixa o desenho de 384 px na etiqueta 80 x 40 e centraliza', () => {
    const T = carregar();
    const doc = documentoFalso();
    const bytes = T.paraEtiqueta(origem(384, 200), { larguraMm: 80, alturaMm: 40, document: doc });
    const { cabeca } = separar(bytes);
    const alvo = doc.criados[0];
    assert.ok(alvo.width <= 80 * 8 - 16 && alvo.height <= 40 * 8 - 16, alvo.width + 'x' + alvo.height);
    assert.ok(cabeca.startsWith('SIZE 80 mm,40 mm\r\nGAP 2 mm,0 mm\r\n'));
    const m = /BITMAP (\d+),(\d+),(\d+),(\d+),0,$/.exec(cabeca);
    assert.equal(Number(m[4]), alvo.height);
    assert.equal(Number(m[2]), Math.floor((320 - alvo.height) / 2));
  });

  test('paraEtiqueta em bobina contínua cresce com o desenho', () => {
    const T = carregar();
    const doc = documentoFalso();
    const { cabeca } = separar(T.paraEtiqueta(origem(384, 800), { larguraMm: 80, continuo: true, document: doc }));
    const alvo = doc.criados[0];
    assert.equal(alvo.width, 624);
    assert.ok(cabeca.startsWith('SIZE 80 mm,' + (Math.ceil(alvo.height / 8) + 2) + ' mm\r\nGAP 0,0\r\n'));
  });

  test('#2202 paraEtiqueta no rolo contínuo corta o branco acima e abaixo do desenho', () => {
    const T = carregar();
    const rgba = rgbaBranco(384, 800);
    for (let y = 300; y < 400; y++) pintar(rgba, 384, 10, y);
    const fonte = { width: 384, height: 800, getContext: () => ({ getImageData: () => ({ data: rgba }) }) };
    const doc = documentoFalso();
    const { cabeca } = separar(T.paraEtiqueta(fonte, { larguraMm: 80, continuo: true, document: doc }));
    const alvo = doc.criados[0];
    assert.deepStrictEqual(alvo.desenhos[0].slice(1, 5), [0, 300, 384, 100], 'só a faixa com tinta');
    assert.equal(alvo.height, 162);
    assert.ok(cabeca.startsWith('SIZE 80 mm,23 mm\r\nGAP 0,0\r\n'), cabeca.slice(0, 40));
    const doc2 = documentoFalso();
    T.paraEtiqueta(fonte, { larguraMm: 80, alturaMm: 40, document: doc2 });
    assert.deepStrictEqual(doc2.criados[0].desenhos[0].slice(1, 5), [0, 0, 384, 800], 'recortada mantém o desenho inteiro');
  });

  test('inverter troca a polaridade e cópias chegam ao PRINT', () => {
    const T = carregar();
    const doc = documentoFalso();
    const { dados, rodape } = separar(T.paraEtiqueta(origem(384, 100), { larguraMm: 80, alturaMm: 40, inverter: true, copias: 2, document: doc }));
    assert.ok(dados.length > 0 && dados.every(b => b === 0x00));
    assert.equal(rodape, '\r\nPRINT 1,2\r\n');
  });

  test('linhasParaCanvas desenha em 384 px', () => {
    const T = carregar();
    const doc = documentoFalso();
    const ctx = { font: '', fillStyle: '', textAlign: '', textBaseline: '', fillRect() {}, fillText() {}, drawImage() {}, measureText: t => ({ width: String(t).length * 10 }) };
    const real = doc.createElement;
    doc.createElement = () => { const c = real(); c.getContext = () => ctx; return c; };
    const c = T.linhasParaCanvas('TESTE', ['Linha 1', 'Linha 2'], { document: doc });
    assert.equal(c.width, 384);
    assert.ok(c.height > 0);
  });
};
