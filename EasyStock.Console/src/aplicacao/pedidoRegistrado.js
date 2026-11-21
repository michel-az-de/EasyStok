// #2944 (E1-19): "Registrar" na janela do pedido avisa quem mostra pedidos. A lista de Pedidos recarrega e destaca o novo;
// a Ficha e o Atendimento releem as conversas pelo mesmo pedido de sincronização que a fila já escuta.
export const EVENTO_PEDIDO_REGISTRADO = 'easystok:pedido-registrado'

export function criarCanalDePedidoRegistrado(alvo) {
  return {
    anunciar(pedido) {
      try {
        if (pedido?.pedidoId) alvo?.dispatchEvent(new CustomEvent(EVENTO_PEDIDO_REGISTRADO, { detail: { pedidoId: pedido.pedidoId } }))
        alvo?.dispatchEvent(new Event('easystok:sincronizar-agora'))
      } catch { /* sem janela */ }
    },
    inscrever(aoRegistrar) {
      const ouvir = (evento) => aoRegistrar(evento.detail?.pedidoId ?? null)
      alvo?.addEventListener(EVENTO_PEDIDO_REGISTRADO, ouvir)
      return () => alvo?.removeEventListener(EVENTO_PEDIDO_REGISTRADO, ouvir)
    },
  }
}

const canal = criarCanalDePedidoRegistrado(globalThis.window)
export const anunciarPedidoRegistrado = (pedido) => canal.anunciar(pedido)
export const inscreverPedidoRegistrado = (aoRegistrar) => canal.inscrever(aoRegistrar)
