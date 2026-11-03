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
  test('cabeçalho TSPL com SIZE, GAP, CLS e PRINT', () => {
    const T = carregar();
    const { cabeca, rodape } = separar(T.etiqueta(rgbaBranco(384, 80), 384, 80, {}));
    const linhas = cabeca.split('\r\n');
    assert.deepEqual(linhas.slice(0, 7), ['SIZE 104 mm,12 mm', 'GAP 2 mm,0 mm', 'DIRECTION 1', 'REFERENCE 0,0', 'DENSITY 8', 'SPEED 4', 'CLS']);
    assert.match(linhas[7], /^BITMAP \d+,0,48,80,0,$/);
    assert.equal(rodape, '\r\nPRINT 1,1\r\n');
    assert.equal(T.DPI, 203);
    assert.equal(T.PONTOS_MM, 8);
  });

  test('papel contínuo usa GAP 0,0 e tamanho informado', () => {
    const T = carregar();
    const { cabeca } = separar(T.etiqueta(rgbaBranco(16, 8), 16, 8, { continuo: true, larguraMm: 58, alturaMm: 40 }));
    assert.ok(cabeca.startsWith('SIZE 58 mm,40 mm\r\nGAP 0,0\r\n'));
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
