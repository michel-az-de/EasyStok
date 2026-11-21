#!/usr/bin/env node
'use strict';
// Banca de regressão: a térmica de 58 mm (ESC/POS, WTP05 e MTP-II) e o cupom
// do pedido têm de sair byte a byte iguais ao master. Roda o botão Imprimir de
// verdade (etqAction) com um Bluetooth falso que grava o MAC e os bytes.
// Uso: node regressao.js --antes <pwa do master> [--depois <pwa>]
process.env.TZ = 'America/Sao_Paulo';
const path = require('path');
const crypto = require('crypto');
const { carregarPwa } = require('./lib/ambiente');
const { batch } = require('./lib/app');
const { DADOS } = require('./casos');

function opt(nome, padrao) {
  const i = process.argv.indexOf('--' + nome);
  return i > 0 ? process.argv[i + 1] : padrao;
}
const RAIZ = path.resolve(__dirname, '..', '..');
const ANTES = opt('antes', null);
const DEPOIS = path.resolve(opt('depois', path.join(RAIZ, 'EasyStock.Api', 'wwwroot', 'pwa')));
if (!ANTES && !process.argv.includes('--um')) { console.error('Uso: node regressao.js --antes <pwa do master> [--depois <pwa>]'); process.exit(2); }

const FIXO = Date.UTC(2026, 10, 3, 23, 45);
const IMPRESSORAS = {
  mtp: { name: 'MTP-II', address: '66:22:B1:4C:58:01', profile: 'escpos' },
  wtp: { name: 'WTP05', address: '86:67:7A:05:58:02' }
};
const PEDIDO = {
  id: 'pedido-0000-ab12cd34', factAt: FIXO, createdAt: FIXO, total: 64.5, notes: 'Entregar na portaria',
  clientSnapshot: { name: 'Maria das Dores', ref: 'Apto 12' }, confirmedBy: 'Thatiane',
  items: [{ qty: 2, name: 'Talharim Fresco de Espinafre 300g', unitPrice: 18.5, note: 'sem sal' }, { qty: 1, name: 'Molho Sugo 250 ml', unitPrice: 27.5 }]
};

// Casos: impressora x documento x modo de leitura x orientação.
function casos() {
  const out = [];
  for (const imp of Object.keys(IMPRESSORAS)) {
    for (const modo of [null, 'qr', 'barcode', 'both', 'none']) for (const orient of ['vertical', 'horizontal']) {
      out.push({ id: `${imp}-producao-${modo || 'padrao'}-${orient}`, imp, doc: 'production', modo, orient, copias: 1 });
    }
    out.push({ id: `${imp}-producao-both-vertical-2copias`, imp, doc: 'production', modo: 'both', orient: 'vertical', copias: 2 });
    out.push({ id: `${imp}-pedido`, imp, doc: 'order', modo: null, orient: 'vertical', copias: 1 });
    out.push({ id: `${imp}-pedido-nota`, imp, doc: 'order', modo: null, orient: 'vertical', copias: 1, nota: 'Troco para 100' });
  }
  return out;
}

function preparar(env, c) {
  const s = env.storage;
  for (const k of Object.keys(s)) delete s[k];
  s['cdb-empresa-name'] = DADOS.empresa;
  if (c.modo) s['cdb-prod-scan-mode'] = c.modo;
  s['cdb-etq-orient'] = c.orient;
  // Só as chaves antigas: a PR tem de migrar sozinha, sem o operador mexer.
  const p = IMPRESSORAS[c.imp];
  s['cdb-printer'] = JSON.stringify(p);
  s['cdb-printers'] = JSON.stringify([p]);
  env.rodar(`(() => {
    const F = ${FIXO}; const D = Date;
    if (!D.__sim) { globalThis.Date = class extends D { constructor(...a) { if (a.length) super(...a); else super(F); } static now() { return F; } }; Date.__sim = true; }
  })()`);
  env.rodar(`products = ${JSON.stringify([DADOS.produto])};`);
  env.rodar(`try { currentEtqOrient = ${JSON.stringify(c.orient)}; } catch (e) {}`);
}

async function imprimir(env, c) {
  preparar(env, c);
  env.sb.__simBatch = batch();
  env.sb.__simPedido = PEDIDO;
  env.sb.__simEscritas = [];
  env.sb.bluetoothSerial = {
    isEnabled: (ok) => ok && ok(),
    disconnect: (ok) => setTimeout(() => ok && ok(), 0),
    connect: (addr, ok) => { env.sb.__simEscritas.push({ connect: addr }); setTimeout(() => ok && ok(), 0); },
    write: (dados, ok) => { env.sb.__simEscritas.push({ bytes: Array.from(dados instanceof Uint8Array ? dados : new Uint8Array(dados)) }); setTimeout(() => ok && ok(), 0); },
    isConnected: (ok, nok) => nok && nok(),
    list: (ok) => ok && ok([])
  };
  env.rodar(`ensureBluetoothPermission = cb => cb(true); try { releasePrinting(); } catch (e) {}`);
  env.rodar(`getEtqCopies = () => ${c.copias}; getEtqNota = () => ${JSON.stringify(c.nota || '')};`);
  env.rodar(c.doc === 'order'
    ? `currentEtiquetas = [{ order: window.__simPedido }]; currentEtiquetasKind = 'order';`
    : `currentEtiquetas = etiquetasFromBatch(window.__simBatch); currentEtiquetasKind = 'producao';`);
  await env.rodar(`etqAction('print')`);
  for (let i = 0; i < 50 && !env.sb.__simEscritas.some(e => e.bytes); i++) await new Promise(r => setTimeout(r, 5));
  const esc = env.sb.__simEscritas;
  const bytes = esc.filter(e => e.bytes).map(e => e.bytes).flat();
  return { mac: (esc.find(e => e.connect) || {}).connect || null, n: bytes.length, sha: crypto.createHash('sha256').update(Buffer.from(bytes)).digest('hex').slice(0, 16), bytes };
}

// Cada PWA roda num processo próprio: duas PWAs com canvas no mesmo processo
// derrubam o V8 (contagem de memória externa).
async function rodarUm(pwa) {
  const env = carregarPwa(path.resolve(pwa));
  const out = {};
  for (const c of casos()) { const r = await imprimir(env, c); out[c.id] = { mac: r.mac, n: r.n, sha: r.sha, bytes: Buffer.from(r.bytes).toString('base64') }; }
  return out;
}

function filho(pwa) {
  const { execFileSync } = require('child_process');
  const txt = execFileSync(process.execPath, [__filename, '--um', pwa], { maxBuffer: 256 * 1024 * 1024, encoding: 'utf8' });
  return JSON.parse(txt);
}

(async () => {
  const um = opt('um', null);
  // A PWA deixa timers vivos: o filho sai assim que entrega o JSON.
  if (um) { const txt = JSON.stringify(await rodarUm(um)); process.stdout.write(txt, () => process.exit(0)); return; }
  const A = filho(path.resolve(ANTES));
  const D = filho(DEPOIS);
  let falhas = 0;
  const linhas = [];
  for (const c of casos()) {
    const a = A[c.id], d = D[c.id];
    const ok = a.n > 0 && a.sha === d.sha && a.mac === d.mac;
    if (!ok) {
      falhas++;
      const ba = Buffer.from(a.bytes, 'base64'), bd = Buffer.from(d.bytes, 'base64');
      let pos = -1; for (let i = 0; i < Math.max(ba.length, bd.length); i++) if (ba[i] !== bd[i]) { pos = i; break; }
      linhas.push(`  x ${c.id}: master ${a.n} B ${a.sha} em ${a.mac} | PR ${d.n} B ${d.sha} em ${d.mac}${pos >= 0 ? ` (primeiro byte diferente: ${pos})` : ''}`);
    } else linhas.push(`  = ${c.id}: ${a.n} B ${a.sha} em ${a.mac}`);
  }
  console.log(`${casos().length - falhas}/${casos().length} impressões ESC/POS iguais ao master`);
  console.log(linhas.join('\n'));
  process.exit(falhas ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
