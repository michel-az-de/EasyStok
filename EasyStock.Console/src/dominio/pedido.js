import { itemPorSku } from './cardapio.js'
import { passoPorId } from './esteira.js'
import { faixaDeHorarios } from './entrega.js'
import { dataCurtaComSemana } from './formato.js'

// #1683: prato que saiu do cardápio visível depois do pedido não some da Ficha. A linha da API
// traz o retrato (nome, preço unitário, porção) e ele vira o produto quando o cardápio não tem.
const produtoDoRetrato = (linha) => (linha.nome
  ? { sku: linha.sku, nome: linha.nome, preco: linha.precoUnitario ?? 0, porcao: linha.porcao ?? '', estoque: null, foraDoCardapio: true }
  : null)

export function itensDetalhados(pedido, cardapio) {
  if (!pedido) return []
  return pedido.itens.map((linha) => ({ ...linha, produto: itemPorSku(cardapio, linha.sku) ?? produtoDoRetrato(linha) }))
}

// Segue só pelo cardápio: o retrato existe em pedido do EasyStok, e nele o total é o do servidor.
export function totalDoPedido(pedido, cardapio) {
  if (!pedido) return 0
  return pedido.itens
    .reduce((soma, linha) => soma + (itemPorSku(cardapio, linha.sku)?.preco ?? 0) * linha.qtd, 0)
}

// Preço da linha na Ficha: o que o pedido gravou; sem retrato (rascunho local), o do cardápio.
export const precoDaLinha = (linha) => (linha.precoUnitario ?? linha.produto?.preco ?? 0) * linha.qtd

// `diferencaAposPagamento` mudou para `dominio/pagamento.js` na integração:
// "quanto já foi pago" tem uma fonte só (registro 38).

// Pedido novo nasce aguardando pagamento, nunca pago. O número vem de fora
// para o domínio não depender de contador global. `pagamentos` (rodada 5,
// seção 3.2 e 4) é a soma de tudo que já caiu no pedido: a cobrança original
// e qualquer complemento, lida por `dominio/pagamento.js`.
//
// `entregador` nasce `null`: US-040, ninguém foi escalado ainda, e um texto
// fixo aqui vira mentira assim que o pedido despachar sem a dona escolher
// quem leva (`dominio/viagem.js: entregadorResolvido`, `dominio/esteira.js:
// avisoDoPasso`).
//
// `janela` também nasce `null` (#1474): o 'j3' fixo virava "Janela 18h30 às 19h30" na comanda
// sem ninguém ter escolhido janela nenhuma.
export const novoPedido = (numero) => ({
  numero, estado: 'aguardando', janela: null, entregador: null, itens: [],
  agradecimentoEnviado: false, pagamentos: [],
})

// Item repetido soma quantidade em vez de virar segunda linha, MAS só quando
// a anotação é igual e os dois são (ou não são) acréscimo: mesmo prato com
// anotação diferente vira linha própria (seção 3.3, "Dois tortéis, um sem
// tempero" é o caso real). `acrescimo`/`entrouEm` marcam item que entrou na
// comanda depois do pagamento (seção 3.2); item normal nasce com os dois
// vazios.
export function comItem(pedido, sku, { obs = '', acrescimo = false, entrouEm = null } = {}) {
  const existente = pedido.itens.find(
    (linha) => linha.sku === sku && linha.obs === obs && Boolean(linha.acrescimo) === acrescimo,
  )
  if (existente) {
    return {
      ...pedido,
      itens: pedido.itens.map((l) => (l === existente ? { ...l, qtd: l.qtd + 1 } : l)),
    }
  }
  return { ...pedido, itens: [...pedido.itens, { sku, qtd: 1, obs, acrescimo, entrouEm }] }
}

export function semItem(pedido, sku) {
  return { ...pedido, itens: pedido.itens.filter((linha) => linha.sku !== sku) }
}

// Observação mora na linha do item, nunca no rodapé do pedido (RN-20): é o
// "sem queijo" que a cozinha precisa ler junto do prato, não depois dele.
export function comObservacao(pedido, sku, texto) {
  return {
    ...pedido,
    itens: pedido.itens.map((linha) => (linha.sku === sku ? { ...linha, obs: texto } : linha)),
  }
}

// Cancelado encerra igual a entregue: nenhum dos dois edita comanda nem anda
// na esteira (secao 5 da direção visual, "Cancelado" fica fora da trilha).
export const pedidoEncerrado = (pedido) =>
  pedido?.estado === 'entregue' || pedido?.estado === 'cancelado'

// #1587: no modo API o pedido já criado é do EasyStok e o console não tem como mudar os itens
// dele (a ação só avisava no rodapé). A tela trava somar, tirar e anotar, e diz o motivo no lugar.
export const comandaTravada = (pedido, { fonteApi = false } = {}) => fonteApi && Boolean(pedido?.pedidoId)

// Número curto de quatro dígitos, o mesmo formato que a comanda e o canhoto
// usam de relance (ex.: "2026-0186" vira "0186").
export const numeroCurto = (numero) => String(numero).split('-').at(-1)

// #1883 (parte do console, #1899): um nome só para o pedido nas três telas. "Comanda N" é o que a
// atendente e o cliente dizem em voz alta; o código curto do EasyStok (8 letras do id) fica como
// referência pequena para achar o pedido no suporte. Enquanto a API não entrega `numeroComanda`
// em Entregas e KDS, vale o curto sozinho, sem inventar número.
// J2 (#1990, #1641-1 parte console): o "ref DB72EC58" na tela era id técnico; o curto fica só no título
// (dica do mouse, para o suporte). A referência pequena ao lado da comanda passa a ser o número do dia
// (`numeroDoDia`, a sequência que a cozinha fala), quando a API manda; ausente ou nulo, nada.
export const numeroDoDiaNaTela = (n) => (Number.isInteger(n) && n > 0 ? String(n).padStart(3, '0') : null)
export function nomeDoPedido({ numeroComanda = null, numeroCurto = null, numeroDoDia = null } = {}) {
  const curto = numeroCurto ? String(numeroCurto) : null
  const dia = numeroDoDiaNaTela(numeroDoDia)
  const titulo = (partes) => [...partes, curto ? `ref ${curto}` : null].filter(Boolean).join(' · ')
  if (numeroComanda) {
    const frase = `Comanda ${numeroComanda}`
    return { principal: frase, referencia: dia ? `nº ${dia}` : null, frase, titulo: titulo([frase, dia ? `nº ${dia}` : null]) }
  }
  if (dia) return { principal: `Nº ${dia}`, referencia: null, frase: `Pedido nº ${dia}`, titulo: titulo([`Nº ${dia}`]) }
  return { principal: curto ?? '', referencia: null, frase: curto ? `Pedido ${curto}` : 'Pedido', titulo: curto ?? '' }
}

// #1474 (R6): no modo API o número é o do EasyStok (8 letras, o mesmo da Cozinha e das
// Entregas). Antes de o pedido existir não há número: o "0001" local seria inventado.
export function numeroDaComanda(pedido, { fonteApi = false } = {}) {
  if (pedido?.pedidoId) return numeroCurto(pedido.numero)
  return fonteApi ? null : numeroCurto(pedido?.numero ?? '')
}

// Ordem fixa da linha de produção na cozinha: quem prepara na hora primeiro,
// quem é para levar e finalizar em casa depois (QA2-19, conteúdo do canhoto).
// A comanda em tela e o canhoto impresso agrupam pela mesma ordem.
export const ORDEM_DAS_LINHAS = ['servir', 'casa']

export function agruparPorLinha(itens, linhas) {
  return [
    ...ORDEM_DAS_LINHAS.map((chave) => ({
      chave,
      rotulo: linhas[chave]?.rotulo ?? chave,
      itens: itens.filter((l) => l.produto?.linha === chave),
    })),
    // #1683: linha que só tem o retrato do pedido não sabe a linha de produção; fica por último.
    { chave: 'fora', rotulo: 'Fora do cardápio', itens: itens.filter((l) => l.produto?.foraDoCardapio) },
  ].filter((grupo) => grupo.itens.length > 0)
}

// #1683/#1712: no conflito, conserva os dados atuais do cliente e reaplica itens e observações.
// O horário local só prevalece quando difere da base da edição.
export const rascunhoMantendoEdicao = (doServidor, daAtendente, baseDaEdicao = daAtendente) => ({
  ...doServidor, itens: daAtendente.itens, observacoes: daAtendente.observacoes,
  ...((daAtendente.janelaId ?? null) !== (baseDaEdicao.janelaId ?? null)
    || (daAtendente.dataEntrega ?? null) !== (baseDaEdicao.dataEntrega ?? null)
    ? { janelaId: daAtendente.janelaId, dataEntrega: daAtendente.dataEntrega } : {}),
})

// J2 (#1990), item 7: a diferença de pagamento da alteração de um pedido pago, ao vivo. O total do
// servidor (`totalPrevisto`) só muda depois de salvar; enquanto ela edita, soma-se a diferença entre
// os itens editados e os da seleção salva, pelo mesmo preço que a tela mostra em cada linha (o da
// seleção, ou o do cardápio para prato novo). Positivo é mais a pagar; negativo, a devolver.
// Devolução em acompanhamento segue o saldo do servidor.
const chaveDoItem = (i) => (i.variacaoId ? `${i.cardapioItemId}~${i.variacaoId}` : i.cardapioItemId)
function somaDosItens(itens, selecao, catalogo) {
  return (itens ?? []).reduce((soma, i) => {
    const atual = (selecao ?? []).find((s) => chaveDoItem(s) === chaveDoItem(i))
    const preco = atual?.preco ?? (catalogo ?? []).find((p) => p.sku === chaveDoItem(i))?.preco ?? 0
    return soma + i.qtd * preco
  }, 0)
}
export function saldoDaAlteracao(c, rascunhoEditado = null, catalogo = []) {
  // #1987: pedido entregue ou cancelado sai do link e não tem diferença a acertar por aqui.
  if (c.pedidoEncerrado) return 0
  if (c.envio?.situacao === 'estorno_pendente') return c.envio.saldo
  const pendente = c.envio?.situacao === 'alteracao_aguardando_confirmacao'
  const total = c.podeEditar || pendente ? c.totalPrevisto ?? 0 : c.pedido?.total ?? c.totalPrevisto ?? 0
  const pago = c.pedido?.alocado ?? c.pedido?.totalPago ?? 0
  const delta = c.podeEditar && rascunhoEditado
    ? somaDosItens(rascunhoEditado.itens, c.selecao, catalogo) - somaDosItens(c.rascunho?.itens, c.selecao, catalogo)
    : 0
  return Math.round((total + delta - pago) * 100) / 100
}

// #1683: o cliente montando a escolha pelo link, sem pedido ainda. A comanda de baixo não soma
// item por conta própria, senão a Ficha mostra duas listas e dois totais da mesma comanda.
// Item que a atendente já pôs no rascunho local segue editável para ela poder tirar.
export const clienteEscolhendoPeloLink = (comandas, pedido) => !pedido?.pedidoId && !pedido?.itens?.length
  && (comandas ?? []).some((c) => c.podeEditar && !c.pedido && (c.rascunho?.itens?.length > 0 || c.ajudaSolicitada))

export const LINHA_CLIENTE_ESCOLHENDO =
  'O cliente está escolhendo pelo link. Abra Revisar itens para acompanhar os itens antes de confirmar o pedido.'

// #1761: a Ficha dizia "Pedido em montagem" com o pedido já confirmado e pago. O título segue o
// pedido: montagem só enquanto ele não existe no EasyStok.
export function tituloDaComanda(pedido, { temLinkComPedido = false } = {}) {
  if (temLinkComPedido) return 'Link do cliente'
  if (!pedido?.pedidoId) return 'Pedido em montagem'
  if (pedido.estado === 'cancelado') return 'Pedido cancelado'
  if (pedido.estado === 'aguardando') return 'Pedido aguardando pagamento'
  return 'Pedido confirmado'
}

// #1761 (L18): a porção embaixo do nome repetia o que o nome já diz ("...600 g" e "600 g").
export function porcaoForaDoNome(produto) {
  const porcao = String(produto?.porcao ?? '').trim()
  if (!porcao) return ''
  // Espaço entre o número e a unidade não conta: "600g" e "600 g" são a mesma porção.
  const escapada = [...porcao.toLowerCase().replace(/\s+/g, '')]
    .map((letra) => letra.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('\\s*')
  const noNome = new RegExp(`(^|[^\\p{L}\\p{N}])${escapada}($|[^\\p{L}\\p{N}])`, 'u')
  return noNome.test(String(produto.nome ?? '').toLowerCase()) ? '' : porcao
}

// #1761 (L3): o pedido acabou de nascer no EasyStok nesta conversa (a cobrança foi gerada). É a
// hora de fechar o cardápio: dali em diante ele só consulta e cobria a lista e a conversa.
export const pedidoAcabouDeNascer = (antes, depois) => Boolean(depois?.pedidoId)
  && !antes?.pedidoId && antes?.conversaId === depois.conversaId

// #1683: sem pedido e sem frete combinado o EasyStok soma só os itens; o cliente já vê o frete.
export function rotuloDoTotalDaSelecao(comanda, emEdicao = false) {
  const semFrete = !comanda.pedido && comanda.freteCombinado == null
  if (emEdicao) return 'Total dos itens salvos' + (semFrete ? ', sem frete' : '')
  return semFrete ? 'Total dos itens, sem frete' : 'Total previsto'
}

// #1683: o histórico traz o estado da esteira (demonstração) ou o status do EasyStok em
// minúsculas e com espaço (`traducaoCliente.js`). Nenhum dos dois vai cru para a tela.
const ROTULO_FORA_DA_ESTEIRA = {
  cancelado: 'Cancelado',
  rascunho: 'Rascunho',
  'aguardando pagamento': 'Aguardando pagamento',
  'aguardando aprovacao baba': 'Aguardando aprovação',
  'aprovado baba': 'Pago',
  preparando: 'Em preparo',
  pronto: 'Pronto',
  'saiu para entrega': 'Saiu para entrega',
}

export function rotuloDoEstado(estado) {
  const texto = String(estado ?? '')
  return passoPorId(texto)?.rotulo ?? ROTULO_FORA_DA_ESTEIRA[texto] ?? texto.charAt(0).toUpperCase() + texto.slice(1)
}

// #1865: encomenda com só o sinal recebido entra como "aprovado baba"; com saldo em aberto não é "Pago".
// #1920: pedido na entrega também entra aprovado, sem dinheiro nenhum. `pago` é o que entrou (modo API);
// sem ele (demonstração) vale a regra de antes.
export function rotuloDoResumo(estado, saldo, pago = null) {
  const rotulo = rotuloDoEstado(estado)
  if (rotulo !== 'Pago' || !(saldo > 0)) return rotulo
  return pago === 0 ? 'A receber na entrega' : 'Sinal pago'
}

// #1709: CEP sem cobertura volta do EasyStok com a frase escrita para o cliente
// (`CepSemCoberturaException`, sem código próprio na resposta). No console quem lê é a equipe:
// a linha orienta os caminhos existentes sem presumir uma área de entrega configurada.
const CEP_SEM_COBERTURA = 'Vamos combinar a entrega e o frete com a equipe.'
const FRETE_A_COMBINAR = 'o frete está a combinar. Mande o Link de compra para combinar o valor com o cliente, ou cadastre a região em Entregas, Janelas de entrega, Frete.'

export function motivoDoPedidoNaoCriado(erro) {
  const mensagem = String(erro?.message ?? '').trim()
  return `Pedido não criado: ${mensagem === CEP_SEM_COBERTURA ? FRETE_A_COMBINAR : mensagem}`
}

// --- Alteração com data nova (#2007, item 4) ------------------------------------------------------
// O cliente pode trocar a data ou a janela pelo link de ajuste. A base da comparação é a do pedido quando o rascunho foi
// salvo (`dataDoPedido`, `janelaDoPedidoId`, como a API compara em `DataPedidaPeloCliente`); sem ela, a janela atual.
export function mudancaDeDataDaAlteracao(comanda) {
  const r = comanda?.rascunho
  const atual = comanda?.pedido?.janela ?? null
  if (!comanda?.pedido || !r?.dataEntrega || !r.janelaId) return null
  const de = { data: r.dataDoPedido ?? atual?.data ?? null, janelaId: r.janelaDoPedidoId ?? atual?.janelaId ?? null }
  if (de.data === r.dataEntrega && de.janelaId === r.janelaId) return null
  return { de, para: { data: r.dataEntrega, janelaId: r.janelaId }, mesmaJanela: de.janelaId === r.janelaId }
}

const hhmm = (h) => String(h ?? '').slice(0, 5)
// "9h00 às 11h00 · Manhã", da janela que a API devolve no pedido.
export const rotuloDaJanelaDoPedido = (j) => (j
  ? [faixaDeHorarios(hhmm(j.horaInicio), hhmm(j.horaFim)), j.label].filter(Boolean).join(' · ') || null
  : null)

// As linhas que "Comparar e aprovar" mostra. `rotuloNovo` só existe quando a tela já leu a janela escolhida.
export function linhasDaMudancaDeData(mudanca, { rotuloAtual = null, rotuloNovo = null } = {}) {
  if (!mudanca) return []
  const linhas = []
  if (mudanca.de.data !== mudanca.para.data) {
    linhas.push(`Data: de ${mudanca.de.data ? dataCurtaComSemana(mudanca.de.data) : 'sem data'} para ${dataCurtaComSemana(mudanca.para.data)}`)
  }
  if (mudanca.mesmaJanela) {
    if (rotuloAtual) linhas.push(`Janela: ${rotuloAtual} (mantida)`)
  } else {
    linhas.push(`Janela: ${rotuloAtual ? `de ${rotuloAtual} ` : ''}para ${rotuloNovo ?? 'outra janela escolhida pelo cliente'}`)
  }
  return linhas
}
