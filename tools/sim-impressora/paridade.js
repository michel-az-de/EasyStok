#!/usr/bin/env node
'use strict';
// Impressão v3, G1 (#2247): o que a tela mostra é o que sai no papel.
// Para os 36 casos da matriz:
//   previa-igual-papel  a prévia da tela de produção e a imagem dos bytes TSPL,
//                       no papel centralizado na cabeça de 104 mm, têm o mesmo
//                       tamanho e diferem em no máximo 0,5% dos pontos;
//   tinta-no-papel      nenhum ponto cai fora do papel;
//   ajuste-fino         com offsetMm {x: 2, y: 1} a tinta anda exatamente
//                       16 e 8 pontos;
//   moldura-centrada    o Teste de alinhamento põe a moldura centrada no papel
//                       (±0,5 mm), sem tinta fora;
//   alinhamento-com-ajuste  o mesmo teste anda exatamente o ajuste fino salvo.
// Uso: node paridade.js [--pwa <pasta>] [--saida <pasta>] [--caso <id>]
process.env.TZ = 'America/Sao_Paulo';
const fs = require('fs');
const path = require('path');
const { carregarPwa } = require('./lib/ambiente');
const tspl = require('./lib/tspl');
const { caixaDeTinta, png } = require('./lib/medidas');
const { etiquetaProducao, previaProducao, testeDeAlinhamento } = require('./lib/app');
const { casos } = require('./casos');

const CABECA_MM = 104;
const LIMITE = 0.005;
const AJUSTE_MM = { x: 2, y: 1 };

function opt(nome, padrao) {
  const i = process.argv.indexOf('--' + nome);
  return i > 0 ? process.argv[i + 1] : padrao;
}
const RAIZ = path.resolve(__dirname, '..', '..');
const PWA = path.resolve(opt('pwa', path.join(RAIZ, 'EasyStock.Api', 'wwwroot', 'pwa')));
const SAIDA = path.resolve(opt('saida', path.join(__dirname, 'saida', 'paridade')));
const SO = opt('caso', null);

function tintaDoCanvas(c) {
  const d = c.getContext('2d').getImageData(0, 0, c.width, c.height).data;
  const t = new Uint8Array(c.width * c.height);
  for (let i = 0; i < t.length; i++) t[i] = (d[i * 4] * 299 + d[i * 4 + 1] * 587 + d[i * 4 + 2] * 114) / 1000 < 128 ? 1 : 0;
  return { tinta: t, w: c.width, h: c.height };
}

async function papelImpresso(env, caso, offsetMm) {
  const g = await etiquetaProducao(env, Object.assign({}, caso, { offsetMm }));
  if (!g.bytes) return { recusa: g.recusa || 'sem bytes' };
  const lido = tspl.interpretar(g.bytes, { cabecaMm: CABECA_MM, origem: 'cabeca' });
  const et = lido.etiquetas[0];
  if (!et) return { recusa: 'TSPL sem PRINT' };
  return tspl.papelNaCabeca(et, caso.fisica.larguraMm, 'centro');
}

function fracaoDiferente(a, b) {
  let n = 0;
  for (let i = 0; i < a.tinta.length; i++) if (a.tinta[i] !== b.tinta[i]) n++;
  return n / a.tinta.length;
}

async function rodarCaso(env, caso) {
  const r = { id: caso.id, asserts: [] };
  const ok = (id, passou, detalhe) => r.asserts.push({ id, ok: !!passou, detalhe });
  const c = previaProducao(env, caso);
  const papel = await papelImpresso(env, caso, { x: 0, y: 0 });
  if (!c) { ok('previa-igual-papel', false, 'a tela não mostrou prévia em canvas'); return r; }
  if (papel.recusa) { ok('previa-igual-papel', false, 'o app não gerou bytes: ' + papel.recusa); return r; }
  const previa = tintaDoCanvas(c);
  fs.writeFileSync(path.join(SAIDA, caso.id + '-previa.png'), png(previa.tinta, previa.w, previa.h));
  fs.writeFileSync(path.join(SAIDA, caso.id + '-papel.png'), png(papel.tinta, papel.w, papel.h));

  if (previa.w !== papel.w || previa.h !== papel.h) {
    ok('previa-igual-papel', false, `tamanho: prévia ${previa.w}x${previa.h} pontos, papel ${papel.w}x${papel.h}`);
  } else {
    const f = fracaoDiferente(previa, papel);
    ok('previa-igual-papel', f <= LIMITE, `${(f * 100).toFixed(2)}% dos pontos diferentes`);
  }

  ok('tinta-no-papel', papel.perdidos === 0, papel.perdidos ? `${papel.perdidos} pontos fora do papel (BITMAP começa ${papel.x0} pontos antes do papel)` : 'toda a tinta no papel');

  const movido = await papelImpresso(env, caso, AJUSTE_MM);
  const a = caixaDeTinta(papel.tinta, papel.w, papel.h);
  const b = movido.recusa ? null : caixaDeTinta(movido.tinta, movido.w, movido.h);
  const esperado = { x: AJUSTE_MM.x * 8, y: AJUSTE_MM.y * 8 };
  if (!a || !b) ok('ajuste-fino', false, 'sem tinta para medir');
  else {
    const dx = b.x0 - a.x0, dy = b.y0 - a.y0;
    ok('ajuste-fino', dx === esperado.x && dy === esperado.y, `andou ${dx},${dy} pontos; esperado ${esperado.x},${esperado.y}`);
  }

  // Teste de alinhamento: a moldura fica centrada no papel (±0,5 mm) e o
  // ajuste fino salvo a desloca exatamente.
  const moldura = testeNoPapel(env, caso, { x: 0, y: 0 });
  if (moldura.recusa) ok('moldura-centrada', false, moldura.recusa);
  else {
    const m = caixaDeTinta(moldura.tinta, moldura.w, moldura.h);
    const desvio = m ? (m.x0 + m.x1 + 1) / 2 - moldura.w / 2 : null;
    ok('moldura-centrada', m && moldura.perdidos === 0 && Math.abs(desvio) <= 4,
      m ? `centro da moldura a ${(desvio / 8).toFixed(2)} mm do centro do papel; ${moldura.perdidos} pontos fora` : 'sem moldura');
    const mm = testeNoPapel(env, caso, AJUSTE_MM);
    const n = mm.recusa ? null : caixaDeTinta(mm.tinta, mm.w, mm.h);
    ok('alinhamento-com-ajuste', m && n && n.x0 - m.x0 === esperado.x && n.y0 - m.y0 === esperado.y,
      n ? `andou ${n.x0 - m.x0},${n.y0 - m.y0} pontos; esperado ${esperado.x},${esperado.y}` : 'sem moldura com ajuste');
  }
  return r;
}

function testeNoPapel(env, caso, offsetMm) {
  const bytes = testeDeAlinhamento(env, Object.assign({}, caso, { offsetMm }));
  if (!bytes) return { recusa: 'o teste de alinhamento não enviou bytes' };
  const et = tspl.interpretar(bytes, { cabecaMm: CABECA_MM, origem: 'cabeca' }).etiquetas[0];
  if (!et) return { recusa: 'teste de alinhamento sem PRINT' };
  return tspl.papelNaCabeca(et, caso.fisica.larguraMm, 'centro');
}

(async () => {
  fs.mkdirSync(SAIDA, { recursive: true });
  const env = carregarPwa(PWA);
  const lista = casos().filter(c => !SO || c.id === SO);
  const resumo = [];
  for (const caso of lista) resumo.push(await rodarCaso(env, caso));
  const porAssert = {};
  for (const r of resumo) {
    const falhas = r.asserts.filter(a => !a.ok);
    console.log(`${falhas.length ? 'REPROVA' : 'ok     '} ${r.id}`);
    for (const a of r.asserts) {
      porAssert[a.id] = porAssert[a.id] || { ok: 0, falha: 0 };
      porAssert[a.id][a.ok ? 'ok' : 'falha']++;
      if (!a.ok) console.log(`          ${a.id}: ${a.detalhe}`);
    }
  }
  const verdes = resumo.filter(r => r.asserts.every(a => a.ok)).length;
  console.log(`\n${verdes}/${resumo.length} casos verdes`);
  for (const [id, s] of Object.entries(porAssert)) console.log(`  ${id}: ${s.ok} ok, ${s.falha} reprovam`);
  fs.writeFileSync(path.join(SAIDA, 'resumo.json'), JSON.stringify(resumo, null, 2));
  process.exit(verdes === resumo.length ? 0 : 1);
})().catch(e => { console.error(e); process.exit(2); });
