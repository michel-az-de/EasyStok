import { chamarApi } from './cliente'

// Parâmetros da loja (`api/configuracoes`, policy Gerente). Sem registro, a API devolve o padrão.
// No PATCH, campo nulo ou ausente mantém o valor gravado; a resposta é a configuração inteira.
export const obterConfiguracaoDaLoja = (empresaId, lojaId) =>
  chamarApi(`/api/configuracoes?empresaId=${encodeURIComponent(empresaId)}&lojaId=${encodeURIComponent(lojaId)}`)

export const atualizarConfiguracaoDaLoja = (corpo) => chamarApi('/api/configuracoes', { metodo: 'PATCH', corpo })
