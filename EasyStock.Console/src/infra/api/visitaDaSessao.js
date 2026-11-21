import { novoPedido } from '../../dominio/pedido'

const PREFIXO = 'easystok.visita'
const chaveDe = sessao => sessao?.empresa?.id && sessao?.usuario?.id
  ? `${PREFIXO}:${sessao.empresa.id}:${sessao.usuario.id}` : null

function comandaLocal(pedido) {
  if (!pedido || pedido.pedidoId || !Array.isArray(pedido.itens)) return null
  const itens = pedido.itens.filter(i => typeof i?.sku === 'string' && Number.isInteger(i.qtd) && i.qtd > 0)
    .map(i => ({ sku: i.sku, qtd: i.qtd, obs: typeof i.obs === 'string' ? i.obs : '' }))
  if (!itens.length) return null
  // Só montagem e versão do vínculo: nenhum token, dado pessoal, endereço ou pagamento.
  const numero = typeof pedido.numero === 'string' || typeof pedido.numero === 'number' ? pedido.numero : null
  const identidade = pedido.identidadeProposta
  const identidadeValida = identidade && (identidade.clienteId === null || typeof identidade.clienteId === 'string' && identidade.clienteId.trim())
    && Number.isInteger(identidade.vinculoRevisao) && identidade.vinculoRevisao >= 0
  return { ...novoPedido(numero), itens, janela: typeof pedido.janela === 'string' ? pedido.janela : null,
    ...(identidadeValida ? { identidadeProposta: { clienteId: identidade.clienteId, vinculoRevisao: identidade.vinculoRevisao } } : {}),
    meio: typeof pedido.meio === 'string' ? pedido.meio : null,
    observacoes: typeof pedido.observacoes === 'string' ? pedido.observacoes : '' }
}

function visitaDe(salva) {
  const comandas = salva?.comandas && typeof salva.comandas === 'object' && !Array.isArray(salva.comandas) ? salva.comandas : {}
  const f = salva?.filtros
  const filtros = f && typeof f === 'object' && !Array.isArray(f) ? {
    busca: typeof f.busca === 'string' ? f.busca : '',
    aba: ['precisa', 'todas'].includes(f.aba) ? f.aba : 'precisa',
    canais: Array.isArray(f.canais) ? f.canais.filter(c => typeof c === 'string') : [],
    ordenacao: ['urgencia', 'recentes', 'entrega'].includes(f.ordenacao) ? f.ordenacao : 'urgencia',
    responsavel: ['todas', 'minhas', 'sem-dono'].includes(f.responsavel) ? f.responsavel : 'todas',
    posicao: Number.isFinite(f.posicao) && f.posicao >= 0 ? f.posicao : 0,
  } : null
  return { ultimaConversa: typeof salva?.ultimaConversa === 'string' ? salva.ultimaConversa : null,
    ...(filtros ? { filtros } : {}),
    comandas: Object.fromEntries(Object.entries(comandas).map(([id, p]) => [id, comandaLocal(p)]).filter(([, p]) => p)) }
}

export function lerVisita(sessao) {
  const chave = chaveDe(sessao)
  if (!chave) return visitaDe(null)
  try { return visitaDe(JSON.parse(sessionStorage.getItem(chave) ?? '{}')) }
  catch { return visitaDe(null) }
}

export function gravarVisita(sessao, visita) {
  const chave = chaveDe(sessao)
  if (!chave) return false
  try { sessionStorage.setItem(chave, JSON.stringify(visitaDe(visita))); return true }
  catch { return false }
}

export function esquecerVisitas() {
  try {
    for (let i = sessionStorage.length - 1; i >= 0; i--) {
      const chave = sessionStorage.key(i)
      if (chave?.startsWith(PREFIXO + ':')) sessionStorage.removeItem(chave)
    }
  } catch { /* Nada a limpar. */ }
}
