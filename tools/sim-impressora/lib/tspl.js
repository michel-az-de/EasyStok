'use strict';
// Interpreta os bytes TSPL que o app manda e rasteriza cada PRINT como a
// impressora faria: 8 pontos/mm, origem no canto do papel, cabeça de 104 mm.
const { createCanvas } = require('@napi-rs/canvas');

const PONTOS_MM = 8;
// Fontes residentes da TSPL (largura x altura em pontos, por caractere).
const FONTE_TSPL = { 1: [8, 12], 2: [12, 20], 3: [16, 24], 4: [24, 32], 5: [32, 48], 6: [14, 19], 7: [21, 27], 8: [14, 25] };

function mm(valor) {
  const s = String(valor || '').trim();
  const n = parseFloat(s);
  if (!Number.isFinite(n)) return 0;
  if (/mm$/i.test(s)) return n;
  if (/dot$/i.test(s)) return n / PONTOS_MM;
  return n * 25.4; // TSPL sem unidade = polegada
}

function args(linha) {
  // Separa por vírgula respeitando aspas.
  const out = []; let atual = ''; let aspas = false;
  for (const ch of linha) {
    if (ch === '"') { aspas = !aspas; atual += ch; continue; }
    if (ch === ',' && !aspas) { out.push(atual.trim()); atual = ''; continue; }
    atual += ch;
  }
  if (atual.trim() !== '' || out.length) out.push(atual.trim());
  return out;
}

function texto(rotulo) { return String(rotulo || '').replace(/^"|"$/g, ''); }

// Lê os comandos. Devolve { etiquetas: [...], comandos: [...], avisos: [...] }.
function interpretar(bytes, opts) {
  const o = Object.assign({ cabecaMm: 104 }, opts || {});
  const dec = b => String.fromCharCode.apply(null, Array.from(b));
  const estado = { larguraMm: 0, alturaMm: 0, gapMm: null, gapOffsetMm: 0, direcao: 0, ref: [0, 0], densidade: null, velocidade: null, rasgo: null, gapdetect: false, autodetect: false, limitfeedMm: null };
  const etiquetas = []; const comandos = []; const avisos = [];
  let tinta = null; let largura = 0; let altura = 0; let cortados = 0; let elementos = [];
  function limpar() {
    largura = Math.round(estado.larguraMm * PONTOS_MM);
    altura = Math.round(estado.alturaMm * PONTOS_MM);
    tinta = new Uint8Array(Math.max(1, largura * Math.max(1, altura)));
    cortados = 0; elementos = [];
  }
  function ponto(x, y, modo) {
    x += estado.ref[0]; y += estado.ref[1];
    if (x < 0 || y < 0 || x >= largura || y >= altura) { cortados++; return; }
    const i = y * largura + x;
    if (modo === 2) tinta[i] ^= 1; else tinta[i] = 1;
  }
  let i = 0;
  while (i < bytes.length) {
    let fim = i;
    if (/^BITMAP\b/i.test(dec(bytes.subarray(i, i + 7)))) {
      // O cabeçalho do BITMAP termina na 5ª vírgula; depois vêm os bytes crus.
      let virgulas = 0;
      while (fim < bytes.length && !(bytes[fim] === 0x2C && ++virgulas === 5)) fim++;
    } else {
      while (fim < bytes.length && bytes[fim] !== 0x0A && bytes[fim] !== 0x0D) fim++;
    }
    const linha = dec(bytes.subarray(i, fim)).trim();
    if (/^BITMAP\b/i.test(linha) && bytes[fim] === 0x2C) {
      const [x, y, wb, h, modo] = args(linha.replace(/^BITMAP\s*/i, '')).map(Number);
      const ini = fim + 1; const n = wb * h;
      const dados = bytes.subarray(ini, ini + n);
      if (dados.length < n) avisos.push(`BITMAP truncado: ${dados.length} de ${n} bytes`);
      if (!tinta) limpar();
      for (let yy = 0; yy < h; yy++) for (let b = 0; b < wb; b++) {
        const byte = dados[yy * wb + b];
        for (let bit = 0; bit < 8; bit++) if (!(byte & (0x80 >> bit))) ponto(x + b * 8 + bit, y + yy, modo);
      }
      elementos.push({ tipo: 'BITMAP', x, y, larguraPontos: wb * 8, altura: h });
      comandos.push(`BITMAP ${x},${y},${wb},${h},${modo},<${n} bytes>`);
      i = ini + n;
      continue;
    }
    i = fim + 1;
    if (!linha) continue;
    comandos.push(linha);
    const [cmd, ...resto] = linha.split(/\s+/);
    const a = args(resto.join(' '));
    switch (cmd.toUpperCase()) {
      case 'SIZE': estado.larguraMm = mm(a[0]); estado.alturaMm = mm(a[1]); break;
      case 'GAP': estado.gapMm = mm(a[0]); estado.gapOffsetMm = mm(a[1]); break;
      case 'BLINE': estado.gapMm = mm(a[0]); estado.marca = true; break;
      case 'DIRECTION': estado.direcao = Number(a[0]) || 0; break;
      case 'REFERENCE': estado.ref = [Number(a[0]) || 0, Number(a[1]) || 0]; break;
      case 'DENSITY': estado.densidade = Number(a[0]); break;
      case 'SPEED': estado.velocidade = Number(a[0]); break;
      case 'GAPDETECT': estado.gapdetect = true; break;
      case 'AUTODETECT': estado.autodetect = true; break;
      case 'LIMITFEED': estado.limitfeedMm = mm(a[0]); break;
      case 'SET': if (/^TEAR$/i.test(resto[0])) estado.rasgo = /ON/i.test(resto[1] || ''); break;
      case 'CLS': limpar(); break;
      case 'BAR': {
        if (!tinta) limpar();
        const [x, y, w, h] = a.map(Number);
        for (let yy = 0; yy < h; yy++) for (let xx = 0; xx < w; xx++) ponto(x + xx, y + yy, 0);
        elementos.push({ tipo: 'BAR', x, y, w, h });
        break;
      }
      case 'BOX': {
        if (!tinta) limpar();
        const [x, y, xe, ye, t] = a.map(Number);
        const esp = Math.max(1, t || 1);
        for (let yy = y; yy <= ye; yy++) for (let xx = x; xx <= xe; xx++) {
          if (yy < y + esp || yy > ye - esp || xx < x + esp || xx > xe - esp) ponto(xx, yy, 0);
        }
        elementos.push({ tipo: 'BOX', x, y, xe, ye, t: esp });
        break;
      }
      case 'TEXT': {
        if (!tinta) limpar();
        const x = Number(a[0]), y = Number(a[1]);
        const fonte = texto(a[2]); const xm = Number(a[4]) || 1, ym = Number(a[5]) || 1;
        const conteudo = texto(a.slice(6).join(','));
        const [fw, fh] = FONTE_TSPL[fonte] || FONTE_TSPL[2];
        const w = Math.max(1, fw * xm * conteudo.length), h = fh * ym;
        const c = createCanvas(w, h); const ctx = c.getContext('2d');
        ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, w, h); ctx.fillStyle = '#000';
        ctx.font = `bold ${h}px monospace`; ctx.textBaseline = 'top';
        for (let k = 0; k < conteudo.length; k++) ctx.fillText(conteudo[k], k * fw * xm, 0, fw * xm);
        const img = ctx.getImageData(0, 0, w, h).data;
        for (let yy = 0; yy < h; yy++) for (let xx = 0; xx < w; xx++) if (img[(yy * w + xx) * 4] < 128) ponto(x + xx, y + yy, 0);
        elementos.push({ tipo: 'TEXT', x, y, fonte, conteudo });
        break;
      }
      case 'PRINT': {
        const m = Math.max(1, Number(a[0]) || 1), n = Math.max(1, Number(a[1]) || 1);
        if (!tinta) limpar();
        etiquetas.push({
          larguraMm: estado.larguraMm, alturaMm: estado.alturaMm, gapMm: estado.gapMm, gapOffsetMm: estado.gapOffsetMm,
          direcao: estado.direcao, densidade: estado.densidade, velocidade: estado.velocidade,
          gapdetect: estado.gapdetect, rasgo: estado.rasgo, limitfeedMm: estado.limitfeedMm,
          largura, altura, tinta: tinta.slice(), cortados, elementos: elementos.slice(), copias: m * n,
          dentroDaCabeca: estado.larguraMm <= o.cabecaMm + 0.01
        });
        break;
      }
      default: break;
    }
  }
  return { etiquetas, comandos, avisos };
}

// Papel gasto por uma etiqueta, dada a mídia que está de fato na impressora.
// midia = { tipo:'continuo', larguraMm } ou { tipo:'recortado', larguraMm, alturaMm, gapMm }.
// limiteBuscaMm: quanto a Oasis avança procurando um gap que não existe
// (25 cm medidos no campo com o app 0.8.7).
function papel(et, midia, opts) {
  const o = Object.assign({ limiteBuscaMm: 250 }, opts || {});
  const pedeGap = et.gapMm != null && et.gapMm > 0;
  const r = { avancoMm: 0, alinhado: true, motivo: [] };
  if (midia.tipo === 'continuo') {
    if (pedeGap || et.gapdetect) {
      r.avancoMm = Math.max(et.alturaMm, et.limitfeedMm || o.limiteBuscaMm);
      r.motivo.push(`GAP ${et.gapMm} mm num rolo contínuo: a impressora procura o recorte e avança ${r.avancoMm} mm`);
      r.alinhado = false;
    } else r.avancoMm = et.alturaMm;
  } else {
    const passo = midia.alturaMm + midia.gapMm;
    if (!pedeGap) {
      r.avancoMm = et.alturaMm;
      r.alinhado = Math.abs(et.alturaMm - passo) <= 1;
      if (!r.alinhado) r.motivo.push(`GAP 0 em etiqueta recortada: avança ${et.alturaMm} mm e desalinha do recorte (passo ${passo} mm)`);
    } else {
      r.avancoMm = passo * Math.max(1, Math.ceil((et.alturaMm - 0.5) / midia.alturaMm));
      const okAltura = Math.abs(et.alturaMm - midia.alturaMm) <= 1;
      const okGap = Math.abs(et.gapMm - midia.gapMm) <= 1;
      r.alinhado = okAltura && okGap;
      if (!okAltura) r.motivo.push(`SIZE ${et.alturaMm} mm em etiqueta de ${midia.alturaMm} mm`);
      if (!okGap) r.motivo.push(`GAP ${et.gapMm} mm em etiqueta com gap de ${midia.gapMm} mm`);
    }
  }
  return r;
}

module.exports = { interpretar, papel, PONTOS_MM };
