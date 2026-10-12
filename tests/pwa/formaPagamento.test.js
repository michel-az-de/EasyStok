// Testes unitarios — forma de pagamento no PWA (#1493)
// Pedido entregue e lancamento de caixa passam a carregar `metodo`, que viaja
// inteiro na mutacao (sync.js manda o objeto como payload). Sem isso o servidor
// gravava tudo como "dinheiro" e inflava a gaveta do Caixa do dia no console.
'use strict';

module.exports = function ({ test, runInSandbox, assert }) {
  // getElementById do runner devolve um stub novo a cada chamada; aqui os
  // elementos ficam em cache para o valor digitado sobreviver entre chamadas.
  const comDomEstavel = (corpo) => runInSandbox(`(function(){
    var cache = {}; var original = document.getElementById;
    document.getElementById = function (id) { return cache[id] || (cache[id] = original(id)); };
    try { ${corpo} } finally { document.getElementById = original; }
  })()`);

  const pedidoPronto = (extra) => `
    products = [];
    cashClosings = [];
    orders = [Object.assign({ id: 'o1', status: STATUS.PRONTO, total: 80, items: [],
      createdAt: Date.now(), updatedAt: Date.now(), history: [] }, ${extra || '{}'})];
  `;

  test('forma: lista igual a do servidor', () => {
    const r = JSON.parse(runInSandbox(`JSON.stringify(FORMAS_PAGAMENTO.map(function (f) { return f.key; }))`));
    assert.deepStrictEqual(r, ['pix', 'dinheiro', 'credito', 'debito', 'transferencia', 'outro']);
  });

  test('forma: normalizarFormaPagamento aceita a lista e recusa o resto', () => {
    const r = JSON.parse(runInSandbox(`JSON.stringify([
      normalizarFormaPagamento('pix'), normalizarFormaPagamento(' Debito '),
      normalizarFormaPagamento(''), normalizarFormaPagamento(null), normalizarFormaPagamento('bitcoin')])`));
    assert.deepStrictEqual(r, ['pix', 'debito', null, null, null]);
  });

  test('forma: entregar pedido sem forma pergunta antes e nao entrega', () => {
    const r = JSON.parse(runInSandbox(`
      ${pedidoPronto()}
      applyAdvanceTransition('o1', STATUS.ENTREGUE);
      JSON.stringify({ status: orders[0].status, pendente: pendingPagamentoEntregaId, metodo: orders[0].metodo || null });
    `));
    assert.strictEqual(r.status, 'pronto', 'so entrega depois de escolher a forma');
    assert.strictEqual(r.pendente, 'o1');
    assert.strictEqual(r.metodo, null);
  });

  test('forma: escolher a forma entrega o pedido com metodo', () => {
    const r = JSON.parse(runInSandbox(`
      ${pedidoPronto()}
      applyAdvanceTransition('o1', STATUS.ENTREGUE);
      escolherFormaEntrega('pix');
      JSON.stringify({ status: orders[0].status, metodo: orders[0].metodo, pendente: pendingPagamentoEntregaId });
    `));
    assert.strictEqual(r.status, 'entregue');
    assert.strictEqual(r.metodo, 'pix');
    assert.strictEqual(r.pendente, null);
  });

  test('forma: cancelar a escolha deixa o pedido pronto', () => {
    const r = runInSandbox(`
      ${pedidoPronto()}
      applyAdvanceTransition('o1', STATUS.ENTREGUE);
      closePagamentoEntrega();
      orders[0].status + '|' + pendingPagamentoEntregaId;
    `);
    assert.strictEqual(r, 'pronto|null');
  });

  test('forma: pedido que ja tem forma entrega direto', () => {
    const r = runInSandbox(`
      ${pedidoPronto("{ metodo: 'credito' }")}
      applyAdvanceTransition('o1', STATUS.ENTREGUE);
      orders[0].status + '|' + orders[0].metodo;
    `);
    assert.strictEqual(r, 'entregue|credito');
  });

  test('forma: lancamento de caixa sem forma nao salva', () => {
    const r = comDomEstavel(`
      cashEntries = []; cashClosings = [];
      openCashModal();
      document.getElementById('cashDesc').value = 'farinha';
      document.getElementById('cashAmount').value = '45,00';
      saveCashEntry();
      return cashEntries.length;
    `);
    assert.strictEqual(r, 0);
  });

  test('forma: lancamento de caixa leva a forma escolhida', () => {
    const r = JSON.parse(comDomEstavel(`
      cashEntries = []; cashClosings = [];
      openCashModal();
      document.getElementById('cashDesc').value = 'farinha';
      document.getElementById('cashAmount').value = '45,00';
      selectCashMetodo('debito');
      saveCashEntry();
      return JSON.stringify(cashEntries);
    `));
    assert.strictEqual(r.length, 1);
    assert.strictEqual(r[0].metodo, 'debito');
    assert.strictEqual(r[0].amount, 45);
  });

  test('forma: editar lancamento preserva e troca a forma', () => {
    const r = comDomEstavel(`
      cashClosings = [];
      cashEntries = [{ id: 'c1', type: 'income', amount: 10, description: 'x', createdAt: Date.now(), metodo: 'pix' }];
      openCashModal('c1');
      var antes = cashMetodo;
      selectCashMetodo('dinheiro');
      saveCashEntry();
      return antes + '|' + cashEntries[0].metodo;
    `);
    assert.strictEqual(r, 'pix|dinheiro');
  });
};
