// Domínio da frente Fidelidade e cupons (rodada 13, issue #45, registro 107).
// Puro: nada de React, nada de infra, tempo e dado chegam por parâmetro
// (regra de `ferramentas/verificar-camadas.mjs`).
//
// Pedido do Felipe (27/09/2026): cupom de desconto e fidelidade (a cada
// pedido pago ganha ponto, ponto resgata produto/frete grátis/sorteio), num
// lugar fácil, sem mexer na UX das outras telas.
//
// Não existe nada disto no EasyStok hoje: só `Cupom` do plano SaaS
// (`TipoDesconto`, `LimiteUsos`, `PodeUsarEm`), sem fidelidade nenhuma
// (mapa-easystok-r13.md). Os nomes abaixo reaproveitam esse padrão
// (`tipoDesconto`, `limiteUsos`, `podeUsarEm`) e nascem já pensando na
// migração: quando o backend real existir, é o mesmo vocabulário.
//
// Pontos NÃO são um contador que o reducer soma e subtrai (isso duplicaria
// verdade com o desfazer/estorno de pagamento, que já existem em
// `dominio/cobranca.js`/`aplicacao/reducer.js`). Pontos são DERIVADOS do
// histórico de pedidos pagos, do mesmo jeito que `resumoFinanceiro`
// (dominio/cliente.js) deriva total gasto: um pedido que teve o pagamento
// desfeito ou estornado deixa de contar sozinho, porque `historico` já
// reflete o estado atual do pedido vivo (`historicoComPedidoVivo`). Só o
// RESGATE (que gasta ponto) precisa de estado próprio, guardado em
// `estado.fidelidade.resgatesPorCadastro`.

import { indiceDoPasso } from './esteira'
import { totalDoPedido } from './pedido'

// ---------------------------------------------------------------------------
// Cupom de desconto.
// ---------------------------------------------------------------------------

export const TIPOS_DESCONTO = ['percentual', 'valor']

export function novoCupom(id, {
  codigo, tipoDesconto = 'percentual', valor, validade = null, limiteUsos = null, minimoPedido = 0,
}) {
  return {
    id,
    codigo: codigo.trim().toUpperCase(),
    tipoDesconto,
    valor: Number(valor) || 0,
    validade, // 'AAAA-MM-DD' ou null (sem vencimento)
    limiteUsos: limiteUsos != null ? Number(limiteUsos) : null,
    usos: 0,
    minimoPedido: Number(minimoPedido) || 0,
    ativo: true,
  }
}

export const encontrarCupom = (cupons, codigo) => (cupons ?? [])
  .find((c) => c.codigo === (codigo ?? '').trim().toUpperCase()) ?? null

// Fim do dia da validade: cupom marcado "até 30/09" ainda vale às 23h59 do
// dia 30, não só até a meia-noite de entrada (RN de desconto comum a
// qualquer comércio, sem âncora específica: é o comportamento que o cliente
// espera de qualquer cupom com data).
function venceu(validade, agora) {
  if (!validade) return false
  const fimDoDia = new Date(`${validade}T23:59:59`).getTime()
  return agora > fimDoDia
}

// Motivo em vez de booleano solto: a tela mostra a frase pronta em vez de
// reinventar "cupom inválido" sem dizer por quê.
export function validarCupom(cupom, { agora, totalPedido }) {
  if (!cupom) return { valido: false, motivo: 'Cupom não encontrado' }
  if (!cupom.ativo) return { valido: false, motivo: 'Cupom inativo' }
  if (venceu(cupom.validade, agora)) return { valido: false, motivo: 'Cupom vencido' }
  if (cupom.limiteUsos != null && cupom.usos >= cupom.limiteUsos) {
    return { valido: false, motivo: 'Limite de usos deste cupom foi atingido' }
  }
  if (totalPedido < cupom.minimoPedido) {
    return { valido: false, motivo: `Pedido mínimo de ${cupom.minimoPedido} para este cupom` }
  }
  return { valido: true, motivo: null }
}

// Nunca desconta mais que o próprio total (cupom de valor fixo maior que a
// comanda não pode gerar pedido "negativo").
export function valorDoDesconto(cupom, totalPedido) {
  if (!cupom) return 0
  const bruto = cupom.tipoDesconto === 'percentual'
    ? totalPedido * (cupom.valor / 100)
    : cupom.valor
  return Math.round(Math.min(Math.max(bruto, 0), totalPedido) * 100) / 100
}

// Total que o cliente paga de fato: fonte única para a Cobrança e para quem
// emite a cobrança de verdade (`aplicacao/AtendimentoProvider.jsx`). Sem
// cupom aplicado, é o mesmo total de sempre.
export function totalComDesconto(pedido, cardapio) {
  const bruto = totalDoPedido(pedido, cardapio)
  return Math.max(0, bruto - (pedido?.valorDesconto ?? 0))
}

// ---------------------------------------------------------------------------
// Regra de fidelidade (proposta ajustável, ver `RegraFidelidade` na Gestão).
// Felipe pediu "a cada pedido pago ganha 1 ponto por R$ 10, OU N pontos a
// cada X pedidos" e disse pra escolher uma. Escolhida `valor` (ponto por
// real gasto): granularidade melhor que "pedidos" (cliente que gasta mais
// por pedido ganha mais, não só quem pede com mais frequência), e é o
// padrão do mercado (Smiles, Livelo etc.). Fica ajustável na Gestão, e as
// duas contas continuam aqui prontas para quando ela decidir trocar.
// ---------------------------------------------------------------------------

export const REGRA_FIDELIDADE_PADRAO = {
  tipo: 'valor', // 'valor' | 'pedidos'
  valorPorPonto: 10, // tipo 'valor': 1 ponto a cada R$ 10 gastos no pedido
  pontosPorFaixa: 1,
  pedidosPorPonto: 5, // tipo 'pedidos': N pontos a cada X pedidos pagos
  pontosPorPedidos: 20,
}

// Estados da esteira a partir de "pago" contam; "aguardando" (ainda não
// pagou), "cancelado" e "agendado" (fora da esteira, `indiceDoPasso` -1)
// nunca contam. Mesmo corte que `pedidoEncerrado`/a esteira já usam.
export const pedidoContaParaFidelidade = (pedidoResumo) =>
  indiceDoPasso(pedidoResumo?.estado) >= indiceDoPasso('pago')

// Historico já vem com o pedido vivo espelhado (`historicoComPedidoVivo`,
// dominio/cliente.js): se ele foi pago e depois DESFEITO ou o passo voltou,
// o `estado` mirrorizado já não é mais "pago" pra frente, e o pedido some da
// conta sozinho. Estorno (`cobranca.estornadaEm`) não muda o `estado` quando
// já passou da entrega (dominio/reducer.js, MARCAR_ESTORNO): por isso o
// pedido AO VIVO leva um confere extra aqui. Pedido antigo do histórico não
// carrega cobrança nenhuma neste protótipo (só resumo: numero/total/estado),
// então um estorno de pedido JÁ FECHADO em rodada anterior não é detectável
// aqui — limite conhecido, registrado no registro 107.
export function historicoElegivelParaFidelidade(historico, pedidoAoVivo) {
  const estornadoAoVivo = Boolean(pedidoAoVivo?.cobranca?.estornadaEm)
  return (historico ?? []).filter((p) => {
    if (estornadoAoVivo && p.numero === pedidoAoVivo.numero) return false
    return pedidoContaParaFidelidade(p)
  })
}

function pontosDoPedidoUnico(totalPedido, regra) {
  return Math.floor(totalPedido / regra.valorPorPonto) * regra.pontosPorFaixa
}

// Pontos GANHOS (antes de descontar resgate nenhum). `pedidosElegiveis` já
// deve vir filtrado por `historicoElegivelParaFidelidade`.
export function pontosGanhos(pedidosElegiveis, regra = REGRA_FIDELIDADE_PADRAO) {
  if (regra.tipo === 'pedidos') {
    return Math.floor(pedidosElegiveis.length / regra.pedidosPorPonto) * regra.pontosPorPedidos
  }
  return pedidosElegiveis.reduce((soma, p) => soma + pontosDoPedidoUnico(p.total, regra), 0)
}

// Pontos ganhos por ESTE pedido isolado, para caber na mensagem transacional
// de pagamento confirmado (RN-40, anti-spam: nunca dispara mensagem própria
// de marketing sozinha, só soma uma linha na que já ia sair). Só a regra
// 'valor' informa isso por pedido: a regra 'pedidos' só fecha faixa depois de
// somar vários pedidos, então um pedido isolado não anuncia nada sozinho.
export function pontosGanhosNoPedido(valorPago, regra = REGRA_FIDELIDADE_PADRAO) {
  if (regra.tipo !== 'valor') return 0
  return pontosDoPedidoUnico(valorPago, regra)
}

export const pontosResgatados = (resgates) => (resgates ?? []).reduce((soma, r) => soma + r.custoPontos, 0)

// Saldo de verdade: ganhos menos o que já foi trocado por recompensa.
export function saldoDePontos(pedidosElegiveis, resgates, regra = REGRA_FIDELIDADE_PADRAO) {
  return Math.max(0, pontosGanhos(pedidosElegiveis, regra) - pontosResgatados(resgates))
}

// ---------------------------------------------------------------------------
// Catálogo de recompensas: produto do cardápio, frete grátis ou número da
// sorte num sorteio (pedido literal do Felipe).
// ---------------------------------------------------------------------------

export const TIPOS_RECOMPENSA = [
  { valor: 'produto', rotulo: 'Produto do cardápio' },
  { valor: 'frete-gratis', rotulo: 'Frete grátis' },
  { valor: 'sorteio', rotulo: 'Número da sorte em sorteio' },
]

export function novaRecompensa(id, {
  tipo, rotulo, custoPontos, skuProduto = null, sorteioId = null,
}) {
  return {
    id, tipo, rotulo: rotulo.trim(), custoPontos: Number(custoPontos) || 0,
    skuProduto: tipo === 'produto' ? skuProduto : null,
    sorteioId: tipo === 'sorteio' ? sorteioId : null,
    ativo: true,
  }
}

export const podeResgatar = (recompensa, saldoDisponivel) =>
  Boolean(recompensa?.ativo) && saldoDisponivel >= recompensa.custoPontos

// ---------------------------------------------------------------------------
// Sorteios com lista de participantes (número da sorte por resgate).
// ---------------------------------------------------------------------------

export function novoSorteio(id, { nome, dataSorteio = null }) {
  return { id, nome: nome.trim(), dataSorteio, participantes: [] }
}

export function comParticipante(sorteio, { cadastroId, nome, em }) {
  const numero = sorteio.participantes.length + 1
  return {
    ...sorteio,
    participantes: [...sorteio.participantes, { cadastroId, nome, numero, em }],
  }
}
