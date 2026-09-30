// Carta do cardápio desenhada. Enquanto não existe foto de verdade, a casa
// manda uma carta com nome, porção e preço legíveis. É honesto: parece arte de
// cardápio, não finge ser fotografia.

import { moeda } from './formato'

const PALETA = {
  'LAS-CLA': { fundo: '#7A1F2B', massa: '#E2A33F' },
  'LAS-VER': { fundo: '#2F6B45', massa: '#9CC48A' },
  'RAV-LIM': { fundo: '#7A5600', massa: '#F0D77A' },
  'RAV-ABO': { fundo: '#A8541F', massa: '#EFA85C' },
  'TOR-COS': { fundo: '#4A2B22', massa: '#C98A5C' },
  'PAP-RAG': { fundo: '#5B2018', massa: '#D98F55' },
  'EXT-PAR': { fundo: '#6B6358', massa: '#EFE6D2' },
}

// Integração 50: nome longo ("Ravióli de abóbora com alho-poró", "Horário de
// funcionamento") passava da borda da carta. O título quebra por palavra em
// até três linhas, e o resto do texto desce junto.
const escapar = (texto) => String(texto).replace(/&/g, '&amp;').replace(/</g, '&lt;')

export function linhasDoTitulo(texto, maximo = 15) {
  const linhas = []
  for (const palavra of String(texto).split(/\s+/)) {
    const atual = linhas.at(-1)
    if (atual && (atual + ' ' + palavra).length <= maximo) linhas[linhas.length - 1] = atual + ' ' + palavra
    else linhas.push(palavra)
  }
  return linhas.slice(0, 3)
}

// Título em até três linhas a partir de `y`, na coluna de texto da carta.
// Devolve o SVG e o y da última linha, para o texto seguinte descer junto.
export function tituloDaCarta(texto, y) {
  const linhas = linhasDoTitulo(texto)
  const svg = '<text font-family="Georgia,serif" font-size="18" fill="#F6EFE2">'
    + linhas.map((l, i) => `<tspan x="156" y="${y + i * 21}">${escapar(l)}</tspan>`).join('')
    + '</text>'
  return { svg, fim: y + (linhas.length - 1) * 21 }
}

export function cartaDoItem(item) {
  const { fundo, massa } = PALETA[item.sku] ?? { fundo: '#5E564C', massa: '#E6DCC8' }
  const n = linhasDoTitulo(item.nome).length
  const titulo = tituloDaCarta(item.nome, Math.round(100 - (n * 21 + 78) / 2 + 17))
  return 'data:image/svg+xml;utf8,' + encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 320 200">'
    + `<rect width="320" height="200" fill="${fundo}"/>`
    + '<circle cx="82" cy="100" r="54" fill="#F6EFE2"/>'
    + `<circle cx="82" cy="100" r="40" fill="${massa}"/>`
    + '<path d="M52 96c14-10 46-10 60 0M56 108c12-8 40-8 52 0" stroke="#00000022" '
    + 'stroke-width="6" fill="none" stroke-linecap="round"/>'
    + titulo.svg
    + `<text x="156" y="${titulo.fim + 24}" font-family="Helvetica,Arial" font-size="14" fill="#F6EFE2CC">${escapar(item.porcao)}</text>`
    + `<text x="156" y="${titulo.fim + 52}" font-family="Helvetica,Arial" font-size="19" font-weight="bold" fill="#F6EFE2">${moeda(item.preco)}</text>`
    + `<text x="156" y="${titulo.fim + 78}" font-family="Helvetica,Arial" font-size="11" fill="#F6EFE299">CASA DA BABA</text>`
    + '</svg>',
  )
}

// ---------------------------------------------------------------------------
// Rodada 12 (#19): foto do prato na vitrine do cardápio do cliente. Feedback
// da Thatiane (26/09/2026): "uma página mais robusta, com mais imagens".
// Continua honesto como a carta acima: é ilustração do prato visto de cima,
// não finge ser fotografia. Sem texto dentro: nome, porção e preço ficam no
// cartão, legíveis e traduzíveis pelo leitor de tela. A massa desenhada
// segue o tipo (lasanha, ravióli, tortéi, pappardelle, extra), a cor segue a
// mesma PALETA da carta, então cada prato tem a sua.
const TIPOS_DE_MASSA = {
  LAS: (m, f) => '<g transform="rotate(-8 160 112)">'
    + `<rect x="112" y="78" width="96" height="68" rx="6" fill="${m}"/>`
    + `<rect x="112" y="92" width="96" height="9" fill="${f}" opacity=".8"/>`
    + `<rect x="112" y="112" width="96" height="9" fill="${f}" opacity=".8"/>`
    + `<rect x="112" y="132" width="96" height="7" fill="${f}" opacity=".6"/>`
    + '</g>',
  RAV: (m) => [[122, 84], [166, 80], [118, 124], [162, 122], [140, 102]]
    .map(([x, y]) => `<rect x="${x}" y="${y}" width="36" height="36" rx="5" fill="${m}" `
      + 'stroke="#00000030" stroke-width="3" stroke-dasharray="3 3"/>'
      + `<circle cx="${x + 18}" cy="${y + 18}" r="8" fill="#00000018"/>`)
    .join(''),
  TOR: (m) => [[128, 96, -20], [170, 92, 15], [134, 132, 10], [176, 128, -12]]
    .map(([x, y, r]) => `<path transform="rotate(${r} ${x} ${y})" d="M${x - 22} ${y}a22 22 0 0 1 44 0z" `
      + `fill="${m}" stroke="#00000030" stroke-width="3"/>`)
    .join(''),
  PAP: (m, f) => [0, 1, 2, 3]
    .map((i) => `<path d="M104 ${82 + i * 18}c18-14 34 14 52 0s34-14 58 0" fill="none" stroke="${m}" `
      + 'stroke-width="12" stroke-linecap="round"/>')
    .join('')
    + [[132, 96], [178, 118], [150, 138], [196, 92]].map(([x, y]) => `<circle cx="${x}" cy="${y}" r="7" fill="${f}"/>`).join(''),
  EXT: (m) => '<circle cx="160" cy="110" r="42" fill="#FFFFFF" stroke="#00000020" stroke-width="3"/>'
    + [[148, 100], [166, 96], [158, 114], [174, 112], [142, 118], [162, 126], [178, 124]]
      .map(([x, y]) => `<rect x="${x}" y="${y}" width="10" height="4" rx="2" fill="${m}" transform="rotate(25 ${x} ${y})"/>`)
      .join(''),
}

const massaGenerica = (m) => '<path d="M120 110c0-26 40-34 52-12s-8 40-30 30-18-34 6-36 38 10 30 34" '
  + `fill="none" stroke="${m}" stroke-width="12" stroke-linecap="round"/>`

export function fotoDoPrato(item) {
  const { fundo, massa } = PALETA[item.sku] ?? { fundo: '#5E564C', massa: '#E6DCC8' }
  const desenhar = TIPOS_DE_MASSA[String(item.sku).split('-')[0]] ?? massaGenerica
  return 'data:image/svg+xml;utf8,' + encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 320 220">'
    + `<rect width="320" height="220" fill="${fundo}"/>`
    + '<path d="M0 40h320M0 90h320M0 140h320M0 190h320" stroke="#FFFFFF10" stroke-width="2"/>'
    + '<ellipse cx="166" cy="120" rx="96" ry="90" fill="#00000030"/>'
    + '<circle cx="160" cy="112" r="92" fill="#F6EFE2"/>'
    + '<circle cx="160" cy="112" r="74" fill="none" stroke="#0000000F" stroke-width="3"/>'
    + desenhar(massa, fundo)
    + '<path d="M212 70c6-8 16-8 20-2-6 6-14 8-20 2z" fill="#4F8A3C"/>'
    + '<path d="M104 150c6-6 14-6 18 0-6 6-12 6-18 0z" fill="#4F8A3C"/>'
    + '</svg>',
  )
}
