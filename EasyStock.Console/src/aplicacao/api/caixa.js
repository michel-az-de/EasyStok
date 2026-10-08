import { observacaoDoFechamento } from '../../dominio/aberturaDaLoja'
import {
  abrirCaixa, estornarMovimento, fecharCaixa, registrarMovimento,
} from '../../infra/api/caixaApi'

// Caixa no modo API (#1443). Mesma assinatura das ações locais (`acoes/caixa.js`), mas devolvem a
// promessa da API: a aba Caixa mostra o erro junto do botão e relê o dia depois de cada ação. Nada
// vai para o reducer: o caixa de verdade é o que a API calcula (`useCaixaDoDiaApi`).
export function criarAcoesCaixaApi() {
  return {
    abrirCaixa: (_agora, saldoInicial, observacoes) => abrirCaixa({ saldoInicial, observacoes }),

    lancarMovimentoCaixa: (_agora, {
      tipoMovimento, categoria, valor, meio, descricao,
    }) => registrarMovimento({
      tipo: tipoMovimento, valor, categoria: (categoria ?? '').trim(), metodo: meio, descricao: (descricao ?? '').trim(),
    }),

    estornarMovimentoCaixa: (_agora, movimentoId, motivo) => estornarMovimento(movimentoId, (motivo ?? '').trim()),

    // A API não guarda o contado: ele vai nas observações junto da diferença.
    fecharCaixa: (_agora, contado, saldoEsperado, nota) =>
      fecharCaixa({ observacoes: observacaoDoFechamento({ contado, saldoEsperado, nota }) }),
  }
}
