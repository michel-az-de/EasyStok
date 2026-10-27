'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const html = fs.readFileSync(path.resolve(__dirname, '../../../EasyStock.Api/wwwroot/pwa/index.html'), 'utf8');
function fn(name) {
  const start = html.indexOf('function ' + name + '('), end = html.indexOf('\nfunction ', start + 1);
  assert.ok(start >= 0 && end > start); return html.slice(start, end);
}
function order(id, status, createdAt = 1) {
  return { id, status, createdAt, clientSnapshot: { name: id }, total: 10, items: [{ name: 'Talharim', qty: 1 }] };
}
function fixture(orders = [], pilot = true) {
  const list = { innerHTML: '' }, title = { textContent: '' }, header = { dataset: {}, querySelector: () => title };
  const fab = { classList: { remove() { fab.visible = false; }, add() { fab.visible = true; } } };
  const fields = { kanban: list, fab, fabIcon: {}, fabLabel: {} };
  const sections = ['novo', 'andamento'].map(tab => ({ dataset: { tab }, style: { display: tab === 'novo' ? 'none' : '' } }));
  let active = true, focus = '', deferred = false, confer = 0;
  const ctx = { window: { CDB_CONFIG: { isPilot: pilot } }, orders, clients: [], console,
    document: { getElementById: id => fields[id], querySelectorAll: selector => selector.includes('pedidos-tab-section') ? sections : [],
      querySelector: selector => {
        if (selector === '.pilot-orders-header') return header;
        if (selector.startsWith('#kanban')) return { focus() { focus = selector; } };
        if (selector.includes('data-screen="pedidos"].active')) return active ? (selector.includes('andamento') ? sections[1] : {}) : null;
        if (selector.includes('data-tab="novo"')) return sections[0];
        return null;
      } },
    updatePedidosTabBadge() {}, escapeHtml: value => String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('"', '&quot;'),
    getConferStatus: () => 'pendente', CONFER_STATUS: { PENDENTE: 'pendente' }, conferStatusLabel: value => value,
    safeRefDisplay: value => value || '', money: value => 'R$ ' + value, timeOf: () => '10:30', renderProgressDots: () => '',
    renderScheduledBadge: value => '<span class="scheduled">' + value + '</span>', channelLabel: value => value,
    renderOrderDraftSummary() {}, FAB_CONFIG: {}, _currentFabAction: null,
    STATUS: { CANCELADO: 'cancelado', ENTREGUE: 'entregue' }, ORDER_FLOW: ['aguardando', 'preparando', 'pronto', 'entregue'],
    getOrderById: id => orders.find(o => o.id === id), showToast() {}, openConfer() { confer++; },
    applyAdvanceTransition(id, status) { if (!deferred) { orders.find(o => o.id === id).status = status; ctx.renderKanban(); } }
  };
  vm.createContext(ctx);
  const start = html.indexOf('const KANBAN_COLS = ['), end = html.indexOf('function renderProgressDots(', start);
  vm.runInContext(html.slice(start, end) + '\n' + fn('advanceOrder') + '\n' + fn('updateFab') + '\n' + fn('goPedidosTab'), ctx);
  return { ctx, list, header, title, fab, sections, active: value => { active = value; }, defer: () => { deferred = true; }, focus: () => focus, confer: () => confer,
    highlight: id => vm.runInContext('highlightOrderId = ' + JSON.stringify(id), ctx) };
}
function selected(f) { return f.list.innerHTML.match(/data-order-status="([^"]+)" aria-pressed="true"/)?.[1]; }

test('APK mostra lista direta, primeiro estágio ocupado e três contagens reais', () => {
  const f = fixture([order('p1', 'preparando'), order('p2', 'preparando'), order('r1', 'pronto'), order('done', 'entregue')]); f.ctx.renderKanban();
  assert.equal(selected(f), 'preparando'); assert.match(f.list.innerHTML, /<strong>0<\/strong>.*<strong>2<\/strong>.*<strong>1<\/strong>/);
  assert.equal((f.list.innerHTML.match(/class="k-card /g) || []).length, 2);
  assert.match(f.list.innerHTML, /pilot-order-list/); assert.doesNotMatch(f.list.innerHTML, /kanban-col|jumpKanban|class="k-status"/);
});
test('filtro vazio permanece selecionado entre renderizações e devolve foco ao controle', () => {
  const f = fixture([order('a', 'aguardando')]); f.ctx.renderKanban(); f.ctx.selectPilotOrderStatus('pronto'); f.ctx.renderKanban();
  assert.equal(selected(f), 'pronto'); assert.match(f.list.innerHTML, /Nenhum pedido nesta etapa/); assert.match(f.focus(), /pronto/);
  f.ctx.selectPilotOrderStatus('<script>'); assert.equal(selected(f), 'pronto');
});
test('zero pedidos mantém todos estágios acessíveis sem duplicar ação de criar', () => {
  const f = fixture(); f.ctx.renderKanban();
  assert.equal((f.list.innerHTML.match(/aria-pressed=/g) || []).length, 3);
  assert.match(f.list.innerHTML, /Nenhum pedido em aberto/); assert.doesNotMatch(f.list.innerHTML, /Criar pedido/);
  f.ctx.selectPilotOrderStatus('preparando'); assert.equal(selected(f), 'preparando');
});
test('novo pedido destacado seleciona estágio uma vez sem reverter escolha posterior', () => {
  const f = fixture([order('a', 'aguardando'), order('r', 'pronto')]); f.ctx.renderKanban(); f.ctx.selectPilotOrderStatus('pronto');
  f.highlight('a'); f.ctx.renderKanban(); assert.equal(selected(f), 'aguardando');
  f.ctx.selectPilotOrderStatus('pronto'); f.ctx.renderKanban(); assert.equal(selected(f), 'pronto');
});
test('primeira carga após vazio escolhe etapa ocupada, mas respeita seleção manual anterior', () => {
  const f = fixture(); f.ctx.renderKanban(); f.ctx.orders.push(order('r', 'pronto')); f.ctx.renderKanban();
  assert.equal(selected(f), 'pronto');
  const chosen = fixture(); chosen.ctx.renderKanban(); chosen.ctx.selectPilotOrderStatus('preparando');
  chosen.ctx.orders.push(order('r', 'pronto')); chosen.ctx.renderKanban(); assert.equal(selected(chosen), 'preparando');
});
test('ordenação por entrega e criação é preservada na lista filtrada', () => {
  const later = order('depois', 'aguardando', 1); later.scheduledDeliveryAt = 9;
  const f = fixture([later, order('antes', 'aguardando', 2)]); f.ctx.renderKanban();
  assert.ok(f.list.innerHTML.indexOf('>antes<') < f.list.innerHTML.indexOf('>depois<'));
});
test('avanço originado na lista acompanha etapa apenas depois da transição real', () => {
  const f = fixture([order('a', 'aguardando')]); f.ctx.renderKanban(); f.defer(); f.ctx.advanceOrder('a'); f.ctx.renderKanban();
  assert.equal(selected(f), 'aguardando');
  f.ctx.orders[0].status = 'preparando'; f.ctx.renderKanban(); assert.equal(selected(f), 'preparando');
});
test('mudança por outra tela ou atualização externa não troca filtro escolhido', () => {
  const f = fixture([order('a', 'aguardando')]); f.ctx.renderKanban(); f.active(false); f.ctx.advanceOrder('a');
  assert.equal(selected(f), 'aguardando');
  f.ctx.orders[0].status = 'pronto'; f.ctx.renderKanban(); assert.equal(selected(f), 'aguardando');
});
test('APK entrega pelo fluxo do pedido sem abrir conferência; PWA mantém seu fluxo', () => {
  const f = fixture([order('r', 'pronto')]); f.ctx.renderKanban(); f.ctx.advanceOrder('r');
  assert.equal(f.confer(), 0); assert.equal(f.ctx.orders[0].status, 'entregue');
  const web = fixture([order('r', 'pronto')], false); web.ctx.advanceOrder('r');
  assert.equal(web.confer(), 1); assert.equal(web.ctx.orders[0].status, 'pronto');
});
test('card APK preserva avisos, ações, segurança dos textos e filtro de quantidade zero', () => {
  const o = order('a', 'aguardando'); o.clientSnapshot.name = '<img>'; o.items.push({ name: 'invisivel', qty: 0 });
  o.notes = '<script>'; o.negativeStockNote = '<faltou>'; o.channel = '<canal>';
  const f = fixture([o]); f.ctx.renderKanban(); const card = f.list.innerHTML;
  assert.doesNotMatch(card, /Conferência|openConfer|openCestaCalc/); assert.match(card, /pilot-order-meta/); assert.match(card, /10:30/);
  assert.match(card, /&lt;img>/); assert.match(card, /&lt;script>/); assert.match(card, /&lt;faltou>/); assert.match(card, /&lt;canal>/);
  assert.doesNotMatch(card, /invisivel|class="apt"|class="k-time"/);
  for (const handler of ['advanceOrder', 'openCobranca', 'printEtiqueta', 'openHistorico', 'cancelOrder']) assert.ok(card.includes(handler + "('a')"));
});
test('card de total zero não oferece cobrança e metadados ausentes não criam slots vazios', () => {
  const o = order('', 'pronto', 0); o.total = 0; const f = fixture([o]); f.ctx.renderKanban();
  assert.doesNotMatch(f.list.innerHTML, /btn-cobrar|class="apt"|10:30|<span>Pedido <\/span>/);
});
test('header acompanha formulário/lista e FAB fica oculto só no APK Pedidos', () => {
  const f = fixture(); f.fab.visible = true; f.ctx.goPedidosTab('novo');
  assert.equal(f.header.dataset.view, 'novo'); assert.equal(f.title.textContent, 'Novo pedido'); assert.equal(f.fab.visible, false);
  f.ctx.goPedidosTab('andamento'); assert.equal(f.title.textContent, 'Pedidos'); assert.equal(f.header.dataset.view, 'andamento');
  const web = fixture([], false); web.ctx.updateFab('pedidos'); assert.equal(web.fab.visible, true);
});
test('PWA continua com colunas, status, saltos e estado vazio originais', () => {
  const f = fixture([order('a', 'aguardando')], false); f.ctx.renderKanban();
  assert.match(f.list.innerHTML, /kanban-col col-aguardando/); assert.match(f.list.innerHTML, /jumpKanban/); assert.match(f.list.innerHTML, /class="k-status"/);
  assert.match(f.list.innerHTML, /Conf\. pendente/); assert.doesNotMatch(f.list.innerHTML, /pilot-order-/);
  const empty = fixture([], false); empty.ctx.renderKanban(); assert.match(empty.list.innerHTML, /Criar pedido/);
});
