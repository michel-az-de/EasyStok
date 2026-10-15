import { chamarApi } from './cliente'

// Caixa de suporte da loja para o atendimento por e-mail (#1432): `api/integracoes/email/caixa`.
// A API nunca devolve a senha, só `senhaDefinida`.
const BASE = '/api/integracoes/email/caixa'

// null quando a caixa nunca foi configurada.
export const obterCaixaEmail = () => chamarApi(BASE)

export const salvarCaixaEmail = (corpo) => chamarApi(BASE, { metodo: 'PUT', corpo })

// Com corpo testa os dados da tela (senha vazia usa a gravada); sem corpo, a caixa gravada.
// { ok, imapOk, imapErro, smtpOk, smtpErro }
export const testarCaixaEmail = (corpo) => chamarApi(`${BASE}/teste`, { metodo: 'POST', corpo: corpo ?? null })
