// Criadores de ação da Frente Caixa (rodada 13, issue #44).
// `AtendimentoProvider.jsx` chama `criarAcoesCaixa(despachar)` e espalha o
// resultado no objeto `acoes` do contexto; esta frente só mexe aqui dentro,
// nunca no Provider. `agora` chega de quem chama (mesmo padrão de
// `criarAcoesEncerramento`/`encerrarComResumo`): este arquivo não tem acesso
// ao relógio do Provider, e os casos do reducer derivam os ids dos
// lançamentos do próprio `agora`, sem precisar de um gerador de id à parte.
import * as acao from '../acoes'

export function criarAcoesCaixa(despachar) {
  return {
    abrirCaixa: (agora, saldoInicial) => despachar({ tipo: acao.ABRIR_CAIXA, agora, saldoInicial }),

    lancarMovimentoCaixa: (agora, {
      tipoMovimento, categoria, valor, meio, descricao,
    }) => despachar({
      tipo: acao.LANCAR_MOVIMENTO_CAIXA, agora, tipoMovimento, categoria, valor, meio, descricao,
    }),

    estornarMovimentoCaixa: (agora, movimentoId, motivo) => despachar({
      tipo: acao.ESTORNAR_MOVIMENTO_CAIXA, agora, movimentoId, motivo,
    }),

    fecharCaixa: (agora, contado) => despachar({ tipo: acao.FECHAR_CAIXA, agora, contado }),

    // #1443: o gesto da loja só é pedido no modo API; fechar o modal é só da tela.
    fecharGestoLoja: () => despachar({ tipo: acao.FECHAR_GESTO_LOJA }),

    // UC-11: itens = [{ sku, qtd }].
    lancarVendaAvulsa: (agora, { nome, itens, meio }) => despachar({
      tipo: acao.LANCAR_VENDA_AVULSA, agora, nome, itens, meio,
    }),
  }
}
