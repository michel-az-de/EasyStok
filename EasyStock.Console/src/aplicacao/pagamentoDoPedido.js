import { FORMA_NA_ENTREGA, trocarFormaPagamento, registrarPagamentoManual } from '../infra/api/comandaApi'
import { MOTIVO_BAIXA_SEM_APROVACAO, baixaCancelaCobrancaOnline, baixaEsperaAprovacao } from '../dominio/cobranca'
import { criarChaveDeEnvio } from './chaveDeEnvio'

// #3076 (E4, L1): a baixa à mão do pedido, a mesma na conversa (`comanda.js`) e no detalhe de Pedidos. Não lê a
// conversa: recebe o `pedidoId` e o pedido no formato da comanda (`statusApi`, `requerAprovacao`, `cobranca`).
// Mantém as regras de antes: uma chave de idempotência por envio (repetir logo depois de uma queda não lança
// duas vezes), recusa sem aprovação, link do Mercado Pago pendente vira "na entrega" antes da baixa, a
// mensagem da API para `pedido_ja_pago` e o aviso de quando a rede não confirmou o registro.
// Devolve `{ ok: true }` ou `{ erro, semConfirmacao, linkCancelado }`; quem chama decide reler e avisar.
export const SEM_CONFIRMACAO_DO_PAGAMENTO = 'Não foi possível confirmar o registro do pagamento. Conferindo o pedido; veja o saldo antes de tentar novamente.'
export const semConfirmacaoDaRede = (erro) => !erro?.status || erro.status >= 500

export function criarRegistroDePagamento({ registrar = registrarPagamentoManual, trocarForma = trocarFormaPagamento, chaves = criarChaveDeEnvio() } = {}) {
  return function registrarPagamentoDoPedido({ pedidoId, pedido, valor, metodo }) {
    if (baixaEsperaAprovacao(pedido)) return Promise.resolve({ erro: MOTIVO_BAIXA_SEM_APROVACAO, aprovacao: true })
    const linkCancelado = baixaCancelaCobrancaOnline(pedido)
    const chave = chaves.para({ pedidoId, valor, metodo })
    return (linkCancelado ? trocarForma(pedidoId, FORMA_NA_ENTREGA) : Promise.resolve())
      .then(() => registrar(pedidoId, valor, metodo, chave))
      .then(
        () => { chaves.confirmar(chave); return { ok: true } },
        (erro) => {
          const semConfirmacao = semConfirmacaoDaRede(erro)
          const mensagem = erro?.codigo === 'pedido_ja_pago' ? erro.message
            : semConfirmacao ? SEM_CONFIRMACAO_DO_PAGAMENTO
              : `Pagamento não registrado: ${erro?.message ?? 'tente de novo.'}`
          return { erro: mensagem, semConfirmacao, linkCancelado }
        },
      )
  }
}
