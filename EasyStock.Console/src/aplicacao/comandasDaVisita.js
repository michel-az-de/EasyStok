import { SINCRONIZAR_CONVERSAS } from './acoes'

export const comandasLocaisDaVisita = conversas => Object.fromEntries(conversas
  .filter(c => c.pedido && !c.pedido.pedidoId && c.pedido.itens?.length)
  .map(c => [c.id, c.pedido]))

// A API fornece a conversa atual. Apenas a comanda local volta como entrada do merge já existente.
export function restaurarComandasDaVisita(estado, evento, comandas) {
  if (evento.tipo !== SINCRONIZAR_CONVERSAS || !comandas) return estado
  const presentes = new Set(estado.conversas.map(c => c.id))
  const restauradas = evento.conversas.filter(c => !presentes.has(c.id) && Object.hasOwn(comandas, c.id))
    .map(c => ({ ...c, pedido: comandas[c.id] }))
  return restauradas.length ? { ...estado, conversas: [...estado.conversas, ...restauradas] } : estado
}
