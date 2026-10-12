// Domínio do resumo de encerramento (rodada 5, seção 5). Arquivo da F5: só ele
// calcula o "relatoriozinho de fechamento" que o dono pediu — nada aqui é
// digitado na tela, tudo sai do estado real do trecho (mensagens, pedido,
// cobrança). Puro, sem React, tempo e conversa chegam por parâmetro.
//
// "Trecho" é o intervalo `atendimentoDesde(conversa)` (dominio/conversa.js) até
// `agora`: o pedaço da conversa que ESTE atendimento cobre, não a conversa
// inteira. Isso já existia pronta do passo zero, a F5 só consome.

import { nomeDoMeio } from './cobranca'
import { pagamentosDoPedido } from './pagamento'
import { atendimentoDesde, ehLead, indiceInicioDoAtendimento } from './conversa'
import { duracao, horaCurta } from './formato'
import { itensDetalhados, totalDoPedido } from './pedido'
import { passoPorId } from './esteira'

const emMs = (iso) => new Date(iso).getTime()

// Mensagens do trecho, separadas em quem falou (seção 5, "Como cada número é
// calculado"). `in` é o cliente, `out` sem `automatica` é ela, `automatica` é
// a casa sozinha. Nota interna não mora em `mensagens` (vive em
// `cliente.notas`) e aviso de sistema (`dir: 'sistema'`) não bate em nenhum
// dos três baldes, então nem entra na conta — as duas exclusões que a direção
// pede saem de graça da própria forma dos dados.
//
// Recebe o trecho JÁ recortado (por índice, `indiceInicioDoAtendimento` em
// dominio/conversa.js), não a conversa inteira mais um horário de corte: duas
// mensagens de atendimentos diferentes podem ter o MESMO carimbo (decisão
// 46, "Encerrar" v2 — o relógio simulado anda em passos), e filtrar por
// `em >= desde` contaria as duas do lado errado da fronteira.
export function mensagensDoTrecho(mensagensDoAtendimento) {
  const doTrecho = mensagensDoAtendimento ?? []
  const cliente = doTrecho.filter((m) => m.dir === 'in').length
  const automatico = doTrecho.filter((m) => m.dir === 'out' && m.automatica).length
  const voce = doTrecho.filter((m) => m.dir === 'out' && !m.automatica).length
  return { cliente, voce, automatico, total: cliente + voce + automatico }
}

// Pedido "nascido pago" (semente, ex.: José Moretti em conversasSemente.js):
// estado já além de "aguardando" mas sem `cobranca` nenhuma gravada, porque a
// semente pula direto pro resultado. A esteira só avança com dinheiro
// reconhecido (regra de negócio, dominio/cobranca.js: "a esteira só abre
// depois que o dinheiro é reconhecido"), então esse avanço sozinho já prova
// que a venda aconteceu, mesmo sem o objeto estruturado. Só entra como último
// recurso, quando não há cobrança nenhuma para medir por data.
const semCobrancaMasAvancado = (pedido) =>
  Boolean(pedido) && !pedido.cobranca && pedido.estado !== 'aguardando'

// Receita do trecho (seção 5): soma do que caiu, estorno separado e negativo.
// `pedido.pagamentos` é o contrato oficial de dominio/pagamento.js (F4), mas a
// F4 ainda não tem, nesta árvore, o caso de reducer que escreve nele — o
// worktree desta frente saiu do `main` antes desse merge. Sem essa escrita, o
// dado real que EXISTE hoje é `pedido.cobranca` (valorPago, pagaEm,
// estornadaEm) ou, para pedido nascido pago, só o próprio `estado`. O cálculo
// usa `pagamentos` quando vier populado (o contrato correto, e o que passa a
// valer sozinho assim que a F4 integrar), cai para a cobrança quando ela
// existe, e só então para o total da comanda. Decisão registrada em
// auditoria/decisoes/36-f5-encerrar.md.
export function receitaDoTrecho(pedido, cardapio, desde) {
  const desdeMs = emMs(desde)
  // Integração (registro 38): lê a fonte única de pagamentos, a cobrança
  // ativa paga mais as cobranças pagas arquivadas (complemento, diferença).
  const doTrecho = pagamentosDoPedido(pedido).filter((p) => emMs(p.em) >= desdeMs)
  let bruto = doTrecho.reduce((soma, p) => soma + p.valor, 0)
  const cobranca = pedido?.cobranca
  if (doTrecho.length === 0 && semCobrancaMasAvancado(pedido)) {
    bruto = totalDoPedido(pedido, cardapio)
  }
  const estorno = cobranca?.estornadaEm && emMs(cobranca.estornadaEm) >= desdeMs
    ? (cobranca.valorEstornado ?? 0)
    : 0
  return { bruto, estorno, liquido: bruto - estorno }
}

// Situação de leitura do pedido para a linha de Vendas, com o mesmo tom da
// esteira (achado 4: situação sem Pílula na tabela). "Cancelado" fica fora da
// trilha da esteira (dominio/pedido.js), por isso não tem entrada em `PASSOS`
// e precisa do próprio rótulo e tom aqui.
function situacaoDoPedido(pedido) {
  if (pedido.estado === 'cancelado') return { rotulo: 'Cancelado', tom: 'perigo' }
  const passo = passoPorId(pedido.estado)
  return passo ? { rotulo: passo.rotulo, tom: passo.tom } : { rotulo: pedido.estado, tom: 'neutro' }
}

// Linha de Vendas (seção 5): só entra se o pedido teve movimento no trecho —
// pagamento, cobrança emitida, cancelamento, ou (pedido nascido pago) o
// próprio avanço de estado. Um pedido por conversa (o domínio não tem lista
// de pedidos), então a "tabela" é zero ou uma linha.
export function vendaDoTrecho(pedido, cardapio, desde) {
  if (!pedido || pedido.itens.length === 0) return null
  const desdeMs = emMs(desde)
  const pagamentoNoTrecho = pagamentosDoPedido(pedido).some((p) => emMs(p.em) >= desdeMs)
  const cobrancaEmitidaNoTrecho = pedido.cobranca?.criadaEm && emMs(pedido.cobranca.criadaEm) >= desdeMs
  const canceladoNoTrecho = pedido.estado === 'cancelado'
  const teveMovimento = pagamentoNoTrecho || cobrancaEmitidaNoTrecho
    || canceladoNoTrecho || semCobrancaMasAvancado(pedido)
  if (!teveMovimento) return null
  const itensTexto = itensDetalhados(pedido, cardapio)
    .map((linha) => `${linha.qtd}× ${linha.produto?.nome ?? linha.sku}`)
    .join(', ')
  // Integração: a F4 grava `meio` na cobrança; a linha lê de lá. Sem
  // cobrança (venda antiga), não inventa meio.
  const nome = pedido.cobranca ? nomeDoMeio(pedido.cobranca.meio) : null
  const meio = nome ? nome[0].toUpperCase() + nome.slice(1) : '—'
  // Número completo (achado 3, pendência 22 da banca 64: "mesmo número do
  // pedido em toda a folha"): o aviso do Encerrar já mostra "2026-0184"; a
  // tabela usava numeroCurto ("0184"), um formato diferente para o mesmo dado.
  const situacao = situacaoDoPedido(pedido)
  return {
    numero: pedido.numero,
    itensTexto,
    meio,
    valor: totalDoPedido(pedido, cardapio),
    situacao: situacao.rotulo,
    situacaoTom: situacao.tom,
  }
}

// Rótulo curto de cada marco (seção 5: "Horários das mensagens automáticas
// marcadas: cobrança, avisos de esteira, pagamento"). Chave é `mensagem.regra`
// (aplicacao/reducer.js e casos/simulacao.js já marcam toda automática com um
// destes IDs); sem rótulo mapeado, mostra o próprio ID em vez de inventar
// texto.
const ROTULO_MARCO = {
  'boas-vindas': 'primeira resposta automática',
  recibo: 'pagamento confirmado',
  agradecimento: 'agradecimento enviado',
  'cobranca-pix': 'cobrança enviada',
  resumo: 'pedido confirmado',
  cancelamento: 'pedido cancelado',
  esteira: 'aviso da esteira',
  encerramento: 'mensagem de encerramento',
}

// Marcos do trecho (seção 5): a primeira mensagem, mais até 7 automáticas.
// Nunca mais que 8 no total, como a direção pede. Mesmo trecho já recortado
// por índice que `mensagensDoTrecho` recebe (ver o comentário lá).
export function marcosDoTrecho(mensagensDoAtendimento) {
  const doTrecho = mensagensDoAtendimento ?? []
  if (doTrecho.length === 0) return []
  const [primeira, ...resto] = doTrecho
  const marcos = [{ em: primeira.em, rotulo: 'primeira mensagem' }]
  resto.forEach((m) => {
    if (!m.automatica || !m.regra) return
    marcos.push({ em: m.em, rotulo: ROTULO_MARCO[m.regra] ?? m.regra })
  })
  return marcos.slice(0, 8)
}

// "Converteu" (seção 5): lead que virou cliente NESTE trecho, calculado, nunca
// editável. RN-03/UC-01 passo 10 (rodada 10, item P1.4): `conta` vira
// 'cliente' só na PRIMEIRA compra confirmada (`comPagamentoReconhecido`,
// reducer.js, e o gêmeo simulado em aplicacao/casos/simulacao.js), nunca ao
// confirmar endereço, que deixa uma marca fixa na própria conversa (mensagem
// de sistema); com essa marca dá para saber se a virada aconteceu ANTES ou
// DENTRO do trecho, mesmo depois que `conta` já mudou. Sem marca nenhuma e
// `conta` já é 'cliente': tratado como "já era cliente" antes do trecho
// começar (leitura mais segura que supor conversão sem prova).
export const MARCA_DE_CONVERSAO = 'Cliente criado a partir do primeiro pagamento confirmado.'

function eraLeadAntesDoTrecho(conversa, desde) {
  if (ehLead(conversa)) return true
  const marca = (conversa.mensagens ?? []).find((m) => m.dir === 'sistema' && m.texto === MARCA_DE_CONVERSAO)
  if (!marca) return false
  return emMs(marca.em) >= emMs(desde)
}

export function converteuNoTrecho(conversa, desde, houvePagamentoNoTrecho) {
  if (!eraLeadAntesDoTrecho(conversa, desde)) return 'ja-cliente'
  return houvePagamentoNoTrecho ? 'sim' : 'nao'
}

export const ROTULO_CONVERTEU = {
  sim: 'Sim',
  nao: 'Não, segue lead',
  'ja-cliente': 'Já era cliente',
}

// Monta o resumo inteiro. `draft` é o que ela já marcou na folha antes de
// clicar Encerrar (avaliação do cliente, autoavaliação, anotação) — vem do
// `conversa.fechamentoRascunho` que `casos/encerramento.js` guarda. Serve
// tanto para a PRÉVIA (antes de encerrar, `agora` anda com o relógio da tela)
// quanto para o resumo definitivo (`casos/encerramento.js` congela isto com
// `agora` do clique em "Encerrar").
export function montarResumoAtendimento(conversa, cardapio, agora, draft = {}) {
  const desde = atendimentoDesde(conversa) ?? agora
  // Mensagens e marcos cortam por ÍNDICE (comentário em `mensagensDoTrecho`);
  // receita, venda e conversão continuam por horário — não têm posição na
  // lista de mensagens para recortar por índice.
  const mensagensDoAtendimento = (conversa.mensagens ?? []).slice(indiceInicioDoAtendimento(conversa))
  const mensagens = mensagensDoTrecho(mensagensDoAtendimento)
  const receita = receitaDoTrecho(conversa.pedido, cardapio, desde)
  // "A receber" é o total da comanda menos o que a PRÓPRIA receita deste
  // resumo já contou como recebido — não `dominio/pagamento.js:faltaPagar`
  // puro, que só olha `pagamentos[]` (vazio nesta árvore, seção acima) e
  // acusaria falta mesmo quando `receitaDoTrecho` já reconheceu o valor pela
  // cobrança ou pelo avanço do pedido.
  const faltaReceber = conversa.pedido
    ? Math.max(totalDoPedido(conversa.pedido, cardapio) - receita.bruto, 0)
    : 0
  const venda = vendaDoTrecho(conversa.pedido, cardapio, desde)
  const marcos = marcosDoTrecho(mensagensDoAtendimento)
  const converteu = converteuNoTrecho(conversa, desde, receita.bruto > 0)
  return {
    desde,
    ate: agora,
    duracaoTexto: duracao(agora - emMs(desde)),
    faixaHorario: `${horaCurta(desde)} a ${horaCurta(agora)}`,
    mensagens,
    receita: { ...receita, faltaReceber },
    vendas: venda ? [venda] : [],
    vendasTotal: venda?.valor ?? 0,
    marcos,
    converteu,
    avaliacaoCliente: draft.avaliacaoCliente ?? null,
    autoavaliacao: draft.autoavaliacao ?? null,
    anotacao: draft.anotacao ?? '',
    guardarNota: draft.guardarNota ?? true,
  }
}
