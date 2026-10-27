import { chamarApi } from './cliente'

// Primeiros passos (#2070): `api/configuracao-inicial`, policy Admin, empresa do token. As rotas só marcam o passo
// ou a configuração inteira; cada ajuste grava pela rota dele. Adiar e concluir são da dona ou do suporte (403 HIERARQUIA).
const BASE = '/api/configuracao-inicial'

export const obterConfiguracaoInicial = () => chamarApi(BASE)
export const marcarPassoDaConfiguracaoInicial = (passo, situacao) =>
  chamarApi(`${BASE}/passos/${encodeURIComponent(passo)}`, { metodo: 'PUT', corpo: { situacao } })
export const adiarConfiguracaoInicial = () => chamarApi(`${BASE}/adiar`, { metodo: 'POST' })
export const concluirConfiguracaoInicial = () => chamarApi(`${BASE}/concluir`, { metodo: 'POST' })

// `PATCH api/auth/me` só com o nome: sem e-mail no corpo a API não pede a senha (AtualizarUsuarioAtualUseCase).
export const atualizarMeuNome = (nome) => chamarApi('/api/auth/me', { metodo: 'PATCH', corpo: { nome } })
