import { desinscreverPushNoNavegador } from '../pushNavegador'

const CHAVE = 'easystok.sessao'
export const EVENTO_SESSAO_ALTERADA = 'easystok:sessao-alterada'
export const EVENTO_FALHA_SAIDA_PUSH = 'easystok:falha-saida-push'
let semStorage = null

const ler = (storage) => {
  try { return JSON.parse(globalThis[storage].getItem(CHAVE)) } catch { return null }
}
const remover = (storage) => {
  try { globalThis[storage].removeItem(CHAVE) } catch { /* armazenamento indisponível */ }
}

export const permitePersistir = (token) => cargaDoToken(token)?.sessaoPersistente === 'true'
// #2201: a sessão aberta pela chave de acesso é do aparelho da pessoa e sobrevive ao Android fechar o app.
const podePersistir = (sessao) => permitePersistir(sessao?.token) || sessao?.dispositivo === true
export const identidadeDaSessao = (sessao) => `${sessao?.empresa?.id ?? '-'}:${sessao?.usuario?.id ?? '-'}`

export function lerSessao() {
  const temporaria = ler('sessionStorage') ?? semStorage
  if (temporaria) return temporaria.expiraEm > Date.now() ? temporaria : null
  const persistida = ler('localStorage')
  // A sessão vencida só serve para renovar. Nenhuma chamada usa seu JWT antigo.
  return persistida?.persistente && persistida.refreshToken && podePersistir(persistida) ? persistida : null
}

export function gravarSessao(sessao) {
  const persistente = Boolean(sessao.persistente && sessao.refreshToken && podePersistir(sessao))
  const atual = { ...sessao, persistente }
  remover('sessionStorage')
  remover('localStorage')
  semStorage = null
  try { globalThis[persistente ? 'localStorage' : 'sessionStorage'].setItem(CHAVE, JSON.stringify(atual)) }
  catch {
    // Sem disco disponível, não prometer permanência entre visitas.
    semStorage = { ...atual, persistente: false }
  }
  window.dispatchEvent(new Event(EVENTO_SESSAO_ALTERADA))
  return lerSessao()
}

// No APK só o proprietário nativo guarda/renova o refresh. O Console recebe JWT curto.
export function gravarSessaoNativa(dados) {
  if (!dados?.autenticado) { limparSessao(); return null }
  const carga = cargaDoToken(dados.token)
  if (carga?.sessaoNativa !== 'true' || !carga.sid || !carga.dispositivoId || !carga.empresaId
    || carga.empresaId !== dados.empresaId || !dados.usuario?.id || carga.sub !== dados.usuario.id
    || carga.dispositivoId !== dados.dispositivoId || dados.refreshToken) {
    throw Object.assign(new Error('A sessão do aplicativo não pôde ser confirmada. Reabra o Atendimento.'), { status: 401, codigo: 'SESSAO_NATIVA_INVALIDA' })
  }
  const venceEm = vencimentoDoToken(dados.token)
  if (!venceEm || venceEm <= Date.now()) throw Object.assign(new Error('Confira a conexão para retomar o Atendimento.'), { codigo: 'SESSAO_NATIVA_VENCIDA' })
  return gravarSessao({ token: dados.token, usuario: dados.usuario, empresa: { id: carga.empresaId, nome: null },
    nativa: true, persistente: false, venceEm, expiraEm: venceEm - 60000 })
}

export function limparSessao() {
  const anterior = ler('sessionStorage') ?? semStorage ?? ler('localStorage')
  remover('sessionStorage')
  remover('localStorage')
  semStorage = null
  window.dispatchEvent(new Event(EVENTO_SESSAO_ALTERADA))
  if (anterior) desinscreverPushNoNavegador(identidadeDaSessao(anterior)).catch(() => {
    window.dispatchEvent(new Event(EVENTO_FALHA_SAIDA_PUSH))
  })
}

export function atualizarTokens(sessao, dados) {
  const token = dados.accessToken ?? dados.token
  const venceEm = vencimentoDoToken(token) ?? Date.now() + dados.expiresIn * 1000
  return { ...sessao, token, refreshToken: dados.refreshToken, venceEm, expiraEm: venceEm - 60000,
    persistente: podePersistir({ ...sessao, token }) && Boolean(globalThis.navigator?.locks) }
}

// Vencimento do JWT lido do próprio token (`exp`, segundos), em ms. Token sem `exp` ou
// ilegível devolve null e quem chama usa o `expiresIn` da resposta do login.
export function vencimentoDoToken(token) {
  const exp = cargaDoToken(token)?.exp
  return Number.isFinite(exp) ? exp * 1000 : null
}

// Empresa do token (claim `empresaId`); null para superadmin ou token ilegível (#1324).
export function empresaDoToken(token) {
  return cargaDoToken(token)?.empresaId ?? null
}

function cargaDoToken(token) {
  try {
    const carga = String(token).split('.')[1].replace(/-/g, '+').replace(/_/g, '/')
    return JSON.parse(atob(carga.padEnd(Math.ceil(carga.length / 4) * 4, '=')))
  } catch {
    return null
  }
}
