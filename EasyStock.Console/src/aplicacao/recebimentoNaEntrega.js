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
// lançamento falhar, a entrega não é marcada. Se o lançamento entrou e a entrega falhou, `recebidos`
// (um Set da tela) lembra: o próximo toque só marca a entrega, nunca lança o pagamento de novo, por
// mais tempo que passe (a Idempotency-Key vale só 2 minutos).
export async function receberEEntregar(
  { registrarPagamento, marcarEntregue, recebidos = new Set() },
  { viagemId, pedidoId, valor, metodo, chave = null },
) {
  if (!metodo) throw new Error('Escolha como recebeu o pagamento.')
  const marca = `${pedidoId}|${valor}`
  if (!recebidos.has(marca)) {
    await registrarPagamento(pedidoId, valor, metodo, chave)
    recebidos.add(marca)
  }
  try {
    await marcarEntregue(viagemId, pedidoId, true)
  } catch (erro) {
    throw new Error(`O pagamento entrou, mas a entrega não foi marcada: ${erro.message} Toque de novo para marcar.`)
  }
  recebidos.delete(marca)
}

// Item 3: sem entregador cadastrado a viagem não saía. "Entrega própria (sem cadastro)" usa o
// entregador "Entrega própria" da casa: o ativo; senão o desativado, reativado; senão um novo.
export async function usarEntregaPropria(
  { listarComInativos, reativarEntregador, criarEntregador, definirEntregador },
  { viagemId, entregadores },
) {
  let entregador = entregadorDaEntregaPropria(entregadores)
  if (!entregador) {
    const inativo = entregadorDaEntregaPropria(await listarComInativos())
    if (inativo) await reativarEntregador(inativo.id)
    entregador = inativo ?? await criarEntregador({ ...CORPO_ENTREGA_PROPRIA })
  }
  if (!entregador?.id) throw new Error('O entregador "Entrega própria" não foi cadastrado.')
  await definirEntregador(viagemId, entregador.id)
}
