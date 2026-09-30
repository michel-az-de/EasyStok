// Criadores de ação da Frente 4 · Cobrança (rodada 5, seção 4).
// `AtendimentoProvider.jsx` chama `criarAcoesCobranca(despachar)` e espalha
// o resultado no objeto `acoes` do contexto; a F4 só mexe aqui dentro, nunca
// no Provider. `despachar` já é o `dispatch` do reducer.
//
// `gerarCobranca`/`reenviarCobranca` (Pix desde a rodada 2) continuam no
// Provider: só ganharam um terceiro parâmetro `meio` (rodada 5), porque
// precisam de `agoraRef`/`estadoRef`, que só existem lá dentro. As ações
// novas desta rodada não precisam de relógio nenhum além do que a própria
// tela já tem em mãos (`agora` chega por prop até `BlocoCobranca`), então
// vivem aqui, como a convenção pede.
import * as acao from '../acoes'
import { emitirCobranca } from '../../infra/provedoresDeCobranca'
import { totalDoPedido } from '../../dominio/pedido'

export function criarAcoesCobranca(despachar) {
  return {
    // Chip de meio marcado (ponta a da integração): fica no pedido.
    escolherMeioPagamento: (id, meio) => despachar({ tipo: acao.ESCOLHER_MEIO_PAGAMENTO, id, meio }),

    // "Recebi"/"Ainda não" da maquininha e do vale (decisão 19, seção 4: a
    // pergunta que hoje mora no "Marcar entregue"). `agora` chega da tela,
    // que já lê o mesmo relógio simulado que o resto do app.
    marcarRecebidoEntrega: (id, recebido, agora) => despachar({
      tipo: acao.MARCAR_RECEBIDO_ENTREGA, id, recebido, agora,
    }),

    // "Refazer cobrança" da maquininha/vale (ver nota de sem-âncora em
    // `aplicacao/casos/cobranca.js`): reemite a MESMA cobrança combinada, sem
    // link e sem mensagem ao cliente.
    refazerCobranca: (id, pedido, cardapio, agora) => {
      const meio = pedido.cobranca?.meio ?? 'maquininha'
      despachar({
        tipo: acao.REFAZER_COBRANCA, id, agora,
        emissao: { ...emitirCobranca(meio, { numeroPedido: pedido.numero, valor: totalDoPedido(pedido, cardapio) }), meio },
      })
    },
  }
}
