import { FUSO } from './formato.js'

// #2247 (impressão v3, G1 item 7): a etiqueta de produção do Console sai do mesmo motor do app
// (infra/motor). Aqui ficam as regras puras: os campos com os termos do app e a leitura do que o
// motor desenhou. O papel é o da etiqueta do APK piloto, 80 x 40 mm, deitada.
export const PAPEL_DA_PRODUCAO = 'etiqueta-80x40'
export const ORIENTACAO_DA_PRODUCAO = 'horizontal'

const data = (iso) => (iso ? new Date(iso).toLocaleDateString('pt-BR', { timeZone: FUSO, day: '2-digit', month: '2-digit', year: 'numeric' }) : '')
// Como o app escreve (pwa/index.html, fmtPesoG): "PESO 350g", "PESO 1,00kg".
const peso = (g) => {
  if (!(g > 0)) return ''
  const kg = (g / 1000).toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2, useGrouping: false })
  return 'PESO ' + (g >= 1000 ? kg + 'kg' : g + 'g')
}

// O código que a etiqueta imprime (QR e texto): sem acento e só ASCII visível. Assim o leitor
// lê o mesmo código em qualquer aparelho.
export const codigoDaEtiqueta = (texto) =>
  String(texto ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^\x20-\x7E]/g, '')

// Revisão da #2081 (R1): etiqueta de comida declara os alérgenos (RDC 26/2015), em maiúsculas:
// "ALÉRGICOS: CONTÉM GLÚTEN, LACTOSE E OVO." Os nomes são os do app (pwa/etiqueta/render.js).
const ALERGENOS = {
  gluten: 'glúten', lactose: 'lactose', ovo: 'ovo', soja: 'soja', amendoim: 'amendoim',
  castanhas: 'castanhas', peixe: 'peixe', crustaceos: 'crustáceos', outros: 'outros',
}
export function textoDosAlergenos(lista) {
  const nomes = (lista ?? []).map((a) => ALERGENOS[a] ?? String(a)).filter(Boolean)
  if (nomes.length === 0) return null
  const juntos = nomes.length === 1 ? nomes[0] : `${nomes.slice(0, -1).join(', ')} e ${nomes.at(-1)}`
  return `ALÉRGICOS: CONTÉM ${juntos.toUpperCase()}.`
}

// Os campos do motor, com os termos do app (pwa/index.html, camposDaEtiqueta). Sem `barras`: o
// Code 128 só sai com a chave curta (D4), que a API do Console não traz; fica só o QR.
export function camposDaEtiqueta({ etiqueta, empresa }) {
  return {
    empresa: empresa ?? '',
    nome: etiqueta.nome ?? '',
    validade: data(etiqueta.validadeEm),
    fabricacao: data(etiqueta.produzidoEm),
    lote: etiqueta.lote ?? '',
    peso: peso(etiqueta.pesoG),
    nota: textoDosAlergenos(etiqueta.alergenos) ?? '',
    qr: codigoDaEtiqueta(etiqueta.codigo),
  }
}

export const RECUSA_ALERGENOS = 'os alérgenos não cabem nesta etiqueta, e comida não sai sem a declaração. Encurte o nome do prato'

// O que o motor devolveu -> o que a tela mostra e imprime: o PNG no tamanho do papel (mm) ou o
// motivo da recusa. `pontosMm` é a resolução do canvas (8 pontos por mm, 203 dpi).
export function lerDesenho(campos, desenho, pontosMm) {
  const vazio = { png: null, larguraMm: null, alturaMm: null, avisos: desenho?.avisos ?? [] }
  if (!desenho || desenho.recusa || !desenho.canvas) return { ...vazio, recusa: desenho?.recusa || 'o motor não desenhou a etiqueta' }
  if (campos.nota && (desenho.omitidos ?? []).includes('nota')) return { ...vazio, recusa: RECUSA_ALERGENOS }
  const c = desenho.canvas
  return { png: c.toDataURL('image/png'), larguraMm: c.width / pontosMm, alturaMm: c.height / pontosMm, avisos: desenho.avisos ?? [], recusa: null }
}
