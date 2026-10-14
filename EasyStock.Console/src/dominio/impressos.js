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

// Perfil = a impressora do aparelho (#1756). Cada impressora dedicada só tem um papel; "sistema"
// deixa a escolha para o diálogo e aceita qualquer papel. O `rotulo` é o que fica no histórico.
export const PERFIS_IMPRESSAO = [
  { valor: 'sistema', rotulo: 'Escolher no diálogo do sistema', papeis: PAPEIS.map((p) => p.valor) },
  { valor: 'hp-a4', rotulo: 'HP · folha A4', papeis: ['a4'] },
  { valor: 'hp-a5', rotulo: 'HP · folha A5', papeis: ['a5'] },
  { valor: 'termica-80', rotulo: 'Térmica · bobina 80 mm', papeis: ['cupom-80'] },
  { valor: 'termica-58', rotulo: 'Térmica · bobina 58 mm', papeis: ['cupom-58'] },
  { valor: 'etiqueta-10x15', rotulo: 'Térmica · etiqueta 10×15', papeis: ['etiqueta-10x15'] },
]

export const perfilDeImpressao = (valor) => PERFIS_IMPRESSAO.find((p) => p.valor === valor) ?? PERFIS_IMPRESSAO[0]
const perfilDedicado = (papel) => PERFIS_IMPRESSAO.find((p) => p.valor !== 'sistema' && p.papeis.includes(papel)).valor

// O que dizer no diálogo para o papel sair na medida: o modelo já traz as margens.
export function orientacaoDoPapel(papel) {
  const sem = 'escala 100%, sem margens, cabeçalhos ou rodapés'
  if (papel === 'cupom-58' || papel === 'cupom-80') {
    return `Selecione a impressora com bobina de ${papel === 'cupom-58' ? '58' : '80'} mm, ${sem}. O comprimento acompanha o conteúdo; confira o corte.`
  }
  if (papel === 'etiqueta-10x15') return `Selecione a etiqueta de 100 × 150 mm em retrato, ${sem}.`
  return `Selecione papel ${papel === 'a5' ? 'A5 (148 × 210 mm)' : 'A4 (210 × 297 mm)'} em retrato, ${sem}. As margens já fazem parte do modelo.`
}

const ehDocumento = (valor) => DOCUMENTOS.some((d) => d.valor === valor)
const ehPapel = (valor) => PAPEIS.some((p) => p.valor === valor)

// O que veio do aparelho pode estar velho ou avariado: papel desconhecido volta ao padrão do
// documento, perfil que não imprime aquele papel vira a impressora dedicada ao papel (a versão 1
// só guardava o papel), e o logo na térmica só liga com `true` de verdade.
export function normalizarPreferenciasImpressao(valor) {
  const papeis = {}
  const perfis = {}
  for (const { valor: documento } of DOCUMENTOS) {
    const salvo = valor?.papeis?.[documento]
    papeis[documento] = ehPapel(salvo) ? salvo : PAPEL_PADRAO[documento]
    const perfil = PERFIS_IMPRESSAO.find((p) => p.valor === valor?.perfis?.[documento])
    perfis[documento] = perfil?.papeis.includes(papeis[documento]) ? perfil.valor : perfilDedicado(papeis[documento])
  }
  return { versao: 2, papeis, perfis, logoTermica: valor?.logoTermica === true }
}

export function trocarPapelPadrao(preferencias, documento, papel) {
  const atual = normalizarPreferenciasImpressao(preferencias)
  if (!ehDocumento(documento) || !ehPapel(papel)) return atual
  return normalizarPreferenciasImpressao({ ...atual, papeis: { ...atual.papeis, [documento]: papel } })
}

export function trocarPerfilPadrao(preferencias, documento, valor) {
  const atual = normalizarPreferenciasImpressao(preferencias)
  const perfil = PERFIS_IMPRESSAO.find((p) => p.valor === valor)
  if (!ehDocumento(documento) || !perfil) return atual
  const papel = perfil.papeis.includes(atual.papeis[documento]) ? atual.papeis[documento] : perfil.papeis[0]
  return { ...atual, papeis: { ...atual.papeis, [documento]: papel }, perfis: { ...atual.perfis, [documento]: perfil.valor } }
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
