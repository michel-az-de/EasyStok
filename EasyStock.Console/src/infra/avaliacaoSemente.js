// Semente determinística de avaliação do cliente (rodada 10, achado P1.2,
// US-007: "o que gostou"). Hoje `avaliacaoCliente` nunca aparece em pedido
// nem atendimento antigo (0 ocorrências em historicoPedidos.js); aqui mora a
// regra que preenche uma fração real dos pedidos concluídos, sem tocar no
// pedido em aberto (esse é ao vivo, ainda sem tempo de resposta do cliente).
//
// Hash em vez de Math.random(): o mesmo pedido gera sempre a mesma
// avaliação, build após build (semente fixa, sem sorteio que muda a cada
// carga).
export function hashPara100(texto) {
  let h = 2166136261
  for (let i = 0; i < texto.length; i += 1) {
    h ^= texto.charCodeAt(i)
    h = Math.imul(h, 16777619)
  }
  return (h >>> 0) % 100
}

// Notas com sentimento explícito (rodada 8 já escreveu isso em texto livre,
// só nunca virou campo estruturado): "avaliou positivo", "reclamou" etc.
const POSITIVA = /adorou|elogiou|gostou|avaliou positivo|indica[çc][aã]o|primeira fornada|trouxe.*indic|fiel|sempre volta/i
const NEGATIVA = /reclamou|virada|cozida demais|contestou|n[aã]o confirmou|preju[íi]zo|depois da janela|ocorr[eê]ncia aberta|n[aã]o chegou|golpe|demora/i

// Só pedido concluído (entregue ou cancelado) pode ter sido avaliado: quem
// ainda está na esteira (pago, aguardando, preparo, entrega, agendado) não
// teve tempo de opinar, e o pedido ao vivo do cliente cai sempre num desses
// estados (regra do dono: "não preencher o mais recente, esse é ao vivo").
const CONCLUIDOS = new Set(['entregue', 'cancelado'])

export function avaliacaoDoPedido(pedido) {
  if (!CONCLUIDOS.has(pedido.estado)) return null
  const nota = pedido.nota ?? ''
  if (NEGATIVA.test(nota)) return 'negativa'
  if (pedido.estado === 'cancelado') return null // cancelamento tranquilo, sem reclamação escrita
  if (POSITIVA.test(nota)) return 'positiva'
  // Sem palavra-chave: fração determinística por hash do número do pedido.
  // ~38% positiva, mais 7% negativa (insatisfação que nunca virou nota
  // escrita), o resto sem avaliação, como a maioria dos clientes de verdade
  // nunca deixa retorno nenhum.
  const h = hashPara100(pedido.numero)
  if (h < 38) return 'positiva'
  if (h < 45) return 'negativa'
  return null
}

// Aplica a avaliação a uma lista de pedidos de um cliente (ordem decrescente
// de data, igual ao resto de historicoPedidos.js), sem mutar a lista de
// entrada.
export function comAvaliacoes(lista) {
  return lista.map((pedido) => ({ ...pedido, avaliacaoCliente: avaliacaoDoPedido(pedido) }))
}
