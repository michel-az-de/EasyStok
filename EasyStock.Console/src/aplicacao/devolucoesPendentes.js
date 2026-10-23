// J2 (#1990), item 5: quanto há a devolver de pedidos cancelados, para o fechamento do caixa avisar.
// A API não tem uma soma pronta: o `aDevolver` é por pedido (GET de estornos manuais, #1967). Só se
// consulta o cancelado que recebeu alguma coisa; o resto não tem o que devolver.
// Portas: `listarCancelados()` devolve os pedidos cancelados recentes (`id`, `totalPago`) e
// `aDevolverDoPedido(id)` o quanto falta devolver daquele pedido.
export async function somarDevolucoesPendentes({ listarCancelados, aDevolverDoPedido }) {
  const comRecebimento = ((await listarCancelados()) ?? []).filter((p) => Number(p.totalPago) > 0)
  const valores = await Promise.all(comRecebimento.map((p) => aDevolverDoPedido(p.id)))
  const pendentes = valores.map((v) => Number(v) || 0).filter((v) => v > 0)
  return {
    total: Math.round(pendentes.reduce((soma, v) => soma + v, 0) * 100) / 100,
    pedidos: pendentes.length,
  }
}
