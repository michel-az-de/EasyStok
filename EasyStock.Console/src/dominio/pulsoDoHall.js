// Contagens da leitura atual. Não interpreta SLA sem o expediente da loja.
const aguardandoResposta = (dados) => dados.itens.filter((c) => c.situacao === 'Assumida' && (c.aguardaResposta || !c.assumidaPorUsuarioId)).length
const naFila = (dados) => dados.itens.filter((p) => ['aguardando', 'preparando'].includes(p.status)).length
const prontos = (dados) => dados.itens.filter((p) => p.status === 'pronto').length
const plural = (n, um, varios, mais = '') => `${n}${mais} ${n === 1 ? um : varios}`

// #1839 D: a linha viva do cartão de cada módulo. Só módulo com fonte lida tem linha; leitura que
// falhou some do cartão (o contador de cima já avisa). `atencao` pede o olhar da dona.
export function pendenciaDoModulo(id, fontes) {
  const fonte = fontes?.[id === 'cozinha' || id === 'entregas' ? 'pedidos' : id]
  const dados = fonte?.estado === 'erro' ? null : fonte?.dados
  if (!dados) return null
  const mais = dados.parcial ? '+' : ''
  if (id === 'atendimento') {
    const n = aguardandoResposta(dados)
    if (!n && dados.parcial) return null
    return n ? { texto: `${plural(n, 'conversa', 'conversas', mais)} sem resposta`, atencao: true } : { texto: 'Nenhuma conversa esperando', atencao: false }
  }
  if (id === 'cozinha') {
    const n = naFila(dados)
    if (!n && dados.parcial) return null
    return n ? { texto: `${plural(n, 'pedido', 'pedidos', mais)} no preparo`, atencao: true } : { texto: 'Fila de preparo vazia', atencao: false }
  }
  if (id === 'entregas') {
    const n = prontos(dados)
    if (!n && dados.parcial) return null
    return n ? { texto: `${plural(n, 'pedido pronto', 'pedidos prontos', mais)} para sair`, atencao: true } : { texto: 'Nada pronto para sair', atencao: false }
  }
  if (id === 'financeiro') {
    if (dados.aberturaPendenteCrossDay) return { texto: 'Caixa de outro dia aberto', atencao: true }
    return { texto: dados.aberto ? 'Caixa aberto' : dados.fechado ? 'Caixa fechado hoje' : 'Caixa do dia ainda fechado', atencao: false }
  }
  if (id === 'cardapio' && Array.isArray(dados.itens)) {
    const ativos = dados.itens.filter((item) => !item.arquivado)
    const pendentes = ativos.filter((item) => item.pendencias?.length).length
    if (pendentes) return { texto: `${plural(pendentes, 'prato', 'pratos')} com pendência`, atencao: true }
    return { texto: `${plural(ativos.filter((item) => item.hoje).length, 'prato', 'pratos')} no cardápio de hoje`, atencao: false }
  }
  return null
}

export function indicadoresDoHall(fontes, ids) {
  return ids.map((id) => {
    const fonte = fontes[id === 'cozinha' || id === 'entregas' ? 'pedidos' : id]
    const dados = fonte?.dados
    let valor = null
    let detalhe = ''
    // #1810: o que pede ação e aparece escrito no hall; o detalhe vai só no título do cartão.
    let alerta = null
    if (id === 'atendimento') {
      if (dados) valor = aguardandoResposta(dados)
      detalhe = 'Assumidas com resposta pendente ou sem responsável.'
    } else if (id === 'cozinha') {
      if (dados) valor = naFila(dados)
      detalhe = 'Fila de hoje e pendências de ontem.'
    } else if (id === 'entregas') {
      if (dados) valor = prontos(dados)
      detalhe = 'Pedidos prontos na fila de hoje e de ontem.'
    } else {
      if (Number.isFinite(dados?.saldoEsperado)) valor = dados.saldoEsperado
      if (dados?.aberturaPendenteCrossDay) alerta = 'Caixa de outro dia ainda aberto. Confira o fechamento.'
      detalhe = !dados ? '' : dados.aberturaPendenteCrossDay ? 'Caixa de outro dia ainda aberto. Confira o fechamento.' : dados.aberto ? 'Total do caixa, incluindo Pix e cartão. Confira a gaveta no financeiro.' : dados.fechado ? 'Caixa fechado hoje.' : 'Caixa do dia ainda não aberto.'
    }
    const atual = fonte?.estado === 'ok' && valor !== null
    return { id, valor, detalhe, alerta, atual, parcial: Boolean(dados?.parcial), estado: fonte?.estado ?? 'carregando', em: fonte?.em, erro: fonte?.erro }
  })
}
