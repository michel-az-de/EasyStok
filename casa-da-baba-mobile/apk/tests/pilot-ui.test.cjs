'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { commandFor, COMMANDS, installProductionOrdersUI } = require('../pilot-ui.js');
const { pilotIcon } = require('../pilot.js');

test('ações curtas conservam seu significado e sempre usam um ícone disponível', () => {
  for (const [text, label] of [
    ['🗑️ Excluir lote', 'Excluir'], ['Criar e usar', 'Etiquetar'],
    ['Conferir 12 unidades', 'Produzir 12'], ['Conferir 1 unidade', 'Produzir 1'], ['Salvar R$ 34,00', 'Salvar'],
    ['Preparar cobrança', 'Cobrar'], ['Copiar mensagem toda', 'Copiar'],
    ['Agendar para depois? (entrega futura)', 'Agendar'],
    ['Escolher imagem', 'Escolher'], ['Restaurar do celular (auto)', 'Recuperar'],
    ['↻ Tentar de novo', 'Repetir']
  ]) {
    const command = commandFor(text);
    assert.equal(command.label, label);
    assert.ok(pilotIcon(command.icon));
  }
  for (const [label, icon] of Object.entries(COMMANDS)) {
    assert.equal(label.split(/\s+/).length, 1);
    assert.ok(pilotIcon(icon), label + ': ícone ausente');
  }
  assert.equal(commandFor('Nhoque de mandioquinha'), null);
  assert.deepEqual(commandFor('Ver pedidos', "go('pedidos')"), { label: 'Ver', icon: 'ficha' });
  assert.equal(commandFor('Ver Produto', "selectClient('cliente')"), null, 'nomes de registros não são comandos');
  assert.equal(commandFor('−'), null);
});

test('ação destrutiva e criação de pedido dizem o objeto e não se confundem com fechar', () => {
  assert.deepEqual(commandFor('Cancelar pedido', "cancelOrder('p1')"), { label: 'Cancelar pedido', icon: 'circle-x' });
  assert.deepEqual(commandFor('Cancelar', "cancelOrder('p1')"), { label: 'Cancelar pedido', icon: 'circle-x' });
  assert.equal(commandFor('Cancelar').icon, 'fechar', 'cancelar um formulário continua sendo fechar');
  for (const text of ['Novo pedido', '+ Novo pedido', 'Criar pedido']) assert.deepEqual(commandFor(text), { label: 'Novo pedido', icon: 'plus' });
  assert.deepEqual(commandFor('Marcar pronto', "advanceOrder('p1')"), { label: 'Pronto', icon: 'check' });
  // O kanban do piloto escreve Finalizar para o pedido em preparo: não pode voltar a parecer Preparar.
  assert.deepEqual(commandFor('Finalizar', "advanceOrder('p1')"), { label: 'Pronto', icon: 'check' });
  for (const verb of ['Desfazer', 'Reverter', 'Recuperar']) assert.equal(COMMANDS[verb], 'undo-2');
  assert.ok(pilotIcon('circle-x') && pilotIcon('undo-2'));
});

test('cada histórico tem nome próprio: movimentos da produção e pedidos finalizados', () => {
  const tab = { append(node) { this.node = node; } }, title = { textContent: 'Finalizados' };
  const created = { addEventListener() {}, set textContent(v) { this.text = v; } };
  installProductionOrdersUI({
    document: { querySelectorAll: () => [], getElementById: id => id === 'producaoTabs' ? tab : null,
      createElement: () => created, querySelector: selector => selector.includes('finalizados') ? title : null },
    cdbPilotIcon: pilotIcon, go() {}, goProducaoTab() {}
  });
  assert.equal(created.text, 'Movimentos');
  assert.equal(title.textContent, 'Pedidos finalizados');
});

test('destinos legados e links internos não abrem módulos excluídos do APK', () => {
  const calls = [], tabs = [], state = { orders: [{ id: 'p1' }], cashEntries: [{ id: 'legado' }] };
  const original = JSON.stringify(state);
  const environment = {
    document: { querySelectorAll: () => [], getElementById: () => null },
    cdbPilotIcon: pilotIcon,
    go: (screen, options) => { calls.push([screen, options]); },
    goProducaoTab: tab => tabs.push(tab), cdbApp: { getState: () => state }
  };
  installProductionOrdersUI(environment);
  for (const screen of ['caixa', 'compras', 'conferencia', 'estoque', 'suporte']) environment.go(screen);
  assert.ok(calls.every(([screen]) => screen === 'producao'));
  environment.go('pedidos', { preserveHistory: true });
  assert.deepEqual(calls.at(-1), ['pedidos', { preserveHistory: true }]);
  environment.go('finalizados'); assert.equal(calls.at(-1)[0], 'finalizados');
  environment.goProducaoTab('calculadora'); environment.goProducaoTab('lotes');
  assert.deepEqual(tabs, ['catalogo', 'lotes']);
  assert.equal(JSON.stringify(state), original, 'retirar módulos não deve apagar registros antigos');
});

test('rótulo atualizado pelo renderizador é normalizado sem trocar handler nem estado', () => {
  let content = 'Excluir lote', markup = '', writes = 0;
  const attrs = { onclick: "deleteBatch('b1')", 'aria-label': 'Excluir lote b1' };
  const classes = new Set();
  const button = {
    disabled: true, dataset: {}, matches: () => false,
    querySelector(selector) {
      if (selector === '.pilot-command-label') return markup.includes('pilot-command-label') ? {} : null;
      if (selector === 'svg[data-pilot-icon]') return markup.includes('data-pilot-icon') ? { getAttribute: () => markup.match(/data-pilot-icon="([^"]+)"/)[1] } : null;
      return null;
    },
    getAttribute: key => attrs[key], hasAttribute: key => key in attrs,
    setAttribute: (key, value) => { attrs[key] = value; },
    classList: { add: name => classes.add(name) },
    get textContent() { return content; },
    get innerHTML() { return markup; },
    set innerHTML(value) { markup = value.replace(/<path ([^>]+)\/>/g, '<path $1></path>'); content = value.replace(/<[^>]*>/g, ''); writes++; }
  };
  const environment = {
    document: { getElementById: () => null, querySelectorAll: selector => selector.startsWith('button,') ? [button] : [] },
    cdbPilotIcon: pilotIcon, go() {}, goProducaoTab() {}
  };
  const ui = installProductionOrdersUI(environment);
  assert.match(markup, /^<svg data-pilot-icon="lixeira"/);
  assert.match(markup, /<span class="pilot-command-label">Excluir<\/span>$/);
  assert.equal(button.disabled, true); assert.equal(attrs.onclick, "deleteBatch('b1')");
  assert.equal(attrs['aria-label'], 'Excluir lote b1');
  ui.refresh(); assert.equal(writes, 1, 'não deve realimentar o MutationObserver');
  content = 'Excluir lote'; markup = 'Excluir lote';
  ui.refresh(); assert.equal(writes, 2);
  assert.equal(button.dataset.command, 'Excluir');
  delete attrs['aria-label'];
  content = markup = 'Criar e usar'; ui.refresh();
  assert.equal(attrs['aria-label'], 'Etiquetar');
  content = markup = 'Salvar'; ui.refresh();
  assert.equal(attrs['aria-label'], 'Salvar', 'nome gerado acompanha a ação do formulário reutilizado');
});
