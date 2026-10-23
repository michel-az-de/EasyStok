import { chamarApi } from './cliente'
import { lerSessao } from './sessao'

// Baixa à mão do pedido (`POST /api/pedidos/{id}/pagamentos`): o "Marcar pago" do Balcão e, desde a
// J2 (#1990), o "Recebi R$ X" das Entregas. Um lugar só para a rota e o corpo.
// Com a `chave` (Idempotency-Key), repetir logo depois de uma queda não lança duas vezes.
export const registrarPagamentoManual = (pedidoId, valor, metodo, chave = null) =>
  chamarApi(`/api/pedidos/${pedidoId}/pagamentos`, {
    metodo: 'POST', chave, corpo: { empresaId: lerSessao()?.empresa?.id, pedidoId, valor, metodo },
  })
