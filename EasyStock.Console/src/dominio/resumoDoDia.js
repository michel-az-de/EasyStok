// Resumo do dia dos pedidos entregues (rodada 12, issue #16). Dúvida da
// Thatiane no áudio de 26/09/2026: o total dos entregues "vai para o fluxo de
// caixa?". Não vai: o protótipo não tem caixa nenhum. Este resumo é o número
// para ela CONFERIR no caixa, e o texto copiado diz isso com todas as letras.
//
// Puro. Reaproveita a conta de total da comanda (`totalDoPedido`) e a de
// dinheiro que entrou (`pagamentosDoPedido`), sem somar nada à mão.

import { totalDoPedido, numeroCurto } from './pedido'
import { pagamentosDoPedido } from './pagamento'
import { nomeDoMeio } from './cobranca'
import { moeda } from './formato'

const entregues = (conversas) => (conversas ?? []).filter((c) => c.pedido?.estado === 'entregue')

export function resumoDoDia(conversas, cardapio) {
  const lista = entregues(conversas)
  const porMeio = new Map()
  const pedidos = lista.map((conversa) => {
    const total = totalDoPedido(conversa.pedido, cardapio)
    const pagamentos = pagamentosDoPedido(conversa.pedido)
    for (const p of pagamentos) {
      const nome = nomeDoMeio(p.meio)
      porMeio.set(nome, (porMeio.get(nome) ?? 0) + p.valor)
    }
    return {
      numero: numeroCurto(conversa.pedido.numero),
      nome: conversa.nome,
      total,
      recebido: pagamentos.reduce((soma, p) => soma + p.valor, 0),
      meios: [...new Set(pagamentos.map((p) => nomeDoMeio(p.meio)))],
    }
  })
  const totalPedidos = pedidos.reduce((soma, p) => soma + p.total, 0)
  const recebido = pedidos.reduce((soma, p) => soma + p.recebido, 0)
  return {
    quantidade: pedidos.length,
    pedidos,
    totalPedidos,
    recebido,
    aReceber: Math.max(totalPedidos - recebido, 0),
    porMeio: [...porMeio].map(([nome, valor]) => ({ nome, valor })),
  }
}

const data = (ms) => new Date(ms).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric' })

// Texto do botão "Copiar resumo do dia": cola no WhatsApp, no e-mail ou na
// planilha do caixa do jeito que está.
export function textoDoResumoDoDia(resumo, agora) {
  const meios = resumo.porMeio.map((m) => `${m.nome} ${moeda(m.valor)}`).join('; ')
  const linhas = [
    `Casa da Baba · resumo do dia ${data(agora)}`,
    `Entregues: ${resumo.quantidade} ${resumo.quantidade === 1 ? 'pedido' : 'pedidos'} · ${moeda(resumo.totalPedidos)}`,
    `Recebido registrado no sistema: ${moeda(resumo.recebido)}${meios ? ` (${meios})` : ''}`,
  ]
  if (resumo.aReceber > 0) linhas.push(`Sem pagamento registrado: ${moeda(resumo.aReceber)}`)
  linhas.push('')
  for (const p of resumo.pedidos) {
    linhas.push(`${p.numero} ${p.nome} · ${moeda(p.total)}${p.meios.length ? ` · ${p.meios.join(', ')}` : ' · sem pagamento registrado'}`)
  }
  linhas.push('', 'Total para conferir no caixa. Este resumo não lança nada no caixa.')
  return linhas.join('\n')
}
