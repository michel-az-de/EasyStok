// O que a polling da inbox relê a cada ciclo (F07, itens 2 e 7). Puro: recebe o
// resumo da API, o que está no cache e a conversa aberta na tela.
//
// Mensagens: a conversa selecionada é relida sempre, porque mudança de status
// (Falhou, Entregue) não mexe em `ultimaMensagemEm` e a dona precisa ver que a
// mensagem não saiu. As outras só quando a conversa mudou.
//
// Pedido: a API ainda não traz o status do pedido no resumo (dependência de
// backend registrada na #1237), então o pedido aberto é relido para a
// selecionada, para a que mudou e, nas outras, só no ciclo lento (1 min).
// Pedido entregue ou cancelado fica no cache até o id mudar.
export const STATUS_FINAIS_DO_PEDIDO = new Set(['entregue', 'cancelado'])

export const deveRelerMensagens = (resumo, guardado, selecionadaId) =>
  !guardado || resumo.id === selecionadaId || guardado.ultima !== resumo.ultimaMensagemEm

export function deveRelerPedido(resumo, guardado, { selecionadaId = null, cicloLento = false } = {}) {
  if (!resumo.pedidoEmAndamentoId) return false
  if (!guardado || guardado.pedidoId !== resumo.pedidoEmAndamentoId) return true
  if (STATUS_FINAIS_DO_PEDIDO.has(guardado.dados?.status)) return false
  return resumo.id === selecionadaId || guardado.ultima !== resumo.ultimaMensagemEm || cicloLento
}
