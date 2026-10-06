// Embedded Signup v4 da Meta com coexistência (#1417): o que o console tira do
// postMessage `WA_EMBEDDED_SIGNUP` que a janela do Facebook manda. Funções puras:
// a origem e os dados chegam por parâmetro.

// O `code` do FB.login vale pouco: a API precisa recebê-lo em até 30 s.
export const PRAZO_CODE_MS = 30000

const EVENTOS_CONCLUSAO = ['FINISH', 'FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING']

// Só a Meta fala com a página: https e host facebook.com ou subdomínio dele.
export function origemDoFacebook(origem) {
  try {
    const url = new URL(origem)
    return url.protocol === 'https:'
      && (url.hostname === 'facebook.com' || url.hostname.endsWith('.facebook.com'))
  } catch {
    return false
  }
}

const comoObjeto = (dados) => {
  if (typeof dados !== 'string') return dados
  try { return JSON.parse(dados) } catch { return null }
}

// Devolve `null` para mensagem que não interessa (outra origem, outro tipo),
// `{ tipo: 'concluido', wabaId, phoneNumberId }` ou `{ tipo: 'cancelado', etapa, erro }`.
export function lerEventoEmbeddedSignup(origem, dadosBrutos) {
  if (!origemDoFacebook(origem)) return null
  const dados = comoObjeto(dadosBrutos)
  if (dados?.type !== 'WA_EMBEDDED_SIGNUP') return null

  const corpo = dados.data ?? {}
  if (EVENTOS_CONCLUSAO.includes(dados.event)) {
    if (!corpo.waba_id || !corpo.phone_number_id) return null
    return { tipo: 'concluido', wabaId: String(corpo.waba_id), phoneNumberId: String(corpo.phone_number_id) }
  }
  if (dados.event === 'CANCEL' || dados.event === 'ERROR') {
    return { tipo: 'cancelado', etapa: corpo.current_step ?? null, erro: corpo.error_message ?? null }
  }
  return null
}

export function textoDoCancelamento({ etapa, erro }) {
  if (erro) return `A Meta interrompeu a conexão: ${erro}`
  if (etapa) return `A conexão foi cancelada na etapa "${etapa}". Abra o fluxo de novo quando quiser.`
  return 'A conexão foi cancelada antes do fim.'
}

// Linha da sincronização pedida à Meta (contatos ou histórico).
export function textoDaSincronizacao(rotulo, sync) {
  if (!sync) return `${rotulo}: não pedida`
  if (sync.solicitada) return `${rotulo}: pedida à Meta (chega em até 24 h)`
  return `${rotulo}: a Meta recusou (${sync.erro ?? 'sem detalhe'}). Dá para refazer em até 24 h.`
}
