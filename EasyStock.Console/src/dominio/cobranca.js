// Cobrança do pedido, por qualquer MEIO (rodada 5, seção 4: "Gerar Pix
// deveria ser Gerar cobrança"). Domínio puro: o identificador, o código
// copia e cola e o link chegam prontos por parâmetro, porque quem emite é o
// provedor (`infra/provedoresDeCobranca.js`, que decide Pix ou Mercado Pago
// por baixo) e não a regra da casa.
//
// `cobranca.meio` guarda qual dos quatro chips de `infra/catalogo.js`
// (MEIOS_DE_PAGAMENTO) gerou esta cobrança. Pix e cartão por link chegam com
// `link` preenchido e vencem em `MINUTOS_PARA_EXPIRAR`, porque são produtos
// online do Mercado Pago (estudo, decisão 18, seção 3.2). Maquininha e vale
// chegam com `link: null`: o Mercado Pago só aceita VR e VA na maquininha,
// nunca online, então "emitir" pra esses dois é só registrar que vai cobrar
// na entrega, sem prazo. A regra daqui é a mesma para os quatro: TEM prazo
// se, e só se, TEM link. Nenhum `if (meio === 'pix')` no meio da lógica.
//
// RN-23: o pedido é gerado ANTES do pagamento, nunca depois. A cobrança não cria
// o pedido, ela cobra um pedido que já existe.
//
// Sete situações, na ordem em que acontecem na vida real:
//
//   combinada      Maquininha ou vale: sem prazo, sem link. Cobra na entrega,
//                  e a esteira anda sem esperar (liberaEsteiraSemPagar).
//   aguardando     Pix ou cartão por link emitido, dentro do prazo.
//   em conferência O cliente diz que pagou e a baixa não chegou. A esteira NÃO
//                  abre aqui: liberar pedido por print é o vetor do comprovante
//                  falso, e quem paga o prejuízo é ela.
//   expirada       Prazo venceu sem pagamento. Reemitir em um toque.
//   conciliada     A baixa chegou e o valor bate com a comanda (RN-24: sem ação
//                  dela). É a única que fecha sozinha.
//   paga           Dinheiro reconhecido por decisão dela: marcou à mão, aceitou
//                  uma diferença, ou confirmou o recebido na entrega. Libera a
//                  esteira e fica dito que não passou pela conciliação do banco.
//   divergente     A baixa chegou com valor diferente do cobrado. Decisão dela,
//                  nunca do sistema: aceitar a diferença ou cobrar o que faltou.

import { itensDetalhados, totalDoPedido } from './pedido.js'
import { lerMoeda, moeda } from './formato.js'

const MS_POR_MINUTO = 60000

// Trinta minutos é o prazo que a casa promete no texto, para Pix e para
// cartão por link. Prazo curto é decisão de operação: massa fresca sai por
// janela, e cobrança aberta o dia inteiro segura vaga de quem paga. É
// também o PISO do próprio Mercado Pago (30 min a 30 dias, achado [F51] da
// decisão 18): quando o provedor real entrar, o texto não muda.
export const MINUTOS_PARA_EXPIRAR = 30

// Nome curto do meio, para entrar em frase ("Pago pelo X", "X venceu").
// Mora aqui, não em `infra/catalogo.js`, porque é o domínio quem escreve o
// compromisso da casa (mesmo motivo de `textoDaCobranca` existir aqui).
export const NOME_DO_MEIO = {
  pix: 'Pix',
  dinheiro: 'dinheiro',
  credito: 'cartão de crédito',
  debito: 'cartão de débito',
  transferencia: 'transferência',
  outro: 'outro meio',
  'cartao-link': 'link de pagamento',
  maquininha: 'maquininha',
  'vale-refeicao': 'vale',
  'dinheiro-entrega': 'dinheiro na entrega',
}
// Cobrança sem `meio` é da massa anterior à F4, quando só existia Pix.
export const nomeDoMeio = (meio) => NOME_DO_MEIO[meio ?? 'pix'] ?? 'cobrança'

// --- Dinheiro na entrega (frente J2, #1990) -----------------------------------
// O EasyStok só conhece a forma "na entrega" (maquininha ou dinheiro). O que diz que é dinheiro e
// quanto de troco levar vai nas observações do pedido, que a API grava no POST e devolve na
// leitura e no KDS: a Cozinha e o entregador leem a mesma frase, e recarregar a página não
// transforma o pedido em maquininha.
export const DINHEIRO_NA_ENTREGA = 'dinheiro-entrega'
export const MEIOS_NA_ENTREGA = new Set([DINHEIRO_NA_ENTREGA, 'maquininha', 'vale-refeicao'])
const PREFIXO_DINHEIRO = 'Pagamento na entrega: dinheiro'

// `trocoPara` em reais; vazio ou zero é "sem troco".
export function observacaoDoPagamentoNaEntrega({ meio, trocoPara = null } = {}) {
  if (meio !== DINHEIRO_NA_ENTREGA) return null
  return trocoPara > 0 ? `${PREFIXO_DINHEIRO}, troco para ${moeda(trocoPara)}.` : `${PREFIXO_DINHEIRO}, sem troco.`
}

// O caminho de volta: a frase gravada no pedido diz o meio e o troco.
export function pagamentoNaEntregaDasObservacoes(observacoes) {
  const texto = String(observacoes ?? '')
  if (!texto.includes(PREFIXO_DINHEIRO)) return null
  const troco = texto.match(/troco para R\$\s*([\d.]+,\d{2})/)
  const trocoPara = troco ? lerMoeda(troco[1]) / 100 : null
  return { meio: DINHEIRO_NA_ENTREGA, trocoPara }
}

// O que o entregador lê na parada: quanto o cliente vai dar e quanto troco levar. Sem dinheiro na
// entrega (`pagamento` nulo), nada.
export function textoDoTroco(pagamento, aReceber = pagamento?.aReceber ?? 0) {
  if (!pagamento) return null
  const trocoPara = pagamento.trocoPara ?? null
  if (!(trocoPara > 0)) return 'Dinheiro na entrega · sem troco'
  const levar = Math.round((trocoPara - (aReceber ?? 0)) * 100) / 100
  return `Dinheiro na entrega · troco para ${moeda(trocoPara)}${levar > 0 ? ` · levar ${moeda(levar)}` : ''}`
}

// `emissao` vem da infraestrutura: { identificador, copiaECola, link, meio }.
export function criarCobranca(pedido, cardapio, agora, emissao, valorForcado = null) {
  const temPrazo = emissao.link != null
  // Cupom aplicado no pedido (frente Fidelidade e cupons, rodada 13, issue
  // #45, registro 107): `pedido.valorDesconto` desconta sozinho aqui, para
  // TODO caminho que gera cobrança nova (pedido, cobrança avulsa, troca de
  // meio, reenvio) sair líquido sem precisar saber de cupom. `valorForcado`
  // continua um conceito à parte (cobrança de diferença/complemento
  // pós-pagamento, `cobranca.diferenca`): os dois nunca se somam, porque
  // complemento é sobre o que falta, não sobre o pedido inteiro de novo.
  const bruto = valorForcado ?? totalDoPedido(pedido, cardapio)
  const valor = valorForcado != null ? bruto : Math.max(0, bruto - (pedido.valorDesconto ?? 0))
  return {
    id: emissao.identificador,
    meio: emissao.meio ?? 'pix',
    valor,
    copiaECola: emissao.copiaECola,
    link: emissao.link,
    criadaEm: agora,
    expiraEm: temPrazo ? agora + MINUTOS_PARA_EXPIRAR * MS_POR_MINUTO : null,
    comprovanteEm: null,
    pagaEm: null,
    valorPago: null,
    liberadaEm: null,
    tentativa: 1,
    diferenca: valorForcado != null,
    estornadaEm: null,
  }
}

// Reenvio conta tentativa. O cliente precisa saber que o código anterior morreu,
// e a dona precisa ver quantas vezes já cobrou antes de ligar para ele.
export function reemitirCobranca(anterior, pedido, cardapio, agora, emissao, valorForcado = null) {
  return {
    ...criarCobranca(pedido, cardapio, agora, emissao, valorForcado),
    tentativa: (anterior?.tentativa ?? 0) + 1,
  }
}

// O cliente mandou o print. Isso registra a espera, não libera nada.
export function marcarComprovante(cobranca, agora) {
  if (!cobranca || cobranca.pagaEm) return cobranca ?? null
  return { ...cobranca, comprovanteEm: agora }
}

// Baixa do provedor. `valorPago` nulo é a marcação à mão dela: dinheiro que ela
// viu, sem extrato conferido do outro lado.
export function aplicarPagamento(cobranca, agora, valorPago = null) {
  if (!cobranca || cobranca.pagaEm) return cobranca ?? null
  return { ...cobranca, pagaEm: agora, valorPago }
}

// Diferença aceita por ela. Fica registrado quem liberou e quando.
export function aceitarDivergencia(cobranca, agora) {
  if (!cobranca?.pagaEm) return cobranca ?? null
  return { ...cobranca, liberadaEm: agora }
}

// Estorno é ela dizendo que já devolveu o dinheiro pelo Pix; o sistema só
// registra quando, nunca decide por ela (seção 5 da direção visual). Copy do
// botão e da confirmação ficam com a Frente A.
//
// UC-06 passo 5: motivo é obrigatório, sem ele a cobrança não muda, igual à
// mesma trava em `dominio/ocorrencia.js`. Valor pode ser parcial (RN-36 não
// promete sempre o total); sem valor informado, devolve o que foi pago.
export function estornarCobranca(cobranca, agora, { motivo, valor } = {}) {
  const motivoLimpo = (motivo ?? '').trim()
  if (!cobranca?.pagaEm || cobranca.estornadaEm || !motivoLimpo) return cobranca ?? null
  const pago = cobranca.valorPago ?? cobranca.valor
  // Estorno maior que o pago devolveria dinheiro que não existe: recusa
  // silenciosa (cobrança intacta), a mesma forma das outras travas aqui.
  if (valor != null && (valor <= 0 || valor > pago)) return cobranca
  const valorEstornado = valor != null ? valor : pago
  return { ...cobranca, estornadaEm: agora, motivoEstorno: motivoLimpo, valorEstornado }
}

// Falta da divergência de UMA cobrança (pagou a menos). O saldo do pedido é `faltaPagar`, de pagamento.js.
export function faltaDaCobranca(cobranca) {
  if (!cobranca?.pagaEm || cobranca.valorPago == null) return 0
  return Math.max(cobranca.valor - cobranca.valorPago, 0)
}

// Encomenda esperando o sinal, com a cobrança dele aberta (link do Mercado Pago ou entrada à mão).
const aguardaSinal = (pedido) => Boolean(pedido?.sinalNaReserva && pedido.statusApi === 'aguardando_pagamento'
  && pedido.cobranca && !pedido.cobranca.pagaEm && !pedido.cobranca.canceladaEm)

export function aguardaSinalManual(pedido) {
  return aguardaSinal(pedido) && !pedido.cobranca.link
}

// #1875: o sinal da encomenda é metade do total, em centavos e arredondando para cima, como o servidor
// (`PlanoPagamentoEncomenda.Sinal`).
export const sinalDeEncomenda = (total) => Math.ceil(Math.round(total * 100) / 2) / 100

export function valorSugeridoParaBaixa(pedido) {
  const total = pedido.totalApi ?? pedido.cobranca?.valor ?? 0
  // #2079 (C3): com o link do sinal pendente, o alvo é o valor do link (o sinal); sugeria o total.
  const alvo = aguardaSinalManual(pedido) ? sinalDeEncomenda(total)
    : aguardaSinal(pedido) ? Math.min(pedido.cobranca.valor ?? total, total) : total
  return Math.max(0, Math.round((alvo - (pedido.alocadoApi ?? pedido.totalPagoApi ?? 0)) * 100) / 100)
}

// Cancelamento do pedido (24/09/2026, bug do dono: cancelar com a cobrança
// vencida não fazia o relógio sumir). Fecha só a cobrança que AINDA NÃO foi
// paga: dinheiro que já entrou não sai daqui, sai por estorno explícito, com
// motivo (decisão 28, "não é automático, alguém tem que entrar e ver"). Sem
// isto, `situacaoDaCobranca` seguia lendo `expiraEm`/`pagaEm` de uma cobrança
// cujo pedido já morreu, e toda tela que lê a situação (ficha, Balcão)
// continuava mostrando o relógio, a marca "Cobrança vencida" ou os botões de
// tentar de novo.
export function cancelarCobranca(cobranca, agora) {
  if (!cobranca || cobranca.pagaEm || cobranca.canceladaEm) return cobranca ?? null
  return { ...cobranca, canceladaEm: agora }
}

// Situação pelo relógio da tela. Nenhum timer: o tempo entra por parâmetro e a
// mesma cobrança sempre dá a mesma resposta.
export function situacaoDaCobranca(cobranca, agora) {
  if (!cobranca) {
    return { chave: 'nenhuma', tom: 'neutro', rotulo: 'Cobrança não gerada', restamMs: 0 }
  }
  // Desfechos que encerram a cobrança sem deixar o relógio vivo: checados
  // antes de `pagaEm` porque um estorno acontece SOBRE uma cobrança paga
  // (pagaEm continua true) e um cancelamento acontece SOBRE uma cobrança
  // ainda em aberto (pagaEm continua null) — os dois precisam vencer a leitura
  // antiga do campo, não se somar a ela.
  if (cobranca.estornadaEm) {
    return { chave: 'estornada', tom: 'neutro', rotulo: 'Estornado', restamMs: 0 }
  }
  if (cobranca.canceladaEm) {
    return { chave: 'cancelada', tom: 'neutro', rotulo: 'Pedido cancelado', restamMs: 0 }
  }
  if (cobranca.pagaEm) {
    if (cobranca.manual || cobranca.valorPago == null) {
      return { chave: 'paga', tom: 'ok', rotulo: 'Pago, marcado à mão', restamMs: 0 }
    }
    if (cobranca.valorPago === cobranca.valor) {
      return { chave: 'conciliada', tom: 'ok', rotulo: 'Pago e conciliado', restamMs: 0 }
    }
    if (cobranca.liberadaEm) {
      return { chave: 'paga', tom: 'ok', rotulo: 'Pago, diferença aceita', restamMs: 0 }
    }
    return { chave: 'divergente', tom: 'aviso', rotulo: 'Valor diferente do cobrado', restamMs: 0 }
  }
  if (cobranca.comprovanteEm) {
    return { chave: 'em-conferencia', tom: 'aviso', rotulo: 'Em conferência', restamMs: 0 }
  }
  // Maquininha ou vale (seção 4, decisão 19): sem link, sem prazo. "Sem anel":
  // quem lê isto não desenha contagem regressiva nenhuma.
  if (cobranca.expiraEm == null) {
    return { chave: 'combinada', tom: 'info', rotulo: 'Recebe na entrega', restamMs: 0 }
  }
  const restamMs = cobranca.expiraEm - agora
  if (restamMs <= 0) {
    return { chave: 'expirada', tom: 'perigo', rotulo: 'Expirou sem pagar', restamMs: 0 }
  }
  return {
    chave: 'aguardando',
    tom: restamMs <= 5 * MS_POR_MINUTO ? 'aviso' : 'info',
    rotulo: 'Aguardando pagamento',
    restamMs,
  }
}

// Só o dinheiro reconhecido abre o fogão: conciliado pelo banco, marcado à mão
// por ela, ou diferença que ela aceitou. Uma regra, um lugar.
export const liberaEsteira = (cobranca) => {
  if (!cobranca?.pagaEm) return false
  return cobranca.valorPago == null
    || cobranca.valorPago === cobranca.valor
    || Boolean(cobranca.liberadaEm)
}

// Sinal verde do pedido (RN-25). Fica aqui para o cartão da caixa de entrada e a
// ficha lerem a MESMA regra, em vez de cada tela decidir o que é estar pago.
export const sinalVerde = (pedido) => liberaEsteira(pedido?.cobranca)

const resumirItens = (pedido, cardapio) => itensDetalhados(pedido, cardapio)
  .map((linha) => linha.qtd + '× ' + (linha.produto?.nome ?? linha.sku))
  .join(', ')

// Texto que sai para o cliente. Mora no domínio porque é compromisso da casa:
// valor, o que ele está comprando e por onde paga. Tela nenhuma reescreve isso.
//
// Dois formatos, pela presença de `link` (a mesma regra de `criarCobranca`):
// com link (Pix, cartão) o cliente recebe o link e o prazo; sem link
// (maquininha, vale) não há nada para ele clicar, o combinado é cobrar na
// entrega, e quem vai receber é o entregador ou a dona.
export function textoDaCobranca(cobranca, pedido, cardapio) {
  const nome = nomeDoMeio(cobranca.meio)
  let abertura = 'Pedido ' + pedido.numero + ' anotado!'
  if (cobranca.diferenca) {
    abertura = 'Faltou uma parte do pedido ' + pedido.numero + '. Segue a cobrança da diferença.'
  } else if (cobranca.meioAnterior) {
    abertura = 'Mudei a forma de pagamento do pedido ' + pedido.numero + ' para ' + nome + '. '
      + 'A cobrança anterior por ' + nomeDoMeio(cobranca.meioAnterior) + ' não vale mais.'
  } else if (cobranca.tentativa > 1) {
    abertura = 'Refiz a cobrança do pedido ' + pedido.numero + ', a anterior venceu.'
  }
  if (!cobranca.link) {
    return [
      abertura,
      cobranca.diferenca ? null : resumirItens(pedido, cardapio) + '.',
      'Total: ' + moeda(cobranca.valor),
      'Combinado: pago na entrega, na ' + nome + '.',
      'Já coloquei na fila de preparo.',
    ].filter(Boolean).join('\n')
  }
  return [
    abertura,
    cobranca.diferenca ? null : resumirItens(pedido, cardapio) + '.',
    'Total: ' + moeda(cobranca.valor),
    (cobranca.meio === 'cartao-link' ? 'Link de pagamento' : 'Pix') + ' de ' + MINUTOS_PARA_EXPIRAR
      + ' minutos: ' + cobranca.link,
    cobranca.copiaECola ? 'Copia e cola: ' + cobranca.copiaECola : null,
    'Assim que cair eu já coloco na fila de preparo.',
  ].filter(Boolean).join('\n')
}

// Resposta ao print. O cliente que manda comprovante quer saber que chegou, e é
// esse silêncio que faz ele perguntar três vezes se caiu.
export const TEXTO_DE_CONFERENCIA = 'Recebi seu comprovante, obrigada! '
  + 'Estou conferindo a baixa e já te confirmo por aqui.'

// ---------------------------------------------------------------------------
// Rodada 12 (issue #13, feedback da Thatiane no vídeo): trocar a forma depois
// que a cobrança saiu, e desfazer a baixa marcada por engano.
// ---------------------------------------------------------------------------

// Pedido que ainda não recebeu nada pode trocar a forma a qualquer momento,
// com Pix em aberto, vencido ou combinado na entrega. Pago não se troca:
// primeiro desfaz o pagamento (engano) ou estorna (dinheiro que entrou).
export function podeAlterarMeio(pedido) {
  const cobranca = pedido?.cobranca
  if (!cobranca || pedido.estado === 'cancelado' || pedido.estado === 'entregue') return false
  if ((pedido.totalPagoApi ?? 0) > 0 || (pedido.sinalNaReserva && (pedido.totalApi ?? 0) > cobranca.valor)) return false
  return !cobranca.pagaEm && !cobranca.canceladaEm && !cobranca.estornadaEm
}

// A cobrança que sai de cena na troca: fica no histórico do pedido, cancelada
// e com o motivo, para ninguém perguntar depois de onde veio o link velho.
export const MOTIVO_TROCA_DE_MEIO = 'Forma de pagamento alterada'
export function cobrancaTrocada(cobranca, agora) {
  return { ...cobranca, canceladaEm: agora, motivoCancelamento: MOTIVO_TROCA_DE_MEIO }
}

// Maquininha e vale andam a esteira sem pagamento. Se a forma nova tem link,
// o pedido que só estava em "pago" por isso volta a esperar o pagamento. Se a
// cozinha já começou, fica onde está: a esteira nunca anda para trás sozinha.
export function estadoDepoisDaTroca(pedido, anterior, novaTemLink) {
  if (novaTemLink && !anterior?.link && pedido.estado === 'pago') return 'aguardando'
  return pedido.estado
}

// Pix e cartão por link pagos só se desfazem antes do preparo: depois disso a
// massa já está no fogo, e o caminho é o estorno. Maquininha e vale recebidos
// ("Recebi") se desfazem em qualquer passo, porque a esteira deles nunca
// dependeu do pagamento.
// #1683: só baixa manual se desfaz; pagamento do Mercado Pago o EasyStok recusa (409, "use o
// estorno"). A API não diz a origem de cada pagamento: o sinal da reserva é sempre online, então
// com sinal só há baixa manual a desfazer do segundo pagamento em diante.
export function podeDesfazerPagamento(pedido) {
  if (pedido?.pagamentosApi) {
    return pedido.pagamentosApi.length > (pedido.sinalNaReserva ? 1 : 0) && !pedido.cobranca?.link
      && ['aguardando', 'aprovado_baba'].includes(pedido.statusApi)
  }
  const cobranca = pedido?.cobranca
  if (!cobranca?.pagaEm || cobranca.estornadaEm || cobranca.canceladaEm) return false
  if (pedido.estado === 'cancelado') return false
  if (!cobranca.link) return true
  return pedido.estado === 'aguardando' || pedido.estado === 'pago'
}

// Baixa marcada por engano. Motivo obrigatório, igual ao estorno: sem ele a
// cobrança não muda. Guarda o que foi desfeito, para a trilha contar depois.
export function desfazerPagamento(cobranca, agora, motivo) {
  const motivoLimpo = (motivo ?? '').trim()
  if (!cobranca?.pagaEm || cobranca.estornadaEm || !motivoLimpo) return cobranca ?? null
  return {
    ...cobranca,
    pagaEm: null,
    valorPago: null,
    liberadaEm: null,
    pagamentoDesfeito: { em: agora, motivo: motivoLimpo, pagaEm: cobranca.pagaEm, valorPago: cobranca.valorPago },
  }
}

// Pix e cartão voltam a esperar; maquininha e vale seguem na esteira.
export const estadoDepoisDeDesfazer = (pedido) =>
  (pedido.cobranca?.link && pedido.estado === 'pago' ? 'aguardando' : pedido.estado)

// --- Baixa à mão com link online pendente (#1474) ---------------------------
// O EasyStok recusa pagamento manual em pedido que ainda espera o Mercado Pago
// ("pré-operacional"). O caminho é virar a forma para "na entrega" (cancela o
// link e põe o pedido na fila) e só então registrar o recebido. A tela avisa
// antes de confirmar, porque o link que o cliente tem deixa de valer.
export const AVISO_BAIXA_COM_LINK = 'O link do Mercado Pago será cancelado e o pagamento fica registrado como recebido por fora.'

// #1474 (R8): pedido que exige aprovação (fora da área liberado) só aceita baixa à mão depois
// de aprovado; antes disso o EasyStok recusa ("ainda não foi confirmado/aprovado").
export const MOTIVO_BAIXA_SEM_APROVACAO = 'Aprove o pedido antes de registrar o pagamento.'
export const aguardaAprovacao = (pedido) => Boolean(pedido?.pedidoId) && pedido.statusApi === 'aguardando_aprovacao_baba'
// Com o link ainda pendente o status não mostra a aprovação que falta; o pedido traz `requerAprovacao`.
export const baixaEsperaAprovacao = (pedido) => aguardaAprovacao(pedido)
  || (Boolean(pedido?.pedidoId) && pedido.requerAprovacao === true)

export const baixaCancelaCobrancaOnline = (pedido) => Boolean(
  pedido?.pedidoId && pedido.cobranca?.link && !pedido.cobranca.pagaEm && !pedido.cobranca.canceladaEm,
)
  && pedido.statusApi === 'aguardando_pagamento'
  && !pedido.cobranca?.manual
  && !pedido.cobranca?.pagaEm

// --- Link vencido no modo API (#1586) ----------------------------------------
// Quem trata é o job de expiração do EasyStok (`CobrancaPedidoJob`, a cada 60 s): na primeira
// vez gera a segunda cobrança e manda o link novo na conversa; na segunda avisa o cliente e deixa o
// pedido aguardando a atendente (#1717-3: avisa, não trava). A Ficha não reenvia nada, então diz o que vai acontecer.
// #1683: o job só reemite e cancela pedido que ainda espera o pagamento. Com o pedido já na fila
// (pagamento na entrega, complemento) ele só marca o link como vencido, e o texto diz isso.
const ULTIMA_TENTATIVA_DE_COBRANCA = 2
// #1924: depois do segundo link vencido o job não manda outro; quem manda é a atendente, pela Ficha
// (`Mandar link novo`: troca a forma para o link do Mercado Pago, que gera e envia na conversa).
export const esperaLinkNovoApi = (cobranca, statusApi) =>
  statusApi === 'aguardando_pagamento' && (cobranca?.tentativa ?? 1) >= ULTIMA_TENTATIVA_DE_COBRANCA
export const ROTULO_MANDAR_LINK_NOVO = 'Mandar link novo'
export const LINK_VENCIDO_PEDIDO_SEGUE = 'O link venceu. O pedido continua valendo e o cliente não recebe outro link. '
  + 'Se o cliente pagou de outro jeito, clique em Marcar pago.'
export const avisoDeLinkVencidoApi = (cobranca, statusApi) => {
  if (statusApi === 'cancelado') return 'Pedido cancelado. O link venceu.'
  if (statusApi !== 'aguardando_pagamento') return LINK_VENCIDO_PEDIDO_SEGUE
  return (cobranca?.tentativa ?? 1) < ULTIMA_TENTATIVA_DE_COBRANCA
    ? 'O EasyStok manda um link novo ao cliente sozinho. Se esse também vencer, o pedido continua guardado e espera você: gere outra cobrança ou cancele.'
    : `Segundo link vencido: o pedido continua guardado e o cliente foi avisado. Use ${ROTULO_MANDAR_LINK_NOVO} ou cancele o pedido.`
}

// --- "Alterar forma" no modo API (#1683) --------------------------------------
// O EasyStok só conhece duas formas: link do Mercado Pago (Pix e cartão no mesmo link) e na
// entrega. Trocar entre Pix e cartão por link não muda nada lá, então a tela oferece só a
// outra forma e diz o que de fato acontece: o aviso ao cliente só sai quando há link novo.
export function trocaDeFormaNoEasyStok(cobranca, nomeCliente) {
  if (cobranca?.link) {
    return {
      meio: 'maquininha',
      texto: `O link de pagamento é cancelado e o pedido passa a receber na entrega. ${nomeCliente} não recebe mensagem: avise pela conversa.`,
    }
  }
  return {
    meio: 'cartao-link',
    texto: `O EasyStok gera um link de pagamento (Pix ou cartão) e manda para ${nomeCliente} se a conversa estiver aberta.`,
  }
}

// "Como recebeu" já preenchido com o meio da cobrança quando ele é um dos métodos
// da baixa; link de cartão não diz se foi crédito ou débito, então fica em branco.
const METODOS_DA_BAIXA = new Set(['pix', 'dinheiro', 'credito', 'debito', 'transferencia'])
export function metodoDaCobranca(cobranca) {
  if (METODOS_DA_BAIXA.has(cobranca?.metodoRecebido)) return cobranca.metodoRecebido
  // J2 (#1990): dinheiro na entrega se recebe em dinheiro.
  if (cobranca?.meio === DINHEIRO_NA_ENTREGA) return 'dinheiro'
  return METODOS_DA_BAIXA.has(cobranca?.meio) ? cobranca.meio : ''
}

// #1686 (item 10): devolução em dinheiro não tem comprovante; Pix, cartão e transferência têm.
// A mesma regra do `RegistrarEstornoManualUseCase` da API.
export const referenciaObrigatoriaNaDevolucao = (metodo) => metodo !== 'dinheiro'

// #1966: o GET de estornos manuais devolve `aDevolver` (PR #1967): pedido cancelado com dinheiro recebido e ainda
// não devolvido, descontado o que virou cupom ou está em estorno pendente. API antiga (sem o campo) vale zero.
export const aDevolverDe = (dados) => {
  const valor = Number(dados?.aDevolver)
  return Number.isFinite(valor) && valor > 0 ? valor : 0
}

// O caminho citado é o que a tela oferece: o estorno online só existe para pagamento online.
export const textoADevolver = ({ comOnline }) => (comOnline
  ? 'O pedido foi cancelado com valor recebido: devolva pelo estorno online abaixo ou registre a devolução manual.'
  : 'O pedido foi cancelado com valor recebido: devolva ao cliente e registre a devolução manual.')

// Resumo do bloco Devoluções recolhido: o que falta devolver vem antes do que já foi devolvido.
export function resumoDasDevolucoes({ aDevolver, totalDevolvido, conferirAcerto }) {
  if (aDevolver > 0) return `${moeda(aDevolver)} a devolver`
  if (totalDevolvido) return `${moeda(totalDevolvido)} devolvidos`
  return conferirAcerto ? 'Recebimento registrado' : 'Recebimentos e estornos'
}
