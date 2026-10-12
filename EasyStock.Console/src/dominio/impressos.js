// Impressos do pedido servidos pela API em HTML pronto (#1632): a página já vem com o `@page`
// do papel. Aqui ficam só o catálogo (documento e papel), os padrões de cada aparelho e o
// endereço de cada impresso. O PDF próprio do console (`dominio/impressao.js`) segue como
// caminho alternativo.

export const DOCUMENTO_COZINHA = 'cozinha'

export const DOCUMENTOS = [
  { valor: 'pedido', rotulo: 'Pedido' },
  { valor: 'agendamento', rotulo: 'Agendamento' },
  { valor: 'entrega', rotulo: 'Etiqueta de entrega' },
  { valor: DOCUMENTO_COZINHA, rotulo: 'Canhoto de cozinha' },
]

export const PAPEIS = [
  { valor: 'a4', rotulo: 'A4' },
  { valor: 'a5', rotulo: 'A5' },
  { valor: 'etiqueta-10x15', rotulo: 'Etiqueta 10×15' },
  { valor: 'cupom-80', rotulo: 'Bobina 80 mm' },
  { valor: 'cupom-58', rotulo: 'Bobina 58 mm' },
]

export const PAPEL_PADRAO = {
  pedido: 'cupom-80',
  agendamento: 'a5',
  entrega: 'etiqueta-10x15',
  cozinha: 'cupom-80',
}

const ehDocumento = (valor) => DOCUMENTOS.some((d) => d.valor === valor)
const ehPapel = (valor) => PAPEIS.some((p) => p.valor === valor)

// O que veio do aparelho pode estar velho ou avariado: papel desconhecido volta ao padrão do
// documento, e o logo na térmica só liga com `true` de verdade.
export function normalizarPreferenciasImpressao(valor) {
  const papeis = {}
  for (const { valor: documento } of DOCUMENTOS) {
    const salvo = valor?.papeis?.[documento]
    papeis[documento] = ehPapel(salvo) ? salvo : PAPEL_PADRAO[documento]
  }
  return { versao: 1, papeis, logoTermica: valor?.logoTermica === true }
}

export function trocarPapelPadrao(preferencias, documento, papel) {
  const atual = normalizarPreferenciasImpressao(preferencias)
  if (!ehDocumento(documento) || !ehPapel(papel)) return atual
  return { ...atual, papeis: { ...atual.papeis, [documento]: papel } }
}

export function trocarLogoNaTermica(preferencias, ligado) {
  return { ...normalizarPreferenciasImpressao(preferencias), logoTermica: ligado === true }
}

// Endereço do impresso na API. O canhoto de cozinha tem rota própria (`comanda`); os outros
// saem de `impresso` com o documento na consulta. Nada fora do catálogo vira endereço.
export function caminhoDoImpresso(pedidoId, { documento, modelo, logoTermica = false } = {}) {
  if (typeof pedidoId !== 'string' || !pedidoId) throw new Error('Pedido sem identificação para imprimir.')
  if (!ehDocumento(documento)) throw new Error('Documento de impressão desconhecido.')
  const papel = ehPapel(modelo) ? modelo : PAPEL_PADRAO[documento]
  const base = `/api/pedidos/${encodeURIComponent(pedidoId)}`
  const fim = `modelo=${papel}&logoTermica=${logoTermica === true}`
  return documento === DOCUMENTO_COZINHA ? `${base}/comanda?${fim}` : `${base}/impresso?documento=${documento}&${fim}`
}
