import { chamarApi } from './cliente'

// Cardápio do dia pelo console (#1241, F11): rotas do Operador sobre o item da comanda.
const ITEM = (id) => `/api/atendimento/comanda/cardapio/${id}`

export const definirDisponibilidade = (id, disponivel) =>
  chamarApi(`${ITEM(id)}/disponivel`, { metodo: 'POST', corpo: { disponivel } })

export const ajustarSaldoDoItem = (id, quantidadeContada, motivo) =>
  chamarApi(`${ITEM(id)}/saldo`, { metodo: 'POST', corpo: { quantidadeContada, motivo } })
