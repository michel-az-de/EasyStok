import { chamarApi } from './cliente'
import { identidadeDaSessao, lerSessao } from './sessao'

export const identidadeDaMontagem = () => identidadeDaSessao(lerSessao())

export function montarFichaIa(conversaId, orientacao = '', { sinal, identidadeEsperada } = {}) {
  if (!/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(conversaId)) throw new Error('Conversa inválida.')
  if (typeof orientacao !== 'string' || orientacao.trim().length > 2000) throw new Error('A orientação pode ter até 2000 caracteres.')
  return chamarApi('/api/atendimento/assistente/ficha', {
    metodo: 'POST', corpo: { conversaId, orientacao: orientacao.trim() }, sinal, identidadeEsperada,
  })
}
