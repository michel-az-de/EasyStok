import { chamarApi } from './cliente'
import { atualizarTokens, empresaDoToken, gravarSessao, identidadeDaSessao, lerSessao, limparSessao } from './sessao'
import { esquecerRascunhos } from './rascunhosDaSessao'
import { prepararPushParaSessao } from '../pushNavegador'

// Casa da Baba (M0.2): a API resolve a empresa ativa; nunca escolhemos uma no navegador.
export async function entrar(email, senha) {
  const dados = await chamarApi('/api/auth/login', {
    metodo: 'POST', corpo: { email, senha, empresaId: null }, autenticado: false,
  })
  const empresaId = empresaDoToken(dados.token)
  if (!empresaId) throw new Error('Este usuário precisa de uma empresa ativa única para entrar na Casa da Baba. Peça à responsável para conferir o acesso.')
  return abrirSessao(dados, { id: empresaId, nome: null })
}

// Login com Google (#1324): o ClientId vem da API; sem ele (404), o botão não aparece.
export async function configGoogle() {
  try {
    const dados = await chamarApi('/api/auth/google/config', { autenticado: false })
    return dados?.clientId ?? null
  } catch {
    return null
  }
}

// A API escolhe a empresa do usuário; o token traz só o id dela.
export async function entrarComGoogle(idToken) {
  const dados = await chamarApi('/api/auth/google/login', {
    metodo: 'POST', corpo: { idToken }, autenticado: false,
  })
  const empresaId = empresaDoToken(dados.token)
  // #1326: superadmin entra quando a API dá a ele a empresa padrão; sem empresa, a inbox viria vazia.
  if (!empresaId) throw new Error('Este usuário não tem empresa para atender. Configure a empresa padrão do login Google.')
  return abrirSessao(dados, empresaId ? { id: empresaId, nome: null } : null)
}

async function abrirSessao(dados, empresa) {
  await prepararPushParaSessao(identidadeDaSessao({ usuario: dados.usuario, empresa }))
  return gravarSessao(atualizarTokens({ usuario: dados.usuario, empresa }, dados))
}

// Sair é decisão dela: os rascunhos guardados para depois do novo login vão junto.
export function sair() {
  const sessao = lerSessao()
  limparSessao()
  esquecerRascunhos()
  return sessao?.refreshToken
    ? chamarApi('/api/auth/logout', { metodo: 'POST', corpo: { refreshToken: sessao.refreshToken }, autenticado: false, sinal: AbortSignal.timeout(15000) })
    : Promise.resolve()
}
