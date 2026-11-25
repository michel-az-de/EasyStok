// #3344: o que oferecer a quem abre a Ficha. Só sai de compra real: o favorito que o servidor calculou sobre todas as
// compras (`relacionamento.favorito`, a partir de 2 pedidos com o mesmo item) e os itens do último pedido que não foi
// cancelado. Sem compra não há sugestão; nada é inventado.
const CANCELADO = /cancel|recus/i
const MAXIMO = 3

export const pedidoValido = (p) => Boolean(p) && !CANCELADO.test(String(p.status ?? ''))
const chave = (nome) => String(nome ?? '').trim().toLocaleLowerCase('pt-BR')

// `ultimoPedido`: o detalhe do pedido (`{ pedido, itens: [{ nome, quantidade }] }`) ou null.
export function pratosParaOferecer({ favorito = null, ultimoPedido = null } = {}) {
  const lista = []
  const ver = new Set()
  const entrar = (nome, motivo) => {
    const texto = String(nome ?? '').trim()
    if (!texto || ver.has(chave(texto)) || lista.length >= MAXIMO) return
    ver.add(chave(texto)); lista.push({ nome: texto, motivo })
  }
  entrar(favorito, 'Costuma pedir')
  for (const item of ultimoPedido?.itens ?? []) entrar(item?.nome, 'Pediu no último pedido')
  return lista
}

// A pergunta ao Assistente leva só o que a ficha já mostra: nome próprio, estágio e os pratos. A resposta volta só
// para a tela, nunca ao cliente.
const PEDIDOS = {
  pratos: 'Com base só nesses dados, diga em 3 linhas o que vale oferecer a ela agora e por quê. Não invente pratos fora da lista.',
  recompra: 'Escreva uma mensagem curta e calorosa de WhatsApp (até 3 linhas) convidando para pedir de novo. Use só os pratos da lista. Não prometa desconto.',
}
export function perguntaSobreOCliente(modo, { primeiroNome, leitura, pratos, restricoes = [] }) {
  const linhas = [
    `Cliente: ${primeiroNome || 'sem nome'}.`,
    leitura ? `Estágio: ${leitura.estagio}${leitura.emRisco ? ' (em risco: está há tempo sem pedir)' : ''}.` : null,
    leitura?.ultimaCompra ? `${leitura.ultimaCompra}.` : null,
    restricoes.length ? `Restrições alimentares: ${restricoes.join(', ')}. Não sugira nada que as contrarie.` : null,
    pratos.length ? `Pratos que ela pede: ${pratos.map((p) => `${p.nome} (${p.motivo.toLocaleLowerCase('pt-BR')})`).join('; ')}.` : 'Sem pratos conhecidos.',
  ].filter(Boolean)
  return `${linhas.join(' ')} ${PEDIDOS[modo] ?? PEDIDOS.pratos}`
}
