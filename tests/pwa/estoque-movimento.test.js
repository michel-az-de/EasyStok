// Testes — #1520 (ADR-0060) estoque contado uma vez so
//
// O aparelho mandava o saldo absoluto no cadastro do produto e o servidor ainda
// descontava a venda na troca de status do pedido (aparelho 9, servidor 8).
// Agora cada mudanca de p.stock vira uma mutation "stock.delta" (contada uma vez
// no servidor pelo MutationId, em qualquer ordem) e o cadastro leva
// stockByDelta=true, que diz ao servidor para nao gravar o saldo que vai junto.
'use strict';

module.exports = function ({ test, sandbox, assert }) {
  const QUEUE_KEY = 'cdb-sync-queue';

  function fila() {
    return JSON.parse(sandbox.localStorage.getItem(QUEUE_KEY) || '[]');
  }
  function limparFila() {
    sandbox.window.cdbSync.clearQueue();
  }
  function estado(products, extra) {
    return Object.assign({ products, clients: [], orders: [], cashEntries: [], batches: [] }, extra || {});
  }
  function produto(id, stock, extra) {
    return Object.assign({ id, name: 'Lasanha', category: 'massa', price: 30, stock }, extra || {});
  }
  // Fixa o snapshot do sync.js no estado dado e esvazia a fila.
  function partirDe(st) {
    sandbox.window.cdbOnPersist(st);
    limparFila();
  }
  function movimentos() {
    return fila().filter(m => m.type === 'stock.delta');
  }
  function cadastros() {
    return fila().filter(m => m.type === 'product.upsert');
  }

  function comApp(state, fn) {
    const original = sandbox.window.cdbApp;
    sandbox.window.cdbApp = Object.assign({}, original, {
      getState: () => state, setPendingSync() {}, markAllSynced() {}, showToast() {}
    });
    return Promise.resolve().then(fn).finally(() => {
      if (original) sandbox.window.cdbApp = original; else delete sandbox.window.cdbApp;
      limparFila();
    });
  }

  test('#1520: venda vira um movimento de -1 e o cadastro avisa que o saldo vai por movimento', () => {
    partirDe(estado([produto('p-mov-1', 10)]));

    sandbox.window.cdbOnPersist(estado([produto('p-mov-1', 9)]));

    const movs = movimentos();
    assert.strictEqual(movs.length, 1, 'um movimento por mudanca de saldo');
    assert.strictEqual(movs[0].payload.productId, 'p-mov-1');
    assert.strictEqual(movs[0].payload.qty, -1);
    assert.ok(/^sd_/.test(movs[0].payload.id), 'id proprio: a fila nao funde dois movimentos');
    const cad = cadastros().find(m => m.payload.id === 'p-mov-1');
    assert.ok(cad, 'o cadastro segue junto: garante que o produto existe no servidor');
    assert.strictEqual(cad.payload.stockByDelta, true);
    limparFila();
  });

  test('#1520: producao, acrescimo em lote e descarte chegam como movimento (qualquer caminho que mexe em p.stock)', () => {
    partirDe(estado([produto('p-mov-2', 10)]));

    sandbox.window.cdbOnPersist(estado([produto('p-mov-2', 15)]));   // confirmProduction
    sandbox.window.cdbOnPersist(estado([produto('p-mov-2', 17)]));   // appendProductionToBatch
    sandbox.window.cdbOnPersist(estado([produto('p-mov-2', 12)]));   // ajustarSaldoDoLote (descarte)

    assert.deepStrictEqual(movimentos().map(m => m.payload.qty), [5, 2, -5],
      'os tres movimentos ficam na fila, em ordem, sem um apagar o outro');
    limparFila();
  });

  test('#1520: editar so o cadastro nao gera movimento', () => {
    partirDe(estado([produto('p-mov-3', 10)]));

    sandbox.window.cdbOnPersist(estado([produto('p-mov-3', 10, { price: 35 })]));

    assert.strictEqual(movimentos().length, 0);
    assert.strictEqual(cadastros().length, 1);
    limparFila();
  });

  test('#1520: produto novo com saldo inicial manda o saldo como movimento', () => {
    partirDe(estado([produto('p-mov-4', 10)]));

    sandbox.window.cdbOnPersist(estado([produto('p-mov-4', 10), produto('p-mov-novo', 3)]));

    const movs = movimentos();
    assert.strictEqual(movs.length, 1);
    assert.strictEqual(movs[0].payload.productId, 'p-mov-novo');
    assert.strictEqual(movs[0].payload.qty, 3);
    limparFila();
  });

  test('#1520: saldo ausente conta como zero e nao gera movimento', () => {
    partirDe(estado([produto('p-mov-5', undefined)]));

    sandbox.window.cdbOnPersist(estado([produto('p-mov-5', 0)]));

    assert.strictEqual(movimentos().length, 0);
    limparFila();
  });

  test('#1520: o que chega do servidor no pull nao vira movimento deste aparelho', async () => {
    const state = estado([produto('p-mov-6', 10)]);
    partirDe(state);
    await comApp(state, async () => {
      const cdbSync = sandbox.window.cdbSync;
      sandbox.AbortController = AbortController;
      sandbox.URLSearchParams = URLSearchParams;
      sandbox.fetch = async (url) => {
        const corpo = String(url).indexOf('/sync/pull') >= 0
          ? { serverTime: 1, mutations: [
              { id: 'srv-1', deviceId: 'outro', type: 'product.upsert', ts: 1, payload: produto('p-mov-6', 14) },
              { id: 'srv-2', deviceId: 'outro', type: 'product.upsert', ts: 1, payload: produto('p-mov-do-servidor', 6) }
            ] }
          : { acceptedIds: [] };
        return { ok: true, status: 200, headers: { get() { return null; } }, json: async () => corpo };
      };
      try {
        await cdbSync.pull();
        assert.strictEqual(state.products.find(p => p.id === 'p-mov-6').stock, 14, 'pull aplicado no estado local');
        limparFila();

        // Um save qualquer antes do reload nao pode devolver ao servidor o saldo que veio dele.
        sandbox.window.cdbOnPersist(state);

        assert.strictEqual(movimentos().length, 0);
      } finally {
        sandbox.fetch = () => Promise.reject(new Error('fetch nao implementado em testes'));
        delete sandbox.AbortController;
        delete sandbox.URLSearchParams;
      }
    });
  });

  test('#1520: durante a atualizacao do PWA o movimento passa, e leva junto o cadastro do produto', () => {
    partirDe(estado([produto('p-mov-9', 10), produto('p-mov-9b', 4)]));
    const interno = sandbox.window.cdbSync._internal;
    interno.bloquearMutations(true);
    try {
      sandbox.window.cdbOnPersist(estado([produto('p-mov-9', 9), produto('p-mov-9b', 4, { price: 31 })]));

      assert.deepStrictEqual(movimentos().map(m => m.payload.qty), [-1], 'movimento nao pode se perder');
      assert.deepStrictEqual(cadastros().map(m => m.payload.id), ['p-mov-9'],
        'so o cadastro que acompanha movimento passa; edicao de cadastro continua esperando');
    } finally {
      interno.bloquearMutations(false);
      limparFila();
    }
  });

  test('#1520: "Sincronizar tudo" manda o saldo absoluto e descarta os movimentos que ele ja contem', async () => {
    const state = estado([produto('p-mov-7', 10)]);
    partirDe(state);
    state.products[0].stock = 8;
    sandbox.window.cdbOnPersist(state);
    assert.strictEqual(movimentos().length, 1, 'pre-condicao: um movimento pendente');

    await comApp(state, async () => {
      sandbox.window.cdbSync.pushAll();

      assert.strictEqual(movimentos().length, 0, 'o absoluto ja inclui o movimento pendente');
      const cad = cadastros().filter(m => m.payload.id === 'p-mov-7');
      assert.strictEqual(cad.length, 1);
      assert.strictEqual(cad[0].payload.stock, 8);
      assert.notStrictEqual(cad[0].payload.stockByDelta, true, 'sem a marca o servidor grava o saldo');
    });
  });

  test('#1520: cadastro por movimento nao tira da fila o saldo absoluto que ainda nao subiu', async () => {
    const state = estado([produto('p-mov-8', 10)]);
    partirDe(state);
    await comApp(state, async () => {
      sandbox.window.cdbSync.pushAll();                 // fila: absoluto 10
      state.products[0].stock = 9;
      sandbox.window.cdbOnPersist(state);               // venda depois do "Sincronizar tudo"

      const cad = cadastros().filter(m => m.payload.id === 'p-mov-8');
      assert.strictEqual(cad.length, 2, 'o absoluto fica; o cadastro novo entra atras');
      assert.strictEqual(cad[0].payload.stock, 10);
      assert.notStrictEqual(cad[0].payload.stockByDelta, true);
      assert.strictEqual(cad[1].payload.stockByDelta, true);
      assert.deepStrictEqual(movimentos().map(m => m.payload.qty), [-1]);
    });
  });
};
