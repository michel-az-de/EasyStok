'use strict';
const { pilotIcon } = require('../pilot.js');

const START = '<!-- Modal novo produto -->';
const END = '<!-- Modal novo cliente -->';
const ADAPTED = '<!-- pilot-product-markup-v1 -->';
const WELCOME_START = '<!-- Boas-vindas / login com nome do operador -->';
const WELCOME_END = '<!-- Tutorial / onboarding (4 steps) -->';

function adaptSplash(html) {
  const start = uniqueIndex(html, '<div id="cdbSplash" aria-hidden="true">');
  const end = uniqueIndex(html, WELCOME_START);
  if (end <= start) throw new Error('Delimitadores da abertura fora de ordem.');
  let splash = html.slice(start, end);
  for (const [before, after] of [
    ['<div id="cdbSplash" aria-hidden="true">', '<div id="cdbSplash" class="pilot-splash" aria-hidden="true">'],
    ['<div class="cdb-splash-emoji">🍝</div>', '<img class="pilot-splash-logo" src="casa-da-baba.png" alt="" width="128" height="104">'],
    ['<div class="cdb-splash-tagline">Massas frescas</div>', '<div class="cdb-splash-tagline">Produção e pedidos</div>']
  ]) {
    uniqueIndex(splash, before);
    splash = splash.replace(before, after);
  }
  return html.slice(0, start) + splash + html.slice(end);
}

function adaptWelcome(html) {
  const start = uniqueIndex(html, WELCOME_START);
  const end = uniqueIndex(html, WELCOME_END);
  if (end <= start) throw new Error('Delimitadores da identificação fora de ordem.');
  let welcome = html.slice(start, end);
  const newline = html.includes('\r\n') ? '\r\n' : '\n';
  for (const [before, after] of [
    ['<div class="emoji">👋</div>', '<img class="pilot-welcome-brand" src="casa-da-baba.png" alt="Casa da Baba" width="96" height="72">'],
    ['Bem-vindo ao Easy Stock Mobile', 'Quem vai usar o app?'],
    ['Para começar, informe seu nome. Ele aparece nos lotes e pedidos registrados e ajuda a manter a operação rastreável.', 'Seu nome identifica os lotes e pedidos que você registrar.'],
    ['<input type="text" id="welcomeName"', '<label for="welcomeName">Seu nome</label>' + newline + '  <input type="text" id="welcomeName"'],
    ['Você pode trocar depois tocando no seu nome no topo.', 'Para trocar depois, abra Configurações e ajuda no início.']
  ]) {
    uniqueIndex(welcome, before);
    welcome = welcome.replace(before, after);
  }
  // O bootstrap original mantém validação, Enter e persistência; apenas sua
  // atribuição condicional de título precisa corresponder ao markup do APK.
  const title = "if (pilot) document.getElementById('welcomeTitle').textContent = 'Bem-vinda à Casa da Baba';";
  uniqueIndex(html, title);
  return (html.slice(0, start) + welcome + html.slice(end)).replace(title,
    "if (pilot) document.getElementById('welcomeTitle').textContent = 'Quem vai usar o app?';");
}

function uniqueIndex(text, marker) {
  const index = text.indexOf(marker);
  if (index < 0 || text.indexOf(marker, index + marker.length) !== -1) {
    throw new Error('Estrutura do formulário de produto incompatível: ' + marker);
  }
  return index;
}

function adaptProduction(html) {
  const demandStart = uniqueIndex(html, '<div class="prod-demand" id="prodDemand" hidden>');
  const demandEnd = uniqueIndex(html, '<div class="prod-screen-summary" id="prodScreenSummary"');
  const demand = html.slice(demandStart, demandEnd);
  const demandHeader = /<div class="pd-head">([\s\S]*?)<\/div>/;
  if (!demandHeader.test(demand)) throw new Error('Resumo da demanda incompatível com o APK.');
  const compactDemand = demand.replace(demandHeader, '<details class="pilot-demand-details"><summary class="pd-head">$1</summary>')
    .replace(/<\/div>(\s*)$/, '</details></div>$1');
  html = html.slice(0, demandStart) + compactDemand + html.slice(demandEnd);
  const metrics = /<div class="prod-metrics">\s*<div><strong id="prodMetricProducts">0<\/strong><span>ativos<\/span><\/div>\s*<div><strong id="prodMetricStock">0<\/strong><span>estoque<\/span><\/div>\s*<div><strong id="prodMetricLow">0<\/strong><span>críticos<\/span><\/div>\s*<\/div>/g;
  const matches = [...html.matchAll(metrics)];
  if (matches.length !== 1) throw new Error('Indicadores de produção incompatíveis com o APK.');
  html = html.replace(matches[0][0], '<details class="pilot-production-metrics"><summary><span>Estoque baixo: <strong id="prodMetricLow">0</strong></span><span class="pilot-metrics-action">Indicadores</span></summary><div class="prod-metrics"><div><span>Produtos ativos:</span><strong id="prodMetricProducts">0</strong></div><div><span>Unidades em estoque:</span><strong id="prodMetricStock">0</strong></div></div></details>');
  const actions = /<div class="cta-row">\s*(<button class="cta-btn" onclick="clearProduction\(\)">Limpar<\/button>\s*<button class="cta-btn primary" id="saveProdBtn" onclick="openProductionConfirm\(\)" disabled>Conferir produção<\/button>)\s*<\/div>/g;
  const actionMatches = [...html.matchAll(actions)];
  if (actionMatches.length !== 1) throw new Error('Ações de produção incompatíveis com o APK.');
  return html.replace(actionMatches[0][0], '<div class="cta-row pilot-production-actions"><p class="pilot-production-empty" role="status">Selecione a quantidade produzida.</p>' + actionMatches[0][1] + '</div>');
}

function adaptOrders(html) {
  const tabs = /<div class="screen-tabs">\s*<button class="screen-tab" data-tab="novo" onclick="goPedidosTab\('novo'\)">\+ Novo<\/button>\s*<button class="screen-tab active" data-tab="andamento" onclick="goPedidosTab\('andamento'\)">\s*Andamento <span class="screen-tab-badge" id="pedidosTabBadge">0<\/span>\s*<\/button>\s*<\/div>/g;
  const matches = [...html.matchAll(tabs)];
  if (matches.length !== 1) throw new Error('Navegação de pedidos incompatível com o APK.');
  html = html.replace(matches[0][0], `<div class="pilot-orders-header" data-view="andamento">
    <button type="button" class="screen-tab active pilot-orders-back" data-tab="andamento" onclick="goPedidosTab('andamento')" aria-label="Voltar aos pedidos">${pilotIcon('chevron-right')}<span>Voltar</span></button>
    <h2>Pedidos</h2>
    <button type="button" class="screen-tab pilot-new-order" data-tab="novo" onclick="goPedidosTab('novo')"><span aria-hidden="true">+</span> Novo pedido</button>
    <span id="pedidosTabBadge" hidden></span>
  </div>`);
  const before = '<div class="empty"><span class="big">📭</span>Nenhum pedido em aberto</div><button class="export-btn"';
  uniqueIndex(html, before);
  // Este fragmento pertence ao literal JS do estado vazio. O SVG canônico usa
  // aspas duplas; nenhuma ação ou condição do renderizador é substituída.
  const icon = pilotIcon('pedido-em-preparo').replace(/\\/g, '\\\\').replace(/'/g, "\\'");
  return html.replace(before, '<div class="empty pilot-orders-empty"><span class="big" aria-hidden="true">' + icon + '</span>Nenhum pedido em aberto</div><button class="export-btn pilot-orders-create"');
}

function adaptBilling(html) {
  const before = '<span>📤</span> Compartilhar no WhatsApp';
  uniqueIndex(html, before);
  // Fonte: EasyStock.Console/src/componentes/Icone.jsx, whatsapp, grid24/traço1.75.
  const icon = '<svg class="pilot-billing-whatsapp" viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M3 21l1.7-4.9A9 9 0 1 1 8 19.6L3 21ZM9 8.5c0 3.3 3.2 6.5 6.5 6.5l1.5-1.5-2-1.2-1 1a4.5 4.5 0 0 1-2.8-2.8l1-1-1.2-2L9 8.5Z"/></svg>';
  return html.replace(before, icon + ' Compartilhar no WhatsApp');
}

// Move somente os blocos originais do modal. IDs, controles e handlers continuam
// pertencendo ao PWA; armazenamento, validação e impressão não são substituídos.
function adaptAppMarkup(html) {
  if (typeof html !== 'string') throw new TypeError('HTML do aplicativo precisa ser texto.');
  if (html.includes(ADAPTED)) throw new Error('Formulário de produto já adaptado para o aplicativo.');
  const start = uniqueIndex(html, START);
  const end = uniqueIndex(html, END);
  if (end <= start) throw new Error('Delimitadores do formulário de produto fora de ordem.');
  const modal = html.slice(start, end);
  uniqueIndex(modal, 'id="newProductModal"');
  const markers = [
    '<label class="modal-label" for="newProdName">',
    '<label class="modal-label" id="emojiGridLabel">',
    '<label class="modal-label" for="newProdPrice">',
    '<label class="modal-label" for="newProdCost">',
    '<div class="modal-ctas">'
  ];
  const offsets = markers.map(marker => uniqueIndex(modal, marker));
  if (offsets.some((offset, index) => index > 0 && offset <= offsets[index - 1])) {
    throw new Error('Blocos do formulário de produto fora de ordem.');
  }
  const [name, emoji, price, cost, actions] = offsets;
  const intro = '<div class="modal-sub">fica disponível na hora</div>';
  let header = modal.slice(0, name);
  uniqueIndex(header, intro);
  header = header.replace(intro, '<div class="modal-sub">Dados do produto e validade.</div>');
  const essentials = modal.slice(name, emoji) + modal.slice(price, cost);
  const options = modal.slice(cost, actions).replace(
    'alerta quando stock ≤ este valor (padrão: 2)',
    'avisa quando o estoque chegar a esta quantidade (padrão: 2)'
  ) + modal.slice(emoji, price);
  const newline = modal.includes('\r\n') ? '\r\n' : '\n';
  const adapted = header + ADAPTED + newline +
    '<div class="pilot-product-section">' + newline + essentials + '</div>' + newline +
    '<details class="pilot-product-options">' + newline +
    '<summary>Mais opções do produto</summary>' + newline + options + '</details>' + newline +
    modal.slice(actions);
  return adaptBilling(adaptOrders(adaptSplash(adaptProduction(adaptWelcome(html.slice(0, start) + adapted + html.slice(end))))));
}

module.exports = { adaptAppMarkup };
