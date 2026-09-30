// Criadores de ação da Frente Janelas de entrega (rodada 13, issue #42,
// registro 104). `AtendimentoProvider.jsx` chama `criarAcoesJanelas(despachar)`
// e espalha o resultado no objeto `acoes` do contexto; esta frente só mexe
// aqui dentro, nunca no Provider.
import * as acao from '../acoes'

const novoId = () => crypto.randomUUID()

export function criarAcoesJanelas(despachar) {
  return {
    criarJanela: (campos) => despachar({ tipo: acao.CRIAR_JANELA, id: novoId(), campos }),
    editarJanela: (id, campos) => despachar({ tipo: acao.EDITAR_JANELA, id, campos }),
    pausarJanela: (id) => despachar({ tipo: acao.PAUSAR_JANELA, id }),
    reativarJanela: (id) => despachar({ tipo: acao.REATIVAR_JANELA, id }),
    excluirJanela: (id) => despachar({ tipo: acao.EXCLUIR_JANELA, id }),
    ajustarRespiroMinimo: (minutos) => despachar({ tipo: acao.AJUSTAR_RESPIRO_MINIMO, minutos }),
  }
}
