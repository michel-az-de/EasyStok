import { chamarApi } from './cliente'

const ROTA = '/api/atendimento/logistica'
export const obterContextoFrete = ({ dia, pedidoId } = {}) => {
  const consulta = new URLSearchParams()
  if (dia) consulta.set('dia', dia)
  if (pedidoId) consulta.set('pedidoId', pedidoId)
  return chamarApi(`${ROTA}/contexto${consulta.size ? `?${consulta}` : ''}`)
}
export const cotarFretes = (corpo) => chamarApi(`${ROTA}/cotacoes`, { metodo: 'POST', corpo })
export const contratarFrete = (corpo) => chamarApi(`${ROTA}/despachos`, { metodo: 'POST', corpo })
export const pagarFrete = (id, corpo) => chamarApi(`${ROTA}/despachos/${id}/pagamento`, { metodo: 'POST', corpo })
export const registrarEventoFrete = (id, corpo) => chamarApi(`${ROTA}/despachos/${id}/evento`, { metodo: 'POST', corpo })
export const salvarPoliticaFrete = (corpo) => chamarApi(`${ROTA}/politica`, { metodo: 'PUT', corpo })
export const contratarAutomatico = (corpo) => chamarApi(`${ROTA}/automatico`, { metodo: 'POST', corpo })
export const prepararDemonstracao = (corpo) => chamarApi(`${ROTA}/demonstracao`, { metodo: 'POST', corpo })
