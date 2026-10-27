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
for (const name of ['producao', 'mensagem', 'pedido-em-preparo', 'produto', 'configuracoes']) {
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
for (const name of ['bell', 'fechar', 'chevron-right', 'printer', 'refresh-cw', 'search', 'house', 'ellipsis']) {
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
  applyConsoleIcons({ querySelectorAll(selector) { return [{ set outerHTML(svg) { writes.push({ selector, svg }); } }]; } });
  assert.equal(writes.length, 6);
  assert.ok(writes.every(item => item.selector.endsWith('svg')));
  assert.ok(writes.some(item => item.selector.includes('data-nav="producao"') && item.svg.includes('data-pilot-icon="producao"')));
  assert.ok(writes.some(item => item.selector.includes('data-nav="pedidos"') && item.svg.includes('data-pilot-icon="pedido-em-preparo"')));
  assert.ok(writes.every(item => !/etq|product-tile|emoji/.test(item.selector)));
  assert.match(moduleIcons, /producao: 'producao'/); assert.match(moduleIcons, /atendimento: 'mensagem'/);
  assert.equal(pilotIcon('<script>'), '');
});
