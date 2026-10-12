// Painel "Cardápio de hoje" (#1448, homologação de 07/10): "eu não estou vendo foto. Eu não estou
// vendo as coisas de maneira mais organizada." A foto vem da API (a mesma da vitrine), os itens
// se agrupam pela categoria que a vitrine já usa e a busca acha pelo nome, categoria ou porção.

import { mensagemDePeca } from './anexos'
import { moeda } from './formato'

// URL gravada no banco carrega o host da época do upload (ex.: ez-api.92.113.33.60.sslip.io).
// O console em produção só alcança a API pelo /api da própria origem, então a foto do cardápio
// passa pela rota de mídia da API, que lê pela chave e ignora o host. Nenhum dado é reescrito.
const MARCA_ARQUIVO = '/files/cardapios/'
export const ROTA_FOTO_CARDAPIO = '/api/public/cardapio/fotos/'

export function urlDeExibicaoDaFoto(url, apiBase = '') {
  if (typeof url !== 'string' || !url.trim()) return null
  const posicao = url.toLowerCase().indexOf(MARCA_ARQUIVO)
  if (posicao < 0) return url
  const caminho = url.slice(posicao + MARCA_ARQUIVO.length).split(/[?#]/)[0]
  return caminho ? `${apiBase}${ROTA_FOTO_CARDAPIO}${caminho}` : url
}

// A primeira da lista é a que o EasyStok envia com índice 0 (galeria, ou a capa sem galeria).
export const fotoDoItem = (item) => item?.fotos?.[0] ?? item?.foto ?? null

export const SEM_CATEGORIA = 'Outros'

// Ordem de quem chega primeiro: a API já devolve por categoria e ordem de exibição. Sem
// categoria vai para o fim, num grupo só.
export function agruparPorCategoria(itens) {
  const grupos = new Map()
  for (const item of itens ?? []) {
    const categoria = item.categoria?.trim() || SEM_CATEGORIA
    if (!grupos.has(categoria)) grupos.set(categoria, [])
    grupos.get(categoria).push(item)
  }
  const lista = [...grupos].map(([categoria, doGrupo]) => ({ categoria, itens: doGrupo }))
  return [
    ...lista.filter((g) => g.categoria !== SEM_CATEGORIA),
    ...lista.filter((g) => g.categoria === SEM_CATEGORIA),
  ]
}

const semAcento = (texto) => String(texto ?? '')
  .normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()

// Cada palavra digitada precisa aparecer em nome, categoria ou porção ("lasanha 600").
export function filtrarCardapio(itens, termo) {
  const palavras = semAcento(termo).split(/\s+/).filter(Boolean)
  if (palavras.length === 0) return itens
  return itens.filter((item) => {
    const alvo = semAcento([item.nome, item.categoria, item.porcao].join(' '))
    return palavras.every((p) => alvo.includes(p))
  })
}

// Legenda da foto que o cliente recebe (#1474): nome, porção e preço. A porção sai quando o
// nome já a traz ("Lasanha 600 g" não vira "Lasanha 600 g / 600 g"), comparando sem acento,
// caixa nem espaço. "Foto N" é só da tela da galeria, nunca vai ao cliente.
const compacto = (texto) => semAcento(texto).replace(/\s+/g, '')
export function legendaDoItem({ nome, porcao, preco }) {
  const porcaoNova = porcao && !compacto(nome).includes(compacto(porcao)) ? porcao : null
  const detalhe = [porcaoNova, Number.isFinite(preco) ? moeda(preco) : null].filter(Boolean).join(' · ')
  return [nome, detalhe].filter(Boolean).join('\n')
}

// Enviar a foto do item ao cliente: a mesma peça da galeria (#1439), que o EasyStok envia pelo
// id do item e índice, sem o navegador baixar a URL. A legenda vem de `legendaDoItem`.
export function mensagemDaFotoDoItem(item) {
  const foto = fotoDoItem(item)
  if (!foto) return null
  return mensagemDePeca({
    nome: item.nome,
    descricao: [item.porcao, moeda(item.preco)].filter(Boolean).join(' · '),
    legenda: legendaDoItem(item),
    foto,
    cardapioItemId: item.sku,
    indice: 0,
  })
}
