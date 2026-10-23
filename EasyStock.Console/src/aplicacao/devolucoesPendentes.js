// J2 (#1990), item 5: quanto há a devolver de pedidos cancelados, para o fechamento do caixa avisar.
// A API não tem uma soma pronta: o `aDevolver` é por pedido (GET de estornos manuais, #1967). Só se
// consulta o cancelado que recebeu alguma coisa; o resto não tem o que devolver.
// Portas: `listarCancelados()` devolve os pedidos cancelados recentes (`id`, `totalPago`) e
// `aDevolverDoPedido(id)` o quanto falta devolver daquele pedido.
// Poucas leituras por vez (não uma rajada a cada abertura do modal) e uma leitura que falha não apaga
// as outras: `falhas` diz quantos pedidos ficaram sem conferir.
export const LEITURAS_POR_VEZ = 4

export async function somarDevolucoesPendentes({ listarCancelados, aDevolverDoPedido }, porVez = LEITURAS_POR_VEZ) {
  const fila = ((await listarCancelados()) ?? []).filter((p) => Number(p.totalPago) > 0).map((p) => p.id)
  const valores = []
  let falhas = 0
  const trabalhar = async () => {
    while (fila.length > 0) {
      const id = fila.shift()
      try { valores.push(Number(await aDevolverDoPedido(id)) || 0) } catch { falhas += 1 }
    }
  }
  await Promise.all(Array.from({ length: Math.max(1, porVez) }, trabalhar))
  const pendentes = valores.filter((v) => v > 0)
  return {
    total: Math.round(pendentes.reduce((soma, v) => soma + v, 0) * 100) / 100,
    pedidos: pendentes.length,
    falhas,
  }
}
