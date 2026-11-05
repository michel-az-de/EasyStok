'use strict';
// Configura o app como o operador configuraria e pede a etiqueta de produção
// pelo mesmo caminho do botão Imprimir (etiquetasTspl). Serve ao master e à PR.
const { createCanvas } = require('@napi-rs/canvas');
const { DADOS } = require('../casos');

const OASIS = { name: 'OIA-8381-B', address: 'DC:0D:30:83:81:0B' };
const MODO_LEGADO = { qr: 'qr', barras: 'barcode', ambos: 'both' };

// Registro de fillText: tamanho de fonte e texto de cada desenho.
const proto = Object.getPrototypeOf(createCanvas(1, 1).getContext('2d'));
let registro = null;
if (!proto.__simFillText) {
  const orig = proto.fillText;
  proto.fillText = function (t, x, y, mw) {
    if (registro) {
      const m = String(this.font).match(/(\d+(?:\.\d+)?)px/);
      registro.push({ texto: String(t), px: m ? parseFloat(m[1]) : null, w: this.canvas && this.canvas.width });
    }
    return mw === undefined ? orig.call(this, t, x, y) : orig.call(this, t, x, y, mw);
  };
  proto.__simFillText = true;
}

function configurar(env, caso) {
  const s = env.storage;
  for (const k of Object.keys(s)) delete s[k];
  s['cdb-empresa-name'] = DADOS.empresa;
  s['cdb-prod-scan-mode'] = MODO_LEGADO[caso.codigos];
  s['cdb-etq-orient'] = caso.orientacao;
  // Master: a impressora salva não tem mídia porque o select não chega (P5).
  const legado = Object.assign({ profile: 'tspl' }, OASIS);
  s['cdb-printer'] = JSON.stringify(legado);
  s['cdb-printers'] = JSON.stringify([legado]);
  // PR: o operador disse no passo 1 qual papel está na Oasis.
  const f = caso.fisica;
  s['cdb-impressoras.v2'] = JSON.stringify({
    versao: 2,
    impressoras: { [OASIS.address]: { address: OASIS.address, nome: OASIS.name, apelido: 'Oasis', linguagem: 'tspl', linguagemManual: false,
      midia: { preset: f.preset, tipo: f.tipo === 'continuo' ? 'continuo' : 'etiqueta', larguraMm: f.larguraMm, alturaMm: f.alturaMm || 0, gapMm: f.gapMm || 0 },
      densidade: 8, velocidade: 4, inverter: false, calibradaEm: f.tipo === 'continuo' ? null : '2026-11-03T20:00:00.000Z', offsetMm: caso.offsetMm || { x: 0, y: 0 } } },
    papeis: { etiqueta: OASIS.address, cupom: null }
  });
  s['cdb-regra-impressao.v1'] = JSON.stringify({ v: 1, documentos: { 'etiqueta-producao': { papel: 'etiqueta', template: null, orientacao: caso.orientacao, codigos: caso.lista, copias: 1 } } });
  env.rodar(`try { currentEtqOrient = ${JSON.stringify(caso.orientacao)}; } catch (e) {}`);
  env.rodar(`products = ${JSON.stringify([DADOS.produto])};`);
}

function batch() {
  return {
    id: DADOS.batchId, lote: DADOS.loteDoDia, createdAt: DADOS.criadoEm, numeroCurto: DADOS.numeroCurto,
    items: [{ productId: DADOS.produto.id, name: DADOS.produto.name, unit: '', qty: 1, weightG: DADOS.pesoG, expiresAt: DADOS.validade, validityDays: 3 }]
  };
}

// Devolve { bytes, fontes: [{texto, pt}], escala, layout, recusa }.
async function etiquetaProducao(env, caso) {
  configurar(env, caso);
  env.sb.__simBatch = batch();
  const backend = env.sb.cdbEtiquetaCanvas;
  // Guarda o resultado do backend novo (layout e recusa) sem mudar o que ele faz.
  if (backend && !backend.__sim) {
    const orig = backend.tspl;
    backend.tspl = function () { const r = orig.apply(this, arguments); env.__capturado = r; return r; };
    backend.__sim = true;
  }
  env.__capturado = null;
  registro = [];
  let bytes = null; let erro = null;
  try {
    bytes = await env.rodar(`(async () => {
      const ets = etiquetasFromBatch(window.__simBatch);
      const printer = typeof impressoraPara === 'function' ? impressoraPara('etiqueta-producao') : getSavedPrinter();
      return etiquetasTspl([ets[0]], 'production', '', printer, 1);
    })()`);
  } catch (e) { erro = e && e.message; }
  const log = registro; registro = null;
  const et = env.rodar('etiquetasFromBatch(window.__simBatch)[0]');
  const r = env.__capturado || {};
  return { bytes: bytes ? Uint8Array.from(bytes) : null, log, layout: r.layout || null, recusa: r.recusa || erro, et, novo: !!backend };
}

// A prévia que a tela de produção mostra, pelo mesmo refreshEtqPreview() da
// tela. Devolve o canvas anexado ao #etqBody ou null.
function previaProducao(env, caso) {
  configurar(env, caso);
  env.sb.__simBatch = batch();
  const corpo = env.elemento('etqBody');
  corpo.filhos = [];
  env.rodar(`currentEtiquetas = [etiquetasFromBatch(window.__simBatch)[0]];
    currentEtiquetasKind = 'production';
    refreshEtqPreview();`);
  const wrap = corpo.filhos[0];
  return (wrap && wrap.filhos && wrap.filhos[0]) || null;
}

module.exports = { etiquetaProducao, previaProducao, configurar, batch, OASIS };
