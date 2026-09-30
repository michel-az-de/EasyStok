// Rodada 12, issue #17 (feedback da Thatiane, 26/09/2026): entregas por bairro
// e onde a casa vende mais e menos. Puro, sem React.
//
// A conta usa o que o estado já tem: o histórico de pedidos anexado a cada
// conversa (`conversa.historico`, `infra/historicoPedidos.js`) e o pedido
// vivo de hoje. Bairro sai do endereço do cadastro, pela mesma leitura que o
// cartão de entrega já usa (`formato.js: partesDoEndereco`).
import { historicoComPedidoVivo } from './cliente'
import { partesDoEndereco } from './formato'
import { totalDoPedido } from './pedido'

export const SEM_BAIRRO = 'Sem bairro'

export const bairroDe = (conversa) => partesDoEndereco(conversa?.cliente?.endereco).bairro || SEM_BAIRRO

const porNome = (a, b) => {
  if (a === SEM_BAIRRO) return 1
  if (b === SEM_BAIRRO) return -1
  return a.localeCompare(b, 'pt-BR')
}

// Entregas do dia juntas por bairro, na ordem alfabética ("Sem bairro" por
// último). Dentro do grupo, a ordem de quem chamou é mantida.
export function agruparPorBairro(conversas) {
  const grupos = new Map()
  for (const conversa of conversas) {
    const bairro = bairroDe(conversa)
    if (!grupos.has(bairro)) grupos.set(bairro, [])
    grupos.get(bairro).push(conversa)
  }
  return [...grupos.entries()]
    .sort(([a], [b]) => porNome(a, b))
    .map(([bairro, itens]) => ({ bairro, itens }))
}

export const PERIODOS_DE_ALCANCE = [
  { valor: 'hoje', rotulo: 'Hoje', dias: 1 },
  { valor: '7', rotulo: '7 dias', dias: 7 },
  { valor: '30', rotulo: '30 dias', dias: 30 },
  { valor: '90', rotulo: '90 dias', dias: 90 },
  { valor: 'tudo', rotulo: 'Tudo', dias: null },
]

const DIA_MS = 24 * 60 * 60 * 1000

const inicioDoDia = (ms) => {
  const dia = new Date(ms)
  dia.setHours(0, 0, 0, 0)
  return dia.getTime()
}

// "2026-09-22" é dia sem hora: meio-dia local, para fuso nenhum empurrar o
// pedido para o dia vizinho.
const emMs = (em) => (typeof em === 'string' && em.length === 10 ? Date.parse(em + 'T12:00:00') : Date.parse(em))

// Uma linha por pedido que conta como venda (cancelado não conta), com o
// bairro de quem comprou. Pedido vivo que já é espelho da última linha do
// histórico conta uma vez só (mesmo número).
function vendas(conversas, agora, cardapio) {
  const vistos = new Set()
  const linhas = []
  for (const conversa of conversas) {
    const bairro = bairroDe(conversa)
    const cliente = conversa.cadastroId ?? conversa.id
    const historico = historicoComPedidoVivo(conversa.historico ?? [], conversa.pedido)
    for (const h of historico) {
      if (vistos.has(h.numero)) continue
      vistos.add(h.numero)
      if (h.estado === 'cancelado') continue
      linhas.push({ bairro, cliente, em: emMs(h.em), total: h.total ?? 0 })
    }
    const pedido = conversa.pedido
    if (!pedido || vistos.has(pedido.numero)) continue
    vistos.add(pedido.numero)
    if (pedido.estado === 'cancelado' || !pedido.itens?.length) continue
    linhas.push({ bairro, cliente, em: agora, total: totalDoPedido(pedido, cardapio ?? []) })
  }
  return linhas
}

// Alcance por bairro num período que termina hoje (`dias` = 1 é só hoje,
// `null` é tudo). Todo bairro que a casa já atendeu aparece, mesmo com zero no
// período: "onde vende menos" inclui onde não vendeu. `anterior` é o mesmo
// tamanho de período logo antes, para comparar sem abrir outra tela.
export function alcancePorBairro(conversas, { agora, dias, cardapio }) {
  const linhas = vendas(conversas, agora, cardapio)
  const fim = inicioDoDia(agora) + DIA_MS
  const inicio = dias == null ? -Infinity : inicioDoDia(agora) - (dias - 1) * DIA_MS
  const inicioAnterior = dias == null ? null : inicio - dias * DIA_MS

  const bairros = new Map()
  const doBairro = (bairro) => {
    if (!bairros.has(bairro)) bairros.set(bairro, { bairro, pedidos: 0, total: 0, clientes: new Set(), anterior: 0 })
    return bairros.get(bairro)
  }
  for (const conversa of conversas) doBairro(bairroDe(conversa))
  for (const linha of linhas) {
    const b = doBairro(linha.bairro)
    if (linha.em >= inicio && linha.em < fim) {
      b.pedidos += 1
      b.total += linha.total
      b.clientes.add(linha.cliente)
    } else if (inicioAnterior != null && linha.em >= inicioAnterior && linha.em < inicio) {
      b.anterior += 1
    }
  }

  const lista = [...bairros.values()]
    .map((b) => ({ ...b, clientes: b.clientes.size, anterior: dias == null ? null : b.anterior }))
    .sort((a, b) => b.pedidos - a.pedidos || b.total - a.total || porNome(a.bairro, b.bairro))
  const totalPedidos = lista.reduce((soma, b) => soma + b.pedidos, 0)
  return {
    bairros: lista.map((b) => ({ ...b, parcela: totalPedidos ? b.pedidos / totalPedidos : 0 })),
    totalPedidos,
    totalValor: lista.reduce((soma, b) => soma + b.total, 0),
  }
}
