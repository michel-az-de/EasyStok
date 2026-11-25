// Testes: o pull que reenvia registro igual ao local não recarrega a tela (#3325)
//
// O servidor devolve ao aparelho a folga de 30 s do cursor (SyncController.SobreposicaoDoCursor), então a
// mesma venda volta em pulls seguidos. Antes, todo upsert marcava "changed" e a página recarregava a cada
// pull, inclusive no arranque que vem depois do reload: a tela inicial piscava até a janela fechar.
'use strict';

module.exports = function ({ test, sandbox, assert }) {
  const pausa = ms => new Promise(r => setTimeout(r, ms));
  function resp(body) { return { ok: true, status: 200, headers: { get() { return null; } }, json: async () => body }; }
  function estado(extra) { return Object.assign({ products: [], clients: [], orders: [], cashEntries: [], batches: [], cashClosings: [] }, extra); }
  function pedido(id, extra) {
    return Object.assign({
      id, clientId: null, clientSnapshot: { name: 'Ana', phone: null }, items: [{ productId: 'p1', name: 'Talharim', qty: 2, unitPrice: 10 }],
      notes: null, total: 20, status: 'aguardando', createdAt: 1000, updatedAt: 1000
    }, extra || {});
  }
  // Produto como o servidor manda (ProductDto, sem count nem photo).
  function produtoDoServidor(id, extra) {
    return Object.assign({ id, name: 'Talharim', emoji: null, category: 'massas', unit: 'un', price: 10, stock: 3, isCustom: false }, extra || {});
  }
  // O app acrescenta count e photo ao abrir (app-nucleo.js, products.map).
  function produtoLocal(id, extra) {
    return Object.assign(produtoDoServidor(id), { count: 0, photo: null }, extra || {});
  }
  const mutacao = (type, payload) => ({ id: 'm-' + payload.id, deviceId: 'web', type, ts: 1, payload });

  // Roda n pulls seguidos devolvendo as mesmas mutações; devolve os reloads de cada pull e o cursor ao final.
  async function puxar(state, mutations, vezes) {
    await pausa(200); // reload agendado por teste anterior não entra na conta
    const S = sandbox.window.cdbSync;
    const original = sandbox.window.cdbApp;
    const reloads = [];
    const reloadOriginal = sandbox.location.reload;
    sandbox.location.reload = () => reloads.push(1);
    sandbox.window.cdbApp = Object.assign({}, original, {
      getState: () => state, setPendingSync() {}, markAllSynced() {}, showToast() {}, getOperator: () => 'Thati'
    });
    sandbox.AbortController = AbortController;
    sandbox.URLSearchParams = URLSearchParams;
    let pulls = 0;
    sandbox.fetch = async (url) => {
      if (String(url).indexOf('/sync/pull') >= 0) { pulls++; return resp({ serverTime: 1000 + pulls, mutations }); }
      return resp({ acceptedIds: [] });
    };
    try {
      S.clearQueue();
      sandbox.localStorage.setItem('cdb-pull-cursor', '500');
      const porPull = [];
      for (let i = 0; i < vezes; i++) {
        const antes = reloads.length;
        await S.pull();
        await pausa(250); // o reload sai num setTimeout depois do flush
        porPull.push(reloads.length - antes);
      }
      return { porPull, cursor: sandbox.localStorage.getItem('cdb-pull-cursor') };
    } finally {
      sandbox.fetch = () => Promise.reject(new Error('fetch nao implementado em testes'));
      delete sandbox.AbortController;
      delete sandbox.URLSearchParams;
      sandbox.location.reload = reloadOriginal;
      if (original) sandbox.window.cdbApp = original; else delete sandbox.window.cdbApp;
      S.clearQueue();
    }
  }

  test('#3325 venda nova do servidor recarrega uma vez; o reenvio dela nos pulls seguintes não recarrega', async () => {
    const state = estado();
    const { porPull } = await puxar(state, [mutacao('order.upsert', pedido('o-3325-nova'))], 3);
    assert.deepStrictEqual(porPull, [1, 0, 0], 'só o primeiro pull, que trouxe a venda, recarrega');
    assert.strictEqual(state.orders.length, 1, 'a venda entrou uma vez');
    assert.strictEqual(state.orders[0].id, 'o-3325-nova');
  });

  test('#3325 registro igual ao local, com as chaves em outra ordem, não é trocado, não recarrega e o cursor avança', async () => {
    const local = pedido('o-3325-igual');
    const state = estado({ orders: [local] });
    const embaralhado = {};
    Object.keys(local).reverse().forEach(k => { embaralhado[k] = local[k]; });
    const { porPull, cursor } = await puxar(state, [mutacao('order.upsert', embaralhado)], 2);
    assert.deepStrictEqual(porPull, [0, 0]);
    assert.strictEqual(state.orders[0], local, 'o objeto local fica como estava');
    assert.strictEqual(cursor, '1002', 'lote só com reenvio igual confirma o cursor; senão o mesmo lote volta para sempre');
  });

  test('#3325 campo só do aparelho e null do servidor contra ausente no local não contam como mudança', async () => {
    const local = Object.assign(pedido('o-3325-extra'), { campoSoLocal: 'x' });
    delete local.notes; // o servidor manda notes: null; o local nunca teve a chave
    const state = estado({ orders: [local] });
    const { porPull } = await puxar(state, [mutacao('order.upsert', pedido('o-3325-extra'))], 2);
    assert.deepStrictEqual(porPull, [0, 0]);
    assert.strictEqual(state.orders[0], local);
  });

  test('#3325 pedido que mudou no servidor substitui o local, recarrega uma vez e o reenvio não recarrega', async () => {
    const state = estado({ orders: [pedido('o-3325-muda')] });
    const { porPull } = await puxar(state, [mutacao('order.upsert', pedido('o-3325-muda', { status: 'pronto', updatedAt: 2000 }))], 2);
    assert.deepStrictEqual(porPull, [1, 0]);
    assert.strictEqual(state.orders[0].status, 'pronto');
  });

  test('#3325 campo que o servidor limpou (null) contra valor no local é mudança', async () => {
    const state = estado({ orders: [pedido('o-3325-limpa', { notes: 'sem cebola' })] });
    const { porPull } = await puxar(state, [mutacao('order.upsert', pedido('o-3325-limpa', { notes: null }))], 2);
    assert.deepStrictEqual(porPull, [1, 0]);
    assert.strictEqual(state.orders[0].notes, null);
  });

  test('#3325 produto igual ao do servidor, com count e a foto que só o aparelho tem, não recarrega e mantém a foto', async () => {
    const state = estado({ products: [produtoLocal('p-3325-igual', { photo: 'data:image/png;base64,AAAA', count: 2 })] });
    const { porPull } = await puxar(state, [mutacao('product.upsert', produtoDoServidor('p-3325-igual'))], 2);
    assert.deepStrictEqual(porPull, [0, 0]);
    assert.strictEqual(state.products[0].photo, 'data:image/png;base64,AAAA', 'a foto local fica');
    assert.strictEqual(state.products[0].count, 2, 'o contador de runtime fica');
  });

  test('#3325 produto só com o estoque mudado (venda de outro aparelho) recarrega uma vez e mantém a foto', async () => {
    const state = estado({ products: [produtoLocal('p-3325-saldo', { photo: 'data:image/png;base64,BBBB' })] });
    const { porPull } = await puxar(state, [mutacao('product.upsert', produtoDoServidor('p-3325-saldo', { stock: 1 }))], 2);
    assert.deepStrictEqual(porPull, [1, 0]);
    assert.strictEqual(state.products[0].stock, 1);
    assert.strictEqual(state.products[0].photo, 'data:image/png;base64,BBBB');
  });

  test('#3325 cliente com apt e sem address no servidor, já migrado no aparelho, não recarrega', async () => {
    // migrateClientsApt (app-nucleo.js) grava "Apto X" no local a cada abertura; o servidor manda address: null.
    const doServidor = { id: 'c-3325', name: 'Ana', apt: '12', address: null, phone: null, lastOrder: 1, orderCount: 1 };
    const state = estado({ clients: [Object.assign({}, doServidor, { address: 'Apto 12' })] });
    const { porPull } = await puxar(state, [mutacao('client.upsert', doServidor)], 2);
    assert.deepStrictEqual(porPull, [0, 0]);
    assert.strictEqual(state.clients[0].address, 'Apto 12');
  });

  test('#3325 pedido igual junto com order.aReceber novo no mesmo lote: um reload só e o saldo gravado', async () => {
    const local = pedido('o-3325-areceber', { aReceber: 20 });
    const state = estado({ orders: [local] });
    const { porPull } = await puxar(state, [
      mutacao('order.upsert', pedido('o-3325-areceber', { aReceber: 20 })),
      { id: 'm-ar', deviceId: 'server', type: 'order.aReceber', ts: 1, payload: { id: 'o-3325-areceber', aReceber: 0 } }
    ], 2);
    assert.strictEqual(porPull[0], 1, 'o saldo mudou: recarrega uma vez, não duas');
    assert.strictEqual(state.orders[0].aReceber, 0);
  });
};
