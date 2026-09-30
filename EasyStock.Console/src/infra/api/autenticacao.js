import { chamarApi } from './cliente'
import { gravarSessao, limparSessao } from './sessao'

// Login em dois passos (ADR-0047): credenciais → empresas do usuário → token da empresa.
export const listarEmpresas = (email, senha) =>
  chamarApi('/api/auth/lista-empresas', { metodo: 'POST', corpo: { email, senha }, autenticado: false })

export async function entrar(email, senha, empresa) {
  const dados = await chamarApi('/api/auth/login', {
    metodo: 'POST', corpo: { email, senha, empresaId: empresa?.id ?? null }, autenticado: false,
  })
  const sessao = {
    token: dados.token,
    // Margem de 1 min para não mandar token vencendo no meio da chamada.
    expiraEm: Date.now() + (dados.expiresIn - 60) * 1000,
    usuario: dados.usuario,
    empresa: empresa ?? null,
  }
  gravarSessao(sessao)
  return sessao
}

export const sair = () => limparSessao()
