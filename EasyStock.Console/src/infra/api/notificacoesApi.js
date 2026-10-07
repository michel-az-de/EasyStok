import { chamarApi } from './cliente'
import { lerSessao } from './sessao'

// Sininho no modo API (#1426): notificações do canal InApp (`api/notificacoes`), lembretes da dona
// (S43, `api/atendimento/lembretes`) e a inscrição Web Push do aparelho (`api/pwa/push`).
const NOTIFICACOES = '/api/notificacoes'
const LEMBRETES = '/api/atendimento/lembretes'
const PUSH = '/api/pwa/push'

// O superadmin precisa dizer a empresa; para o resto a API usa a do token e só confere esta.
const daEmpresa = () => {
  const empresaId = lerSessao()?.empresa?.id
  return empresaId ? `empresaId=${encodeURIComponent(empresaId)}` : ''
}

const consulta = (...partes) => {
  const juntas = partes.filter(Boolean).join('&')
  return juntas ? `?${juntas}` : ''
}

// Só as não lidas: lida sai do sininho.
export const listarNotificacoesNaoLidas = (quantas = 20) =>
  chamarApi(NOTIFICACOES + consulta(daEmpresa(), 'lida=false', `pageSize=${quantas}`))

export const marcarNotificacaoLida = (id) =>
  chamarApi(`${NOTIFICACOES}/${encodeURIComponent(id)}/lida${consulta(daEmpresa())}`, { metodo: 'PATCH' })

// Abertos, do usuário e da equipe toda (padrão do S43).
export const listarLembretes = () => chamarApi(LEMBRETES)

// `venceEm` em ISO UTC; sem ele a API faz vencer na hora.
export const criarLembreteNaApi = ({ texto, venceEm = null, conversaId = null }) =>
  chamarApi(LEMBRETES, { metodo: 'POST', corpo: { texto, venceEm, conversaId } })

export const concluirLembreteNaApi = (id) =>
  chamarApi(`${LEMBRETES}/${encodeURIComponent(id)}/concluir`, { metodo: 'POST' })

// { publicKey, subject }. Pública e anônima; 404 quando o servidor não tem VAPID.
export const obterChavePush = () => chamarApi(`${PUSH}/vapid-public`, { autenticado: false })

// Idempotente na API: o mesmo endpoint atualiza as chaves e religa ao usuário atual.
export const inscreverPush = ({ endpoint, p256dh, auth, userAgent = null }) =>
  chamarApi(`${PUSH}/subscribe`, { metodo: 'POST', corpo: { endpoint, p256dh, auth, userAgent } })
