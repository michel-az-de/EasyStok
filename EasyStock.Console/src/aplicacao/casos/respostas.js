// Casos de reducer da biblioteca de respostas prontas (rodada 7, pedido do
// dono 24/09/2026). Ações previstas em `aplicacao/acoes.js`: INCLUIR_RESPOSTA_PRONTA,
// EDITAR_RESPOSTA_PRONTA, ALTERNAR_ARQUIVAMENTO_RESPOSTA_PRONTA. Mexe só em
// `estado.catalogo.respostasProntas`, mesmo molde de `casos/cardapio.js`.
// Mensagem automática (estado.regras) não entra aqui: aquela já tem
// EDITAR_REGRA/ALTERNAR_REGRA, e é a mesma porta que a biblioteca usa.
import * as acao from '../acoes'
import {
  alternarArquivamentoRespostaPronta, editarRespostaPronta, incluirRespostaPronta,
} from '../../dominio/respostas'

export const casosRespostas = {
  [acao.INCLUIR_RESPOSTA_PRONTA]: (estado, { dados }) => {
    const { lista } = incluirRespostaPronta(estado.catalogo.respostasProntas, dados)
    return { ...estado, catalogo: { ...estado.catalogo, respostasProntas: lista } }
  },

  [acao.EDITAR_RESPOSTA_PRONTA]: (estado, { id, dados }) => ({
    ...estado,
    catalogo: {
      ...estado.catalogo,
      respostasProntas: editarRespostaPronta(estado.catalogo.respostasProntas, id, dados),
    },
  }),

  [acao.ALTERNAR_ARQUIVAMENTO_RESPOSTA_PRONTA]: (estado, { id }) => ({
    ...estado,
    catalogo: {
      ...estado.catalogo,
      respostasProntas: alternarArquivamentoRespostaPronta(estado.catalogo.respostasProntas, id),
    },
  }),
}
