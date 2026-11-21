#!/usr/bin/env node
'use strict';
// Prancha do gate 4: ANTES (master), DEPOIS e o protótipo lado a lado, na
// mesma escala física, um PNG por tamanho de papel.
// Uso: node prancha.js [--antes saida/antes] [--depois saida/depois] [--saida saida]
// Rode antes: node matriz.js --pwa <pwa do master> --saida saida/antes && node matriz.js
const fs = require('fs');
const path = require('path');
const { createCanvas, loadImage, GlobalFonts } = require('@napi-rs/canvas');
const { casos, TAMANHOS } = require('./casos');

function opt(nome, padrao) {
  const i = process.argv.indexOf('--' + nome);
  return path.resolve(i > 0 ? process.argv[i + 1] : padrao);
}
const ANTES = opt('antes', path.join(__dirname, 'saida', 'antes'));
const DEPOIS = opt('depois', path.join(__dirname, 'saida', 'depois'));
const SAIDA = opt('saida', path.join(__dirname, 'saida'));
const PROTO = path.join(__dirname, 'referencia-prototipo');

for (const arq of ['Manrope-400', 'Manrope-600', 'Manrope-800']) GlobalFonts.registerFromPath(path.join(__dirname, 'fontes', arq + '.ttf'), 'Manrope');

const COR = { fundo: '#eef0f3', tinta: '#111418', fraco: '#4a5260', verde: '#0b6b2f', vermelho: '#b3121b', borda: '#9aa3b0', cabecalho: '#1b2330' };
const PROTO_ID = { '52': 'rolo52', '80': 'rolo80', '10x15': '10x15' };
const PROTO_CODIGOS = { qr: 'qr', barras: 'barras', ambos: 'ambos' };
const LARGURA_CELULA = 300;  // px da largura física do rolo
const ALTURA_MAX = 560;      // px; o que passa disso é recortado na prancha
const PAD = 28, LEGENDA = 140, TITULO = 150, ROTULO = 210;

const ler = d => {
  const j = JSON.parse(fs.readFileSync(path.join(d, 'resultado.json'), 'utf8'));
  return Object.fromEntries(j.casos.map(c => [c.id, c]));
};

function linhas(r) {
  if (!r) return [{ t: 'sem resultado', c: COR.vermelho }];
  const probs = [...new Set((r.falhas || []).map(f => f.problema).filter(Boolean))].sort();
  const status = r.ok ? { t: 'VERDE', c: COR.verde } : { t: 'FALHA ' + probs.join(' '), c: COR.vermelho };
  if (r.recusa) return [status, { t: 'recusa: ' + r.recusa, c: COR.fraco }];
  const p = r.papel || {}, f = r.fontes || {};
  const cod = [r.qr && r.qr.moduloPontos ? 'QR ' + r.qr.moduloPontos + ' pt' : null, r.barras && r.barras.moduloPontos ? 'barra ' + r.barras.moduloPontos + ' pt' : null].filter(Boolean).join(', ');
  return [status, { t: `avança ${p.avancoMm} mm, conteúdo ${p.conteudoMm} mm`, c: COR.fraco }, { t: `nome ${f.nomePt} pt, menor ${f.menorPt} pt`, c: COR.fraco }].concat(cod ? [{ t: 'módulo ' + cod, c: COR.fraco }] : []);
}

function linhasProto(m) {
  if (!m) return [{ t: 'sem referência', c: COR.fraco }];
  const cod = [m.qrModuloDots ? 'QR ' + m.qrModuloDots + ' pt' : null, m.barrasModuloDots ? 'barra ' + m.barrasModuloDots + ' pt' : null].filter(Boolean).join(', ');
  return [{ t: 'PROTÓTIPO', c: COR.cabecalho }, { t: m.tspl.replace(' | ', ', '), c: COR.fraco }, { t: `nome ${m.nomePt} pt, menor ${m.menorPt} pt`, c: COR.fraco }].concat(cod ? [{ t: 'módulo ' + cod, c: COR.fraco }] : []);
}

async function imagem(arquivo) {
  return fs.existsSync(arquivo) ? loadImage(fs.readFileSync(arquivo)) : null;
}

function celula(ctx, img, x, y, escala, legenda) {
  if (img) {
    const w = Math.round(img.width * escala), hTotal = Math.round(img.height * escala), h = Math.min(hTotal, ALTURA_MAX);
    ctx.imageSmoothingEnabled = escala < 1;
    ctx.drawImage(img, 0, 0, img.width, Math.round(h / escala), x, y, w, h);
    ctx.strokeStyle = COR.borda; ctx.lineWidth = 2; ctx.strokeRect(x - 1, y - 1, w + 2, h + 2);
    if (hTotal > h) {
      ctx.fillStyle = 'rgba(179,18,27,0.92)'; ctx.fillRect(x, y + h - 40, w, 40);
      ctx.fillStyle = '#fff'; ctx.font = '600 20px Manrope';
      ctx.fillText(`segue por mais ${Math.round((hTotal - h) / escala / 8)} mm`, x + 10, y + h - 13);
    }
  }
  let ly = y + ALTURA_MAX + 34;
  legenda.forEach((l, i) => {
    ctx.fillStyle = l.c; ctx.font = (i === 0 ? '800 24px' : '400 20px') + ' Manrope';
    ctx.fillText(l.t, x, ly); ly += i === 0 ? 32 : 27;
  });
}

async function prancha(tamanho, antes, depois, proto) {
  const fis = TAMANHOS[tamanho];
  const escala = LARGURA_CELULA / (fis.continuo.larguraMm * 8);  // px da prancha por ponto
  const colunas = [
    { titulo: 'ANTES contínuo', fonte: 'antes', midia: 'continuo' },
    { titulo: 'ANTES recortado', fonte: 'antes', midia: 'recortado' },
    { titulo: 'DEPOIS contínuo', fonte: 'depois', midia: 'continuo' },
    { titulo: 'DEPOIS recortado', fonte: 'depois', midia: 'recortado' },
    { titulo: 'PROTÓTIPO', fonte: 'proto' }
  ];
  const grupos = [];
  for (const orientacao of ['vertical', 'horizontal']) for (const codigos of ['qr', 'barras', 'ambos']) grupos.push({ orientacao, codigos });
  const larguraCol = LARGURA_CELULA + Math.round(12 * escala) + PAD;
  const alturaLinha = ALTURA_MAX + LEGENDA + PAD * 2;
  const W = ROTULO + colunas.length * larguraCol + PAD, H = TITULO + 60 + grupos.length * alturaLinha;
  const cv = createCanvas(W, H), ctx = cv.getContext('2d');
  ctx.fillStyle = COR.fundo; ctx.fillRect(0, 0, W, H);

  const lista = casos().filter(c => c.tamanho === tamanho);
  const verdes = d => lista.filter(c => d[c.id] && d[c.id].ok).length;
  ctx.fillStyle = COR.cabecalho; ctx.font = '800 44px Manrope';
  ctx.fillText(`Etiqueta de produção, papel ${tamanho === '10x15' ? '10x15 (100 mm)' : tamanho + ' mm'}`, PAD, 62);
  ctx.font = '600 26px Manrope';
  ctx.fillStyle = verdes(antes) === lista.length ? COR.verde : COR.vermelho; ctx.fillText(`ANTES (master): ${verdes(antes)}/${lista.length} verdes`, PAD, 112);
  ctx.fillStyle = verdes(depois) === lista.length ? COR.verde : COR.vermelho; ctx.fillText(`DEPOIS: ${verdes(depois)}/${lista.length} verdes`, PAD + 430, 112);
  ctx.fillStyle = COR.fraco; ctx.font = '400 22px Manrope';
  ctx.fillText(`Escala física igual em todas as células. Recortado: ${fis.recortado.larguraMm}x${fis.recortado.alturaMm} mm, gap ${fis.recortado.gapMm} mm. Cinza: papel avançado sem uso.`, PAD + 760, 112);
  colunas.forEach((c, i) => { ctx.fillStyle = COR.cabecalho; ctx.font = '800 26px Manrope'; ctx.fillText(c.titulo, ROTULO + i * larguraCol, TITULO + 34); });

  for (let g = 0; g < grupos.length; g++) {
    const { orientacao, codigos } = grupos[g];
    const y = TITULO + 60 + g * alturaLinha + PAD;
    ctx.fillStyle = COR.cabecalho; ctx.font = '800 28px Manrope'; ctx.fillText(orientacao, PAD, y + 34);
    ctx.font = '600 24px Manrope'; ctx.fillStyle = COR.fraco; ctx.fillText(codigos === 'ambos' ? 'QR + barra' : codigos === 'qr' ? 'só QR' : 'só barra', PAD, y + 70);
    for (let i = 0; i < colunas.length; i++) {
      const c = colunas[i], x = ROTULO + i * larguraCol;
      if (c.fonte === 'proto') {
        const pid = `${PROTO_ID[tamanho]}-${orientacao}`;
        const m = proto.find(p => p.id === pid && p.codigos === PROTO_CODIGOS[codigos]);
        celula(ctx, await imagem(path.join(PROTO, `${pid}__${PROTO_CODIGOS[codigos]}.png`)), x, y, escala, linhasProto(m));
        continue;
      }
      const id = `${tamanho}-${orientacao}-${codigos}-${c.midia}`;
      const dir = c.fonte === 'antes' ? ANTES : DEPOIS, res = c.fonte === 'antes' ? antes : depois;
      celula(ctx, await imagem(path.join(dir, id + '.png')), x, y, escala, linhas(res[id]));
    }
  }
  const arq = path.join(SAIDA, `prancha-${tamanho}.png`);
  fs.writeFileSync(arq, cv.toBuffer('image/png'));
  return { arq, antes: verdes(antes), depois: verdes(depois), total: lista.length };
}

(async () => {
  const antes = ler(ANTES), depois = ler(DEPOIS);
  const proto = JSON.parse(fs.readFileSync(path.join(PROTO, 'metricas.json'), 'utf8'));
  fs.mkdirSync(SAIDA, { recursive: true });
  for (const t of Object.keys(TAMANHOS)) {
    const r = await prancha(t, antes, depois, proto);
    console.log(`${path.relative(process.cwd(), r.arq)}: ANTES ${r.antes}/${r.total}, DEPOIS ${r.depois}/${r.total}`);
  }
})().catch(e => { console.error(e); process.exit(1); });
