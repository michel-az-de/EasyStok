import { itensDetalhados, numeroDaComanda, totalDoPedido } from './pedido.js'

const UUID = /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i
const TIPOS = ['resumo', 'comanda', 'nota', 'frete', 'acao']
const ACOES = ['abrir_cardapio', 'rascunho']
const campos = (objeto, permitidos) => objeto && typeof objeto === 'object' && !Array.isArray(objeto)
  && Object.keys(objeto).every((chave) => permitidos.includes(chave))
const invalida = () => { throw new Error('A IA não devolveu uma organização válida. Tente de novo.') }

export function validarMontagemFicha(bruta, conversaId) {
  if (!campos(bruta, ['conversaId', 'geracaoId', 'geradoEm', 'blocos']) || bruta.conversaId !== conversaId
    || !UUID.test(bruta.geracaoId) || typeof bruta.geradoEm !== 'string' || !Number.isFinite(Date.parse(bruta.geradoEm))
    || !Array.isArray(bruta.blocos) || bruta.blocos.length < 1 || bruta.blocos.length > 6) return invalida()
  const ids = new Set()
  const blocos = bruta.blocos.map((b) => {
    if (!campos(b, ['id', 'tipo', 'texto', 'acao']) || !TIPOS.includes(b.tipo)
      || !new RegExp(`^${b.tipo}-[1-6]$`).test(b.id) || ids.has(b.id)
      || typeof b.texto !== 'string' || !b.texto.trim() || /<[a-z/!?]/i.test(b.texto)
      || (b.tipo === 'acao' ? !ACOES.includes(b.acao) : b.acao != null)) return invalida()
    const limite = b.tipo === 'nota' || b.tipo === 'resumo' ? 500 : b.acao === 'rascunho' ? 1000 : 180
    if (b.texto.length > limite) return invalida()
    ids.add(b.id)
    return { id: b.id, tipo: b.tipo, texto: b.texto.trim(), acao: b.tipo === 'acao' ? b.acao : null }
  })
  return { conversaId, geracaoId: bruta.geracaoId, geradoEm: bruta.geradoEm, blocos }
}

export const tituloDoBlocoIa = (bloco) => ({ resumo: 'Para este atendimento', comanda: 'Comanda', nota: 'Nota proposta', frete: 'Entrega',
  acao: bloco.acao === 'rascunho' ? 'Resposta proposta' : 'Cardápio' })[bloco.tipo]

// A IA escolhe o bloco, mas nomes, quantidades e valores continuam nas fontes do pedido/cardápio.
export function comandaDaFichaIa(conversa, cardapio = [], fonteApi = true) {
  const pedido = conversa?.pedido
  if (!pedido) return null
  const itens = itensDetalhados(pedido, cardapio)
  return {
    numero: numeroDaComanda(pedido, { fonteApi }), nome: conversa.nome,
    itens: itens.map((linha) => ({ id: linha.sku, nome: linha.produto?.nome ?? linha.nome ?? 'Item fora do cardápio', quantidade: linha.qtd })),
    total: fonteApi && pedido.pedidoId ? (Number.isFinite(pedido.totalApi) ? pedido.totalApi : null) : totalDoPedido(pedido, cardapio),
    rotuloTotal: pedido.pedidoId ? 'Total do pedido' : 'Total dos itens',
  }
}
