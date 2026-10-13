import { chamarApi } from './cliente'
import { lerSessao } from './sessao'

// Usuários da empresa (#1753, M7.2). A empresa vem da sessão: nunca é escolhida na tela.
const empresa = () => lerSessao()?.empresa?.id ?? ''
const comEmpresa = (caminho, extra = {}) => {
  const consulta = new URLSearchParams({ empresaId: empresa(), ...extra })
  return `${caminho}?${consulta}`
}

export const listarUsuarios = (sinal) =>
  chamarApi(comEmpresa('/api/usuarios', { pageSize: '100' }), { sinal })

export const listarPerfis = (sinal) => chamarApi(comEmpresa('/api/usuarios/perfis'), { sinal })

// Sem senha: a API cria o convite e manda o link por e-mail (N9).
export const convidarUsuario = ({ nome, email, perfilId }) =>
  chamarApi('/api/usuarios', {
    metodo: 'POST', corpo: { empresaId: empresa(), nome: nome.trim(), email: email.trim(), senha: null, perfilId, lojaId: null },
  })

export const reenviarConvite = (usuarioId) => chamarApi(`/api/usuarios/${usuarioId}/convite`, { metodo: 'POST' })

export const trocarPerfil = (usuarioId, perfilId) =>
  chamarApi(`/api/usuarios/${usuarioId}/perfis`, { metodo: 'PUT', corpo: { empresaId: empresa(), perfilId, lojaId: null } })

export const desativarUsuario = (usuarioId) => chamarApi(comEmpresa(`/api/usuarios/${usuarioId}`), { metodo: 'DELETE' })

export const reativarUsuario = (usuarioId) => chamarApi(comEmpresa(`/api/usuarios/${usuarioId}/reativar`), { metodo: 'POST' })

export const listarAuditoria = ({ usuarioId = null, pagina = 1, tamanho = 50 } = {}, sinal) =>
  chamarApi(comEmpresa('/api/usuarios/auditoria', {
    page: String(pagina), pageSize: String(tamanho), ...(usuarioId ? { usuarioId } : {}),
  }), { sinal, envelope: true })

// Anônimo: quem aceita o convite ainda não tem sessão.
export const aceitarConvite = (token, novaSenha) =>
  chamarApi('/api/auth/aceitar-convite', { metodo: 'POST', corpo: { token, novaSenha }, autenticado: false })
