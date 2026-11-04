'use strict';
const { createCanvas, loadImage } = require('@napi-rs/canvas');

function caixaDeTinta(tinta, w, h) {
  let x0 = w, y0 = h, x1 = -1, y1 = -1;
  for (let y = 0; y < h; y++) {
    const off = y * w;
    for (let x = 0; x < w; x++) if (tinta[off + x]) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
  }
  return x1 < 0 ? null : { x0, y0, x1, y1, w: x1 - x0 + 1, h: y1 - y0 + 1 };
}

// Recorta o raster da etiqueta para a largura do papel (o que passa dele não sai).
function recortar(tinta, w, h, larguraNova) {
  const W = Math.min(w, larguraNova);
  const out = new Uint8Array(W * h);
  let perdidos = 0;
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      if (x < W) out[y * W + x] = tinta[y * w + x];
      else if (tinta[y * w + x]) perdidos++;
    }
  }
  return { tinta: out, w: W, h, perdidos };
}

// PNG do papel: branco = papel, cinza = fora do papel (gap ou avanço vazio).
function png(tinta, w, h, opts) {
  const o = opts || {};
  const H = Math.max(h, o.alturaTotal || h);
  const c = createCanvas(Math.max(1, w), Math.max(1, H));
  const ctx = c.getContext('2d');
  const img = ctx.createImageData(c.width, c.height);
  const d = img.data;
  for (let y = 0; y < H; y++) for (let x = 0; x < w; x++) {
    const i = (y * w + x) * 4;
    let v = 255;
    if (y < h) v = tinta[y * w + x] ? 0 : 255;
    else v = o.corAvanco != null ? o.corAvanco : 245;
    d[i] = d[i + 1] = d[i + 2] = v; d[i + 3] = 255;
  }
  ctx.putImageData(img, 0, 0);
  return c.toBuffer('image/png');
}

async function lerPng(arquivo) {
  const img = await loadImage(arquivo);
  const c = createCanvas(img.width, img.height);
  const ctx = c.getContext('2d');
  ctx.drawImage(img, 0, 0);
  return { w: img.width, h: img.height, data: ctx.getImageData(0, 0, img.width, img.height).data };
}

// Fração de pixels diferentes entre dois PNG (tamanhos diferentes = 100%).
async function diferenca(a, b) {
  const [A, B] = await Promise.all([lerPng(a), lerPng(b)]);
  if (A.w !== B.w || A.h !== B.h) return { fracao: 1, motivo: `tamanho ${A.w}x${A.h} vs ${B.w}x${B.h}` };
  let n = 0;
  for (let i = 0; i < A.data.length; i += 4) if (Math.abs(A.data[i] - B.data[i]) > 64) n++;
  return { fracao: n / (A.w * A.h) };
}

module.exports = { caixaDeTinta, recortar, png, lerPng, diferenca };
