import * as acao from '../acoes'
import { sugerirResposta } from '../../infra/api/conversasApi'
import { sugestaoDaApi } from '../../infra/api/traducaoConversas'
import { EVENTO_SESSAO_ALTERADA, identidadeDaSessao, lerSessao } from '../../infra/api/sessao'
import { combinarSinais } from '../../infra/api/sinais'

export function identidadeDaSugestao() {
  const sessao = lerSessao()
  return sessao?.empresa?.id && sessao?.usuario?.id ? identidadeDaSessao(sessao) : null
}

let memoriaProativa = null
let sessaoObservada = false
function memoriaDaSugestao() {
  const identidade = identidadeDaSugestao()
  if (!memoriaProativa || memoriaProativa.identidade !== identidade)
    memoriaProativa = { identidade, cache: new Map(), proxima: 0 }
  return memoriaProativa
}

function observarSessao() {
  if (sessaoObservada || !globalThis.window?.addEventListener) return
  sessaoObservada = true
  // Continua valendo sem Composer montado. Renovar token ou gravar outra
  // preferência não limpa o orçamento; sair/trocar empresa ou pessoa limpa.
  window.addEventListener(EVENTO_SESSAO_ALTERADA, memoriaDaSugestao)
  window.addEventListener('storage', memoriaDaSugestao)
}

// #2178: consulta opcional, sem despachar mensagem, pedido ou rascunho.
// Uma tentativa por contexto entre os 12 guardados; resultado vale por 2 min.
// Falha, dispensa e cancelamento não provocam repetição automática nem polling.
export function criarConsultaProativa({ aoMudar, agora = Date.now, agendar = setTimeout, cancelar = clearTimeout }) {
  observarSessao()
  let memoria = memoriaDaSugestao()
  let atual = null, ativa = false, temporizador = null, expiracao = null, controle = null, registroEmVoo = null
  const publicar = (estado = 'ocioso', sugestao = null) => {
    cancelar(expiracao)
    expiracao = null
    aoMudar({ chave: atual?.chave ?? null, estado, sugestao })
    if (sugestao) {
      const contexto = atual
      expiracao = agendar(() => { if (valido(contexto)) publicar() }, Math.max(0, memoria.cache.get(contexto.chave).ate - agora()))
    }
  }
  const interromper = () => {
    cancelar(temporizador)
    temporizador = null
    if (controle) {
      controle.abort()
      if (registroEmVoo?.estado === 'pensando') registroEmVoo.estado = 'ocioso'
      controle = null
      registroEmVoo = null
    }
  }
  const valido = contexto => ativa && atual?.chave === contexto?.chave && contexto?.identidade === identidadeDaSugestao() && memoria === memoriaDaSugestao()
  const obterSugestaoAtual = contexto => {
    const registro = memoria.cache.get(contexto?.chave)
    return valido(contexto) && registro?.estado === 'pronto' && registro.ate > agora() ? registro.sugestao : null
  }
  return {
    obterSugestaoAtual,
    atualizar(contexto, habilitada) {
      const novaMemoria = memoriaDaSugestao()
      if (memoria !== novaMemoria) { interromper(); memoria = novaMemoria; atual = null; ativa = false }
      const pode = Boolean(habilitada && contexto && contexto.identidade === memoria.identidade)
      if (atual?.chave === contexto?.chave && ativa === pode) return
      interromper()
      atual = contexto; ativa = pode
      if (!pode) { publicar(); return }
      const guardada = memoria.cache.get(contexto.chave)
      if (guardada) {
        const sugestao = obterSugestaoAtual(contexto)
        publicar(sugestao ? 'pronto' : 'ocioso', sugestao)
        return
      }
      publicar('aguardando')
      const consultar = async () => {
        temporizador = null
        if (!valido(contexto)) return
        // Outro Composer pode ter consultado/agendado antes desta montagem.
        if (memoria.cache.has(contexto.chave)) { publicar(); return }
        if (memoria.proxima > agora()) { temporizador = agendar(consultar, memoria.proxima - agora()); return }
        const solicitacao = new AbortController()
        controle = solicitacao
        const registro = { estado: 'pensando', sugestao: null, ate: 0 }
        registroEmVoo = registro
        if (memoria.cache.size >= 12) memoria.cache.delete(memoria.cache.keys().next().value)
        memoria.cache.set(contexto.chave, registro)
        memoria.proxima = agora() + 8000
        publicar('pensando')
        try {
          const dados = await sugerirResposta(contexto.conversaId, {
            sinal: combinarSinais([solicitacao.signal, AbortSignal.timeout(15000)]), identidadeEsperada: contexto.identidade,
          })
          if (solicitacao.signal.aborted || controle !== solicitacao || !valido(contexto)) return
          registro.sugestao = dados?.texto?.trim() ? sugestaoDaApi(dados, agora()) : null
          registro.estado = registro.sugestao ? 'pronto' : 'indisponivel'
          registro.ate = agora() + 120000
          publicar(registro.estado, registro.sugestao)
        } catch {
          if (!solicitacao.signal.aborted && controle === solicitacao && valido(contexto)) {
            registro.estado = 'indisponivel'
            publicar('indisponivel')
          }
        } finally { if (controle === solicitacao) { controle = null; registroEmVoo = null } }
      }
      temporizador = agendar(consultar, Math.max(1200, memoria.proxima - agora()))
    },
    dispensar(contexto) {
      if (!valido(contexto)) return
      interromper()
      memoria.cache.set(contexto.chave, { estado: 'dispensada', sugestao: null, ate: 0 })
      publicar()
    },
  }
}

// "Sugerir" do painel do agente no modo API (#1420): o EasyStok monta o contexto da conversa pelo id
// (histórico, transcrições, cadastro, caderno) e chama o mesmo agente do automático, só com consulta.
// Nada sai ao cliente: o texto chega ao painel, e "Usar no rascunho" o põe no campo de escrever.
// A marca "Precisa de você" e a ocorrência do modo simulado não nascem aqui: no modo API quem as
// grava é o EasyStok, e o navegador não finge o que o servidor não fez (F06).
export function criarAcoesAgenteApi({ despachar }) {
  let consultaAtual = 0
  let conversaAtual = null
  const pendentes = new Map()
  return {
    consultarAgente: (id) => {
      if (pendentes.has(id) && conversaAtual === id) return pendentes.get(id).resposta
      const consulta = ++consultaAtual
      conversaAtual = id
      despachar({ tipo: acao.AGENTE_PEDINDO, id })
      const registro = { chamada: pendentes.get(id)?.chamada ?? sugerirResposta(id) }
      registro.resposta = registro.chamada
        .then(dados => {
          if (consulta === consultaAtual) despachar({ tipo: acao.AGENTE_RESPONDEU, id, sugestao: sugestaoDaApi(dados) })
        })
        .catch(erro => {
          if (consulta === consultaAtual) despachar({ tipo: acao.AGENTE_FALHOU, id, erro: erro.message })
        })
        .finally(() => { if (pendentes.get(id) === registro) pendentes.delete(id) })
      pendentes.set(id, registro)
      return registro.resposta
    },
  }
}
