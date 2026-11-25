// #2478: Pedidos no módulo Cadastros. Regras puras da lista e do detalhe sobre `GET api/pedidos` e
// `GET api/pedidos/{id}`: consulta, situação no vocabulário da Cozinha, canal legível, número do dia,
// saldo e linha do tempo. A tela só desenha; nada aqui conhece React nem a API.
import { nomeDoMetodoCaixa, normalizarMetodoCaixa } from './caixa'
import { dataIsoNoFuso, diaMes, horaCurta, idadeDoAviso, moeda, somarDiasIso } from './formato'
import { COLUNAS_KDS } from './kds'
import { numeroDoDiaNaTela } from './pedido'

export const PAGINA_PEDIDOS = 20

const naCozinha = (status) => COLUNAS_KDS.find((c) => c.status === status)?.rotulo

// Opções do filtro, na ordem da vida do pedido. `valor` é o que vai no `status` da API (lista por vírgula);
// `pedeAcao` marca a pílula (regra da `Pilula`: só o estado que pede ação de alguém da loja).
// "Na fila" junta o pago comum e o aprovado pela dona, como a coluna da Cozinha (#2055).
export const SITUACOES_PEDIDO = [
  { valor: 'aguardando_pagamento', rotulo: 'Aguardando pagamento', pedeAcao: true },
  { valor: 'aguardando_aprovacao_baba', rotulo: 'Aguardando aprovação', pedeAcao: true },
  { valor: 'aguardando,aprovado_baba', rotulo: naCozinha('aguardando') },
  { valor: 'preparando', rotulo: naCozinha('preparando') },
  { valor: 'pronto', rotulo: naCozinha('pronto') },
  { valor: 'saiu_para_entrega', rotulo: naCozinha('saiu_para_entrega') },
  { valor: 'entregue', rotulo: 'Entregue' },
  { valor: 'cancelado', rotulo: 'Cancelado' },
  { valor: 'rascunho', rotulo: 'Rascunho' },
]

const situacaoDoToken = (status) => SITUACOES_PEDIDO.find((s) => s.valor.split(',').includes(status))

// Token desconhecido (a API ganhou situação nova) vira texto legível; nunca some nem aparece cru.
const legivel = (texto) => {
  const limpo = String(texto ?? '').replace(/[_-]+/g, ' ').trim().toLowerCase()
  return limpo ? limpo.charAt(0).toUpperCase() + limpo.slice(1) : ''
}

export const rotuloDaSituacao = (status) => situacaoDoToken(status)?.rotulo ?? (legivel(status) || 'Sem situação')
export const situacaoPedeAcao = (status) => Boolean(situacaoDoToken(status)?.pedeAcao)

// #2944 (E1-17): pedido entregue ou cancelado não volta atrás; o botão diz por que não cancela.
export const motivoParaNaoCancelar = (status) => (status === 'entregue' ? 'Já foi entregue' : status === 'cancelado' ? 'Já foi cancelado' : null)

// A mesma regra da Ficha (acoesDoPedido): precisa da permissão `cancelarPedido` e, com pagamento recebido, da capacidade
// `cancelarPedidoPago`. Devolve o motivo de não poder, ou null quando pode.
export function motivoParaNaoCancelarPedido(item, { permitido = true, capacidades = null } = {}) {
  const porSituacao = motivoParaNaoCancelar(item?.status)
  if (porSituacao) return porSituacao
  if (!permitido) return 'Seu perfil não cancela pedidos'
  if (centavos(item?.totalPago) > 0 && capacidades?.cancelarPedidoPago !== true) return 'Pedido com pagamento: só a gerência cancela'
  return null
}

// O motivo do cancelamento: 3 a 500 caracteres, igual na Ficha (api/comanda.js) e na lista de Pedidos.
export const MOTIVO_CANCELAMENTO_MINIMO = 3
export const MOTIVO_CANCELAMENTO_MAXIMO = 500
export const ERRO_MOTIVO_CANCELAMENTO = `Informe um motivo entre ${MOTIVO_CANCELAMENTO_MINIMO} e ${MOTIVO_CANCELAMENTO_MAXIMO} caracteres.`
export function motivoDoCancelamentoValido(motivo) {
  const tamanho = String(motivo ?? '').trim().length
  return tamanho >= MOTIVO_CANCELAMENTO_MINIMO && tamanho <= MOTIVO_CANCELAMENTO_MAXIMO
}

// `Origem` é texto livre na API; os conhecidos ganham o nome que a loja usa.
const CANAIS = {
  web: 'Painel',
  console: 'Painel',
  storefront: 'Site',
  mobile: 'Aplicativo',
  kds: 'Cozinha',
  producao: 'Produção',
  whatsapp: 'WhatsApp',
  comanda_cliente: 'Link da comanda',
  cupom_cliente: 'Cupom do cliente',
  mercadopago: 'Mercado Pago',
  balcao: 'Balcão',
  viagem: 'Viagem',
  api: 'Integração',
  webhook: 'Integração',
  sistema: 'Automático',
}

export const rotuloDoCanal = (origem) => {
  const chave = String(origem ?? '').trim().toLowerCase()
  if (!chave) return null
  return CANAIS[chave] ?? legivel(chave)
}

// Forma do pagamento: os seis métodos do caixa com o nome do caixa; outro texto da API aparece legível.
const METODOS_FORA_DO_CAIXA = { mercadopago: 'Mercado Pago', cartao: 'Cartão', link: 'Link de pagamento' }
export function rotuloDoMetodo(metodo) {
  const chave = normalizarMetodoCaixa(String(metodo ?? ''))
  if (!chave) return 'Forma não informada'
  const doCaixa = nomeDoMetodoCaixa(chave)
  return doCaixa !== 'Outro' || chave === 'outro' ? doCaixa : METODOS_FORA_DO_CAIXA[chave] ?? legivel(chave)
}

export const PERIODOS_PEDIDO = [
  { valor: 'tudo', rotulo: 'Qualquer data', dias: null },
  { valor: 'hoje', rotulo: 'Hoje', dias: 1 },
  { valor: '7d', rotulo: 'Últimos 7 dias', dias: 7 },
  { valor: '30d', rotulo: 'Últimos 30 dias', dias: 30 },
]

export const ORDENS_PEDIDO = [
  { valor: 'mais-recentes', rotulo: 'Mais recentes', sort: 'criadoem', order: 'desc' },
  { valor: 'mais-antigos', rotulo: 'Mais antigos', sort: 'criadoem', order: 'asc' },
  { valor: 'maior-valor', rotulo: 'Maior valor', sort: 'total', order: 'desc' },
  { valor: 'cliente', rotulo: 'Cliente (A a Z)', sort: 'cliente', order: 'asc' },
  // #3076 (E4, L2): pelo dia da entrega (agendado ou, sem agendamento, o dia em que foi feito), a mais cedo primeiro.
  { valor: 'entrega', rotulo: 'Entrega (mais cedo)', sort: 'entrega', order: 'asc' },
]

// #3076 (E4, L2): "Entrega hoje" é o pedido cuja entrega cai hoje no dia da loja, inclusive o pedido para agora feito hoje.
export const ENTREGAS_PEDIDO = [
  { valor: 'tudo', rotulo: 'Qualquer dia' },
  { valor: 'hoje', rotulo: 'Hoje' },
]

// Parâmetros de `GET api/pedidos`. O período conta hoje e usa o dia da loja (a API lê `desde` puro como dia
// de Brasília). Campo vazio não vai na URL.
export function consultaDePedidos({ busca = '', pagina = 1, situacao = '', periodo = 'tudo', ordem = 'mais-recentes', entrega = 'tudo' } = {}, agoraMs = Date.now()) {
  const escolhida = ORDENS_PEDIDO.find((o) => o.valor === ordem)
  if (!escolhida) throw new Error(`Pedidos: ordem desconhecida (${ordem}).`)
  const dias = PERIODOS_PEDIDO.find((p) => p.valor === periodo)?.dias ?? null
  const termo = String(busca ?? '').trim()
  return {
    page: pagina,
    pageSize: PAGINA_PEDIDOS,
    ...(termo ? { search: termo } : {}),
    ...(situacao ? { status: situacao } : {}),
    ...(dias ? { desde: somarDiasIso(dataIsoNoFuso(agoraMs), 1 - dias) } : {}),
    ...(entrega === 'hoje' ? { entregaDesde: dataIsoNoFuso(agoraMs), entregaAte: dataIsoNoFuso(agoraMs) } : {}),
    sort: escolhida.sort,
    order: escolhida.order,
  }
}

// O número do dia volta a 1 todo dia: sem a data, "Nº 012" é ambíguo numa lista de vários dias.
export function numeroDoPedido(pedido) {
  const dia = numeroDoDiaNaTela(pedido?.numeroDoDia)
  // O número vale no dia de produção (`dataNumero`, data pura AAAA-MM-DD): encomenda feita no dia 12 para o dia 15
  // é "Nº 007 de 15/11". Sem número, vale o dia em que o pedido foi feito.
  const producao = dia && /^\d{4}-\d{2}-\d{2}$/.test(pedido?.dataNumero ?? '') ? `${pedido.dataNumero.slice(8, 10)}/${pedido.dataNumero.slice(5, 7)}` : null
  const data = producao ?? (pedido?.criadoEm ? diaMes(pedido.criadoEm) : null)
  if (dia) return data ? `Nº ${dia} de ${data}` : `Nº ${dia}`
  return data ? `Pedido de ${data}` : 'Pedido'
}

// Título que se lê sozinho (modal, leitor de tela, busca): "Pedido Nº 012 de 13/11" ou "Pedido de 13/11", nunca "Pedido Pedido".
export function tituloDoPedido(pedido) {
  const numero = numeroDoPedido(pedido)
  return numero.startsWith('Nº') ? `Pedido ${numero}` : numero
}

const centavos = (v) => Math.round(Number(v ?? 0) * 100)

// A pergunta da dona ao abrir um pedido: está pago? Cancelado não cobra; total zero não inventa pendência.
// Dinheiro que sobrou (recebido a mais, ou recebido num pedido cancelado) aparece em `sobra`, nunca escondido.
export function situacaoDoPagamento(pedido) {
  if (!pedido) return null
  const pago = centavos(pedido.totalPago)
  if (pedido.status === 'cancelado') return pago > 0 ? { texto: `Cancelado com ${moeda(pago / 100)} recebido`, falta: 0, sobra: pago / 100 } : null
  const total = centavos(pedido.total)
  if (total <= 0) return null
  if (pago > total) return { texto: `Recebido ${moeda((pago - total) / 100)} a mais`, falta: 0, sobra: (pago - total) / 100 }
  const falta = total - pago
  return falta === 0 ? { texto: 'Pago', falta: 0 } : { texto: `Falta ${moeda(falta / 100)}`, falta: falta / 100 }
}

const comDetalhe = (frase, detalhes) => (detalhes ? `${frase}: ${detalhes}` : frase)

// Texto de cada evento do pedido (tipos de `PedidoEvento`). O desconhecido vira legível, nunca cru.
const TEXTO_DO_EVENTO = {
  criado: (e) => comDetalhe('Pedido criado', e.statusNovo ? rotuloDaSituacao(e.statusNovo) : e.detalhes),
  status_changed: (e) => (e.statusAntigo && e.statusNovo
    ? `${rotuloDaSituacao(e.statusAntigo)} → ${rotuloDaSituacao(e.statusNovo)}`
    : comDetalhe('Situação alterada', e.statusNovo ? rotuloDaSituacao(e.statusNovo) : e.detalhes)),
  cancelado: (e) => comDetalhe('Pedido cancelado', e.detalhes),
  pagamento: (e) => comDetalhe('Pagamento registrado', e.detalhes),
  pagamento_removido: (e) => comDetalhe('Pagamento removido', e.detalhes),
  pagamento_desfeito: (e) => comDetalhe('Pagamento desfeito', e.detalhes),
  pagamento_divergente: (e) => comDetalhe('Pagamento com valor diferente', e.detalhes),
  pagamento_link_antigo: (e) => comDetalhe('Pagamento por link antigo', e.detalhes),
  forma_pagamento_trocada: (e) => comDetalhe('Forma de pagamento trocada', e.detalhes),
  devolucao_online: (e) => comDetalhe('Devolução online', e.detalhes),
  devolucao_online_descartada: (e) => comDetalhe('Devolução online descartada', e.detalhes),
  devolucao_manual: (e) => comDetalhe('Devolução manual', e.detalhes),
  item_added: (e) => comDetalhe('Item incluído', e.detalhes),
  item_removed: (e) => comDetalhe('Item retirado', e.detalhes),
  agendamento_alterado: (e) => comDetalhe('Data de entrega alterada', e.detalhes),
  cupom_cliente_aplicado: (e) => comDetalhe('Cupom aplicado', e.detalhes),
  comanda_cliente_alterada: (e) => comDetalhe('Cliente alterou a comanda', e.detalhes),
  aprovado_storefront: (e) => comDetalhe('Aprovado no site', e.detalhes),
  recusado_storefront: (e) => comDetalhe('Recusado no site', e.detalhes),
  celular: (e) => comDetalhe('Alterado no celular', e.detalhes),
  texto: (e) => e.detalhes || 'Anotação',
}

// Linha do tempo do detalhe: ordem dos fatos, com quem fez e por onde.
export function linhaDoTempoDoPedido(eventos) {
  if (!Array.isArray(eventos)) return []
  return [...eventos]
    .sort((a, b) => Date.parse(a.ocorridoEm) - Date.parse(b.ocorridoEm))
    .map((e) => ({
      quando: e.ocorridoEm,
      texto: (TEXTO_DO_EVENTO[e.tipo] ?? ((x) => comDetalhe(legivel(x.tipo) || 'Evento', x.detalhes)))(e),
      quem: [e.usuarioNome, rotuloDoCanal(e.origem)].filter(Boolean).join(' · ') || null,
    }))
}

// #3076 (E4, L2): quando o pedido sai, na lista e no detalhe. Agendado mostra dia e hora de início (a faixa fica
// fora da v1); sem agendamento é o pedido para agora.
export function quandoEntregar(pedido, agoraMs = Date.now()) {
  if (!pedido?.agendadoParaEm) return 'Na hora'
  const ms = new Date(pedido.agendadoParaEm).getTime()
  if (Number.isNaN(ms)) return 'Na hora'
  const hoje = dataIsoNoFuso(agoraMs), dia = dataIsoNoFuso(ms)
  const rotulo = dia === hoje ? 'Hoje' : dia === somarDiasIso(hoje, 1) ? 'Amanhã' : dia === somarDiasIso(hoje, -1) ? 'Ontem' : diaMes(pedido.agendadoParaEm)
  return `${rotulo}, ${horaCurta(pedido.agendadoParaEm)}`
}

// #3076 (E4, L1): motivo do "Registrar pagamento" desligado no pedido pré-operacional com saldo (tabela da spec).
// `aguardando_pagamento` não diz que a API recusa: ela aceita o sinal manual; o atalho completa a frase.
export const MOTIVOS_SEM_BAIXA_NO_DETALHE = {
  rascunho: 'A cliente ainda não terminou este pedido no cardápio.',
  aguardando_aprovacao_baba: 'Aprove o pedido em Entregas, no dia da entrega, antes de registrar o pagamento.',
  aguardando_pagamento: 'Este pedido espera o pagamento do link ou o sinal combinado.',
}
export const COMPLEMENTO_COM_CONVERSA = 'Registre pela conversa do pedido.'

// #3076 (E4, L1): o pagamento no cabeçalho do detalhe e o botão. O saldo vem de `aReceber` da API (cupom, estorno e
// quitado do histórico já contados); sem ele (API antiga), total menos pago. Cancelado não lê `aReceber`: saldo zero
// e o texto de sempre. `botao`: null (não aparece), `{ ligado: true, valor }` ou `{ ligado: false, motivo }`.
export function pagamentoNoDetalhe(pedido) {
  if (!pedido) return null
  if (pedido.status === 'cancelado') {
    const texto = situacaoDoPagamento(pedido)?.texto ?? 'Cancelado'
    return { texto, falta: 0, botao: null }
  }
  const falta = typeof pedido.aReceber === 'number' ? Math.max(pedido.aReceber, 0) : (situacaoDoPagamento(pedido)?.falta ?? 0)
  if (centavos(falta) === 0) {
    const pago = centavos(pedido.totalPago), total = centavos(pedido.total)
    // Saldo zero sem nenhum pagamento é o quitado do histórico; com pagamento menor que o total, o cupom ou o estorno
    // fecharam a conta: "Pago" (revisão da #3144).
    return { texto: total > 0 && pago === 0 ? 'Quitado' : pago > total ? `Recebido ${moeda((pago - total) / 100)} a mais` : 'Pago', falta: 0, botao: null }
  }
  const motivo = MOTIVOS_SEM_BAIXA_NO_DETALHE[pedido.status]
  return { texto: `Falta receber ${moeda(falta)}`, falta, botao: motivo ? { ligado: false, motivo } : { ligado: true, valor: falta } }
}

// #3294: os indicadores da tela (`GET api/pedidos/resumo`) usam os mesmos filtros da lista, menos situação, página e
// ordem: a faixa mede o recorte inteiro, e a situação vira filtro ao clicar no número dela.
const SO_DA_LISTA = ['page', 'pageSize', 'sort', 'order']
export function consultaDoResumo(filtros = {}, agoraMs = Date.now()) {
  const consulta = consultaDePedidos({ ...filtros, situacao: '', pagina: 1, ordem: 'mais-recentes' }, agoraMs)
  return Object.fromEntries(Object.entries(consulta).filter(([chave]) => !SO_DA_LISTA.includes(chave)))
}

// O recorte que a faixa mede, dito como a dona fala.
export function rotuloDoRecorte({ periodo = 'tudo', entrega = 'tudo', busca = '' } = {}) {
  const quando = periodo === 'tudo' ? 'Em todo o período' : PERIODOS_PEDIDO.find((p) => p.valor === periodo)?.rotulo ?? 'Em todo o período'
  return [quando, entrega === 'hoje' && 'entrega hoje', String(busca ?? '').trim() && `busca "${String(busca).trim()}"`].filter(Boolean).join(' · ')
}

// A janela anterior tem o mesmo tamanho e para na mesma hora: hoje compara com ontem até agora, nunca com o dia cheio.
const COMPARADO_COM = {
  hoje: { nome: 'ontem até esta hora', em: 'ontem até esta hora', a: 'a ontem até esta hora' },
  '7d': { nome: 'os 7 dias anteriores', em: 'nos 7 dias anteriores', a: 'aos 7 dias anteriores' },
  '30d': { nome: 'os 30 dias anteriores', em: 'nos 30 dias anteriores', a: 'aos 30 dias anteriores' },
}
export function variacaoDoVendido(resumo, periodo) {
  const anterior = resumo?.anterior
  const com = COMPARADO_COM[periodo]
  if (!anterior || !com) return null
  const agora = centavos(resumo.vendido), antes = centavos(anterior.vendido)
  if (antes === 0) return agora === 0 ? { texto: `Sem vendas também ${com.em}`, tom: 'igual' } : { texto: `Nada vendido ${com.em}`, tom: 'sobe' }
  const pct = Math.round(((agora - antes) / antes) * 100)
  if (pct === 0) return { texto: `Igual ${com.a} (${moeda(anterior.vendido)})`, tom: 'igual' }
  return { texto: `${pct > 0 ? '+' : '−'}${Math.abs(pct)}% sobre ${com.nome} (${moeda(anterior.vendido)})`, tom: pct > 0 ? 'sobe' : 'desce' }
}

// Quantos voltaram: compra de cliente que já tinha comprado antes, sobre todas as compras com cliente.
export function clientesQueVoltaram(resumo) {
  const primeira = resumo?.primeiraCompra ?? 0, voltaram = resumo?.recompra ?? 0
  const comCliente = primeira + voltaram
  if (comCliente === 0) return null
  return { primeira, voltaram, percentual: Math.round((voltaram / comCliente) * 100) }
}

const QUANTIDADE = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 3 })
const UNIDADES_CONTADAS = ['', 'un', 'und', 'unid', 'unidade', 'unidades']
// "2× Bolo" ou, no que se pesa, "1,5 kg Brigadeiro".
export function textoDoItem({ nome, quantidade, unidade } = {}) {
  const qtd = QUANTIDADE.format(Number(quantidade ?? 0))
  const un = String(unidade ?? '').trim().toLowerCase()
  return UNIDADES_CONTADAS.includes(un) ? `${qtd}× ${nome}` : `${qtd} ${un} ${nome}`
}

// O que foi pedido, na linha: até 3 itens na ordem em que entraram e "+N" pelo resto (`itensCount` é o total).
export function itensDaLinha(pedido) {
  const resumo = Array.isArray(pedido?.itensResumo) ? pedido.itensResumo : []
  if (resumo.length === 0) return null
  const resto = Math.max(0, (pedido.itensCount ?? resumo.length) - resumo.length)
  return { texto: resumo.map(textoDoItem).join(', '), resto: resto > 0 ? `+${resto}` : null }
}

// "1ª compra" é cliente nova; da segunda em diante é a ordem da compra dela na loja.
export function compraDoClienteNaLinha(pedido) {
  const n = pedido?.compraDoCliente
  return Number.isInteger(n) && n > 0 ? { texto: `${n}ª compra`, nova: n === 1 } : null
}

// Desde quando está na situação. Pedido fechado (entregue, cancelado) não conta tempo: a pergunta já foi respondida.
const FECHADOS = ['entregue', 'cancelado']
export function tempoNaSituacao(pedido, agoraMs = Date.now()) {
  if (!pedido?.situacaoDesde || FECHADOS.includes(pedido.status)) return null
  return idadeDoAviso(pedido.situacaoDesde, agoraMs)
}
