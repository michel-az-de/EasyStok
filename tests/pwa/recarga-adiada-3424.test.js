// Testes: mudança do servidor espera a tela ficar livre para recarregar (#3424)
//
// Depois da #3325 só mudança real recarrega, mas a recarga saía na hora: fechava o modal aberto e apagava o
// pedido em construção e a contagem de produção, que só existem na memória. Agora ela espera a operadora
// fechar o que está fazendo e sai uma vez só. Enquanto espera, o dado do servidor já está na memória, e um
// salvamento da operadora não pode devolvê-lo ao servidor como se fosse edição deste aparelho.
'use strict';

module.exports = function ({ test, sandbox, assert, runInSandbox }) {
  // O app de verdade, guardado antes dos testes: outros arquivos trocam ou apagam window.cdbApp.
  const appDoBoot = sandbox.window.cdbApp;
  const pausa = ms => new Promise(r => setTimeout(r, ms));
  const ESPERA = 800; // a tela é conferida a cada 500 ms
  function resp(body) { return { ok: true, status: 200, headers: { get() { return null; } }, json: async () => body }; }
  function estado(extra) { return Object.assign({ products: [], clients: [], orders: [], cashEntries: [], batches: [], cashClosings: [] }, extra); }
  function pedido(id, extra) {
    return Object.assign({
      id, clientId: null, clientSnapshot: { name: 'Ana', phone: null }, items: [{ productId: 'p1', name: 'Talharim', qty: 2, unitPrice: 10 }],
      notes: null, total: 20, status: 'aguardando', createdAt: 1000, updatedAt: 1000
    }, extra || {});
  }
  const mutacao = (type, payload) => ({ id: 'm-' + payload.id, deviceId: 'web', type, ts: 1, payload });

  // App falso com a tela ocupada sob controle do teste. O pull devolve o lote que o teste mandar.
  function montar(state) {
    const S = sandbox.window.cdbSync;
    const original = sandbox.window.cdbApp;
    const reloadOriginal = sandbox.location.reload;
    const tela = { ocupada: false, reloads: 0, emEdicao: {} };
    sandbox.location.reload = () => { tela.reloads++; };
    sandbox.window.cdbApp = Object.assign({}, original, {
      getState: () => state, setPendingSync() {}, markAllSynced() {}, showToast() {}, getOperator: () => 'Thati',
      telaOcupada: () => tela.ocupada, registrosEmEdicao: () => tela.emEdicao
    });
    sandbox.AbortController = AbortController;
    sandbox.URLSearchParams = URLSearchParams;
    let pulls = 0, lote = [];
    sandbox.fetch = async (url) => {
      if (String(url).indexOf('/sync/pull') >= 0) { pulls++; return resp({ serverTime: 1000 + pulls, mutations: lote }); }
      return resp({ acceptedIds: [] });
    };
    // Base do diff = o estado de agora; nada do que existe antes do pull conta como edição.
    sandbox.window.cdbOnPersist(state);
    S.clearQueue();
    sandbox.localStorage.setItem('cdb-pull-cursor', '500');
    return {
      S, tela,
      async puxar(mutations) { lote = mutations; await S.pull(); },
      async desmontar() {
        // A recarga pendente sai aqui, com este app falso, e não vaza para o teste seguinte.
        tela.ocupada = false;
        await pausa(ESPERA);
        sandbox.fetch = () => Promise.reject(new Error('fetch nao implementado em testes'));
        delete sandbox.AbortController;
        delete sandbox.URLSearchParams;
        sandbox.location.reload = reloadOriginal;
        if (original) sandbox.window.cdbApp = original; else delete sandbox.window.cdbApp;
        S.clearQueue();
      }
    };
  }

  test('#3424 venda nova com modal aberto não recarrega; fechou o modal, recarrega uma vez', async () => {
    await pausa(200); // reload agendado por teste anterior não entra na conta
    const state = estado();
    const t = montar(state);
    try {
      t.tela.ocupada = true;
      await t.puxar([mutacao('order.upsert', pedido('o-3424-modal'))]);
      await pausa(ESPERA * 2);
      assert.strictEqual(t.tela.reloads, 0, 'com o modal aberto a tela não recarrega');
      assert.strictEqual(state.orders.length, 1, 'a venda já está na memória enquanto espera');
      t.tela.ocupada = false;
      await pausa(ESPERA);
      assert.strictEqual(t.tela.reloads, 1, 'modal fechado: uma recarga');
      await pausa(ESPERA);
      assert.strictEqual(t.tela.reloads, 1, 'e só uma');
    } finally { await t.desmontar(); }
  });

  test('#3424 duas mudanças enquanto a tela está ocupada viram uma recarga só', async () => {
    const state = estado();
    const t = montar(state);
    try {
      t.tela.ocupada = true;
      await t.puxar([mutacao('order.upsert', pedido('o-3424-a'))]);
      await t.puxar([mutacao('order.upsert', pedido('o-3424-b'))]);
      await pausa(ESPERA);
      assert.strictEqual(t.tela.reloads, 0);
      t.tela.ocupada = false;
      await pausa(ESPERA * 2);
      assert.strictEqual(t.tela.reloads, 1);
      assert.strictEqual(state.orders.length, 2);
    } finally { await t.desmontar(); }
  });

  test('#3424 salvar durante a espera não devolve ao servidor a venda que veio dele', async () => {
    const state = estado({ orders: [pedido('o-3424-antiga')] });
    const t = montar(state);
    try {
      t.tela.ocupada = true;
      await t.puxar([
        mutacao('order.upsert', pedido('o-3424-outro-canal', { status: 'pronto' })),
        mutacao('order.upsert', pedido('o-3424-antiga', { status: 'entregue', updatedAt: 2000 }))
      ]);
      // A operadora termina o pedido dela e salva com a recarga ainda pendente.
      state.orders.push(pedido('o-3424-da-operadora', { createdAt: 3000, updatedAt: 3000 }));
      sandbox.window.cdbOnPersist(state);
      const enviados = t.S.getBackupQueue().filter(m => m.type === 'order.upsert').map(m => m.payload.id);
      assert.deepStrictEqual(enviados, ['o-3424-da-operadora'], 'só o pedido da operadora vai ao servidor');
    } finally { await t.desmontar(); }
  });

  test('#3424 cliente atualizado pelo servidor continua sendo o mesmo objeto que o pedido em construção escolheu', async () => {
    const escolhido = { id: 'c-3424', name: 'Ana', apt: null, address: 'Rua A', phone: null, lastOrder: 1, orderCount: 1 };
    const state = estado({ clients: [escolhido] });
    const t = montar(state);
    try {
      t.tela.ocupada = true;
      await t.puxar([mutacao('client.upsert', Object.assign({}, escolhido, { phone: '11999990000' }))]);
      assert.strictEqual(state.clients[0], escolhido, 'a edição da operadora no cliente escolhido cai no registro da coleção');
      assert.strictEqual(escolhido.phone, '11999990000', 'e já com o dado do servidor');
      escolhido.lastOrder = 5000; // saveOrder grava no cliente escolhido
      sandbox.window.cdbOnPersist(state);
      const enviados = t.S.getBackupQueue().filter(m => m.type === 'client.upsert');
      assert.strictEqual(enviados.length, 1, 'a edição da operadora vai ao servidor');
      assert.strictEqual(enviados[0].payload.lastOrder, 5000);
    } finally { await t.desmontar(); }
  });

  test('#3424 servidor mudou campo do formulário de edição aberto: recarrega na hora, como antes; o resto espera', async () => {
    const cliente = (id, extra) => Object.assign({ id, name: 'Ana', apt: null, address: 'Rua A', phone: '1111', lastOrder: 1, orderCount: 1 }, extra);
    const produto = extra => Object.assign({ id: 'p-3424-edit', name: 'Talharim', emoji: null, category: 'massas', unit: 'un', price: 10, stock: 3, isCustom: false }, extra);
    const casos = [
      ['outro cliente mudou: o formulário aberto não corre risco', 0, 'client.upsert', [cliente('c-3424-edit'), cliente('c-3424-outro')], cliente('c-3424-outro', { phone: '2222' })],
      ['telefone do cliente aberto mudou: salvar devolveria o antigo', 1, 'client.upsert', [cliente('c-3424-edit')], cliente('c-3424-edit', { phone: '2222' })],
      ['só a última compra do cliente aberto mudou: o formulário não grava esse campo', 0, 'client.upsert', [cliente('c-3424-edit')], cliente('c-3424-edit', { lastOrder: 9, orderCount: 2 })],
      ['só o saldo do produto aberto mudou (venda em outro caixa)', 0, 'product.upsert', [produto({ count: 0, photo: null })], produto({ stock: 1 })],
      ['preço do produto aberto mudou', 1, 'product.upsert', [produto({ count: 0, photo: null })], produto({ price: 12 })],
      ['cliente aberto apagado no servidor', 1, 'client.delete', [cliente('c-3424-edit')], { id: 'c-3424-edit' }]
    ];
    for (const [motivo, esperado, tipo, locais, doServidor] of casos) {
      const [colecao] = tipo.split('.');
      const state = estado(colecao === 'client' ? { clients: locais } : { products: locais });
      const t = montar(state);
      try {
        t.tela.ocupada = true;
        t.tela.emEdicao = { client: 'c-3424-edit', product: 'p-3424-edit', cashEntry: null };
        await t.puxar([mutacao(tipo, doServidor)]);
        await pausa(ESPERA);
        assert.strictEqual(t.tela.reloads, esperado, motivo);
      } finally { await t.desmontar(); }
    }
  });

  test('#3424 estoque novo do servidor mantém a contagem de produção em andamento no produto', async () => {
    const local = { id: 'p-3424', name: 'Talharim', emoji: null, category: 'massas', unit: 'un', price: 10, stock: 3, isCustom: false, count: 4, photo: null };
    const state = estado({ products: [local] });
    const t = montar(state);
    try {
      t.tela.ocupada = true;
      const { count, photo, ...doServidor } = local;
      await t.puxar([mutacao('product.upsert', Object.assign({}, doServidor, { stock: 1 }))]);
      assert.strictEqual(state.products[0].stock, 1, 'o saldo do servidor entra');
      assert.strictEqual(state.products[0].count, 4, 'a contagem que a operadora fazia fica');
    } finally { await t.desmontar(); }
  });

  test('#3424 telaOcupada do app: livre sem nada; ocupada com modal, pedido em construção, contagem ou campo de texto focado', () => {
    const app = appDoBoot;
    assert.ok(app, 'app-nucleo.js expõe window.cdbApp');
    assert.strictEqual(typeof app.telaOcupada, 'function', 'o app diz ao sync se a tela está ocupada');
    assert.strictEqual(app.telaOcupada(), false, 'tela livre');
    const doc = sandbox.document, qs = doc.querySelector;
    runInSandbox('globalThis.__telaAntes3424 = { tela: currentScreen, produtos: products }');
    const naTela = (tela, codigo) => () => runInSandbox(`currentScreen = '${tela}'; ${codigo}`);
    const sair = codigo => () => runInSandbox(`currentScreen = __telaAntes3424.tela; products = __telaAntes3424.produtos; ${codigo}`);
    const modal = sel => () => { doc.querySelector = s => String(s).indexOf(sel) >= 0 ? { id: 'aberto' } : null; };
    const naAba = aba => () => { doc.querySelector = s => String(s).indexOf('.screen-tab.active') >= 0 ? { dataset: { tab: aba } } : null; };
    const fecharModal = () => { doc.querySelector = qs; };
    const focar = el => () => { doc.activeElement = el; };
    const desfocar = () => { delete doc.activeElement; };
    const rascunho = "currentOrderItems = [{ productId: 'p1', qty: 1 }]";
    const casos = [
      ['modal de overlay aberto', true, modal('[class*="overlay"].show'), fecharModal],
      ['editor de etiqueta e confirmação abertos (.open)', true, modal('[class*="overlay"].open'), fecharModal],
      ['modal criado na hora (.modal.show) aberto', true, modal('.modal.show'), fecharModal],
      ['diálogo sem "overlay" na classe aberto', true, modal('[role="dialog"].open'), fecharModal],
      ['relatório do primeiro pull aberto', true, modal('.ipr-overlay:not([hidden])'), fecharModal],
      ['item no pedido em construção', true, naTela('pedidos', rascunho), sair('currentOrderItems = []')],
      ['cliente escolhido no pedido em construção', true, naTela('pedidos', "selectedClient = { id: 'c1', name: 'Ana' }"), sair('selectedClient = null')],
      ['contagem de produção', true, naTela('producao', "products = [{ id: 'p1', count: 2 }]"), sair('')],
      ['campo de texto focado com texto', true, focar({ tagName: 'INPUT', type: 'text', value: 'Ana' }), desfocar],
      ['observação com texto focada', true, focar({ tagName: 'TEXTAREA', value: 'sem cebola' }), desfocar],
      ['rascunho segura também na aba Andamento: conferir não é abandonar', true, () => { naTela('pedidos', rascunho)(); naAba('andamento')(); }, () => { fecharModal(); sair('currentOrderItems = []')(); }],
      ['rascunho na aba Novo de Pedidos segura', true, () => { naTela('pedidos', rascunho)(); naAba('novo')(); }, () => { fecharModal(); sair('currentOrderItems = []')(); }],
      ['pedido em construção abandonado em outra tela não segura a recarga', false, naTela('home', rascunho), sair('currentOrderItems = []')],
      ['contagem abandonada em outra tela não segura a recarga', false, naTela('pedidos', "products = [{ id: 'p1', count: 2 }]"), sair('')],
      ['campo vazio focado (teclado já fechou) não segura', false, focar({ tagName: 'INPUT', type: 'search', value: '' }), desfocar],
      ['caixa de marcar focada não é edição', false, focar({ tagName: 'INPUT', type: 'checkbox', value: 'on' }), desfocar]
    ];
    casos.forEach(([nome, esperado, ligar, desligar]) => {
      ligar();
      try { assert.strictEqual(app.telaOcupada(), esperado, nome); } finally { desligar(); }
    });
    assert.strictEqual(app.telaOcupada(), false, 'livre de novo');
  });
};
