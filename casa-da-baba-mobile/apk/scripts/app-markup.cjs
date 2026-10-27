'use strict';

const START = '<!-- Modal novo produto -->';
const END = '<!-- Modal novo cliente -->';
const ADAPTED = '<!-- pilot-product-markup-v1 -->';

function uniqueIndex(text, marker) {
  const index = text.indexOf(marker);
  if (index < 0 || text.indexOf(marker, index + marker.length) !== -1) {
    throw new Error('Estrutura do formulário de produto incompatível: ' + marker);
  }
  return index;
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
  return html.slice(0, start) + adapted + html.slice(end);
}

module.exports = { adaptAppMarkup };
