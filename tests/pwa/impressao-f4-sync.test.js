'use strict';
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// #2209 (impressão v2, F4 opção A): o aparelho recebe do servidor o número curto
// do lote (na resposta do push, para o lote que ele lançou, e no pull, para os
// outros) e a regra de impressão da loja do pareamento (GET api/mobile/impressao).
const PWA = process.env.PWA_HTML ? path.dirname(process.env.PWA_HTML) : path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa');

function isolado(arquivos) {
  const ctx = {};
  ctx.globalThis = ctx;
  vm.createContext(ctx);
  for (const a of arquivos) vm.runInContext(fs.readFileSync(path.join(PWA, a), 'utf8'), ctx);
  return ctx;
}

module.exports = function ({ test, sandbox, runInSandbox, assert }) {
  const REGRA = 'cdb-regra-impressao.v1';
  const pausa = ms => new Promise(r => setTimeout(r, ms));
  function resp(body, status = 200) {
    return { ok: status >= 200 && status < 300, status, headers: { get() { return null; } }, json: async () => body };
  }
  function estado(batches) {
    return { products: [], clients: [], orders: [], cashEntries: [], batches, cashClosings: [] };
  }
  function loteLocal(id, extra) {
    return Object.assign({
      id, code: 'LOT-1', lote: 'LOT-261104', createdAt: 1, status: 'open', lastOperatorName: 'Thati',
      items: [{ productId: 'p1', name: 'Talharim', qty: 2, photo: null }]
    }, extra || {});
  }
  function loteDoServidor(id, extra) {
    return Object.assign({
      id, code: 'LOT-1', lote: 'LOT-261104', createdAt: 1, batchPhoto: null,
      items: [{ productId: 'p1', name: 'Talharim', qty: 2, photo: null }],
      deleted: null, deletedAt: null, deletedBy: null,
      discarded: null, discardedAt: null, discardedBy: null, discardReason: null
    }, extra || {});
  }

  // Roda fn com o app, o fetch e o reload trocados; devolve quantos reloads houve.
  async function comServidor(state, servidor, fn) {
    await pausa(200);   // reload agendado por teste anterior não entra na conta
    const cdbSync = sandbox.window.cdbSync;
    const original = sandbox.window.cdbApp;
    const reloads = [];
    const reloadOriginal = sandbox.location.reload;
    sandbox.location.reload = () => reloads.push(1);
    sandbox.window.cdbApp = Object.assign({}, original, {
      getState: () => state, setPendingSync() {}, markAllSynced() {}, showToast() {}, getOperator: () => 'Thati'
    });
    sandbox.AbortController = AbortController;
    sandbox.URLSearchParams = URLSearchParams;
    sandbox.fetch = async (url, opts) => servidor(String(url), opts || {});
    try {
      cdbSync.clearQueue();
      await fn(cdbSync);
      await pausa(250);   // o reload sai num setTimeout depois do flush
    } finally {
      sandbox.fetch = () => Promise.reject(new Error('fetch nao implementado em testes'));
      delete sandbox.AbortController;
      delete sandbox.URLSearchParams;
      sandbox.location.reload = reloadOriginal;
      if (original) sandbox.window.cdbApp = original; else delete sandbox.window.cdbApp;
      cdbSync.clearQueue();
    }
    return reloads.length;
  }

  test('#2209 push: o número da resposta entra no lote que o aparelho lançou, sem recarregar e sem reenviar o lote', async () => {
    const lote = loteLocal('b-f4-push');
    const state = estado([]);
    const envios = [];
    const reloads = await comServidor(state, async (url, opts) => {
      if (url.indexOf('/sync/pull') >= 0) return resp({ serverTime: 1, mutations: [] });
      if (url.indexOf('/sync') >= 0 && opts.method === 'POST') {
        const corpo = JSON.parse(opts.body);
        envios.push(corpo.mutations.map(m => m.type + ':' + m.payload.id));
        return resp({ acceptedIds: corpo.mutations.map(m => m.id), numerosCurtos: { 'b-f4-push': '00000042', 'b-de-outro': '00000099' } });
      }
      return resp({}, 404);
    }, async (cdbSync) => {
      sandbox.window.cdbOnPersist(state);   // fixa o snapshot no estado vazio
      cdbSync.clearQueue();
      state.batches.push(lote);
      sandbox.window.cdbOnPersist(state);   // lote novo: enfileira e já tenta enviar
      await cdbSync.flush();
      await pausa(50);
      sandbox.window.cdbOnPersist(state);   // o app salva de novo depois da resposta
      assert.strictEqual(cdbSync.queueSize(), 0, 'o número do servidor não volta como mudança do aparelho');
    });

    assert.deepStrictEqual(envios, [['batch.upsert:b-f4-push']]);
    assert.strictEqual(state.batches[0].numeroCurto, '00000042');
    assert.strictEqual(state.batches.length, 1, 'número de lote que o aparelho não tem é ignorado');
    const gravado = JSON.parse(sandbox.localStorage.getItem('cdb-batches') || '[]').find(b => b.id === 'b-f4-push');
    assert.ok(gravado && gravado.numeroCurto === '00000042', 'gravado no aparelho');
    assert.strictEqual(reloads, 0, 'só o número mudou: a tela não recarrega');
  });

  test('#2209 pull: lote que o aparelho já tem ganha o número sem recarregar, e o pull sem número não o apaga', async () => {
    const state = estado([loteLocal('b-f4-pull'), loteLocal('b-f4-tem', { numeroCurto: '00000007' })]);
    const reloads = await comServidor(state, async (url) => {
      if (url.indexOf('/sync/pull') >= 0) return resp({ serverTime: 1, mutations: [
        { id: 's1', deviceId: 'outro', type: 'batch.upsert', ts: 1, payload: loteDoServidor('b-f4-pull', { numeroCurto: '00000043' }) },
        { id: 's2', deviceId: 'outro', type: 'batch.upsert', ts: 1, payload: loteDoServidor('b-f4-tem') }
      ] });
      return resp({}, 404);
    }, cdbSync => cdbSync.pull());

    assert.strictEqual(state.batches[0].numeroCurto, '00000043');
    assert.strictEqual(state.batches[1].numeroCurto, '00000007', 'servidor antigo, sem o campo: o número fica');
    assert.strictEqual(reloads, 0);
  });

  test('#2209 número fora do formato (8 ou 10 dígitos) é ignorado no pull, no lote que o aparelho tem e no novo', async () => {
    const state = estado([loteLocal('b-f4-ruim'), loteLocal('b-f4-ruim2')]);
    await comServidor(state, async (url) => {
      if (url.indexOf('/sync/pull') >= 0) return resp({ serverTime: 1, mutations: [
        { id: 's3', deviceId: 'outro', type: 'batch.upsert', ts: 1, payload: loteDoServidor('b-f4-ruim', { numeroCurto: '42' }) },
        { id: 's4', deviceId: 'outro', type: 'batch.upsert', ts: 1, payload: loteDoServidor('b-f4-ruim2', { numeroCurto: '0000004A' }) },
        { id: 's5', deviceId: 'outro', type: 'batch.upsert', ts: 1, payload: loteDoServidor('b-f4-novo-ruim', { numeroCurto: '12345' }) },
        { id: 's6', deviceId: 'outro', type: 'batch.upsert', ts: 1, payload: loteDoServidor('b-f4-novo', { numeroCurto: '0000000044' }) }
      ] });
      return resp({}, 404);
    }, cdbSync => cdbSync.pull());

    assert.strictEqual(state.batches[0].numeroCurto, undefined);
    assert.strictEqual(state.batches[1].numeroCurto, undefined);
    const gravados = JSON.parse(sandbox.localStorage.getItem('cdb-batches') || '[]');
    const novo = id => gravados.find(b => b.id === id);
    assert.ok(novo('b-f4-novo-ruim') && !('numeroCurto' in novo('b-f4-novo-ruim')), 'lote novo entra sem o número inválido');
    assert.strictEqual(novo('b-f4-novo') && novo('b-f4-novo').numeroCurto, '0000000044', 'lote novo entra com o número de 10 dígitos');
  });

  test('#2209 número fora do formato é ignorado na resposta do push', async () => {
    const lote = loteLocal('b-f4-push-ruim');
    const state = estado([]);
    await comServidor(state, async (url, opts) => {
      if (url.indexOf('/sync/pull') >= 0) return resp({ serverTime: 1, mutations: [] });
      if (url.indexOf('/sync') >= 0 && opts.method === 'POST') {
        const corpo = JSON.parse(opts.body);
        return resp({ acceptedIds: corpo.mutations.map(m => m.id), numerosCurtos: { 'b-f4-push-ruim': '4200' } });
      }
      return resp({}, 404);
    }, async (cdbSync) => {
      sandbox.window.cdbOnPersist(state);
      cdbSync.clearQueue();
      state.batches.push(lote);
      sandbox.window.cdbOnPersist(state);
      await cdbSync.flush();
      await pausa(50);
    });

    assert.strictEqual(state.batches[0].numeroCurto, undefined);
  });

  test('#2209 regra da loja: busca com a chave do aparelho, guarda normalizada e vale para a impressão', async () => {
    const pareamento = sandbox.localStorage.getItem('cdb-pairing');
    const lojaId = 'aaaaaaaa-0000-0000-0000-000000000001';
    sandbox.localStorage.setItem('cdb-pairing', JSON.stringify({ apiKey: 'tk-teste', empresaId: 'eeeeeeee-0000-0000-0000-000000000001', lojaId }));
    sandbox.localStorage.removeItem(REGRA);
    const pedidos = [];
    try {
      await comServidor(estado([]), async (url, opts) => {
        if (url.indexOf('/api/mobile/impressao') >= 0) {
          pedidos.push({ url, chave: opts.headers && opts.headers['X-Mobile-Api-Key'] });
          return resp({ data: { lojaId: lojaId.toUpperCase(), versao: 5, alteradoEm: '2026-11-04T15:00:00Z', impressao: {
            v: 1, documentos: { 'etiqueta-producao': { papel: 'etiqueta-52', template: null, orientacao: 'horizontal', codigos: ['barras', 'qr', 'x'], copias: 2 } }
          } } });
        }
        return resp({}, 404);
      }, async (cdbSync) => {
        assert.strictEqual(typeof cdbSync.atualizarRegraImpressao, 'function', 'cdbSync.atualizarRegraImpressao exposto');
        assert.strictEqual(await cdbSync.atualizarRegraImpressao({ forcar: true }), true);
      });

      assert.strictEqual(pedidos.length, 1);
      assert.ok(!/lojaId|empresaId/i.test(pedidos[0].url), 'o aparelho não escolhe loja: vem do pareamento');
      assert.strictEqual(pedidos[0].chave, 'tk-teste');
      const guardada = JSON.parse(sandbox.localStorage.getItem(REGRA));
      assert.strictEqual(guardada.versao, 5);
      assert.deepStrictEqual(guardada.documentos['etiqueta-producao'].codigos, ['qr', 'barras'], 'normalizada: ordem do catálogo, sem o que não é do contrato');
      const r = sandbox.window.cdbImpressoras.regra(sandbox.localStorage, 'etiqueta-producao');
      assert.deepStrictEqual(JSON.parse(JSON.stringify({ codigos: r.codigos, orientacao: r.orientacao, papel: r.papel })),
        { codigos: ['qr', 'barras'], orientacao: 'horizontal', papel: 'etiqueta' });
    } finally {
      if (pareamento === null) sandbox.localStorage.removeItem('cdb-pairing'); else sandbox.localStorage.setItem('cdb-pairing', pareamento);
      sandbox.localStorage.removeItem(REGRA);
    }
  });

  test('#2209 regra da loja: nula apaga o cache; de outra loja, 404 ou erro mantêm o cache', async () => {
    const pareamento = sandbox.localStorage.getItem('cdb-pairing');
    const lojaId = 'aaaaaaaa-0000-0000-0000-000000000002';
    const cache = JSON.stringify({ v: 1, documentos: { 'etiqueta-producao': { codigos: ['barras'] } }, lojaId, versao: 1 });
    sandbox.localStorage.setItem('cdb-pairing', JSON.stringify({ apiKey: 'tk-teste', empresaId: 'eeeeeeee-0000-0000-0000-000000000002', lojaId }));
    const respostas = [
      resp({ data: { lojaId: 'bbbbbbbb-0000-0000-0000-000000000009', versao: 9, alteradoEm: null, impressao: { v: 1, documentos: {} } } }),
      resp({ error: 'x' }, 404),
      null,
      resp({ data: { lojaId, versao: 0, alteradoEm: null, impressao: null } })
    ];
    const cacheDepois = [];
    try {
      sandbox.localStorage.setItem(REGRA, cache);
      await comServidor(estado([]), async (url) => {
        if (url.indexOf('/api/mobile/impressao') < 0) return resp({}, 404);
        const r = respostas.shift();
        if (!r) throw new Error('rede caiu');
        return r;
      }, async (cdbSync) => {
        for (let i = 0; i < 4; i++) {
          await cdbSync.atualizarRegraImpressao({ forcar: true });
          cacheDepois.push(sandbox.localStorage.getItem(REGRA));
        }
      });
      assert.deepStrictEqual(cacheDepois.slice(0, 3), [cache, cache, cache], 'outra loja, 404 e erro: fica a regra guardada');
      assert.strictEqual(cacheDepois[3], null, 'loja sem regra: o aparelho volta ao padrão dele');
    } finally {
      if (pareamento === null) sandbox.localStorage.removeItem('cdb-pairing'); else sandbox.localStorage.setItem('cdb-pairing', pareamento);
      sandbox.localStorage.removeItem(REGRA);
    }
  });

  test('#2209 regra da loja: busca que falhou volta a tentar em 1 min, sem esperar os 10', async () => {
    const pareamento = sandbox.localStorage.getItem('cdb-pairing');
    const lojaId = 'aaaaaaaa-0000-0000-0000-000000000003';
    sandbox.localStorage.setItem('cdb-pairing', JSON.stringify({ apiKey: 'tk-teste', empresaId: 'eeeeeeee-0000-0000-0000-000000000003', lojaId }));
    sandbox.localStorage.removeItem(REGRA);
    const agora = Date.now, t0 = agora.call(Date);
    let pedidos = 0;
    const resultados = [];
    try {
      await comServidor(estado([]), async (url) => {
        if (url.indexOf('/api/mobile/impressao') < 0) return resp({}, 404);
        if (++pedidos === 1) throw new Error('sem rede');
        return resp({ data: { lojaId, versao: 3, alteradoEm: null, impressao: { v: 1, documentos: {} } } });
      }, async (cdbSync) => {
        Date.now = () => t0;
        resultados.push(await cdbSync.atualizarRegraImpressao({ forcar: true }));
        Date.now = () => t0 + 30 * 1000;
        resultados.push(await cdbSync.atualizarRegraImpressao());
        Date.now = () => t0 + 61 * 1000;
        resultados.push(await cdbSync.atualizarRegraImpressao());
      });
      assert.deepStrictEqual(resultados, [false, false, true], 'falha, espera 1 min, tenta de novo');
      assert.strictEqual(pedidos, 2, 'em 30 s não busca de novo');
      assert.strictEqual(JSON.parse(sandbox.localStorage.getItem(REGRA)).versao, 3);
    } finally {
      Date.now = agora;
      if (pareamento === null) sandbox.localStorage.removeItem('cdb-pairing'); else sandbox.localStorage.setItem('cdb-pairing', pareamento);
      sandbox.localStorage.removeItem(REGRA);
    }
  });

  test('#2209 regra da loja: troca de loja durante a busca não grava a regra da loja antiga', async () => {
    const pareamento = sandbox.localStorage.getItem('cdb-pairing');
    const l1 = 'aaaaaaaa-0000-0000-0000-000000000004', l2 = 'aaaaaaaa-0000-0000-0000-000000000005';
    const parear = lojaId => sandbox.localStorage.setItem('cdb-pairing', JSON.stringify({ apiKey: 'tk-teste', empresaId: 'eeeeeeee-0000-0000-0000-000000000004', lojaId }));
    parear(l1);
    sandbox.localStorage.removeItem(REGRA);
    let soltarL1;
    const respostaL1 = new Promise(r => { soltarL1 = r; });
    let pedidos = 0;
    try {
      await comServidor(estado([]), async (url) => {
        if (url.indexOf('/api/mobile/impressao') < 0) return resp({}, 404);
        if (++pedidos === 1) { await respostaL1; return resp({ data: { lojaId: l1, versao: 1, alteradoEm: null, impressao: { v: 1, documentos: {} } } }); }
        return resp({ data: { lojaId: l2, versao: 2, alteradoEm: null, impressao: { v: 1, documentos: {} } } });
      }, async (cdbSync) => {
        const daL1 = cdbSync.atualizarRegraImpressao({ forcar: true });
        await pausa(10);
        parear(l2);                               // o switchLoja troca o pareamento e esquece a regra
        cdbSync.esquecerRegraImpressao();
        const daL2 = cdbSync.atualizarRegraImpressao({ forcar: true });
        soltarL1();
        await daL1;
        await daL2;
      });
      assert.strictEqual(pedidos, 2, 'a busca forçada da loja nova não reaproveita a da antiga');
      const guardada = JSON.parse(sandbox.localStorage.getItem(REGRA));
      assert.strictEqual(guardada && guardada.lojaId, l2);
    } finally {
      if (pareamento === null) sandbox.localStorage.removeItem('cdb-pairing'); else sandbox.localStorage.setItem('cdb-pairing', pareamento);
      sandbox.localStorage.removeItem(REGRA);
    }
  });

  test('#2209 regra: o papel do catálogo da loja vira a impressora do aparelho (etiqueta ou cupom)', () => {
    const I = isolado(['impressao-papeis.js', 'impressoras.js']).cdbImpressoras;
    const mem = obj => ({ getItem: k => (k in obj ? obj[k] : null) });
    const com = papel => I.regra(mem({ [REGRA]: JSON.stringify({ v: 1, documentos: {
      'etiqueta-producao': { papel }, 'pedido': { papel } } }) }), 'etiqueta-producao').papel;
    const doPedido = papel => I.regra(mem({ [REGRA]: JSON.stringify({ v: 1, documentos: { 'pedido': { papel } } }) }), 'pedido').papel;
    assert.strictEqual(com('etiqueta-52'), 'etiqueta');
    assert.strictEqual(com('etiqueta-10x15'), 'etiqueta');
    assert.strictEqual(com('cupom-58'), 'cupom');
    assert.strictEqual(com('a4'), 'etiqueta', 'folha não sai na térmica: fica o papel do documento');
    assert.strictEqual(doPedido('a4'), 'cupom');
    assert.strictEqual(doPedido('cupom-80'), 'cupom');
  });

  test('#2209 impressão: etiqueta montada antes da resposta do push espera o número e sai com a barra', async () => {
    runInSandbox(`
      window.__f4 = (async () => {
        const oldP=products, oldB=batches, modo=localStorage.getItem('cdb-prod-scan-mode'), C=window.cdbEtiquetaCanvas, orig=C.tspl, S=window.cdbSync, flushOrig=S.flush;
        const barras=[];
        try {
          products=[{id:'p1',name:'Talharim',sku:'TALHARIM'}];
          const lote={id:'b-f4-print',lote:'LOT-261104',createdAt:'2026-11-04T12:00:00-03:00',items:[{productId:'p1',name:'Talharim',qty:2}]};
          batches=[lote];
          localStorage.setItem('cdb-prod-scan-mode','both');
          const ets=etiquetasFromBatch(lote);
          const antes=ets.map(e=>e.chaveCurta);
          S.flush=()=>new Promise(r=>setTimeout(()=>{ lote.numeroCurto='00000042'; r('ok'); },30));
          C.tspl=(o)=>{ barras.push(o.campos.barras); return {bytes:[1]}; };
          await etiquetasTspl(ets,'production','',{name:'OIA-8381-B',address:'DC:0D:30:83:81:0B',linguagem:'tspl'},1);
          return JSON.stringify({antes,barras});
        } finally { C.tspl=orig; S.flush=flushOrig; products=oldP; batches=oldB;
          if (modo===null) localStorage.removeItem('cdb-prod-scan-mode'); else localStorage.setItem('cdb-prod-scan-mode',modo); }
      })();
      'ok'
    `);
    const r = JSON.parse(await sandbox.window.__f4);
    assert.deepStrictEqual(r, { antes: [null, null], barras: ['000000420001', '000000420002'] });
  });

  test('#2209 impressão: o envio em andamento era anterior ao lote; um segundo envio traz o número', async () => {
    runInSandbox(`
      window.__f4c = (async () => {
        const oldP=products, oldB=batches, modo=localStorage.getItem('cdb-prod-scan-mode'), C=window.cdbEtiquetaCanvas, orig=C.tspl, S=window.cdbSync, flushOrig=S.flush;
        const barras=[];
        let envios=0;
        try {
          products=[{id:'p1',name:'Talharim',sku:'TALHARIM'}];
          const lote={id:'b-f4-segundo',lote:'LOT-261104',createdAt:'2026-11-04T12:00:00-03:00',items:[{productId:'p1',name:'Talharim',qty:1}]};
          batches=[lote];
          localStorage.setItem('cdb-prod-scan-mode','both');
          S.flush=()=>new Promise(r=>setTimeout(()=>{ if (++envios===2) lote.numeroCurto='00000077'; r('ok'); },20));
          C.tspl=(o)=>{ barras.push(o.campos.barras); return {bytes:[1]}; };
          await etiquetasTspl(etiquetasFromBatch(lote),'production','',{name:'OIA-8381-B',address:'DC:0D:30:83:81:0B',linguagem:'tspl'},1);
          return JSON.stringify({barras,envios});
        } finally { C.tspl=orig; S.flush=flushOrig; products=oldP; batches=oldB;
          if (modo===null) localStorage.removeItem('cdb-prod-scan-mode'); else localStorage.setItem('cdb-prod-scan-mode',modo); }
      })();
      'ok'
    `);
    const r = JSON.parse(await sandbox.window.__f4c);
    assert.deepStrictEqual(r, { barras: ['000000770001'], envios: 2 });
  });

  test('#2209 impressão: sem resposta em 5 s a etiqueta sai sem barra, sem travar', async () => {
    runInSandbox(`
      window.__f4b = (async () => {
        const oldP=products, oldB=batches, modo=localStorage.getItem('cdb-prod-scan-mode'), C=window.cdbEtiquetaCanvas, orig=C.tspl, S=window.cdbSync, flushOrig=S.flush;
        const barras=[];
        try {
          products=[{id:'p1',name:'Talharim',sku:'TALHARIM'}];
          const lote={id:'b-f4-mudo',lote:'LOT-261104',createdAt:'2026-11-04T12:00:00-03:00',items:[{productId:'p1',name:'Talharim',qty:1}]};
          batches=[lote];
          localStorage.setItem('cdb-prod-scan-mode','both');
          S.flush=()=>new Promise(()=>{});
          C.tspl=(o)=>{ barras.push(o.campos.barras); return {bytes:[1]}; };
          const t0=Date.now();
          const out=await etiquetasTspl(etiquetasFromBatch(lote),'production','',{name:'OIA-8381-B',address:'DC:0D:30:83:81:0B',linguagem:'tspl'},1);
          return JSON.stringify({barras,imprimiu:!!out,segundos:Math.round((Date.now()-t0)/1000)});
        } finally { C.tspl=orig; S.flush=flushOrig; products=oldP; batches=oldB;
          if (modo===null) localStorage.removeItem('cdb-prod-scan-mode'); else localStorage.setItem('cdb-prod-scan-mode',modo); }
      })();
      'ok'
    `);
    const r = JSON.parse(await sandbox.window.__f4b);
    assert.deepStrictEqual(r.barras, ['']);
    assert.strictEqual(r.imprimiu, true);
    assert.ok(r.segundos <= 6, 'espera no máximo uns 5 s');
  });

  test('#2209 leitor: chave que bate em dois lotes (com e sem loja dividem o contador) não escolhe um', () => {
    const r = JSON.parse(runInSandbox(`(function(){
      const oldP=products, oldB=batches;
      try {
        products=[{id:'p1',name:'Talharim',sku:'TALHARIM'}];
        batches=[
          {numeroCurto:'00000042',id:'b-a',lote:'LOT-261104',createdAt:'2026-11-04T12:00:00-03:00',items:[{productId:'p1',name:'Talharim',qty:1}]},
          {numeroCurto:'00000042',id:'b-b',lote:'LOT-261103',createdAt:'2026-11-03T12:00:00-03:00',items:[{productId:'p1',name:'Talharim',qty:1}]}
        ];
        const dupla=findUnitByCode('000000420001');
        const qr=findUnitByCode(etiquetasFromBatch(batches[1])[0].codigoBarras);
        return JSON.stringify({dupla,qr:qr&&qr.batch.id});
      } finally { products=oldP; batches=oldB; }
    })()`));
    assert.deepStrictEqual(r, { dupla: null, qr: 'b-b' });
  });
};
