import { pedidoEncerrado } from './pedido'

// O que a Ficha mostra recolhido quando ninguém escolheu (#1697). Medido na tela real, 1440x900,
// pedido aguardando pagamento: a comanda nascia 1,5 tela abaixo e a cobrança 3,3 telas abaixo,
// depois do cadastro, das tags e do histórico. Com pedido em andamento a Ficha é do pedido: o
// que já está resolvido recolhe e mostra o resumo ao lado do título. Cadastro incompleto fica
// aberto, porque é o que falta para cobrar. Sem pedido, a Ficha é a do cliente: tudo aberto.
export function fichaRecolhidaDePadrao({ pedido, cliente }) {
  const emPedido = Boolean(pedido) && !pedidoEncerrado(pedido)
  return {
    contato: emPedido && Boolean(cliente?.telefone && cliente?.endereco),
    tags: emPedido,
    historico: emPedido,
  }
}
