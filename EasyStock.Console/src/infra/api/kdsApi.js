import { chamarApi } from './cliente'

// KDS do console (S19, `api/kds`) e reimpressão do canhoto (S20). O canhoto em si é o
// PDF de 80 mm do protótipo, montado no console (issue #1446).
// Mudança de status passa pela máquina de estados da API; transição inválida
// volta 400 com a mensagem pronta para a tela.
// `data` (YYYY-MM-DD, #1474): sem ela a API devolve a fila de hoje.
export const listarPedidosKds = (data = null) =>
  chamarApi(data ? `/api/kds/pedidos?data=${encodeURIComponent(data)}` : '/api/kds/pedidos')

export const mudarStatusKds = (id, status) =>
  chamarApi(`/api/kds/pedidos/${id}/status`, { metodo: 'PATCH', corpo: { status } })

export const reimprimirCanhoto = (id) => chamarApi(`/api/pedidos/${id}/reimprimir`, { metodo: 'POST' })

// Fila de impressão (S20) consumida pela aba da Cozinha (issue #1446): o pedido pago entra
// pendente; a aba imprime o canhoto e confirma. Confirmar é idempotente na API.
export const listarImpressoesPendentes = () => chamarApi('/api/impressao/pendentes?limite=20')

export const confirmarImpressao = (id) => chamarApi(`/api/impressao/${id}/impressa`, { metodo: 'POST' })
