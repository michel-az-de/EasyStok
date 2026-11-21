'use strict';
// Interpreta ESC/POS da térmica de 58 mm (cabeça de 48 mm = 384 pontos):
// raster GS v 0, avanço ESC d / LF e texto em fonte A (12x24). Basta para
// ver a etiqueta e medir o papel; a regressão compara os bytes.
const { createCanvas } = require('@napi-rs/canvas');

const LINHA_PONTOS = 30; // espaçamento padrão de 1/6" a 203 dpi, arredondado como na WTP05

function interpretar(bytes, opts) {
  const o = Object.assign({ larguraPontos: 384 }, opts || {});
  const W = o.larguraPontos;
  const linhas = []; // cada item: Uint8Array(W) com 1 = preto
  const avisos = [];
  let texto = ''; let negrito = false; let alinhar = 0; let escala = 1;
  function linhaTexto() {
    const h = 24 * escala;
    const c = createCanvas(W, h); const ctx = c.getContext('2d');
    ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, W, h); ctx.fillStyle = '#000';
    ctx.font = `${negrito ? 'bold ' : ''}${h - 2}px monospace`; ctx.textBaseline = 'top';
    const largura = texto.length * 12 * escala;
    const x = alinhar === 1 ? (W - largura) / 2 : alinhar === 2 ? W - largura : 0;
    for (let k = 0; k < texto.length; k++) ctx.fillText(texto[k], x + k * 12 * escala, 1, 12 * escala);
    const img = ctx.getImageData(0, 0, W, h).data;
    for (let y = 0; y < h; y++) {
      const l = new Uint8Array(W);
      for (let x2 = 0; x2 < W; x2++) l[x2] = img[(y * W + x2) * 4] < 128 ? 1 : 0;
      linhas.push(l);
    }
    texto = '';
  }
  function avancar(n) { for (let k = 0; k < n; k++) linhas.push(new Uint8Array(W)); }
  let i = 0;
  while (i < bytes.length) {
    const b = bytes[i];
    if (b === 0x1B) {
      const c = bytes[i + 1];
      if (c === 0x40) { i += 2; continue; }                       // ESC @
      if (c === 0x64) { if (texto) linhaTexto(); avancar(bytes[i + 2] * LINHA_PONTOS); i += 3; continue; } // ESC d n
      if (c === 0x61) { alinhar = bytes[i + 2] % 48; i += 3; continue; } // ESC a n
      if (c === 0x45) { negrito = !!bytes[i + 2]; i += 3; continue; }    // ESC E n
      if (c === 0x21) { escala = (bytes[i + 2] & 0x30) ? 2 : 1; i += 3; continue; } // ESC ! n
      if (c === 0x4A) { avancar(bytes[i + 2]); i += 3; continue; }       // ESC J n
      i += 3; continue;
    }
    if (b === 0x1D) {
      const c = bytes[i + 1];
      if (c === 0x76 && bytes[i + 2] === 0x30) {                   // GS v 0 m xL xH yL yH
        const wb = bytes[i + 4] | (bytes[i + 5] << 8); const h = bytes[i + 6] | (bytes[i + 7] << 8);
        const ini = i + 8;
        if (wb * 8 > W) avisos.push(`raster de ${wb * 8} pontos numa cabeça de ${W}`);
        for (let y = 0; y < h; y++) {
          const l = new Uint8Array(W);
          for (let x = 0; x < wb; x++) {
            const byte = bytes[ini + y * wb + x];
            for (let bit = 0; bit < 8; bit++) { const px = x * 8 + bit; if (px < W && (byte & (0x80 >> bit))) l[px] = 1; }
          }
          linhas.push(l);
        }
        i = ini + wb * h; continue;
      }
      if (c === 0x21) { escala = (bytes[i + 2] & 0x0F) ? 2 : 1; i += 3; continue; } // GS ! n
      if (c === 0x56) { i += bytes[i + 2] >= 65 ? 4 : 3; continue; }  // GS V corte
      if (c === 0x6B) {                                               // GS k barras: não desenha
        const m = bytes[i + 2];
        if (m >= 65) { i += 4 + bytes[i + 3]; } else { let k = i + 3; while (k < bytes.length && bytes[k] !== 0) k++; i = k + 1; }
        avisos.push('código de barras ESC/POS nativo não simulado'); avancar(60); continue;
      }
      if (c === 0x28 && bytes[i + 2] === 0x6B) { const n = bytes[i + 3] | (bytes[i + 4] << 8); i += 5 + n; continue; } // GS ( k
      i += 3; continue;
    }
    if (b === 0x0A) { if (texto) linhaTexto(); else avancar(LINHA_PONTOS); i++; continue; }
    if (b === 0x0D) { i++; continue; }
    texto += String.fromCharCode(b);
    i++;
  }
  if (texto) linhaTexto();
  const altura = linhas.length;
  const tinta = new Uint8Array(W * Math.max(1, altura));
  linhas.forEach((l, y) => tinta.set(l, y * W));
  return { largura: W, altura, tinta, avancoMm: altura / 8, avisos };
}

module.exports = { interpretar };
