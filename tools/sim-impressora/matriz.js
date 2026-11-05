#!/usr/bin/env node
'use strict';
// Gate 4: roda os 36 casos contra uma PWA e reprova o que não passa.
// Uso: node matriz.js [--pwa <pasta>] [--saida <pasta>] [--golden <pasta>] [--atualizar-golden]
process.env.TZ = 'America/Sao_Paulo';
const fs = require('fs');
const path = require('path');
const { carregarPwa } = require('./lib/ambiente');
const tspl = require('./lib/tspl');
const { lerQrJs, lerQrZxing, lerBarras } = require('./lib/leitura');
const { caixaDeTinta, png, diferenca } = require('./lib/medidas');
const { etiquetaProducao } = require('./lib/app');
const { casos, DADOS } = require('./casos');

const PT_MM = 25.4 / 72;
const PISO = { nome: 10, dado: 7.5, validade: 9, qrModulo: 3, qrQuieta: 4, barraModulo: 2, barraQuieta: 10, barraAlturaMm: 8 };
const LIMITE_GOLDEN = 0.005;

function opt(nome, padrao) {
  const i = process.argv.indexOf('--' + nome);
  return i > 0 ? process.argv[i + 1] : padrao;
}
const RAIZ = path.resolve(__dirname, '..', '..');
const PWA = path.resolve(opt('pwa', path.join(RAIZ, 'EasyStock.Api', 'wwwroot', 'pwa')));
const SAIDA = path.resolve(opt('saida', path.join(__dirname, 'saida', 'depois')));
const GOLDEN = opt('golden', null);
const ATUALIZAR = process.argv.includes('--atualizar-golden');

// #2247 (v3): o papel sai centralizado na cabeça de 104 mm (guias da Oasis).
function noPapel(et, fisica) {
  return tspl.papelNaCabeca(et, fisica.larguraMm, 'centro');
}

function fontes(log, r, et) {
  // Escala do desenho até a cabeça: 1 no backend novo (desenho no tamanho
  // final); no master, BITMAP / 384 px do desenho de 58 mm.
  const bmp = (r.etq.elementos || []).find(e => e.tipo === 'BITMAP');
  const escala = r.novo ? 1 : (bmp ? bmp.larguraPontos / 384 : 1);
  const venc = new Date(et.expiresAt).toLocaleDateString('pt-BR');
  const nomeTokens = String(et.productName).split(/\s+/);
  const itens = log.filter(l => l.px && l.texto.trim()).map(l => ({ texto: l.texto, pt: Math.round(l.px * escala / 8 / PT_MM * 10) / 10 }));
  const nome = itens.filter(i => i.texto.split(/\s+/).every(t => nomeTokens.includes(t)) && i.texto !== et.empresa);
  const val = itens.filter(i => i.texto.includes(venc));
  return {
    escala: Math.round(escala * 1000) / 1000,
    menorPt: itens.length ? Math.min(...itens.map(i => i.pt)) : null,
    nomePt: nome.length ? Math.min(...nome.map(i => i.pt)) : null,
    validadePt: val.length ? Math.min(...val.map(i => i.pt)) : null
  };
}

async function rodarCaso(env, caso) {
  const g = await etiquetaProducao(env, caso);
  const r = { id: caso.id, caso, novo: g.novo, recusa: g.recusa || null, asserts: [] };
  const ok = (id, passou, detalhe, problema) => r.asserts.push({ id, ok: !!passou, detalhe, problema });
  if (!g.bytes) { ok('gerou', false, 'o app não gerou bytes: ' + (g.recusa || 'sem motivo'), 'P3'); return r; }
  const lido = tspl.interpretar(g.bytes, { cabecaMm: 104, origem: 'cabeca' });
  r.comandos = lido.comandos.filter(c => !/^BITMAP/.test(c)).slice(0, 12);
  r.bitmap = lido.comandos.find(c => /^BITMAP/.test(c)) || null;
  const etq = lido.etiquetas[0];
  r.etq = etq;
  if (!etq) { ok('gerou', false, 'TSPL sem PRINT', 'P3'); return r; }
  const fis = caso.fisica;
  const p = noPapel(etq, fis);
  const consumo = tspl.papel(etq, fis);
  r.papel = { sizeMm: `${etq.larguraMm} x ${etq.alturaMm}`, gapMm: etq.gapMm, avancoMm: Math.round(consumo.avancoMm * 10) / 10, alinhado: consumo.alinhado, motivo: consumo.motivo };
  // PNG do papel com o avanço desperdiçado em cinza (até 300 mm).
  const totalPx = Math.round(Math.min(300, consumo.avancoMm) * 8);
  r.png = png(p.tinta, p.w, Math.min(p.h, totalPx), { alturaTotal: totalPx, corAvanco: 215 });

  // 1. Códigos.
  const caixa = caixaDeTinta(p.tinta, p.w, p.h);
  if (caso.lista.includes('qr')) {
    const a = lerQrJs(p.tinta, p.w, p.h); const b = lerQrZxing(p.tinta, p.w, p.h);
    r.qr = { jsqr: a && a.texto, zxing: b && b.texto, moduloPontos: a && a.moduloPontos };
    if (a && a.caixa) r.qrCaixa = Object.assign({ mod: a.moduloExato }, a.caixa);
    ok('qr', a && a.texto === DADOS.qr && b && b.texto === DADOS.qr, `jsQR=${a ? a.texto : 'não leu'} ZXing=${b ? b.texto : 'não leu'}`, 'P3');
    if (a) ok('piso-qr', a.moduloPontos >= PISO.qrModulo - 0.2, `módulo ${a.moduloPontos} pontos (piso ${PISO.qrModulo})`, 'P1');
    // Zona quieta de 4 módulos entre o símbolo e cada borda do papel (a leitura
    // estima os cantos, daí a folga de meio módulo).
    if (a && a.caixa) {
      const q = a.caixa, folga = Math.min(q.y0, q.x0, p.w - 1 - q.x1, p.h - 1 - q.y1) / a.moduloExato;
      ok('quieta-qr', folga >= PISO.qrQuieta - 0.5, `zona quieta do QR até a borda: ${folga.toFixed(1)} módulos (mínimo ${PISO.qrQuieta})`, 'P3');
    }
  }
  if (caso.lista.includes('barras')) {
    const b = lerBarras(p.tinta, p.w, p.h);
    r.barras = b;
    ok('barras', b && b.texto === DADOS.barras, b ? `ZXing=${b.texto}` : 'Code128 não lido (a barra não saiu)', 'P4');
    if (b && b.moduloPontos) {
      ok('piso-barras', b.moduloPontos >= PISO.barraModulo - 0.2 && b.quietaModulos >= PISO.barraQuieta && b.alturaMm >= PISO.barraAlturaMm - 0.2,
        `módulo ${b.moduloPontos} pt, zona quieta ${b.quietaModulos} módulos, altura ${b.alturaMm} mm`, 'P4');
    }
  }
  // 2. Fontes.
  const f = fontes(g.log, r, g.et);
  r.fontes = f;
  ok('piso-fonte', f.menorPt >= PISO.dado && f.nomePt >= PISO.nome && f.validadePt >= PISO.validade,
    `nome ${f.nomePt} pt, validade ${f.validadePt} pt, menor ${f.menorPt} pt (escala ${f.escala})`, 'P1');
  // 2b. Conteúdo, em qualquer formato: validade, fabricação, lote e o nome
  // inteiro; com barras pedidas, o número embaixo delas (P3).
  const textos = g.log.filter(l => l.texto && l.texto.trim()).map(l => l.texto).join(' ');
  const exigidos = [
    ['validade', new Date(g.et.expiresAt).toLocaleDateString('pt-BR')],
    ['fabricação', new Date(DADOS.criadoEm).toLocaleDateString('pt-BR')],
    // O código inteiro do lote (lote do dia e o sufixo do batch), não só o prefixo.
    ['lote', `${DADOS.loteDoDia}-${DADOS.batchId.slice(-6)}`]
  ].concat(String(g.et.productName).split(/\s+/).map(t => ['nome', t]));
  if (caso.lista.includes('barras')) exigidos.push(['número das barras', DADOS.barras]);
  const faltam = exigidos.filter(([, v]) => !textos.includes(v)).map(([k, v]) => `${k} "${v}"`);
  ok('conteudo', !faltam.length, faltam.length ? 'falta ' + faltam.join(', ') : 'validade, fabricação, lote e nome presentes', 'P3');
  // 3. Ocupação da área útil (margem 1,5 mm abaixo de 40 mm, senão 2 mm).
  const larguraMm = Math.min(fis.larguraMm, etq.larguraMm), alturaMm = fis.tipo === 'continuo' ? etq.alturaMm : fis.alturaMm;
  const m = Math.min(larguraMm, alturaMm) < 40 ? 1.5 : 2;
  const occW = caixa ? (caixa.w / 8) / (larguraMm - 2 * m) : 0;
  const occH = caixa ? (caixa.h / 8) / (alturaMm - 2 * m) : 0;
  r.ocupacao = { largura: Math.round(occW * 100), altura: Math.round(occH * 100) };
  const occOk = fis.tipo === 'continuo' ? occW >= 0.85 : occW >= 0.85 && occH >= 0.85;
  ok('ocupacao', occOk, `largura ${r.ocupacao.largura}%, altura ${r.ocupacao.altura}%${fis.tipo === 'continuo' ? ' (contínuo: vale a largura)' : ''}`, 'P1');
  // 4. Papel. O conteúdo inclui a zona quieta de 4 módulos do QR e a de 10
  // módulos da barra: elas fazem parte do código e não podem ser comidas
  // pela etiqueta seguinte.
  let topo = caixa ? caixa.y0 : 0, base = caixa ? caixa.y1 : -1;
  if (r.qr && r.qrCaixa) {
    const q = r.qrCaixa;
    topo = Math.max(0, Math.min(topo, q.y0 - 4 * q.mod));
    base = Math.min(p.h - 1, Math.max(base, q.y1 + 4 * q.mod));
  }
  // Barra lida girada: os módulos correm no sentido do avanço do papel.
  if (r.barras && r.barras.girada && r.barras.modulo) {
    const b = r.barras, q = PISO.barraQuieta * b.modulo;
    topo = Math.max(0, Math.min(topo, p.h - 1 - b.fim - q));
    base = Math.min(p.h - 1, Math.max(base, p.h - 1 - b.ini + q));
  }
  const conteudoMm = caixa ? (base - topo + 1) / 8 : 0;
  r.papel.conteudoMm = Math.round(conteudoMm * 10) / 10;
  if (fis.tipo === 'continuo') {
    ok('papel', consumo.avancoMm <= conteudoMm + 4 + 0.01, `avança ${r.papel.avancoMm} mm para ${r.papel.conteudoMm} mm de conteúdo (teto +4 mm)${consumo.motivo.length ? '; ' + consumo.motivo.join('; ') : ''}`, 'P2');
  } else {
    ok('papel', consumo.alinhado && Math.abs(consumo.avancoMm - (fis.alturaMm + fis.gapMm)) < 0.5, `avança ${r.papel.avancoMm} mm, passo ${fis.alturaMm + fis.gapMm} mm${consumo.motivo.length ? '; ' + consumo.motivo.join('; ') : ''}`, 'P2');
  }
  // 5. Nada cortado.
  ok('corte', p.perdidos === 0 && etq.cortados === 0 && etq.dentroDaCabeca,
    `${p.perdidos + etq.cortados} pontos fora do papel; SIZE ${etq.larguraMm} mm no papel de ${fis.larguraMm} mm`, 'P5');
  return r;
}

(async () => {
  const env = carregarPwa(PWA);
  fs.mkdirSync(SAIDA, { recursive: true });
  const resultados = [];
  for (const caso of casos()) {
    const r = await rodarCaso(env, caso);
    if (r.png) fs.writeFileSync(path.join(SAIDA, caso.id + '.png'), r.png);
    resultados.push(r);
  }
  // 6. Layout por tamanho: vertical e horizontal não podem sair iguais (P3).
  for (const r of resultados) {
    if (r.caso.orientacao !== 'horizontal' || !r.png) continue;
    const par = resultados.find(o => o.caso.orientacao === 'vertical' && o.caso.tamanho === r.caso.tamanho && o.caso.codigos === r.caso.codigos && o.caso.midia === r.caso.midia);
    const igual = par && par.png && Buffer.compare(par.png, r.png) === 0;
    r.asserts.push({ id: 'orientacao', ok: !igual, detalhe: igual ? 'sai igual à vertical: a orientação é ignorada' : 'layout próprio da horizontal', problema: 'P3' });
  }
  // 7. Golden.
  if (GOLDEN) {
    fs.mkdirSync(GOLDEN, { recursive: true });
    for (const r of resultados) {
      if (!r.png) continue;
      const g = path.join(GOLDEN, r.id + '.png');
      if (ATUALIZAR || !fs.existsSync(g)) { fs.writeFileSync(g, r.png); r.asserts.push({ id: 'golden', ok: true, detalhe: ATUALIZAR ? 'golden atualizado' : 'golden criado' }); continue; }
      const d = await diferenca(path.join(SAIDA, r.id + '.png'), g);
      r.asserts.push({ id: 'golden', ok: d.fracao <= LIMITE_GOLDEN, detalhe: `${(d.fracao * 100).toFixed(2)}% de pixels diferentes${d.motivo ? ' (' + d.motivo + ')' : ''}` });
    }
  }
  const resumo = resultados.map(r => ({
    id: r.id, ok: r.asserts.every(a => a.ok), papel: r.papel, ocupacao: r.ocupacao, fontes: r.fontes, qr: r.qr, barras: r.barras && { texto: r.barras.texto, moduloPontos: r.barras.moduloPontos },
    recusa: r.recusa, comandos: r.comandos, bitmap: r.bitmap, falhas: r.asserts.filter(a => !a.ok), asserts: r.asserts
  }));
  fs.writeFileSync(path.join(SAIDA, 'resultado.json'), JSON.stringify({ pwa: PWA, geradoEm: new Date().toISOString(), casos: resumo }, null, 2));
  const verdes = resumo.filter(r => r.ok).length;
  const porProblema = {};
  for (const r of resumo) for (const f of r.falhas) if (f.problema) (porProblema[f.problema] = porProblema[f.problema] || new Set()).add(r.id);
  console.log(`${verdes}/${resumo.length} casos verdes (${PWA})`);
  for (const [p, s] of Object.entries(porProblema).sort()) console.log(`  ${p}: ${s.size} casos reprovam`);
  for (const r of resumo.filter(x => !x.ok)) console.log(`  x ${r.id}: ${r.falhas.map(f => f.id + ' (' + f.detalhe + ')').join('; ')}`);
  if (env.erros.length) console.log('Erros de carga da PWA: ' + env.erros.join(' | '));
  process.exit(verdes === resumo.length ? 0 : 1);
})().catch(e => { console.error(e); process.exit(2); });
