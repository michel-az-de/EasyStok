'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { installLabelWorkspace } = require('../pilot.js');

function fixture() {
  const byId = new Map(), frames = [];
  function element(tag, className = '', id) {
    const classes = new Set(className.split(' ').filter(Boolean));
    const node = { tag, className, dataset: {}, children: [], parentElement: null, open: false,
      style: {}, attributes: {}, handlers: {}, value: '', innerHTML: '', textContent: '',
      classList: { add: name => classes.add(name), contains: name => classes.has(name) },
      append(...nodes) { for (const child of nodes) this.insertBefore(child, null); },
      insertBefore(child, reference) {
        if (reference && !this.children.includes(reference)) throw new Error('NotFoundError');
        if (child.parentElement) child.parentElement.children.splice(child.parentElement.children.indexOf(child), 1);
        const index = reference ? this.children.indexOf(reference) : this.children.length;
        this.children.splice(index, 0, child); child.parentElement = this;
      },
      querySelector(selector) {
        for (const child of this.children) {
          if (child.classList.contains(selector.slice(1))) return child;
          const nested = child.querySelector(selector); if (nested) return nested;
        }
        return null;
      },
      setAttribute(name, value) { this.attributes[name] = value; },
      addEventListener(name, callback) { this.handlers[name] = callback; }
    };
    if (id) byId.set(id, node);
    return node;
  }
  const overlay = element('div', '', 'etiquetasPreview');
  const sheet = element('div', 'etq-sheet'); overlay.append(sheet);
  const config = element('div', 'etq-config-bar', 'etqConfigBar');
  const chips = element('div', '', 'etqCodeChips'); chips.textContent = 'QR + Barras Só QR Só Barras Sem código'; config.append(chips);
  const banner = element('div', 'etq-success-banner', 'etqSuccessBanner'); banner.textContent = 'Produção cadastrada';
  const preview = element('div', 'etq-body', 'etqBody'); preview.innerHTML = '<div class="etq-card">Prévia física preservada</div>';
  const note = element('div', 'etq-note-bar'); const input = element('input', '', 'etqNoteInput'); input.value = 'Manter refrigerado'; input.oninput = () => 'note'; note.append(input);
  const copiesRow = element('div'); const copies = element('select', '', 'etqCopiesSelect'); copies.value = '5';
  const retry = element('button', '', 'etqRetryBtn'); retry.style.display = 'none'; retry.onclick = () => 'retry'; copiesRow.append(copies, retry);
  const actions = element('div', 'etq-actions');
  const buttons = ['print', 'save', 'share'].map(action => {
    const button = element('button', 'etq-' + action); button.onclick = () => action; actions.append(button); return button;
  });
  sheet.append(config, banner, preview, note, copiesRow, actions);
  const environment = { document: { getElementById: id => byId.get(id), createElement: tag => element(tag) },
    requestAnimationFrame: callback => frames.push(callback), scaleEtqCards() { environment.resizes++; }, resizes: 0 };
  return { environment, byId, sheet, config, chips, banner, preview, note, input, copiesRow, copies, retry, actions, buttons, frames };
}

test('reorganiza nós existentes sem alterar prévia, escolhas, valores ou handlers', () => {
  const f = fixture(); const before = [...f.byId.values()]; const previewHtml = f.preview.innerHTML;
  installLabelWorkspace(f.environment);
  assert.deepEqual([...f.byId.values()], before);
  const options = f.config.parentElement;
  assert.equal(options.tag, 'details'); assert.equal(options.open, false);
  assert.equal(options.children[0].textContent, 'Ajustar etiqueta');
  assert.equal(f.note.parentElement, options);
  assert.equal(f.preview.parentElement, f.sheet); assert.equal(f.preview.innerHTML, previewHtml);
  assert.equal(f.banner.parentElement, f.sheet); assert.equal(f.banner.textContent, 'Produção cadastrada');
  assert.equal(f.chips.textContent, 'QR + Barras Só QR Só Barras Sem código');
  assert.equal(f.input.value, 'Manter refrigerado'); assert.equal(f.input.oninput(), 'note');
  assert.equal(f.copies.value, '5'); assert.equal(f.copiesRow.parentElement, f.actions);
  assert.deepEqual(f.buttons.map(button => button.onclick()), ['print', 'save', 'share']);
  assert.equal(f.retry.onclick(), 'retry'); assert.equal(f.retry.style.display, 'none');
  assert.equal(f.retry.parentElement.parentElement, f.sheet);
  f.retry.style.display = 'inline-block';
  assert.equal(f.retry.parentElement.open, false, 'Retry não depende de abrir opções');
  assert.deepEqual(f.buttons.map(button => button.attributes['aria-label']), ['Imprimir', 'Salvar PNG', 'Enviar']);
  assert.ok(f.buttons.every(button => button.innerHTML.includes('<svg')));
});

test('expansão e recolhimento recalculam escala após layout e instalação é única', () => {
  const f = fixture(); installLabelWorkspace(f.environment);
  const options = f.config.parentElement, children = [...f.sheet.children], toggle = options.handlers.toggle;
  installLabelWorkspace(f.environment);
  assert.deepEqual(f.sheet.children, children); assert.equal(options.handlers.toggle, toggle);
  for (const open of [true, false]) {
    options.open = open; const count = f.environment.resizes;
    options.handlers.toggle(); assert.equal(f.environment.resizes, count);
    f.frames.shift()(); assert.equal(f.environment.resizes, count + 1);
  }
});

test('markup incompleto ou estrutura inesperada fica intacto', () => {
  for (const breakFixture of [f => f.byId.delete('etqCopiesSelect'), f => { f.retry.parentElement = f.sheet; }]) {
    const f = fixture(); breakFixture(f); const children = [...f.sheet.children];
    installLabelWorkspace(f.environment);
    assert.deepEqual(f.sheet.children, children); assert.equal(f.config.parentElement, f.sheet);
    assert.equal(f.sheet.dataset.pilotLabels, undefined);
  }
  assert.doesNotThrow(() => installLabelWorkspace({ document: { getElementById: () => null } }));
});

test('contrato de controles existe no HTML real e não renomeia ações de impressão', () => {
  const html = fs.readFileSync(path.resolve(__dirname, '../../../EasyStock.Api/wwwroot/pwa/index.html'), 'utf8');
  const start = html.indexOf('id="etiquetasPreview"'), end = html.indexOf('id="cobrancaModal"', start);
  assert.ok(start >= 0 && end > start);
  const modal = html.slice(start, end);
  for (const id of ['etqConfigBar', 'etqBody', 'etqNoteInput', 'etqCopiesSelect', 'etqRetryBtn']) assert.ok(modal.includes('id="' + id + '"'));
  for (const action of ['print', 'save', 'share']) assert.ok(modal.includes('onclick="etqAction(\'' + action + '\')"'));
  assert.match(modal, /oninput="onEtqNoteInput\(\)"/);
  assert.match(modal, /onclick="etqRetry\(\)"/);
});
