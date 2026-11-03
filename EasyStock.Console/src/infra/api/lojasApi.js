import { chamarApi } from './cliente'
import { lerSessao } from './sessao'

// Lojas da empresa logada (`api/lojas`). O PUT é da policy Gerente e sobrescreve os cinco campos.
export async function listarLojasDaEmpresa() {
  const empresaId = lerSessao()?.empresa?.id
  if (!empresaId) throw new Error('Entre novamente na empresa para continuar.')
  return (await chamarApi(`/api/lojas?empresaId=${encodeURIComponent(empresaId)}`)) ?? []
}

export const atualizarLoja = (id, corpo) => chamarApi(`/api/lojas/${encodeURIComponent(id)}`, { metodo: 'PUT', corpo })
