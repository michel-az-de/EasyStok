// Frente J2 (#1990): o que acontece na porta, no modo API, sem React (a tela chama pelo
// `useEntregasApi`; a prova chama com portas de mentira).
import { pagamentoNaEntregaDasObservacoes } from '../dominio/cobranca'
import { CORPO_ENTREGA_PROPRIA, entregadorDaEntregaPropria } from '../dominio/entregasApi'

// Item 2: no "Entregue" com saldo, ela diz como recebeu. São os métodos da baixa à mão do Balcão
// ("Marcar pago", `POST /api/pedidos/{id}/pagamentos`); maquininha é débito ou crédito.
export const METODOS_NA_PORTA = [
  { valor: '', rotulo: 'Como recebeu?' },
  { valor: 'dinheiro', rotulo: 'Dinheiro' },
  { valor: 'debito', rotulo: 'Maquininha · débito' },
  { valor: 'credito', rotulo: 'Maquininha · crédito' },
  { valor: 'pix', rotulo: 'Pix' },
]

// Pedido de dinheiro na entrega já vem com "Dinheiro" marcado; o resto, ela escolhe.
export const metodoSugeridoNaPorta = (observacoes) => (pagamentoNaEntregaDasObservacoes(observacoes) ? 'dinheiro' : '')

// Lança o recebido e só então marca entregue (confirmando o saldo, que acabou de ser pago). Se o
// lançamento falhar, a entrega não é marcada e o erro volta para a tela.
export async function receberEEntregar({ registrarPagamento, marcarEntregue }, { viagemId, pedidoId, valor, metodo, chave = null }) {
  if (!metodo) throw new Error('Escolha como recebeu o pagamento.')
  await registrarPagamento(pedidoId, valor, metodo, chave)
  await marcarEntregue(viagemId, pedidoId, true)
}

// Item 3: sem entregador cadastrado a viagem não saía. "Entrega própria (sem cadastro)" usa o
// entregador "Entrega própria" da casa, cadastrando-o na primeira vez.
export async function usarEntregaPropria({ criarEntregador, definirEntregador }, { viagemId, entregadores }) {
  const existente = entregadorDaEntregaPropria(entregadores)
  const entregador = existente ?? await criarEntregador({ ...CORPO_ENTREGA_PROPRIA })
  if (!entregador?.id) throw new Error('O entregador "Entrega própria" não foi cadastrado.')
  await definirEntregador(viagemId, entregador.id)
}
