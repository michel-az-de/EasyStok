import { gavetaDoDia } from './caixa'
import { colunaDoStatusKds } from './kds'

// Contagens da leitura atual. Não interpreta SLA sem o expediente da loja.
const aguardandoResposta = (dados) => dados.itens.filter((c) => c.situacao === 'Assumida' && (c.aguardaResposta || !c.assumidaPorUsuarioId)).length
// #2055: `colunaDoStatusKds` põe o aprovado pela dona (`aprovado_baba`) em Na fila, como a Cozinha.
const naFila = (dados) => dados.itens.filter((p) => ['aguardando', 'preparando'].includes(colunaDoStatusKds(p.status))).length
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
    if (dados.esquecidoAberto) return { texto: 'Caixa de outro dia aberto', atencao: true }
    // #2078 (B2): a API manda `aberto` também no dia que já fechou; fechado vem primeiro (`caixa.js: linhaDoFluxo`).
    return { texto: dados.fechado ? 'Caixa fechado hoje' : dados.aberto ? 'Caixa aberto' : 'Caixa do dia ainda fechado', atencao: false }
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
    let rotulo = null
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
      // #2078 (B7/E3/D7): o número é o que a tela do Caixa destaca, com o mesmo rótulo: o dinheiro da
      // gaveta (`gavetaDoDia`). O `saldoEsperado` soma Pix e cartão e não é o que ela confere. Sem abrir
      // o caixa ninguém disse quanto havia na gaveta: a tela diz "Em dinheiro", e o Hall também.
      rotulo = dados && !dados.aberto && !dados.fechado ? 'Em dinheiro' : 'Na gaveta (dinheiro)'
      if (Number.isFinite(dados?.saldoEsperado)) valor = gavetaDoDia(dados).naGaveta
      if (dados?.esquecidoAberto) alerta = 'Caixa de outro dia ainda aberto. Confira o fechamento.'
      detalhe = !dados ? '' : dados.esquecidoAberto ? 'Caixa de outro dia ainda aberto. Confira o fechamento.'
        : dados.fechado ? 'Caixa fechado hoje. Dinheiro que ficou na gaveta.'
          : dados.aberto ? 'Dinheiro na gaveta agora. Pix e cartão ficam no Caixa do dia.' : 'Caixa do dia ainda não aberto. Dinheiro que já entrou hoje.'
    }
    const atual = fonte?.estado === 'ok' && valor !== null
    return { id, valor, rotulo, detalhe, alerta, atual, parcial: Boolean(dados?.parcial), estado: fonte?.estado ?? 'carregando', em: fonte?.em, erro: fonte?.erro }
  })
}
