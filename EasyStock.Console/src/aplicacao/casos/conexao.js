import { pedidosAbertosParaPapel } from '../../dominio/loteDePapel'
import * as acao from '../acoes'

// Queda e volta da conexão (`estado.conexao`). O modo API também despacha estes casos (`cicloDeSincronizacao.js`).

export const casosConexao = {
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
}
