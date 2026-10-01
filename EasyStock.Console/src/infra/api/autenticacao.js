import { chamarApi } from './cliente'
import { empresaDoToken, gravarSessao, limparSessao, vencimentoDoToken } from './sessao'
import { esquecerRascunhos } from './rascunhosDaSessao'

// Login em dois passos (ADR-0047): credenciais → empresas do usuário → token da empresa.
export const listarEmpresas = (email, senha) =>
  chamarApi('/api/auth/lista-empresas', { metodo: 'POST', corpo: { email, senha }, autenticado: false })

export async function entrar(email, senha, empresa) {
  const dados = await chamarApi('/api/auth/login', {
    metodo: 'POST', corpo: { email, senha, empresaId: empresa?.id ?? null }, autenticado: false,
  })
  return abrirSessao(dados, empresa ?? null)
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

function abrirSessao(dados, empresa) {
  const sessao = {
    token: dados.token,
    // Margem de 1 min para não mandar token vencendo no meio da chamada.
    expiraEm: Date.now() + (dados.expiresIn - 60) * 1000,
    // Vencimento real do token, para o aviso de 10 min antes (F07, item 6).
    venceEm: vencimentoDoToken(dados.token) ?? Date.now() + dados.expiresIn * 1000,
    usuario: dados.usuario,
    empresa,
  }
  gravarSessao(sessao)
  return sessao
}

// Sair é decisão dela: os rascunhos guardados para depois do novo login vão junto.
export function sair() {
  limparSessao()
  esquecerRascunhos()
}
