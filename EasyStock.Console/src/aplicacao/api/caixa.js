import { observacaoDoFechamento } from '../../dominio/aberturaDaLoja'
import { criarChaveDeEnvio } from '../chaveDeEnvio'
import {
  abrirCaixa, estornarMovimento, fecharCaixa, registrarMovimento,
} from '../../infra/api/caixaApi'

// Caixa no modo API (#1443). Mesma assinatura das ações locais (`acoes/caixa.js`), mas devolvem a
// promessa da API: a aba Caixa mostra o erro junto do botão e relê o dia depois de cada ação. Nada
// vai para o reducer: o caixa de verdade é o que a API calcula (`useCaixaDoDiaApi`).
export function criarAcoesCaixaApi({ gerarChave } = {}) {
  // Sangria repetida depois de "sem conexão" não pode virar duas saídas.
  const envio = criarChaveDeEnvio(gerarChave)

  return {
    abrirCaixa: (_agora, saldoInicial, observacoes) => abrirCaixa({ saldoInicial, observacoes }),

    lancarMovimentoCaixa: async (_agora, {
      tipoMovimento, categoria, valor, meio, descricao,
    }) => {
      const movimento = {
        tipo: tipoMovimento, valor, categoria: (categoria ?? '').trim(), metodo: meio, descricao: (descricao ?? '').trim(),
      }
      const chave = envio.para(movimento)
      const resposta = await registrarMovimento(movimento, chave)
      envio.confirmar(chave)
      return resposta
    },

    estornarMovimentoCaixa: (_agora, movimentoId, motivo) => estornarMovimento(movimentoId, (motivo ?? '').trim()),

    // #2113: o contado vai em `valorContado` (a API grava desde a #2084) e continua nas observações, com a diferença.
    fecharCaixa: (_agora, contado, saldoEsperado, nota) =>
      fecharCaixa({ observacoes: observacaoDoFechamento({ contado, saldoEsperado, nota }), valorContado: contado }),
  }
}
