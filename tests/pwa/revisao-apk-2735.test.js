// Testes — #2735: revisão do APK Casa da Baba Piloto, parte da PWA embarcada.
//
// O que o operador via no aparelho:
// - "2 items" no cartão do pedido e "(2 itemns sem custo)" no caixa;
// - total do resumo do pedido sem centavos, ao lado do total da folha com centavos;
// - total gravado com ruído de ponto flutuante (3 x 19,90 = 59,699999999999996);
// - "faltam NaN" no seletor de itens quando o produto vinha sem estoque;
// - busca de item que não achava "Pão" digitando "pao";
// - "vence hoje" para lote que vence amanhã cedo, e "vence em 2d" para amanhã à noite;
// - cliente "recorrente" por dois pedidos na mesma noite (dia contado em UTC);
// - voltar do Android saindo da tela com a folha de pagamento, histórico ou lote aberta;
// - aviso de pareamento apontando "Ajustes > Diagnostico", que não existe no piloto;
// - "Hoje" em Finalizados mostrando o que foi entregue ontem à tarde.
'use strict';
const { lerPwaMontada } = require(require('path').resolve(__dirname, '../../casa-da-baba-mobile/apk/scripts/pwa-montagem.cjs'));

const fs = require('fs');
const path = require('path');

module.exports = function ({ test, runInSandbox, assert }) {
  const HTML = lerPwaMontada(path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa/index.html'));
  const run = body => JSON.parse(runInSandbox('(function(){ ' + body + ' })()'));
  const trecho = (inicio, tamanho) => {
    const i = HTML.indexOf(inicio);
    assert.ok(i >= 0, 'trecho não encontrado: ' + inicio);
    return HTML.slice(i, i + tamanho);
  };

  test('#2735 contagem de itens: "1 item" e "N itens", nunca "items" nem "itemns"', () => {
    const r = run(`return JSON.stringify([contaItens(0), contaItens(1), contaItens(2), contaItens(21), contaItens('3'), contaItens(undefined)]);`);
    assert.deepStrictEqual(r, ['0 itens', '1 item', '2 itens', '21 itens', '3 itens', '0 itens']);
    assert.doesNotMatch(HTML, /\bitem\$\{/, 'ainda há plural de "item" montado à mão');
    // Histórico do pedido, mescla de lotes e lista de compras são lidos pelo operador.
    assert.match(HTML, /Pedido para \$\{snapshot\.name\} · \$\{contaItens\(order\.items\.length\)\}/);
    assert.match(trecho('function mergeBatchesFlow() {', 420), /contaItens\(\(b\.items \|\| \[\]\)\.length\)/);
    assert.doesNotMatch(HTML, /item\(ns\) removido\(s\)/);
  });

  test('#2735 novo pedido: total do resumo mostra centavos como o total da folha', () => {
    const r = run(`
      const old = { client: selectedClient, items: currentOrderItems, get: document.getElementById };
      const els = {}; document.getElementById = id => els[id] || (els[id] = old.get.call(document, id));
      try {
        selectedClient = { id: 'c-2735', name: 'Cliente' };
        currentOrderItems = [{ name: 'Nhoque', qty: 2, unitPrice: 12 }];
        renderOrderDraftSummary();
        const inteiro = els.odsTotal.textContent;
        currentOrderItems = [{ name: 'Nhoque', qty: 3, unitPrice: 19.9 }];
        renderOrderDraftSummary();
        return JSON.stringify({ inteiro, quebrado: els.odsTotal.textContent });
      } finally { selectedClient = old.client; currentOrderItems = old.items; document.getElementById = old.get; }
    `);
    assert.deepStrictEqual(r, { inteiro: 'R$ 24,00', quebrado: 'R$ 59,70' });
    assert.match(HTML, /id="odsTotal">R\$ 0,00</);
    assert.match(HTML, /id="orderTotal">R\$ 0,00</);
  });

  test('#2735 novo pedido: total gravado é arredondado ao centavo', () => {
    const r = run(`return JSON.stringify([
      orderItemsTotal([{ qty: 3, unitPrice: 19.9 }]),
      orderItemsTotal([{ qty: 1, unitPrice: 0.1 }, { qty: 1, unitPrice: 0.2 }]),
      orderItemsTotal([{ qty: '2', unitPrice: '12.5' }]),
      orderItemsTotal([{ qty: 1 }, { unitPrice: 5 }]),
      orderItemsTotal([]),
      orderItemsTotal(null)
    ]);`);
    assert.deepStrictEqual(r, [59.7, 0.3, 25, 0, 0, 0]);
    // A folha, o resumo e o pedido gravado usam a mesma conta.
    assert.match(trecho('function saveOrder() {', 700), /const total = orderItemsTotal\(currentOrderItems\);/);
    assert.match(trecho('function renderOrderItems() {', 700), /const total = orderItemsTotal\(currentOrderItems\);/);
    assert.match(trecho('function renderOrderDraftSummary() {', 700), /const total = orderItemsTotal\(currentOrderItems\);/);
  });

  test('#2735 seletor de itens: busca ignora acento e estoque ausente não vira "faltam NaN"', () => {
    const r = run(`
      const old = { products, get: document.getElementById };
      const filhos = [];
      const lista = {
        set innerHTML(v) { filhos.length = 0; if (v) filhos.push({ innerHTML: v }); },
        get innerHTML() { return ''; },
        appendChild(el) { filhos.push(el); }
      };
      const busca = { value: '' };
      try {
        document.getElementById = id => id === 'itemPickerList' ? lista : id === 'itemSearch' ? busca : old.get.call(document, id);
        products = [
          { id: 'p1', name: 'Nhoque de Mandioquinha', unit: '500 g', price: 30, stock: 4 },
          { id: 'p2', name: 'Pão de Queijo', unit: 'un', price: 5 },
          { id: 'p3', name: 'Molho Sugo', unit: 'pote', price: 18, stock: -2 },
          { id: 'p4', name: 'Capeletti', unit: 'kg', price: 60, stock: 0 },
          { id: 'p5', name: 'Ravioli Avulso', price: 28, stock: 3 }
        ];
        const linhas = q => { busca.value = q; searchItem(); return filhos.map(f => f.innerHTML); };
        return JSON.stringify({ tudo: linhas(''), pao: linhas('pao'), maiuscula: linhas('MANDIOQUINHA'), exato: linhas('pao de queijo'), novo: linhas('lasanha') });
      } finally { products = old.products; document.getElementById = old.get; }
    `);
    const linha = (linhas, nome) => linhas.find(l => l.includes(nome)) || '';
    assert.doesNotMatch(r.tudo.join('|'), /NaN/);
    assert.match(linha(r.tudo, 'Pão de Queijo'), /em falta/);
    assert.match(linha(r.tudo, 'Capeletti'), /em falta/);
    assert.match(linha(r.tudo, 'Molho Sugo'), /faltam 2/);
    assert.match(linha(r.tudo, 'Nhoque de Mandioquinha'), /4 disp\./);
    // Produto sem unidade não começa a linha com o separador solto.
    assert.match(linha(r.tudo, 'Ravioli Avulso'), /<div class="sr">R\$ 28 · /);
    assert.match(linha(r.tudo, 'Nhoque de Mandioquinha'), /<div class="sr">500 g · R\$ 30 · /);
    assert.match(r.pao.join('|'), /Pão de Queijo/, '"pao" precisa achar "Pão de Queijo"');
    assert.match(r.maiuscula.join('|'), /Nhoque de Mandioquinha/);
    // Nome igual a menos de acento já existe: não oferece cadastrar de novo.
    assert.doesNotMatch(r.exato.join('|'), /Cadastrar/);
    assert.match(r.novo.join('|'), /Cadastrar "lasanha"/);
  });

  test('#2735 validade do lote: o rótulo segue o dia do calendário, não janelas de 24 h', () => {
    const r = run(`
      const d = (dia, h, m) => new Date(2026, 10, dia, h, m || 0).getTime();
      const agora = d(15, 14);
      const t = fim => { const v = validadeRotulo(fim, agora); return v ? v.texto + (v.urgente ? '!' : '') : null; };
      return JSON.stringify([t(d(15, 13)), t(d(15, 23, 30)), t(d(16, 10)), t(d(16, 20)), t(d(17, 9)), t(d(21, 23)), t(d(22, 10)), t(d(22, 15)), t(0), t(undefined)]);
    `);
    assert.deepStrictEqual(r, ['vencido!', 'vence hoje!', 'vence amanhã!', 'vence amanhã', 'vence em 2 dias', 'vence em 6 dias', 'vence em 7 dias', null, null, null]);
  });

  test('#2735 validade do lote: o cartão usa o rótulo do calendário e o piloto não leva emoji', () => {
    const r = run(`
      const old = window.CDB_CONFIG;
      const hoje = new Date();
      const amanha = new Date(hoje.getFullYear(), hoje.getMonth(), hoje.getDate() + 1, 0, 1).getTime();
      const lote = { id: 'b-2735', createdAt: Date.now(), items: [{ productId: 'p1', name: 'Nhoque', qty: 2, expiresAt: amanha }] };
      try {
        window.CDB_CONFIG = undefined; const web = batchCardHtml(lote);
        window.CDB_CONFIG = { isPilot: true }; const piloto = batchCardHtml(lote);
        return JSON.stringify({ web, piloto });
      } finally { window.CDB_CONFIG = old; }
    `);
    assert.match(r.web, /vence amanhã · Nhoque/);
    assert.match(r.piloto, />vence amanhã · Nhoque</);
    assert.doesNotMatch(r.piloto, /font-size:1[01]px/);
  });

  test('#2735 clientes: dois pedidos no mesmo dia local não contam como dias diferentes', () => {
    const r = run(`
      const old = { orders, clients };
      try {
        const hoje = new Date();
        const ontem = (h, m) => new Date(hoje.getFullYear(), hoje.getMonth(), hoje.getDate() - 1, h, m).getTime();
        clients = [{ id: 'c-2735', name: 'Dona Maria' }];
        orders = [
          { id: 'o1', clientId: 'c-2735', status: STATUS.ENTREGUE, total: 50, createdAt: ontem(0, 30), items: [{ name: 'Nhoque', qty: 1, unitPrice: 50 }] },
          { id: 'o2', clientId: 'c-2735', status: STATUS.ENTREGUE, total: 60, createdAt: ontem(23, 30), items: [{ name: 'Nhoque', qty: 1, unitPrice: 60 }] }
        ];
        const row = buildClientInsights().find(x => x.client.id === 'c-2735');
        return JSON.stringify({ pedidos: row.orders, dias: row.distinctDayCount, categoria: row.category });
      } finally { orders = old.orders; clients = old.clients; }
    `);
    assert.deepStrictEqual(r, { pedidos: 2, dias: 1, categoria: 'novo' });
    // Em fuso UTC a conta acima passa por acaso; o dia tem de vir do fuso local.
    assert.doesNotMatch(trecho('function buildClientInsights', 3500), /toISOString\(\)\.slice\(0, 10\)/);
  });

  test('#2735 botões "×" têm nome acessível', () => {
    const semRotulo = HTML.split(/\r?\n/)
      .filter(l => /<button[^>]*>(&times;|×)<\/button>/.test(l) && !/aria-label=/.test(l))
      .map(l => l.trim());
    assert.deepStrictEqual(semRotulo, []);
  });

  test('#2735 voltar do Android fecha pagamento, descarte, cesta, conferência, histórico e detalhes do lote', () => {
    const r = run(`
      const ids = ['pagamentoEntregaModal', 'batchDiscardModal', 'cestaCalcModal', 'conferModal', 'historicoModal', 'batchDetailsModal'];
      const old = { get: document.getElementById, printing: isPrinting, pag: pendingPagamentoEntregaId, desc: _discardingBatchId, conf: pendingConferOrderId };
      try {
        isPrinting = false;
        const saida = ids.map(id => {
          const classes = new Set(['show']);
          const el = { id, style: {}, getClientRects: () => [1], classList: {
            contains: c => classes.has(c), add: c => { classes.add(c); }, remove: c => { classes.delete(c); },
            toggle: (c, f) => { const on = f === undefined ? !classes.has(c) : !!f; if (on) classes.add(c); else classes.delete(c); return on; }
          } };
          document.getElementById = x => x === id ? el : old.get.call(document, x);
          pendingPagamentoEntregaId = 'o-pag'; _discardingBatchId = 'b-desc'; pendingConferOrderId = 'o-conf';
          const interceptou = window.cdbHandleBack({}) === true;
          return { id, interceptou, aberto: classes.has('show'), pag: pendingPagamentoEntregaId, desc: _discardingBatchId, conf: pendingConferOrderId };
        });
        return JSON.stringify(saida);
      } finally {
        document.getElementById = old.get; isPrinting = old.printing;
        pendingPagamentoEntregaId = old.pag; _discardingBatchId = old.desc; pendingConferOrderId = old.conf;
      }
    `);
    for (const linha of r) {
      assert.strictEqual(linha.interceptou, true, linha.id + ': o voltar precisa ser tratado pelo app');
      assert.strictEqual(linha.aberto, false, linha.id + ': continuou aberto');
    }
    // Cada folha fecha pelo seu próprio fechador, que limpa o que estava pendente.
    const por = id => r.find(l => l.id === id);
    assert.strictEqual(por('pagamentoEntregaModal').pag, null);
    assert.strictEqual(por('batchDiscardModal').desc, null);
    assert.strictEqual(por('conferModal').conf, null);
    // Descarte abre por cima dos detalhes do lote: precisa fechar primeiro.
    const lista = trecho('var MODAL_IDS = [', 900);
    assert.ok(lista.indexOf("'batchDiscardModal'") < lista.indexOf("'batchDetailsModal'"));
  });

  test('#2735 pareamento invalidado: o aviso aponta um caminho que existe em cada app', () => {
    const r = run(`
      const old = window.CDB_CONFIG;
      try {
        window.CDB_CONFIG = { isPilot: true }; const piloto = pairingInvalidMessage();
        window.CDB_CONFIG = undefined; const web = pairingInvalidMessage();
        return JSON.stringify({ piloto, web });
      } finally { window.CDB_CONFIG = old; }
    `);
    assert.match(r.piloto, /Início/);
    assert.doesNotMatch(r.piloto, /Ajustes|Diagn/);
    assert.match(r.web, /Ajustes > Diagnóstico/);
    for (const texto of [r.piloto, r.web]) assert.doesNotMatch(texto, /—|re-parear/);
    assert.match(trecho('onPairingInvalid: function () {', 200), /showToast\(pairingInvalidMessage\(\)\)/);
  });

  test('#2735 piloto não registra service worker: o pacote do APK não leva sw.js', () => {
    assert.match(HTML, /if \('serviceWorker' in navigator && window\.CDB_CONFIG\?\.isPilot !== true\) \{/);
    const copia = fs.readFileSync(path.resolve(__dirname, '../../casa-da-baba-mobile/apk/scripts/copy-web.cjs'), 'utf8');
    assert.match(copia, /sw\.js/, 'copy-web.cjs deixou de remover o sw.js: reveja a guarda do registro');
  });

  test('#2735 piloto não busca as receitas da calculadora na abertura: a cesta fica escondida no APK', () => {
    const r = run(`
      const old = window.CDB_CONFIG, carga = cestaCalcCarregarCacheReceitas, observa = cestaCalcInstalarObservadorDraft;
      let cargas = 0, observadores = 0;
      try {
        cestaCalcCarregarCacheReceitas = () => { cargas++; }; cestaCalcInstalarObservadorDraft = () => { observadores++; };
        window.CDB_CONFIG = { isPilot: true }; cestaCalcIniciar(); const piloto = cargas;
        window.CDB_CONFIG = undefined; cestaCalcIniciar(); const web = cargas - piloto;
        return JSON.stringify({ piloto, web, observadores });
      } finally { window.CDB_CONFIG = old; cestaCalcCarregarCacheReceitas = carga; cestaCalcInstalarObservadorDraft = observa; }
    `);
    assert.deepStrictEqual(r, { piloto: 0, web: 1, observadores: 2 });
    assert.match(HTML, /window\.addEventListener\('load', cestaCalcIniciar\);/);
    const ui = fs.readFileSync(path.resolve(__dirname, '../../casa-da-baba-mobile/apk/pilot-ui.js'), 'utf8');
    assert.match(ui, /\[onclick\*="openCestaCalc"\]/, 'o piloto voltou a mostrar a calculadora de cesta: devolva a carga das receitas na abertura');
  });

  test('#2735 a página declara o ícone: sem ele o WebView pede /favicon.ico e recebe 404 em toda abertura', () => {
    assert.match(HTML, /<link rel="icon" type="image\/png" href="icons\/favicon\.png">/);
    assert.ok(fs.existsSync(path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa/icons/favicon.png')));
  });

  test('#2735 finalizados: "Hoje" começa à meia-noite local, não 24 h atrás', () => {
    const r = run(`
      const agora = new Date(2026, 10, 15, 10, 0).getTime();
      const meiaNoite = new Date(2026, 10, 15, 0, 0).getTime();
      return JSON.stringify({ hoje: finCutoffMs('hoje', agora) === meiaNoite, d7: agora - finCutoffMs('7d', agora), d30: agora - finCutoffMs('30d', agora), tudo: finCutoffMs('all', agora) });
    `);
    assert.deepStrictEqual(r, { hoje: true, d7: 7 * 864e5, d30: 30 * 864e5, tudo: 0 });
    assert.match(trecho('function renderFinalizadosScreen() {', 400), /finCutoffMs\(finPeriod, /);
  });

  test('#2735 textos à vista do operador com acento', () => {
    assert.match(HTML, /<span class="screen-subtitle">Recorrência, ticket e retorno<\/span>/);
    assert.doesNotMatch(HTML, /para destravar producao/);
  });
};
