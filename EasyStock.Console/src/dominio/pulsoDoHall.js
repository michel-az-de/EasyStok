// Contagens da leitura atual. Não interpreta SLA sem o expediente da loja.
export function indicadoresDoHall(fontes, ids) {
  return ids.map((id) => {
    const fonte = fontes[id === 'cozinha' || id === 'entregas' ? 'pedidos' : id]
    const dados = fonte?.dados
    let valor = null
    let detalhe = ''
    // #1810: o que pede ação e aparece escrito no hall; o detalhe vai só no título do cartão.
    let alerta = null
    if (id === 'atendimento') {
      if (dados) valor = dados.itens.filter((c) => c.situacao === 'Assumida' && (c.aguardaResposta || !c.assumidaPorUsuarioId)).length
      detalhe = 'Assumidas com resposta pendente ou sem responsável.'
    } else if (id === 'cozinha') {
      if (dados) valor = dados.itens.filter((p) => ['aguardando', 'preparando'].includes(p.status)).length
      detalhe = 'Fila de hoje e pendências de ontem.'
    } else if (id === 'entregas') {
      if (dados) valor = dados.itens.filter((p) => p.status === 'pronto').length
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
