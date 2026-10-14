import * as acao from './acoes'
import {
  assumir, encerrar, enviarTexto, gerarLinkCardapio, liberarAutomatico, listarAtendentes, listarNaoEntregues, marcarLida, reabrir, reenviarMensagem,
} from '../infra/api/conversasApi'
import { mensagemDaApi, naoEntregueDaApi, situacaoConversaDaApi } from '../infra/api/traducaoConversas'
import { lerSessao } from '../infra/api/sessao'
import { proximoId } from '../infra/repositorioConversas'
import { avisoDeVariaveisNaoResolvidas } from '../dominio/respostas'
import { FRASE_DA_FALHA, TIPOS_DE_FALHA, textoDaFalhaDeEnvio } from '../dominio/falhaDeEnvio'
import { deveCarregarDossie } from './planoDeSincronizacao'
import { envioBloqueado } from '../dominio/janela'
import { canalDaConversa } from '../dominio/canal'
import { criarAcoesExpedienteApi } from './api/expediente'
import { criarAcoesCaixaApi } from './api/caixa'
import { criarAcoesLembretesApi } from './api/lembretes'
import { criarAcoesConfiguracaoApi } from './api/configuracao'
import { criarAcoesAssistenteApi } from './api/assistente'
import { criarAcoesAgenteApi } from './api/agente'
import { criarAcoesConsentimentosApi } from './api/consentimentos'
import { criarAcoesOcorrenciasApi } from './api/ocorrencias'
import { criarAcoesComandaApi } from './api/comanda'
import { criarAcoesClienteApi } from './api/cliente'
import { criarAcoesEncerramentoEMidiaApi } from './api/encerramentoEMidia'
import { criarAcoesRespostasApi } from './api/respostas'
import { criarAcoesTagsApi } from './api/tags'
import { criarAcoesLoteApi } from './api/lote'
import { criarAcoesCardapioApi } from './api/cardapio'
import { criarAcoesMensagensProgramadasApi } from './api/mensagensProgramadas'
import { criarAcoesModelosApi } from './api/modelos'
import { criarAvisosNaoLigadas, envioNaoLigado, textoNaoLigado } from './api/naoLigadas'
import { criarChavesDeMensagem } from './chavesDeMensagem'

const FORA_DA_JANELA = 'fora_da_janela_24h'

const CANAL_FALHOU = 'CANAL_FALHOU'

// #1786: respostas da proteção contra envio repetido. A mensagem não ficou sem sair: já saiu ou está saindo.
const ENVIO_REPETIDO = {
  IDEMPOTENCY_PAYLOAD_MISMATCH: 'Esta mensagem já tinha saído. Confira na conversa antes de mandar de novo.',
  IDEMPOTENCY_IN_FLIGHT: 'Esta mensagem ainda está saindo. Confira na conversa em instantes.',
}

const motivoDaRecusa = (erro) => {
  // #1761: o detalhe do canal é o erro cru do provedor, em inglês; a frase sai do mapa do domínio.
  if (erro.codigo === FORA_DA_JANELA) return FRASE_DA_FALHA[TIPOS_DE_FALHA.FORA_DA_JANELA]
  if (ENVIO_REPETIDO[erro.codigo]) return ENVIO_REPETIDO[erro.codigo]
  if (erro.codigo === CANAL_FALHOU && erro.detalhe) return textoDaFalhaDeEnvio(erro.detalhe)
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
export function comApi(acoes, { despachar, agoraRef, estadoRef, comItensForaRef = null }) {
  const avisar = (erro) => despachar({ tipo: acao.AVISO_API, mensagem: erro.message })
  // #1787: o reducer recusa o envio sem criar balão; sem esta guarda o texto saía pela API e a falha ficava muda.
  // A mesma regra do reducer (envioBloqueado); conversa que o estado não conhece segue para o EasyStok decidir.
  const envioRecusado = (id) => {
    const { conversas = [], catalogo } = estadoRef.current
    const conversa = conversas.find((c) => c.id === id)
    if (!conversa || !envioBloqueado(conversa, agoraRef.current, canalDaConversa(catalogo?.canais, conversa))) return false
    despachar({ tipo: acao.AVISO_API, mensagem: 'Cadastro bloqueado: a mensagem não saiu. Desbloqueie na ficha para escrever.' })
    return true
  }
  const clienteApi = criarAcoesClienteApi({ despachar, estadoRef })
  const chaves = criarChavesDeMensagem()
  const confirmarResponsavel = (id, chamada) => chamada(id)
    .then((resumo) => { despachar({ tipo: acao.CONFIRMAR_RESPONSAVEL_API, id, situacao: situacaoConversaDaApi(resumo, lerSessao()?.usuario) }); return true })
    .catch((erro) => { avisar(erro); return false })

  return {
    ...acoes,
    listarAtendentes,
    ...criarAvisosNaoLigadas(despachar),
    ...criarAcoesEncerramentoEMidiaApi({ despachar, agoraRef, estadoRef, falhaDoEnvio, envioRecusado, chaves }),
    // #1474: sem `fixo`, fecha só o aviso de ação; o persistente (configuração) só com `fixo: true`.
    fecharAvisoApi: ({ fixo = false } = {}) => despachar({ tipo: acao.FECHAR_AVISO_API, fixo }),
    ...criarAcoesExpedienteApi({ despachar, estadoRef }),
    ...criarAcoesCaixaApi(),
    ...criarAcoesLembretesApi({ despachar }),
    ...criarAcoesConfiguracaoApi(),
    ...criarAcoesAssistenteApi(),
    ...criarAcoesAgenteApi({ despachar }),
    ...criarAcoesConsentimentosApi(),
    ...criarAcoesOcorrenciasApi(),
    ...criarAcoesComandaApi(acoes, { despachar, estadoRef }),
    ...clienteApi,
    // #1441: respostas prontas, automáticas e tags do cliente.
    ...criarAcoesRespostasApi({ despachar, estadoRef }),
    ...criarAcoesTagsApi({ despachar, estadoRef }),
    // #1241: lote de papel no EasyStok.
    ...criarAcoesLoteApi({ despachar, estadoRef }),
    // #1241: cardápio do dia (disponibilidade e saldo) no EasyStok.
    ...criarAcoesCardapioApi({ despachar, estadoRef, comItensForaRef }),
    ...criarAcoesMensagensProgramadasApi({ estadoRef }),
    ...criarAcoesModelosApi({ despachar }),
    enviar: (id, texto, opcoes = {}) => {
      // #1287: texto vazio a API recusa (400); modelo e automática ainda não têm endpoint.
      if (!texto?.trim()) return false
      if (opcoes.modelo) {
        despachar({ tipo: acao.AVISO_API, mensagem: 'Escolha o modelo e preencha os parâmetros em Modelos aprovados.' })
        return false
      }
      const naoLigado = envioNaoLigado(opcoes)
      if (naoLigado) {
        despachar({ tipo: acao.AVISO_API, mensagem: textoNaoLigado(naoLigado) })
        return false
      }
      // #1474: defesa em profundidade do composer. Variável sem valor não sai literal ao cliente.
      const variavelSobrando = avisoDeVariaveisNaoResolvidas(texto)
      if (variavelSobrando) {
        despachar({ tipo: acao.AVISO_API, mensagem: variavelSobrando })
        return false
      }
      if (envioRecusado(id)) return false
      const mensagemId = proximoId('msg')
      despachar({ tipo: acao.ENVIAR_MENSAGEM, id, texto, mensagemId, agora: agoraRef.current })
      return chaves.enviar(id, { texto }, (chave) => enviarTexto(id, texto, { chave }))
        .then((m) => {
          despachar({ tipo: acao.CONFIRMAR_ENVIO_API, id, mensagemId, mensagem: mensagemDaApi(m) })
          return true
        })
        .catch((erro) => {
          despachar(falhaDoEnvio(id, mensagemId, erro))
          return false
        })
    },
    // S57: só no modo API (a massa local não tem envio de verdade). A resposta substitui o balão.
    reenviar: (id, mensagemId) =>
      reenviarMensagem(id, mensagemId)
        .then((m) => {
          const mensagem = mensagemDaApi(m, id)
          despachar({ tipo: acao.CONFIRMAR_ENVIO_API, id, mensagemId, mensagem })
          return mensagem
        })
        // #1396: o erro do reenvio fica no próprio balão, que segue com o id do servidor.
        .catch((erro) => {
          despachar({ tipo: acao.FALHAR_ENVIO_API, id, mensagemId, erro: motivoDaRecusa(erro) })
          return null
        }),
    // #1353: a tela do link no console não tem dado no modo API; o link é o da loja.
    obterLinkCardapio: (id) => gerarLinkCardapio(id)
      .then((link) => ({ url: link.url, numeroComanda: link.numeroComanda, daLoja: true }))
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
    assumirAtendimento: (id) => confirmarResponsavel(id, assumir),
    devolverAutomatico: (id) => confirmarResponsavel(id, liberarAutomatico),
    encerrarAtendimento: (id) => {
      despachar({ tipo: acao.ENCERRAR_ATENDIMENTO, id })
      encerrar(id).catch(avisar)
    },
    // A confirmação vem do servidor. Falha ou resposta perdida mantém a tela até a próxima leitura.
    reabrir: (id) => reabrir(id)
      .then((resumo) => despachar({
        tipo: acao.CONFIRMAR_REABERTURA_API, id,
        situacao: situacaoConversaDaApi(resumo, lerSessao()?.usuario),
      }))
      .catch(avisar),
  }
}
