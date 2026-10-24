// Casos de reducer da frente Lote de papel (rodada 8, US-042, D6, UC-04 E1).
// Arquivo próprio, no mesmo molde de aplicacao/casos/<tema>.js: só mexe em
// reducer.js na linha de import e na linha que espalha `casosLote`.
import * as acao from '../acoes'
import { construirLancamentos } from '../../dominio/loteDePapel'

const mapear = (estado, id, transformar) => ({
  ...estado,
  conversas: estado.conversas.map((c) => (c.id === id ? transformar(c) : c)),
})

// Mesma forma de `comMensagem` do reducer.js, duplicada aqui pelo mesmo
// motivo já registrado em `casos/simulacao.js`: nenhuma frente exporta função
// de dentro do reducer, e reducer.js nunca importa de um `casos/*.js`.
function comMensagem(conversa, mensagem, { id, agora }) {
  return {
    ...conversa,
    atrasada: false,
    ultimaEm: new Date(agora).toISOString(),
    mensagens: [...conversa.mensagens, { id, em: new Date(agora).toISOString(), ...mensagem }],
  }
}

export const casosLote = {
  // Aplica cada lançamento já decidido por dominio/loteDePapel.js (quem, para
  // qual passo, com qual texto) e fecha a foto: depois de lançado, o aviso de
  // conexão não tem mais pendência para mostrar.
  [acao.LANCAR_LOTE_PAPEL]: (estado, { selecoes, agora, loteId }) => {
    const lancamentos = construirLancamentos(
      estado.conversas, estado.conexao.pedidosAbertos, selecoes, estado.catalogo.janelas, agora,
    )
    const comLancamentos = lancamentos.reduce((acc, lanc, indice) => mapear(acc, lanc.conversaId, (c) => ({
      ...comMensagem(c, {
        dir: 'out', status: 'lida', automatica: true, regra: 'lote-papel', texto: lanc.mensagem,
      }, { id: `${loteId}-${indice}`, agora }),
      pedido: {
        ...c.pedido,
        estado: lanc.para,
        agradecimentoEnviado: lanc.para === 'entregue' ? true : c.pedido.agradecimentoEnviado,
      },
    })), estado)
    return { ...comLancamentos, conexao: { ...comLancamentos.conexao, pedidosAbertos: [], offlineDesde: null } }
  },
}
