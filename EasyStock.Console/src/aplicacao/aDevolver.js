// J2 (#1990), item 4: o "a devolver" do pedido cancelado é do EasyStok (`aDevolver` do GET de
// estornos manuais, #1967). Só se pergunta por pedido cancelado que recebeu alguma coisa: o resto
// não tem o que devolver e a Ficha não faz a leitura à toa.
export function consultaADevolver(pedido) {
  if (!pedido?.pedidoId || pedido.estado !== 'cancelado') return false
  const recebido = Math.max(Number(pedido.totalPagoApi) || 0, Number(pedido.alocadoApi) || 0)
  return recebido > 0
}
