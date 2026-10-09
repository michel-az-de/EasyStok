// Casos de reducer da frente Lote de papel (rodada 8, US-042, D6, UC-04 E1).
// Arquivo próprio, no mesmo molde de aplicacao/casos/<tema>.js: só mexe em
// reducer.js na linha de import e na linha que espalha `casosLote`.
import * as acao from '../acoes'
import { construirLancamentos, pedidosAbertosParaPapel } from '../../dominio/loteDePapel'

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
  // UC-04 E1: a partir daqui a tela para de fingir que manda mensagem — só
  // registra o instante e a foto de quem estava aberto (RN-33, "pagos e já
  // impressos"). Guardado contra o cenário do Simular disparar de novo
  // enquanto já está offline, o que reiniciaria a foto e perderia pedidos
  // que só entraram nela na primeira queda.
  [acao.CONEXAO_CAIU]: (estado, { agora }) => {
    if (!estado.conexao.online) return estado
    return {
      ...estado,
      conexao: {
        online: false,
        offlineDesde: new Date(agora).toISOString(),
        pedidosAbertos: pedidosAbertosParaPapel(estado.conversas),
      },
    }
  },

  [acao.CONEXAO_VOLTOU]: (estado, { agora }) => {
    if (estado.conexao.online) return estado
    return { ...estado, conexao: { ...estado.conexao, online: true, voltouEm: new Date(agora).toISOString() } }
  },

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

  // #1241 (modo API): o EasyStok já aplicou os passos e não avisa o cliente retroativo, então
  // nada entra na conversa. O pedido novo chega pela releitura; aqui só fecha a pendência.
  [acao.LOTE_PAPEL_LANCADO_API]: (estado) => ({
    ...estado, conexao: { ...estado.conexao, pedidosAbertos: [], offlineDesde: null },
  }),
}
