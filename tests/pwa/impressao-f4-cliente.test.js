'use strict';
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// #2209 (impressão v2, F4 no cliente): chave da barra com o número curto do
// servidor (8 dígitos) e a unidade com 4 dígitos (decisão do Felipe, opção A),
// leitor pela chave e normalizador da regra da loja igual ao do servidor.
const PWA = process.env.PWA_HTML ? path.dirname(process.env.PWA_HTML) : path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa');
const FIXTURE = path.resolve(__dirname, '../../docs/contracts/impressao-loja.v1.fixture.json');

function isolado(arquivos) {
  const ctx = {};
  ctx.globalThis = ctx;
  vm.createContext(ctx);
  for (const a of arquivos) vm.runInContext(fs.readFileSync(path.join(PWA, a), 'utf8'), ctx);
  return ctx;
}

module.exports = function ({ test, runInSandbox, assert }) {
  const run = body => JSON.parse(runInSandbox(`(function(){ ${body} })()`));
  const lote = qty => `{id:'b-1730700000001',lote:'LOT-261104',createdAt:'2026-11-04T12:00:00-03:00',items:[{productId:'p1',name:'Talharim',qty:${qty}}]}`;

  test('#2209 chave da barra: número curto de 8 dígitos e unidade com 4 dá 12 dígitos, e o QR não muda', () => {
    const r = run(`
      const old=products;
      try {
        products=[{id:'p1',name:'Talharim',sku:'TALHARIM'}];
        const oito=etiquetasFromBatch(Object.assign({numeroCurto:'00000042'},${lote(2)}));
        const dez=etiquetasFromBatch(Object.assign({numeroCurto:'0100000000'},${lote(1)}));
        return JSON.stringify({chave:oito.map(e=>e.chaveCurta),dez:dez[0].chaveCurta,qr:oito[1].codigoBarras,idx:oito[1].idxStr});
      } finally { products=old; }
    `);
    assert.deepStrictEqual(r, {
      chave: ['000000420001', '000000420002'],
      dez: '01000000000001',
      qr: 'TALHARIM-LOT-261104-000001-002',
      idx: '002'
    });
  });

  test('#2209 lote com mais de 9.999 etiquetas: sem chave, e a impressão com barra é recusada com o motivo', () => {
    const r = run(`
      const old=products, modo=localStorage.getItem('cdb-prod-scan-mode');
      try {
        products=[{id:'p1',name:'Talharim',sku:'TALHARIM'}];
        const ets=etiquetasFromBatch(Object.assign({numeroCurto:'00000042'},${lote(10000)}));
        localStorage.setItem('cdb-prod-scan-mode','both');
        const comBarra=etiquetaProducaoTsplV2(ets[0],'',{name:'OIA-8381-B',address:'DC:0D:30:83:81:0B',linguagem:'tspl'},1);
        const recusa=etiquetasTspl.recusa;
        return JSON.stringify({semChave:ets.every(e=>e.chaveCurta===null),excedida:ets[0].barraExcedida===true,comBarra,aviso:avisoDaRecusa(recusa)});
      } finally { products=old; localStorage.setItem('cdb-prod-scan-mode',modo); }
    `);
    assert.strictEqual(r.semChave, true, 'nenhuma etiqueta leva chave truncada');
    assert.strictEqual(r.excedida, true);
    assert.strictEqual(r.comBarra, null, 'recusa, não imprime');
    assert.match(r.aviso, /^Escolha só o QR em Ajustes\./);
    assert.match(r.aviso, /9\.999/);
    assert.doesNotMatch(r.aviso, /papel maior/);
  });

  test('#2209 leitor: acha a unidade pela chave da barra, e o QR antigo continua achado', () => {
    const r = run(`
      const oldP=products, oldB=batches;
      try {
        products=[{id:'p1',name:'Talharim',sku:'TALHARIM'}];
        batches=[Object.assign({numeroCurto:'00000042'},${lote(3)})];
        const barra=findUnitByCode('000000420003');
        const qr=findUnitByCode('TALHARIM-LOT-261104-000001-003');
        const outra=findUnitByCode('000000430003');
        return JSON.stringify({barra:barra&&barra.idxStr,qr:qr&&qr.idxStr,outra});
      } finally { products=oldP; batches=oldB; }
    `);
    assert.deepStrictEqual(r, { barra: '003', qr: '003', outra: null });
  });

  test('#2209 regra da loja: o normalizador JS dá a saída da fixture, no mesmo texto canônico, e é idempotente', () => {
    const fixture = JSON.parse(fs.readFileSync(FIXTURE, 'utf8'));
    const I = isolado(['impressao-papeis.js', 'impressoras.js']).cdbImpressoras;
    assert.strictEqual(typeof I.normalizarRegra, 'function', 'normalizarRegra exposto');
    for (const c of fixture.normalizacao) {
      const uma = JSON.stringify(I.normalizarRegra(c.entrada));
      assert.strictEqual(uma, JSON.stringify(c.saida), c.nome);
      assert.strictEqual(JSON.stringify(I.normalizarRegra(JSON.parse(uma))), uma, `${c.nome}: idempotente`);
    }
  });
};
