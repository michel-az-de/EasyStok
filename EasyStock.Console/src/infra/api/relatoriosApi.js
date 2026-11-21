import { chamarApi } from './cliente'
import { caminhoPedidosPorAnuncio } from '../../dominio/pedidosPorAnuncio'

// #2928 (S5): linhas { plataforma, campanha, totalPedidos, valorTotal, ticketMedio }; a última é "Sem origem de anúncio".
export async function obterPedidosPorAnuncio(periodo) {
  const linhas = await chamarApi(caminhoPedidosPorAnuncio(periodo))
  if (!Array.isArray(linhas)) throw new Error('O relatório por anúncio não foi recebido por completo.')
  return linhas
}
