// Domínio da Frente 2 · Ficha do lead e do cliente (rodada 5, seção 2).
// Funções puras: conversas, catálogo e relógio chegam por parâmetro, nada
// aqui importa infra nem React (regra de ferramentas/verificar-camadas.mjs).

// Restrição decide campanha (RN-39) e o que o agente não responde sozinho
// (RN-52): vem sempre primeiro entre as tags. Substring sem caixa porque a
// tag digitada varia ("Lactose", "Alergia a lactose").
export function ehRestricao(tag, restricoes = []) {
  const alvo = tag.toLowerCase()
  return restricoes.some((r) => alvo.includes(r.toLowerCase()))
}

// Restrição primeiro, o resto na ordem em que foi criado. `sort` só troca
// posição quando o critério de restrição muda: mantém estável o resto.
export function ordenarTags(tags, restricoes = []) {
  return [...tags].sort((a, b) => Number(ehRestricao(b, restricoes)) - Number(ehRestricao(a, restricoes)))
}

// D10/RN-12/RN-13: mesmo endereço é sinal de convivência, nunca fusão de
// cadastro (LGPD). Derivado só do endereço atual: sem estado próprio para
// "vínculo", o sinal se desfaz sozinho (RN-13) assim que um dos dois muda de
// endereço.
export function mesmoDomicilio(conversa, conversas) {
  const endereco = conversa?.cliente?.endereco
  if (!endereco) return null
  return conversas.find(
    (c) => c.cadastroId !== conversa.cadastroId && c.cliente?.endereco === endereco,
  ) ?? null
}

// "Chegou hoje pelo Instagram" (situação do lead). Sem hora: a dona lê o
// dia, o minuto cru não ajuda nela a entender o caso.
export function diasEntre(iso, agora) {
  const inicio = new Date(iso); inicio.setHours(0, 0, 0, 0)
  const hoje = new Date(agora); hoje.setHours(0, 0, 0, 0)
  return Math.round((hoje - inicio) / 86400000)
}

// "hoje" / "ontem" / "há N dias": mesmo texto usado na situação do lead
// (Bloco Cliente) e na faixa de números da modal Histórico.
export const quandoRelativo = (dias) => (dias <= 0 ? 'hoje' : dias === 1 ? 'ontem' : `há ${dias} dias`)

export function chegouQuando(conversa, agora) {
  const primeira = conversa.mensagens?.[0]?.em
  if (!primeira) return null
  return `chegou ${quandoRelativo(diasEntre(primeira, agora))} pelo ${conversa.canal}`
}

// Integração 72: o pedido em aberto entra no histórico como espelho (69,
// US-007), e o estado do espelho é o do pedido vivo. Sem isso o Histórico
// seguia dizendo "pago" depois do preparo, do despacho (US-040) ou do lote
// de papel (US-042), e um pedido cancelado continuava somando nos gastos.
export function historicoComPedidoVivo(historico, pedido) {
  if (!pedido) return historico
  // Rodada 12 (issue #17): quem levou o pedido vivo vai junto, para o
  // histórico do cliente dizer qual entregador foi, com placa.
  return historico.map((h) => (h.numero === pedido.numero
    ? { ...h, estado: pedido.estado, entregador: pedido.entregador ?? null }
    : h))
}

// Total gasto e ticket médio (Números da ficha e faixa da modal Histórico):
// uma conta só, para as duas telas nunca discordarem. Pedido cancelado não
// entra na conta. Mais recente primeiro, sem confiar na ordem de quem chamou.
export function resumoFinanceiro(historico) {
  const validos = (historico ?? []).filter((p) => p.estado !== 'cancelado')
  const total = validos.reduce((soma, p) => soma + p.total, 0)
  const ticketMedio = validos.length ? total / validos.length : 0
  const ordenado = [...(historico ?? [])].sort((a, b) => new Date(b.em) - new Date(a.em))
  return { total, ticketMedio, ultimoEm: ordenado[0]?.em ?? null }
}

// Sugestão do campo "Nova tag" (seção 2): restrição vem do catálogo, sempre
// oferecida; gosto vem do que a casa já usou em outros cadastros, sem lista
// nova para manter. A massa de teste carrega tag de rastro ("Chegou pelo
// Instagram") e de risco/operação ("Bloqueado", "Deu prejuízo", "Insistiu"),
// que não são gosto nenhum. "Mesmo endereço de X" NÃO é tag: é fato derivado
// por `mesmoDomicilio` (D10, RN-13), mostrado só como linha com link e
// desfeito quando o endereço muda; `coerencia-da-massa.mjs` barra essa tag
// na massa (integração 50). Só
// entra tag que soa como preferência de verdade (começa com um verbo de
// pedir ou gostar). Falso negativo aqui é seguro, falso positivo confunde
// Thatiane lendo a sugestão como gosto quando é alerta de risco.
const PARECE_GOSTO = /^(gosta|prefere|pede|sempre|s[oó]\s|quis|molho|indica)/i

function gostosConhecidos(conversas, cadastroIdAtual, restricoes) {
  const usados = new Set()
  for (const c of conversas) {
    if (c.cadastroId === cadastroIdAtual) continue
    for (const tag of c.cliente?.tags ?? []) {
      if (ehRestricao(tag, restricoes) || !PARECE_GOSTO.test(tag)) continue
      usados.add(tag)
    }
  }
  return [...usados].sort()
}

export function sugestoesDeTag(texto, { conversas, cadastroIdAtual, tagsAtuais, restricoes }) {
  const termo = texto.trim().toLowerCase()
  const bate = (t) => !tagsAtuais.includes(t) && (termo === '' || t.toLowerCase().includes(termo))
  return {
    restricao: restricoes.filter(bate),
    gosto: gostosConhecidos(conversas, cadastroIdAtual, restricoes).filter(bate),
  }
}
