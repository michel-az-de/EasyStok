import * as acao from './acoes'
import {
  assumir, encerrar, enviarTexto, liberarAutomatico, marcarLida,
} from '../infra/api/conversasApi'
import { mensagemDaApi } from '../infra/api/traducaoConversas'
import { proximoId } from '../infra/repositorioConversas'
import { criarAcoesExpedienteApi } from './api/expediente'
import { criarAcoesConfiguracaoApi } from './api/configuracao'
import { criarAcoesAssistenteApi } from './api/assistente'
import { criarAcoesConsentimentosApi } from './api/consentimentos'
import { criarAcoesComandaApi } from './api/comanda'
import { criarAcoesEncerramentoEMidiaApi } from './api/encerramentoEMidia'
import { criarAvisosNaoLigadas, envioNaoLigado, textoNaoLigado } from './api/naoLigadas'

const FORA_DA_JANELA = 'fora_da_janela_24h'

const motivoDaRecusa = (erro) => (erro.codigo === FORA_DA_JANELA
  ? 'Não enviada: passou a janela de 24 h. Só modelo aprovado sai até o cliente responder.'
  : `Não enviada: ${erro.message}`)

// Modo API (F01, F02, F03, F06): as ações que a caixa de entrada, o expediente, a configuração,
// o assistente, os avisos da Ficha, a comanda (pedido e cobrança), o encerramento e a foto
// já ligam passam a valer no EasyStok.
// O despacho local vem antes, para a tela responder na hora; a próxima sincronização
// traz o estado do servidor. As ações ainda não ligadas (lista única em
// `api/naoLigadas.js`) não mexem na memória do navegador: só avisam na faixa.
export function comApi(acoes, { despachar, agoraRef, estadoRef }) {
  const avisar = (erro) => despachar({ tipo: acao.AVISO_API, mensagem: erro.message })

  return {
    ...acoes,
    ...criarAvisosNaoLigadas(despachar),
    ...criarAcoesEncerramentoEMidiaApi({ despachar, agoraRef, estadoRef, motivoDaRecusa }),
    fecharAvisoApi: () => despachar({ tipo: acao.FECHAR_AVISO_API }),
    ...criarAcoesExpedienteApi({ despachar, estadoRef }),
    ...criarAcoesConfiguracaoApi(),
    ...criarAcoesAssistenteApi(),
    ...criarAcoesConsentimentosApi(),
    ...criarAcoesComandaApi(acoes, { despachar, estadoRef }),
    enviar: (id, texto, opcoes = {}) => {
      // #1287: texto vazio a API recusa (400); modelo e automática ainda não têm endpoint.
      if (!texto?.trim()) return
      const naoLigado = envioNaoLigado(opcoes)
      if (naoLigado) {
        despachar({ tipo: acao.AVISO_API, mensagem: textoNaoLigado(naoLigado) })
        return
      }
      const mensagemId = proximoId('msg')
      despachar({ tipo: acao.ENVIAR_MENSAGEM, id, texto, mensagemId, agora: agoraRef.current })
      enviarTexto(id, texto)
        .then((m) => despachar({ tipo: acao.CONFIRMAR_ENVIO_API, id, mensagemId, mensagem: mensagemDaApi(m) }))
        .catch((erro) => despachar({ tipo: acao.FALHAR_ENVIO_API, id, mensagemId, erro: motivoDaRecusa(erro) }))
    },
    selecionar: (id) => {
      despachar({ tipo: acao.SELECIONAR_CONVERSA, id })
      marcarLida(id).catch(() => {})
    },
    assumirAtendimento: (id) => {
      despachar({ tipo: acao.ASSUMIR_ATENDIMENTO, id })
      assumir(id).catch(avisar)
    },
    devolverAutomatico: (id) => {
      despachar({ tipo: acao.DEVOLVER_AUTOMATICO, id })
      liberarAutomatico(id).catch(avisar)
    },
    encerrarAtendimento: (id) => {
      despachar({ tipo: acao.ENCERRAR_ATENDIMENTO, id })
      encerrar(id).catch(avisar)
    },
  }
}
