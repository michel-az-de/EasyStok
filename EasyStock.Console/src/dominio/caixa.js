// Caixa do dia (rodada 13, issue #44). Modelado igual ao EasyStok (mapa da
// rodada, `auditoria/decisoes/106-r13-caixa.md`): não existe "sessão de
// caixa" como agregado, o dia é a AGREGAÇÃO de lançamentos `MovimentoCaixa`
// com `tipo` abertura/entrada/saida/fechamento e `categoria` texto livre
// (sangria e despesa são `saida` com categoria diferente, não tipos próprios).
// Puro: nenhuma função aqui lê relógio nem gera id sozinha, tudo chega por
// parâmetro, igual ao resto do domínio.
//
// Pagamento de pedido (Pix, maquininha etc., domínio de `dominio/cobranca.js`)
// NUNCA vira `MovimentoCaixa`: fica em `PedidoPagamento` (aqui, o registro que
// `dominio/pagamento.js: pagamentosDoPedido` já devolve) e o resumo do caixa
// SOMA os dois mundos sem misturar os dados de um dentro do outro.

import { inicioDoDiaNoFuso, moeda } from './formato.js'
import { pagamentosDoPedido } from './pagamento.js'
import { itemPorSku } from './cardapio.js'

export const TIPOS_MOVIMENTO = {
  ABERTURA: 'abertura', ENTRADA: 'entrada', SAIDA: 'saida', FECHAMENTO: 'fechamento',
}

// Sugestões de categoria (mapa EasyStok: "sangria e despesa são `saida` com
// categoria diferente"). Texto livre por baixo: a dona pode digitar outra
// categoria, estas são só atalho de um toque. #1474: suprimento é reforço de
// troco, dinheiro que ENTRA na gaveta; como saída ele subtraía da conta dela.
export const CATEGORIAS_SAIDA_SUGERIDAS = ['Sangria', 'Despesa']
// #1683: a venda de balcão recebida na hora não tinha atalho; a única entrada sugerida era o troco.
export const CATEGORIAS_ENTRADA_SUGERIDAS = ['Venda no balcão', 'Suprimento']

// #1683: fechar o caixa também decide a loja. O caixa do dia não reabre e a loja só abre com ele
// aberto (`aberturaDaLoja.js`, situação `fechadoHoje`).
export const AVISO_AO_FECHAR_CAIXA = 'Depois de fechar, o caixa de hoje não reabre. Sem caixa aberto, a loja não abre mais hoje. Os lançamentos de hoje não podem mais ser estornados.'

// #1584: a sugestão é do tipo. Trocar Entrada/Saída com "Sangria" marcada gravava uma entrada
// rotulada Sangria. A sugerida do outro tipo sai; a que a dona digitou fica.
export function categoriaAoTrocarTipo(categoria, novoTipo) {
  const doOutroTipo = novoTipo === TIPOS_MOVIMENTO.SAIDA ? CATEGORIAS_ENTRADA_SUGERIDAS : CATEGORIAS_SAIDA_SUGERIDAS
  return doOutroTipo.includes(categoria) ? '' : categoria
}

// Métodos de pagamento do CAIXA (mapa EasyStok, campo fechado, diferente do
// meio da cobrança por conversa: pix/dinheiro/credito/debito/transferencia/outro).
export const METODOS_CAIXA = ['pix', 'dinheiro', 'credito', 'debito', 'transferencia', 'outro']

const NOME_DO_METODO_CAIXA = {
  pix: 'Pix', dinheiro: 'Dinheiro', credito: 'Crédito', debito: 'Débito',
  transferencia: 'Transferência', outro: 'Outro',
}
export const nomeDoMetodoCaixa = (metodo) => NOME_DO_METODO_CAIXA[metodo] ?? 'Outro'

// Proposta ajustável para a Thati: o protótipo nasceu antes deste caixa
// existir e a cobrança por conversa (F4, `dominio/cobranca.js`) guarda um meio
// próprio (pix, cartão por link, maquininha, vale-refeição), diferente dos
// seis métodos fechados do caixa do EasyStok. Esta tabela só serve para o
// RESUMO do caixa somar os dois mundos numa mesma linha; ninguém decidiu ainda
// se a maquininha cai em crédito ou débito, nem se o vale vai para "outro".
// Ajustável aqui, sem mexer em mais nada.
const METODO_CAIXA_DA_COBRANCA = {
  pix: 'pix', 'cartao-link': 'credito', maquininha: 'credito', 'vale-refeicao': 'outro',
}
export const metodoCaixaDoMeio = (meio) => (
  METODOS_CAIXA.includes(meio) ? meio : (METODO_CAIXA_DA_COBRANCA[meio] ?? 'outro')
)

// Mesmo dia de calendário (o dia do caixa segue o relógio simulado da tela,
// nunca o relógio real da máquina). #1600: dia da loja, não do aparelho.
const mesmoDia = (a, b) => inicioDoDiaNoFuso(a) === inicioDoDiaNoFuso(b)

export const movimentosDoDia = (movimentos, agora) =>
  (movimentos ?? []).filter((m) => mesmoDia(m.criadoEm, agora))

export const caixaAberturaDoDia = (movimentos, agora) =>
  movimentosDoDia(movimentos, agora).find((m) => m.tipo === TIPOS_MOVIMENTO.ABERTURA) ?? null

export const caixaFechamentoDoDia = (movimentos, agora) =>
  movimentosDoDia(movimentos, agora).find((m) => m.tipo === TIPOS_MOVIMENTO.FECHAMENTO) ?? null

export const caixaAberto = (movimentos, agora) =>
  Boolean(caixaAberturaDoDia(movimentos, agora)) && !caixaFechamentoDoDia(movimentos, agora)

// Abrir de novo no mesmo dia não existe (um dia, uma abertura): quem quiser
// corrigir o saldo inicial lança uma entrada com a diferença.
export const podeAbrirCaixa = (movimentos, agora) => !caixaAberturaDoDia(movimentos, agora)

// Lançar (entrada/saída/venda avulsa) e fechar exigem o dia aberto e ainda não
// fechado. "Não é possível estornar movimento de dia já fechado" (mapa
// EasyStok) é a mesma trava, olhando o dia DO LANÇAMENTO, não o de hoje.
export const podeLancarNoCaixa = (movimentos, agora) => caixaAberto(movimentos, agora)

export function abrirCaixaMovimento(saldoInicial, agora, autorNome) {
  const valor = Math.max(Number(saldoInicial) || 0, 0)
  return {
    id: `mov-${agora}-abertura`,
    tipo: TIPOS_MOVIMENTO.ABERTURA,
    categoria: 'Abertura de caixa',
    valor,
    meio: null,
    descricao: '',
    criadoEm: agora,
    autorNome,
    estornadoEm: null,
    estornadoPorNome: null,
    motivoEstorno: null,
  }
}

// Entrada ou saída com categoria (aceite: "lançar entrada/saída com
// categoria"). `null` quando os dados não formam um lançamento válido (sem
// categoria, sem valor positivo): o caso do reducer decide o que fazer com o
// `null` (não muda o estado), esta função não precisa saber de estado nenhum.
export function criarMovimentoCaixa({
  tipo, categoria, valor, meio, descricao, agora, autorNome,
}) {
  if (tipo !== TIPOS_MOVIMENTO.ENTRADA && tipo !== TIPOS_MOVIMENTO.SAIDA) return null
  const valorNum = Number(valor)
  if (!Number.isFinite(valorNum) || valorNum <= 0) return null
  const categoriaLimpa = (categoria ?? '').trim()
  if (!categoriaLimpa) return null
  return {
    id: `mov-${agora}-${tipo}`,
    tipo,
    categoria: categoriaLimpa,
    valor: valorNum,
    meio: METODOS_CAIXA.includes(meio) ? meio : 'dinheiro',
    descricao: (descricao ?? '').trim(),
    criadoEm: agora,
    autorNome,
    estornadoEm: null,
    estornadoPorNome: null,
    motivoEstorno: null,
  }
}

// Estorno soft, no próprio lançamento (mapa EasyStok): `estornadoEm`,
// `estornadoPorNome`, `motivoEstorno`, nunca delete. Abertura e fechamento não
// se estornam (são o marco do dia, não dinheiro solto); dia fechado bloqueia
// (RN do mapa, texto literal "Não é possível estornar movimento de dia já
// fechado"); motivo é obrigatório, mesma trava de `dominio/cobranca.js:
// estornarCobranca`.
export function estornarMovimentoCaixa(movimento, agora, motivo, autorNome, diaFechado) {
  const motivoLimpo = (motivo ?? '').trim()
  if (!movimento || movimento.estornadoEm || !motivoLimpo) return movimento ?? null
  if (movimento.tipo === TIPOS_MOVIMENTO.ABERTURA || movimento.tipo === TIPOS_MOVIMENTO.FECHAMENTO) {
    return movimento
  }
  if (diaFechado) return movimento
  return {
    ...movimento, estornadoEm: agora, estornadoPorNome: autorNome, motivoEstorno: motivoLimpo,
  }
}

// Pagamentos de pedido do dia, líquidos de estorno (RN-36/UC-06: estorno
// devolve o valor, e o caixa precisa refletir isso). `conversas` é o estado
// de verdade (uma por cliente); cada uma contribui os pagamentos arquivados
// (`pedido.pagamentos`, cobranças substituídas) e o da cobrança ativa, se paga
// hoje. Comida não retorna (RN-36): o estorno não desfaz a venda, só o
// dinheiro, então o líquido pode ser zero mas a linha de venda continua
// existindo em `totalVendas` via `pedidosPagosNoDia`.
export function pagamentosDoDia(conversas, agora) {
  const linhas = []
  for (const conversa of conversas ?? []) {
    const pedido = conversa.pedido
    if (!pedido) continue
    for (const registro of pedido.pagamentos ?? []) {
      if (mesmoDia(registro.em, agora)) linhas.push({ valor: registro.valor, meio: registro.meio })
    }
    const cobranca = pedido.cobranca
    if (cobranca?.pagaEm && mesmoDia(cobranca.pagaEm, agora)) {
      const bruto = cobranca.valorPago ?? cobranca.valor
      const estornoHoje = cobranca.estornadaEm && mesmoDia(cobranca.estornadaEm, agora)
        ? cobranca.valorEstornado ?? 0
        : 0
      const liquido = bruto - estornoHoje
      if (liquido !== 0) linhas.push({ valor: liquido, meio: cobranca.meio ?? 'pix' })
    }
  }
  return linhas
}

// Pedidos com pagamento reconhecido hoje (bruto, sem descontar estorno): é o
// "TotalVendas" do EasyStok, a régua para comparar com o que efetivamente
// entrou (TotalPagamentosPedidos). Reusa `pagamentosDoPedido`
// (dominio/pagamento.js), mesma fonte que `dominio/resumoDoDia.js` já usa.
export function pedidosPagosNoDia(conversas, agora) {
  const vistos = new Set()
  const pedidos = []
  for (const conversa of conversas ?? []) {
    const pedido = conversa.pedido
    if (!pedido || vistos.has(pedido.numero)) continue
    const pago = pagamentosDoPedido(pedido).some((p) => mesmoDia(p.em, agora))
    if (pago) {
      vistos.add(pedido.numero)
      pedidos.push(pedido)
    }
  }
  return pedidos
}

// Vendas do dia para a lista da aba Caixa (aceite: "estorno em um toque... a
// partir do caixa e do pedido", RN-36/UC-06). O estorno em si REAPROVEITA a
// ação `marcarEstorno` que já existe (Frente Cobrança/Reclamação, RN-34 a
// RN-38): esta função só decide quais pedidos entram na lista e com que
// rótulo, a tela é quem chama a ação de verdade.
export function vendasDoDia(conversas, agora) {
  const linhas = []
  for (const conversa of conversas ?? []) {
    const pedido = conversa.pedido
    const cobranca = pedido?.cobranca
    if (!cobranca?.pagaEm || !mesmoDia(cobranca.pagaEm, agora)) continue
    linhas.push({
      conversaId: conversa.id,
      numero: pedido.numero,
      nome: conversa.nome,
      valor: cobranca.valorPago ?? cobranca.valor,
      // Cobrança sem `meio` é da massa anterior à Frente Cobrança (mesma nota
      // de `dominio/cobranca.js: nomeDoMeio`): mesmo default 'pix' das duas
      // leituras, senão o resumo por método (que já aplica esse default via
      // `pagamentosDoDia`) e esta lista discordavam entre si.
      meio: metodoCaixaDoMeio(cobranca.meio ?? 'pix'),
      estornado: Boolean(cobranca.estornadaEm),
      valorEstornado: cobranca.valorEstornado ?? 0,
    })
  }
  return linhas
}

// Resumo em tempo real (aceite: "resumo por método em tempo real"). `cardapio`
// entra só para `totalDoPedido`; nenhuma outra conta de estoque mora aqui.
export function resumoCaixa(movimentos, conversas, cardapio, agora) {
  const doDia = movimentosDoDia(movimentos, agora).filter((m) => !m.estornadoEm)
  const abertura = doDia.find((m) => m.tipo === TIPOS_MOVIMENTO.ABERTURA) ?? null
  const entradas = doDia.filter((m) => m.tipo === TIPOS_MOVIMENTO.ENTRADA)
  const saidas = doDia.filter((m) => m.tipo === TIPOS_MOVIMENTO.SAIDA)
  const saldoInicial = abertura?.valor ?? 0
  const totalEntradasExtras = entradas.reduce((soma, m) => soma + m.valor, 0)
  const totalSaidasExtras = saidas.reduce((soma, m) => soma + m.valor, 0)

  const pagamentos = pagamentosDoDia(conversas, agora)
  const totalPagamentosPedidos = pagamentos.reduce((soma, p) => soma + p.valor, 0)

  // "TotalVendas" soma o total da COMANDA (não só o que caiu líquido): pedido
  // com estorno parcial continua vendido por inteiro, só recebeu menos.
  const totalVendas = pedidosPagosNoDia(conversas, agora)
    .reduce((soma, pedido) => soma + (pedido.itens ?? []).reduce((s, linha) => {
      const item = itemPorSku(cardapio ?? [], linha.sku) // M1.4b: linha de porção tem sku composto
      return s + (item?.preco ?? 0) * linha.qtd
    }, 0), 0)

  const porMetodo = new Map()
  const soma = (chave, valor) => porMetodo.set(chave, (porMetodo.get(chave) ?? 0) + valor)
  for (const p of pagamentos) soma(metodoCaixaDoMeio(p.meio), p.valor)
  for (const m of entradas) soma(m.meio, m.valor)
  for (const m of saidas) soma(m.meio, -m.valor)

  const saldoEsperado = saldoInicial + totalEntradasExtras - totalSaidasExtras + totalPagamentosPedidos

  return {
    aberto: caixaAberto(movimentos, agora),
    abertura,
    saldoInicial,
    totalEntradasExtras,
    totalSaidasExtras,
    totalPagamentosPedidos,
    totalVendas,
    saldoEsperado,
    porMetodo: [...porMetodo.entries()]
      .filter(([, valor]) => valor !== 0)
      .map(([metodo, valor]) => ({ metodo, nome: nomeDoMetodoCaixa(metodo), valor })),
    lancamentosEntrada: entradas,
    lancamentosSaida: saidas,
    quantidadeVendas: pagamentos.length,
  }
}

// Snapshot imutável `FechamentoCaixa` (mapa EasyStok: SaldoInicial, TotalVendas,
// TotalPagamentosPedidos, TotalEntradasExtras, TotalSaidasExtras, SaldoFinal),
// carregado dentro do MARCADOR `MovimentoCaixa` tipo `fechamento` (o protótipo
// guarda tudo numa lista só de lançamentos, sem uma segunda coleção). Conferência
// (aceite): `contado` é o que ela contou na mão, `diferenca` é contado - esperado.
export function fecharCaixaMovimento(resumo, contado, agora, autorNome) {
  const contadoNum = Number(contado)
  const saldoContado = Number.isFinite(contadoNum) ? contadoNum : resumo.saldoEsperado
  return {
    id: `mov-${agora}-fechamento`,
    tipo: TIPOS_MOVIMENTO.FECHAMENTO,
    categoria: 'Fechamento do dia',
    valor: resumo.saldoEsperado,
    meio: null,
    descricao: '',
    criadoEm: agora,
    autorNome,
    estornadoEm: null,
    estornadoPorNome: null,
    motivoEstorno: null,
    fechamento: {
      saldoInicial: resumo.saldoInicial,
      totalVendas: resumo.totalVendas,
      totalPagamentosPedidos: resumo.totalPagamentosPedidos,
      totalEntradasExtras: resumo.totalEntradasExtras,
      totalSaidasExtras: resumo.totalSaidasExtras,
      saldoFinal: resumo.saldoEsperado,
      saldoContado,
      diferenca: saldoContado - resumo.saldoEsperado,
    },
  }
}

// --- Caixa do dia no modo API: gaveta separada (#1474) ----------------------
// O "saldo esperado" da API soma tudo que entrou no dia, Pix e cartão também. A dona
// confere a GAVETA, então a conta dela é só dinheiro: saldo inicial + entradas em
// dinheiro - saídas em dinheiro + pagamentos e vendas em dinheiro. Lançamento estornado
// não conta. Movimento sem método é do caixa físico (nasceu antes de o método existir);
// pagamento sem método fica fora da gaveta, porque ninguém sabe onde ele caiu.
const centavos = (v) => Math.round((Number(v) || 0) * 100)
const ehDinheiro = (meio, semMetodoEhDinheiro) => meio === 'dinheiro' || (meio == null && semMetodoEhDinheiro)
const sinalDoMovimento = (tipo) => (tipo === TIPOS_MOVIMENTO.ENTRADA ? 1 : tipo === TIPOS_MOVIMENTO.SAIDA ? -1 : 0)

// #1627: o que veio sem forma de pagamento fica à parte (`semForma`). A venda do pedido entregue
// pelo PWA nasce sem forma no EasyStok (#1630); somada em "Pix e cartão", a tela afirmava que
// dinheiro vivo estava fora da gaveta e a conferência do fechamento dava sobra.
// #1683: `pixECartao` soma tudo o que não é dinheiro (transferência, outro e vale também); o
// rótulo passou a dizer isso. A conta não mudou.
export const ROTULO_FORA_DA_GAVETA = 'Fora da gaveta (Pix, cartão e outros)'
export const ROTULO_FORA_DO_DINHEIRO = 'Pix, cartão e outros'
export function gavetaDoDia(dia) {
  let gaveta = centavos(dia?.saldoInicial)
  let fora = 0
  let semForma = 0
  for (const m of dia?.movimentos ?? []) {
    if (m.estornadoEm) continue
    const valor = sinalDoMovimento(m.tipo) * centavos(m.valor)
    if (ehDinheiro(m.meio, true)) gaveta += valor
    else fora += valor
  }
  for (const l of dia?.linhasExtras ?? []) {
    if (ehDinheiro(l.meio, false)) gaveta += centavos(l.valor)
    else if (l.meio == null) semForma += centavos(l.valor)
    else fora += centavos(l.valor)
  }
  return { naGaveta: gaveta / 100, pixECartao: fora / 100, semForma: semForma / 100 }
}

// --- Fluxo de caixa de vários dias (#1624) ----------------------------------
// Uma linha por dia, com as palavras da dona: entrou (vendas, pagamentos de pedido e outras
// entradas), saiu (saídas) e o saldo do dia. O saldo inicial fica de fora: é dinheiro que já
// estava na gaveta, não entrou no dia. `sem-abertura` é dia com dinheiro e sem "abrir o caixa":
// o PWA não abre caixa, e Pix de pedido entra de qualquer jeito.
//
// #1627: suprimento (reforço de troco) e sangria (retirada para o cofre) só trocam o dinheiro de
// lugar: ficam fora de entrou e de saiu, senão um dia de 300 em vendas com 100 de suprimento e 350
// de sangria mostrava "saldo do dia 50". Vale para a categoria sugerida pela tela; categoria que a
// dona escreveu de outro jeito continua contando.
// `depoisDoFechamento`: a API recalcula o dia fechado ao vivo; Pix que entra depois de fechar muda
// o dia e o retrato do fechamento não. A diferença vai na linha, para as duas tabelas não se
// contradizerem em silêncio.
const TROCAS_DE_GAVETA = new Set(['suprimento', 'sangria'])
const ehTrocaDeGaveta = (movimento) => TROCAS_DE_GAVETA.has(String(movimento.categoria ?? '').trim().toLocaleLowerCase('pt-BR'))
const trocasDoDia = (dia, tipo) => (dia?.movimentos ?? [])
  .filter((m) => m.tipo === tipo && !m.estornadoEm && ehTrocaDeGaveta(m))
  .reduce((soma, m) => soma + centavos(m.valor), 0)
const resultadoEmCentavos = (totais) =>
  centavos(totais?.totalVendas) + centavos(totais?.totalPagamentosPedidos) + centavos(totais?.totalEntradasExtras) - centavos(totais?.totalSaidasExtras)

export function linhaDoFluxo(dia) {
  const entrou = centavos(dia?.totalVendas) + centavos(dia?.totalPagamentosPedidos) + centavos(dia?.totalEntradasExtras) - trocasDoDia(dia, TIPOS_MOVIMENTO.ENTRADA)
  const saiu = centavos(dia?.totalSaidasExtras) - trocasDoDia(dia, TIPOS_MOVIMENTO.SAIDA)
  // A API manda `aberto` também no dia que já fechou: fechado vem primeiro.
  const situacao = dia?.fechado ? 'fechado' : dia?.aberto ? 'aberto' : entrou !== 0 || saiu !== 0 ? 'sem-abertura' : 'sem-movimento'
  const depois = dia?.fechado && dia?.fechamento ? resultadoEmCentavos(dia) - resultadoEmCentavos(dia.fechamento) : 0
  return { data: dia?.data ?? null, entrou: entrou / 100, saiu: saiu / 100, saldo: (entrou - saiu) / 100, situacao, depoisDoFechamento: depois / 100 }
}

// As linhas da tabela: hoje primeiro, depois os dias anteriores. Caixa esquecido aberto (#596): a
// linha de hoje soma a sessão desde a abertura, então os dias que já estão dentro dela saem da
// tabela (somariam duas vezes) e a linha diz desde quando (`desde`).
export function linhasDoFluxo(hoje, anteriores) {
  const desde = hoje?.esquecidoAberto ? hoje.abertoDesde ?? null : null
  return [
    { ...linhaDoFluxo(hoje), desde },
    ...(anteriores ?? []).filter((d) => !desde || d.data < desde).map((d) => ({ ...linhaDoFluxo(d), desde: null })),
  ]
}

export function totaisDoFluxo(linhas) {
  const soma = (campo) => (linhas ?? []).reduce((total, l) => total + centavos(l[campo]), 0) / 100
  return { entrou: soma('entrou'), saiu: soma('saiu'), saldo: soma('saldo') }
}

// O dia tem dinheiro para mostrar mesmo sem abertura? A rota do dia não devolve lançamento estornado:
// dia cujo único lançamento foi estornado não tem movimento.
export const temMovimentoNoDia = (dia) =>
  (dia?.movimentos ?? []).some((m) => m.tipo !== TIPOS_MOVIMENTO.ABERTURA && m.tipo !== TIPOS_MOVIMENTO.FECHAMENTO)
  || (dia?.linhasExtras ?? []).length > 0
  || [dia?.totalVendas, dia?.totalPagamentosPedidos, dia?.totalEntradasExtras, dia?.totalSaidasExtras].some((v) => centavos(v) !== 0)

// Lançamentos do dia numa lista só, na ordem da hora (movimentos e pagamentos vinham em
// dois blocos e a sangria das 20:30 aparecia entre 20:18 e 20:28). DateTime sem fuso vem
// da API em UTC: o Z completa antes de comparar.
const instanteDe = (valor) => Date.parse(/[zZ]|[+-]\d{2}:\d{2}$/.test(valor ?? '') ? valor : `${valor}Z`)

export function lancamentosEmOrdem(dia) {
  const movimentos = (dia?.movimentos ?? [])
    .filter((m) => m.tipo !== TIPOS_MOVIMENTO.FECHAMENTO)
    .map((m) => ({ ...m, origemLinha: 'movimento' }))
  const extras = (dia?.linhasExtras ?? []).map((l) => ({ ...l, origemLinha: 'extra' }))
  return [...movimentos, ...extras].sort((a, b) => instanteDe(a.em) - instanteDe(b.em))
}

// Conferência da gaveta (#1474): o campo "contei" nasce vazio (`null`), porque nascer com o
// esperado mostrava "bate certo" sem ninguém contar. Diferença dita do jeito que ela fala:
// faltam, sobram ou bate certo. Até 5 reais de diferença é aviso; acima, perigo.
export function conferenciaDaGaveta(centavosContados, esperado) {
  if (centavosContados == null) return { pronta: false, texto: null, tom: 'neutro', diferenca: null }
  const diferenca = (centavosContados - centavos(esperado)) / 100
  if (diferenca === 0) return { pronta: true, texto: 'Bate certo', tom: 'ok', diferenca }
  const texto = `${diferenca < 0 ? 'Faltam' : 'Sobram'} ${moeda(Math.abs(diferenca))}`
  return { pronta: true, texto, tom: Math.abs(diferenca) <= 5 ? 'aviso' : 'perigo', diferenca }
}
