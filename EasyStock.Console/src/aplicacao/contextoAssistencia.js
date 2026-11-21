import { identidadeDaSessao, lerSessao } from '../infra/api/sessao'
import { useChaveDaSessao } from './useChaveDaSessao'
import { revisaoDoContexto } from '../dominio/contextoDaSugestao'

export const identidadeDaAssistencia = () => identidadeDaSessao(lerSessao())
export const chaveDaAssistencia = (conversa, identidade) => JSON.stringify([
  identidade, conversa?.id ?? null, revisaoDoContexto(conversa),
])

export function useContextoAssistencia(conversa) {
  const identidade = useChaveDaSessao(identidadeDaSessao)
  return { identidade, chave: chaveDaAssistencia(conversa, identidade) }
}
