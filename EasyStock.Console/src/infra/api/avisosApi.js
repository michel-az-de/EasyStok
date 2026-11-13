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

// #2211: quem da equipe recebe os avisos da equipe no WhatsApp. Uma pessoa por PUT, os três avisos juntos.
export const obterEquipeAvisada = () => chamarApi(`${BASE}/equipe`, { sinal: AbortSignal.timeout(15000) })

export const salvarPessoaAvisada = (usuarioId, { atencao, pedido, pagamento }) => chamarApi(`${BASE}/equipe/${encodeURIComponent(usuarioId)}`, {
  metodo: 'PUT',
  identidadeEsperada: identidadeDaSessao(lerSessao()),
  sinal: AbortSignal.timeout(15000),
  corpo: { atencao: atencao === true, pedido: pedido === true, pagamento: pagamento === true },
})
