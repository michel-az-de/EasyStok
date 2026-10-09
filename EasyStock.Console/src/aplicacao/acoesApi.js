import * as acao from './acoes'
import {
  assumir, encerrar, enviarTexto, gerarLinkCardapio, liberarAutomatico, listarNaoEntregues, marcarLida, reenviarMensagem,
} from '../infra/api/conversasApi'
import { mensagemDaApi, naoEntregueDaApi } from '../infra/api/traducaoConversas'
import { proximoId } from '../infra/repositorioConversas'
import { avisoDeVariaveisNaoResolvidas } from '../dominio/respostas'
import { deveCarregarDossie } from './planoDeSincronizacao'
import { criarAcoesExpedienteApi } from './api/expediente'
import { criarAcoesCaixaApi } from './api/caixa'
import { criarAcoesLembretesApi } from './api/lembretes'
import { criarAcoesConfiguracaoApi } from './api/configuracao'
import { criarAcoesAssistenteApi } from './api/assistente'
import { criarAcoesAgenteApi } from './api/agente'
import { criarAcoesConsentimentosApi } from './api/consentimentos'
import { criarAcoesComandaApi } from './api/comanda'
import { criarAcoesClienteApi } from './api/cliente'
import { criarAcoesEncerramentoEMidiaApi } from './api/encerramentoEMidia'
import { criarAcoesRespostasApi } from './api/respostas'
import { criarAcoesTagsApi } from './api/tags'
import { criarAcoesLoteApi } from './api/lote'
import { criarAcoesCardapioApi } from './api/cardapio'
import { criarAcoesMensagensProgramadasApi } from './api/mensagensProgramadas'
import { criarAvisosNaoLigadas, envioNaoLigado, textoNaoLigado } from './api/naoLigadas'

const FORA_DA_JANELA = 'fora_da_janela_24h'

const CANAL_FALHOU = 'CANAL_FALHOU'

const motivoDaRecusa = (erro) => {
  if (erro.codigo === FORA_DA_JANELA) return 'Não enviada: passou a janela de 24 h. Só modelo aprovado sai até o cliente responder.'
  if (erro.codigo === CANAL_FALHOU && erro.detalhe) return `Não enviada: ${erro.detalhe}`
  return `Não enviada: ${erro.message}`
}

// #1396: falha do canal devolve a mensagem que o EasyStok gravou como falhou; o balão local troca
// para ela (id do servidor, que o Reenviar usa). Sem ela, o balão fica sem Reenviar.
const falhaDoEnvio = (id, mensagemId, erro) => ({
  tipo: acao.FALHAR_ENVIO_API,
  id,
  mensagemId,
  erro: motivoDaRecusa(erro),
  ...(erro.dados?.id ? { mensagem: mensagemDaApi(erro.dados, id) } : { semIdServidor: true }),
})

// Modo API (F01, F02, F03, F06, #1276, #1420, #1424): as ações que a caixa de entrada, o expediente, a
// configuração, o assistente, a sugestão do agente, os avisos da Ficha, a comanda (pedido e cobrança), o cadastro do
// cliente, o encerramento, a foto e a mensagem programada já ligam passam a valer no EasyStok.
// O despacho local vem antes, para a tela responder na hora; a próxima sincronização
// traz o estado do servidor. As ações ainda não ligadas (lista única em
// `api/naoLigadas.js`) não mexem na memória do navegador: só avisam na faixa.
export function comApi(acoes, { despachar, agoraRef, estadoRef }) {
  const avisar = (erro) => despachar({ tipo: acao.AVISO_API, mensagem: erro.message })
  const clienteApi = criarAcoesClienteApi({ despachar, estadoRef })

  return {
    ...acoes,
    ...criarAvisosNaoLigadas(despachar),
    ...criarAcoesEncerramentoEMidiaApi({ despachar, agoraRef, estadoRef, falhaDoEnvio }),
    // #1474: sem `fixo`, fecha só o aviso de ação; o persistente (configuração) só com `fixo: true`.
    fecharAvisoApi: ({ fixo = false } = {}) => despachar({ tipo: acao.FECHAR_AVISO_API, fixo }),
    ...criarAcoesExpedienteApi({ despachar, estadoRef }),
    ...criarAcoesCaixaApi(),
    ...criarAcoesLembretesApi({ despachar }),
    ...criarAcoesConfiguracaoApi(),
    ...criarAcoesAssistenteApi(),
    ...criarAcoesAgenteApi({ despachar }),
    ...criarAcoesConsentimentosApi(),
    ...criarAcoesComandaApi(acoes, { despachar, estadoRef }),
    ...clienteApi,
    // #1441: respostas prontas, automáticas e tags do cliente.
    ...criarAcoesRespostasApi({ despachar, estadoRef }),
    ...criarAcoesTagsApi({ despachar, estadoRef }),
    // #1241: lote de papel no EasyStok.
    ...criarAcoesLoteApi({ despachar, estadoRef }),
    // #1241: cardápio do dia (disponibilidade e saldo) no EasyStok.
    ...criarAcoesCardapioApi({ despachar, estadoRef }),
    ...criarAcoesMensagensProgramadasApi({ estadoRef }),
    enviar: (id, texto, opcoes = {}) => {
      // #1287: texto vazio a API recusa (400); modelo e automática ainda não têm endpoint.
      if (!texto?.trim()) return
      const naoLigado = envioNaoLigado(opcoes)
      if (naoLigado) {
        despachar({ tipo: acao.AVISO_API, mensagem: textoNaoLigado(naoLigado) })
        return
      }
      // #1474: defesa em profundidade do composer. Variável sem valor não sai literal ao cliente.
      const variavelSobrando = avisoDeVariaveisNaoResolvidas(texto)
      if (variavelSobrando) {
        despachar({ tipo: acao.AVISO_API, mensagem: variavelSobrando })
        return
      }
      const mensagemId = proximoId('msg')
      despachar({ tipo: acao.ENVIAR_MENSAGEM, id, texto, mensagemId, agora: agoraRef.current })
      enviarTexto(id, texto)
        .then((m) => despachar({ tipo: acao.CONFIRMAR_ENVIO_API, id, mensagemId, mensagem: mensagemDaApi(m) }))
        .catch((erro) => despachar(falhaDoEnvio(id, mensagemId, erro)))
    },
    // S57: só no modo API (a massa local não tem envio de verdade). A resposta substitui o balão.
    reenviar: (id, mensagemId) =>
      reenviarMensagem(id, mensagemId)
        .then((m) => despachar({ tipo: acao.CONFIRMAR_ENVIO_API, id, mensagemId, mensagem: mensagemDaApi(m, id) }))
        // #1396: o erro do reenvio fica no próprio balão, que segue com o id do servidor.
        .catch((erro) => despachar({ tipo: acao.FALHAR_ENVIO_API, id, mensagemId, erro: motivoDaRecusa(erro) })),
    // #1353: a tela do link no console não tem dado no modo API; o link é o da loja.
    obterLinkCardapio: (id) => gerarLinkCardapio(id)
      .then((link) => ({ url: link.url, daLoja: true }))
      .catch((erro) => {
        avisar(erro)
        return null
      }),
    // S59: painel "Não entregues", direto da API (não passa pelo reducer).
    listarNaoEntregues: () => listarNaoEntregues().then((linhas) => (linhas ?? []).map(naoEntregueDaApi)),
    selecionar: (id) => {
      despachar({ tipo: acao.SELECIONAR_CONVERSA, id })
      marcarLida(id).catch(() => {})
      // Conversa com cliente e Ficha ainda não lida do EasyStok: o dossiê preenche (#1276).
      const c = estadoRef.current.conversas.find((x) => x.id === id)
      if (deveCarregarDossie(c)) clienteApi.carregarClienteDaConversa(id)
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
