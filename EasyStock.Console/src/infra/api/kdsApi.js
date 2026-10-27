import { DOCUMENTO_COZINHA, caminhoDoImpresso } from '../../dominio/impressos'
import { chamarApi } from './cliente'

// KDS do console (S19, `api/kds`) e reimpressão do canhoto (S20). O canhoto em si é o
// PDF de 80 mm do protótipo, montado no console (issue #1446).
// Mudança de status passa pela máquina de estados da API; transição inválida
// volta 400 com a mensagem pronta para a tela.
// `data` (YYYY-MM-DD, #1474): sem ela a API devolve a fila de hoje.
export const listarPedidosKds = (data = null) =>
  chamarApi(data ? `/api/kds/pedidos?data=${encodeURIComponent(data)}` : '/api/kds/pedidos')

// #1776 (item 19): "entregue" com saldo a receber volta 409 SALDO_A_RECEBER até `confirmarSaldoAReceber`.
export const mudarStatusKds = (id, status, { confirmarSaldoAReceber = false } = {}) =>
  chamarApi(`/api/kds/pedidos/${id}/status`, {
    metodo: 'PATCH', corpo: confirmarSaldoAReceber ? { status, confirmarSaldoAReceber } : { status },
    // #2005 (estoque-10): o toque pendente sai ao fechar ou esconder a aba (`enviarPendentesAoSair`).
    keepalive: true,
  })

export const reimprimirCanhoto = (id) => chamarApi(`/api/pedidos/${id}/reimprimir`, { metodo: 'POST' })

// Fila de impressão (S20) consumida pela aba da Cozinha (issue #1446): o pedido pago entra
// pendente; a aba imprime o canhoto e confirma. Confirmar é idempotente na API.
export const listarImpressoesPendentes = () => chamarApi('/api/impressao/pendentes?limite=20')

export const confirmarImpressao = (id) => chamarApi(`/api/impressao/${id}/impressa`, { metodo: 'POST' })

// #2096: a impressão automática não conseguiu imprimir; a dona reimprime pelo console.
export const registrarFalhaImpressao = (id, erro) =>
  chamarApi(`/api/impressao/${id}/falhou`, { metodo: 'POST', corpo: { erro: erro ? String(erro).slice(0, 500) : null } })

// #2096: o canhoto de cozinha em HTML no papel escolhido para este aparelho (a rota `canhoto?formato=html`
// da S20 só sai em 80 mm). Mesma credencial da fila.
export const obterCanhotoParaImprimir = (pedidoId, { modelo, logoTermica = false } = {}, sinal) =>
  chamarApi(caminhoDoImpresso(pedidoId, { documento: DOCUMENTO_COZINHA, modelo, logoTermica }), { texto: true, sinal, prazoComCancelamento: true })
