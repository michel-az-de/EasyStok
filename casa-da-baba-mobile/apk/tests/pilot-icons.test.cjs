'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { pilotIcon, applyConsoleIcons } = require('../pilot.js');
const components = path.resolve(__dirname, '../../../EasyStock.Console/src/componentes');
const brand = fs.readFileSync(path.join(components, 'MarcaIcone.jsx'), 'utf8');
const utility = fs.readFileSync(path.join(components, 'Icone.jsx'), 'utf8');
const moduleIcons = fs.readFileSync(path.join(components, 'IconeDoModulo.jsx'), 'utf8');
for (const name of ['producao', 'mensagem', 'pedido-em-preparo', 'produto', 'configuracoes', 'caixa', 'estoque']) {
  test('geometria V4 canônica do Console: ' + name, () => {
    const source = brand.match(new RegExp('^\\s*(?:\'' + name + '\'|' + name + '):\\s*(<g[^\\n]+</g>),', 'm'));
    assert.ok(source, 'Fonte canônica precisa continuar identificável');
    const body = source[1].replaceAll('strokeWidth', 'stroke-width').replaceAll('strokeLinecap', 'stroke-linecap').replaceAll('strokeLinejoin', 'stroke-linejoin');
    const svg = pilotIcon(name);
    assert.ok(svg.includes(body), 'Geometria não pode divergir da fonte viva');
    assert.match(svg, /viewBox="0 0 32 32"/);
    assert.match(svg, /aria-hidden="true" focusable="false"/);
    if (['producao', 'pedido-em-preparo'].includes(name)) assert.match(svg, /width="32" height="32"/);
  });
}
for (const name of ['bell', 'fechar', 'chevron-right', 'printer', 'download', 'enviar', 'refresh-cw', 'search', 'house', 'ellipsis', 'shopping-cart', 'archive', 'ficha', 'historico', 'ajuda']) {
  test('traço utilitário canônico do Console: ' + name, () => {
    const source = utility.match(new RegExp('^\\s*(?:\'' + name + '\'|' + name + '):\\s*((?:\'[^\']*\'\\s*\\+\\s*)*\'[^\']*\')', 'm'));
    assert.ok(source);
    const d = [...source[1].matchAll(/'([^']*)'/g)].map(part => part[1]).join('');
    const svg = pilotIcon(name);
    assert.ok(svg.includes('<path d="' + d + '"/>'));
    assert.match(svg, /viewBox="0 0 24 24"/); assert.match(svg, /stroke-width="1.75"/);
  });
}
test('adaptador aplica somente SVGs fixos conhecidos e respeita significado dos módulos', () => {
  const writes = [];
  applyConsoleIcons({ querySelectorAll(selector) { return [{ set outerHTML(svg) { writes.push({ selector, svg }); }, set innerHTML(svg) { writes.push({ selector, svg }); } }]; } });
  assert.equal(writes.length, 16);
  assert.ok(writes.every(item => item.selector.endsWith('svg') || item.selector.startsWith('#moreMenu ')));
  assert.ok(writes.some(item => item.selector.includes('data-nav="producao"') && item.svg.includes('data-pilot-icon="producao"')));
  assert.ok(writes.some(item => item.selector.includes('data-nav="pedidos"') && item.svg.includes('data-pilot-icon="pedido-em-preparo"')));
  assert.ok(writes.every(item => !/#etiquetasPreview|product-tile|emoji/.test(item.selector)));
  assert.match(moduleIcons, /producao: 'producao'/); assert.match(moduleIcons, /atendimento: 'mensagem'/);
  assert.equal(pilotIcon('<script>'), '');
});

test('os dez atalhos reais do menu recebem ícones canônicos sem trocar rótulos ou ações', () => {
  const html = fs.readFileSync(path.resolve(__dirname, '../../../EasyStock.Api/wwwroot/pwa/index.html'), 'utf8');
  const menu = html.slice(html.indexOf('<div class="more-menu" id="moreMenu"'), html.indexOf('<!-- Bottom nav -->'));
  const buttons = [...menu.matchAll(/<button\b([^>]+)>([\s\S]*?)<\/button>/g)]
    .filter(match => /class="more-menu-(?:search-)?icon"/.test(match[2]))
    .map(match => ({ attributes: match[1], action: match[1].match(/onclick="([^"]+)"/)[1],
      label: match[2].match(/<(?:strong|b)>(.*?)<\/(?:strong|b)>/)[1], search: /more-menu-search-icon/.test(match[2]), icon: { innerHTML: '' } }));
  assert.equal(buttons.length, 10);
  const controls = buttons.map(({ attributes, action, label }) => ({ attributes, action, label }));
  const document = { querySelectorAll(selector) {
    if (selector === '#moreMenu .more-menu-search-icon') return buttons.filter(button => button.search).map(button => button.icon);
    const action = selector.match(/^#moreMenu button\[onclick="([^"]+)"\] \.more-menu-icon$/)?.[1];
    return action ? buttons.filter(button => button.action === action).map(button => button.icon) : [];
  } };
  applyConsoleIcons(document);
  const expected = ['search', 'caixa', 'shopping-cart', 'archive', 'estoque', 'ficha', 'historico', 'printer', 'configuracoes', 'ajuda'];
  buttons.forEach((button, i) => assert.equal(button.icon.innerHTML, pilotIcon(expected[i]), button.label));
  applyConsoleIcons(document);
  assert.deepEqual(buttons.map(({ attributes, action, label }) => ({ attributes, action, label })), controls);
  buttons.forEach(button => assert.equal((button.icon.innerHTML.match(/<svg /g) || []).length, 1));
});
