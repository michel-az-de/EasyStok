import { novoPedido } from '../../dominio/pedido'

const PREFIXO = 'easystok.visita'
const chaveDe = sessao => sessao?.empresa?.id && sessao?.usuario?.id
  ? `${PREFIXO}:${sessao.empresa.id}:${sessao.usuario.id}` : null

function comandaLocal(pedido) {
  if (!pedido || pedido.pedidoId || !Array.isArray(pedido.itens)) return null
  const itens = pedido.itens.filter(i => typeof i?.sku === 'string' && Number.isInteger(i.qtd) && i.qtd > 0)
    .map(i => ({ sku: i.sku, qtd: i.qtd, obs: typeof i.obs === 'string' ? i.obs : '' }))
  if (!itens.length) return null
  // Só a montagem: nenhum token, cadastro, endereço, pagamento ou retrato do cliente.
  const numero = typeof pedido.numero === 'string' || typeof pedido.numero === 'number' ? pedido.numero : null
  return { ...novoPedido(numero), itens, janela: typeof pedido.janela === 'string' ? pedido.janela : null,
    meio: typeof pedido.meio === 'string' ? pedido.meio : null,
    observacoes: typeof pedido.observacoes === 'string' ? pedido.observacoes : '' }
}

function visitaDe(salva) {
  const comandas = salva?.comandas && typeof salva.comandas === 'object' && !Array.isArray(salva.comandas) ? salva.comandas : {}
  return { ultimaConversa: typeof salva?.ultimaConversa === 'string' ? salva.ultimaConversa : null,
    comandas: Object.fromEntries(Object.entries(comandas).map(([id, p]) => [id, comandaLocal(p)]).filter(([, p]) => p)),
    // #2070: os Primeiros passos já abriram sozinhos nesta visita; só a marca, nunca o estado deles.
    ...(salva?.primeirosPassosAbertos === true ? { primeirosPassosAbertos: true } : {}) }
}

export function lerVisita(sessao) {
  const chave = chaveDe(sessao)
  if (!chave) return visitaDe(null)
  try { return visitaDe(JSON.parse(sessionStorage.getItem(chave) ?? '{}')) }
  catch { return visitaDe(null) }
}

export function gravarVisita(sessao, visita) {
  const chave = chaveDe(sessao)
  if (!chave) return
  try { sessionStorage.setItem(chave, JSON.stringify(visitaDe(visita))) }
  catch { /* Sem storage: a montagem continua nesta carga. */ }
}

export function esquecerVisitas() {
  try {
    for (let i = sessionStorage.length - 1; i >= 0; i--) {
      const chave = sessionStorage.key(i)
      if (chave?.startsWith(PREFIXO + ':')) sessionStorage.removeItem(chave)
    }
  } catch { /* Nada a limpar. */ }
}
