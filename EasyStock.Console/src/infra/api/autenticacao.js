import { chamarApi } from './cliente'
import { gravarSessao, limparSessao, vencimentoDoToken } from './sessao'
import { esquecerRascunhos } from './rascunhosDaSessao'

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
    // Vencimento real do token, para o aviso de 10 min antes (F07, item 6).
    venceEm: vencimentoDoToken(dados.token) ?? Date.now() + dados.expiresIn * 1000,
    usuario: dados.usuario,
    empresa: empresa ?? null,
  }
  gravarSessao(sessao)
  return sessao
}

// Sair é decisão dela: os rascunhos guardados para depois do novo login vão junto.
export function sair() {
  limparSessao()
  esquecerRascunhos()
}
