// Casos de reducer da Frente Janelas de entrega (rodada 13, issue #42,
// registro 104). Mexe só em `estado.catalogo.janelas` e
// `estado.catalogo.respiroMinutos`, nunca em `estado.conversas` (quem marca
// janela NO PEDIDO é a base de `reducer.js`, ESCOLHER_JANELA/TROCAR_JANELA/
// FORCAR_ENCAIXE, de antes desta rodada). Ações previstas em
// `aplicacao/acoes.js`: CRIAR_JANELA, EDITAR_JANELA, PAUSAR_JANELA,
// REATIVAR_JANELA, EXCLUIR_JANELA, AJUSTAR_RESPIRO_MINIMO.
import * as acao from '../acoes'
import {
  ajustarRespiroMinimo, criarJanela, editarJanela, erroDaJanela, janelaPorId,
  pausarJanela, podeExcluirJanela, reativarJanela,
} from '../../dominio/entrega'

const substituirJanela = (janelas, id, transformar) =>
  janelas.map((j) => (j.id === id ? transformar(j) : j))

export const casosJanelas = {
  // Validação mora no domínio (`erroDaJanela`): a ação só confia nela para
  // nunca gravar campo fora do formato que o resto do app espera ler.
  [acao.CRIAR_JANELA]: (estado, { id, campos }) => {
    if (erroDaJanela(campos)) return estado
    return {
      ...estado,
      catalogo: { ...estado.catalogo, janelas: [...estado.catalogo.janelas, criarJanela(id, campos)] },
    }
  },

  [acao.EDITAR_JANELA]: (estado, { id, campos }) => {
    if (erroDaJanela(campos)) return estado
    const janela = janelaPorId(estado.catalogo.janelas, id)
    if (!janela) return estado
    return {
      ...estado,
      catalogo: {
        ...estado.catalogo,
        janelas: substituirJanela(estado.catalogo.janelas, id, (j) => editarJanela(j, campos)),
      },
    }
  },

  // Pausar/reativar nunca mexe em pedido (Aceite: "janela com pedidos não se
  // apaga, só pausa"): quem já marcou continua vendo a própria janela
  // (`ocupacaoDeHoje` mantém o `atual` visível mesmo pausado).
  [acao.PAUSAR_JANELA]: (estado, { id }) => {
    const janela = janelaPorId(estado.catalogo.janelas, id)
    if (!janela) return estado
    return {
      ...estado,
      catalogo: {
        ...estado.catalogo,
        janelas: substituirJanela(estado.catalogo.janelas, id, pausarJanela),
      },
    }
  },

  [acao.REATIVAR_JANELA]: (estado, { id }) => {
    const janela = janelaPorId(estado.catalogo.janelas, id)
    if (!janela) return estado
    return {
      ...estado,
      catalogo: {
        ...estado.catalogo,
        janelas: substituirJanela(estado.catalogo.janelas, id, reativarJanela),
      },
    }
  },

  // Só sai da lista quem nunca teve pedido (`podeExcluirJanela`, conta os
  // pedidos reais de `estado.conversas`): o mesmo motivo que a tela usa para
  // desabilitar o botão vale aqui de novo, porque o reducer nunca confia só
  // na tela.
  [acao.EXCLUIR_JANELA]: (estado, { id }) => {
    const janela = janelaPorId(estado.catalogo.janelas, id)
    if (!janela || !podeExcluirJanela(janela, estado.conversas)) return estado
    return {
      ...estado,
      catalogo: {
        ...estado.catalogo,
        janelas: estado.catalogo.janelas.filter((j) => j.id !== id),
      },
    }
  },

  // RN-22 é regra escrita, não decisão dela: `ajustarRespiroMinimo` trava o
  // piso em 40 min mesmo que a tela mande um número menor.
  [acao.AJUSTAR_RESPIRO_MINIMO]: (estado, { minutos }) => ({
    ...estado,
    catalogo: { ...estado.catalogo, respiroMinutos: ajustarRespiroMinimo(minutos) },
  }),
}
