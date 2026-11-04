'use strict';
// Lê QR (jsQR e ZXing) e Code128 (ZXing) direto do raster impresso, como um
// leitor apontado para a etiqueta. Entrada: tinta (1 = preto), largura, altura.
const jsQR = require('jsqr');
const ZXing = require('@zxing/library');

// Raster 1 bit -> luminância com borda branca (o papel em volta da etiqueta).
function luminancia(tinta, w, h, escala, borda) {
  const s = escala || 1, b = borda || 0;
  const W = w * s + 2 * b, H = h * s + 2 * b;
  const lum = new Uint8ClampedArray(W * H).fill(255);
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    if (!tinta[y * w + x]) continue;
    for (let dy = 0; dy < s; dy++) {
      const off = (b + y * s + dy) * W + b + x * s;
      lum.fill(0, off, off + s);
    }
  }
  return { lum, W, H };
}

function rgba({ lum, W, H }) {
  const out = new Uint8ClampedArray(W * H * 4);
  for (let i = 0; i < lum.length; i++) { out[i * 4] = out[i * 4 + 1] = out[i * 4 + 2] = lum[i]; out[i * 4 + 3] = 255; }
  return out;
}

function girar90(tinta, w, h) {
  const out = new Uint8Array(w * h);
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) out[x * h + (h - 1 - y)] = tinta[y * w + x];
  return out;
}

function lerQrJs(tinta, w, h) {
  for (const s of [1, 2, 3]) {
    const L = luminancia(tinta, w, h, s, 16);
    const r = jsQR(rgba(L), L.W, L.H, { inversionAttempts: 'dontInvert' });
    if (r) {
      const n = 17 + 4 * r.version;
      const a = r.location.topLeftFinderPattern, c = r.location.topRightFinderPattern;
      const dist = Math.hypot(c.x - a.x, c.y - a.y);
      const mod = (dist / (n - 7)) / s;
      // Caixa do símbolo no raster (sem a borda de 16 px da leitura).
      const L0 = r.location;
      const xs = [L0.topLeftCorner.x, L0.topRightCorner.x, L0.bottomLeftCorner.x, L0.bottomRightCorner.x].map(v => (v - 16) / s);
      const ys = [L0.topLeftCorner.y, L0.topRightCorner.y, L0.bottomLeftCorner.y, L0.bottomRightCorner.y].map(v => (v - 16) / s);
      const caixa = { x0: Math.min(...xs), y0: Math.min(...ys), x1: Math.max(...xs), y1: Math.max(...ys) };
      return { texto: r.data, moduloPontos: Math.round(mod * 10) / 10, modulos: n, caixa, moduloExato: mod };
    }
  }
  return null;
}

function lerQrZxing(tinta, w, h) {
  const hints = new Map([[ZXing.DecodeHintType.TRY_HARDER, true]]);
  for (const s of [1, 2]) {
    try {
      const L = luminancia(tinta, w, h, s, 16);
      const bmp = new ZXing.BinaryBitmap(new ZXing.HybridBinarizer(new ZXing.RGBLuminanceSource(L.lum, L.W, L.H)));
      return { texto: new ZXing.QRCodeReader().decode(bmp, hints).getText() };
    } catch (e) { /* tenta a próxima escala */ }
  }
  return null;
}

// Code128: varre o raster nas duas direções (a etiqueta pode sair girada).
function lerBarras(tinta, w, h) {
  const hints = new Map([[ZXing.DecodeHintType.TRY_HARDER, true], [ZXing.DecodeHintType.POSSIBLE_FORMATS, [ZXing.BarcodeFormat.CODE_128]]]);
  const tentativas = [{ t: tinta, w, h, girada: false }, { t: girar90(tinta, w, h), w: h, h: w, girada: true }];
  for (const v of tentativas) {
    try {
      const L = luminancia(v.t, v.w, v.h, 1, 24);
      const bmp = new ZXing.BinaryBitmap(new ZXing.HybridBinarizer(new ZXing.RGBLuminanceSource(L.lum, L.W, L.H)));
      const r = new ZXing.Code128Reader().decode(bmp, hints);
      const pts = r.getResultPoints();
      const y = Math.round(pts[0].getY()) - 24;
      const x0 = Math.round(Math.min(pts[0].getX(), pts[1].getX())) - 24;
      const x1 = Math.round(Math.max(pts[0].getX(), pts[1].getX())) - 24;
      return Object.assign({ texto: r.getText(), girada: v.girada }, medirBarras(v.t, v.w, v.h, y, x0, x1, r.getText()));
    } catch (e) { /* tenta girada */ }
  }
  return null;
}

// Módulos de um Code128 com o conteúdo dado (C para dígitos em pares, B no resto).
function modulosCode128(texto) {
  const s = String(texto);
  if (/^\d+$/.test(s)) return (Math.floor(s.length / 2) + (s.length % 2 ? 2 : 0) + 3) * 11 + 2;
  return (s.length + 3) * 11 + 2;
}

// Do primeiro ao último preto na linha lida: largura do símbolo e zona quieta.
function medirBarras(t, w, h, y, x0, x1, texto) {
  if (y < 0 || y >= h) return {};
  const linha = t.subarray(y * w, (y + 1) * w);
  const pad = Math.round((x1 - x0) * 0.2) + 8;
  let ini = -1, fim = -1;
  for (let x = Math.max(0, x0 - pad); x <= Math.min(w - 1, x1 + pad); x++) if (linha[x]) { if (ini < 0) ini = x; fim = x; }
  if (ini < 0) return {};
  const mods = modulosCode128(texto);
  const modulo = (fim - ini + 1) / mods;
  let quietaE = 0; for (let x = ini - 1; x >= 0 && !linha[x]; x--) quietaE++;
  let quietaD = 0; for (let x = fim + 1; x < w && !linha[x]; x++) quietaD++;
  // Altura das barras: coluna do primeiro preto, para cima e para baixo.
  let alt = 0; for (let yy = 0; yy < h; yy++) if (t[yy * w + ini]) alt++;
  return { moduloPontos: Math.round(modulo * 10) / 10, quietaModulos: Math.floor(Math.min(quietaE, quietaD) / modulo), alturaMm: alt / 8, ini, fim, modulo };
}

module.exports = { lerQrJs, lerQrZxing, lerBarras, modulosCode128, girar90 };
