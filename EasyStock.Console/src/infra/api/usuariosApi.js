import { chamarApi } from './cliente'
import { lerSessao } from './sessao'

// Usuários da empresa (#1753, M7.2). A empresa vem da sessão: nunca é escolhida na tela.
const empresa = () => lerSessao()?.empresa?.id ?? ''
const comEmpresa = (caminho, extra = {}) => {
  const consulta = new URLSearchParams({ empresaId: empresa(), ...extra })
  return `${caminho}?${consulta}`
}

// Leituras com `prazoComCancelamento`: sem ele, passar `sinal` desliga o prazo de 20 s e a tela ficava carregando para sempre.
export const listarUsuarios = (sinal) =>
  chamarApi(comEmpresa('/api/usuarios', { pageSize: '100' }), { sinal, prazoComCancelamento: true })

export const listarPerfis = (sinal) => chamarApi(comEmpresa('/api/usuarios/perfis'), { sinal, prazoComCancelamento: true })

// Sem senha: a API cria o convite e manda o link por e-mail (N9). #2070: `dona` só com perfil de nível Admin.
export const convidarUsuario = ({ nome, email, perfilId, dona = false }) =>
  chamarApi('/api/usuarios', {
    metodo: 'POST',
    corpo: { empresaId: empresa(), nome: nome.trim(), email: email.trim(), senha: null, perfilId, lojaId: null, dona: dona === true },
  })

// #2070: marca e desmarca a dona. O motivo é do suporte (a API exige dele, com 10 caracteres ou mais).
export const marcarDona = (usuarioId, { dona, motivo = null }) => {
  const texto = motivo?.trim()
  return chamarApi(comEmpresa(`/api/usuarios/${usuarioId}/dona`), {
    metodo: 'PUT', corpo: { dona: dona === true, ...(texto ? { motivo: texto } : {}) },
  })
}

export const reenviarConvite = (usuarioId) => chamarApi(`/api/usuarios/${usuarioId}/convite`, { metodo: 'POST' })

export const trocarPerfil = (usuarioId, perfilId) =>
  chamarApi(`/api/usuarios/${usuarioId}/perfis`, { metodo: 'PUT', corpo: { empresaId: empresa(), perfilId, lojaId: null } })

export const desativarUsuario = (usuarioId) => chamarApi(comEmpresa(`/api/usuarios/${usuarioId}`), { metodo: 'DELETE' })

export const reativarUsuario = (usuarioId) => chamarApi(comEmpresa(`/api/usuarios/${usuarioId}/reativar`), { metodo: 'POST' })

export const listarAuditoria = ({ usuarioId = null, pagina = 1, tamanho = 50 } = {}, sinal) =>
  chamarApi(comEmpresa('/api/usuarios/auditoria', {
    page: String(pagina), pageSize: String(tamanho), ...(usuarioId ? { usuarioId } : {}),
  }), { sinal, envelope: true, prazoComCancelamento: true })

// Anônimo: quem aceita o convite ainda não tem sessão.
export const aceitarConvite = (token, novaSenha) =>
  chamarApi('/api/auth/aceitar-convite', { metodo: 'POST', corpo: { token, novaSenha }, autenticado: false })

// #2070: entrar pelo Google no convite troca a sessão de quem estava no aparelho; esta encerra a anterior no servidor
// (o `sair` da autenticação só encerra a sessão atual).
export const encerrarSessaoAnterior = (refreshToken) =>
  chamarApi('/api/auth/logout', { metodo: 'POST', corpo: { refreshToken }, autenticado: false, sinal: AbortSignal.timeout(15000) })
