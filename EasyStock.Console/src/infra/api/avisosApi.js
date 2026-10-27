import { corpoDosAvisos } from '../../dominio/avisosDaOperacao'
import { chamarApi } from './cliente'
import { identidadeDaSessao, lerSessao } from './sessao'

// Avisos da operação (#2096): `api/atendimento/avisos`, no envelope da configuração do atendimento.
// O GET traz também o SLA de primeira resposta (só leitura); o PUT vai sem ele e devolve o que ficou salvo.
const BASE = '/api/atendimento/avisos'

export const obterAvisos = () => chamarApi(BASE, { sinal: AbortSignal.timeout(15000) })

export const salvarAvisos = (avisos) => chamarApi(BASE, {
  metodo: 'PUT',
  identidadeEsperada: identidadeDaSessao(lerSessao()),
  sinal: AbortSignal.timeout(15000),
  corpo: corpoDosAvisos(avisos),
})
