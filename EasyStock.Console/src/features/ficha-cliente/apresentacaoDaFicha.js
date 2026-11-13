// Apresentação do Resumo e das modais da ficha (#1868). Puro e sem JSX, para a
// prova (`ferramentas/prova-1868-visual.mjs`) conferir a mesma regra que a tela usa.

import { podeDesfazerPagamento } from '../../dominio/cobranca'
import { passoAnterior } from '../../dominio/esteira'
import { moeda, partesDoEndereco } from '../../dominio/formato'

// O saldo é o número que a atendente diz ao cliente. Estava a 15 px, cinza, como
// subtítulo do acesso Pagamento, enquanto o estado da cozinha tinha 20 px em negrito.
// Com pedido criado e saldo aberto ele ganha linha própria no topo do Resumo.
export function saldoEmDestaque({ editavel = true, pedidoCriado = false, saldo = 0, cancelado = false } = {}) {
  return editavel && pedidoCriado && !cancelado && saldo > 0
}

// Subtítulo do acesso Pagamento. Com o saldo em destaque logo acima, repetir o valor
// aqui era a terceira vez na mesma coluna: fica o que se faz lá dentro.
// J2 (#1990): cancelado com dinheiro recebido diz quanto falta devolver (`aDevolver` do EasyStok).
export function resumoDoPagamento({ cancelado = false, saldo = 0, destaque = false, aDevolver = 0 } = {}) {
  if (cancelado && aDevolver > 0) return `${moeda(aDevolver)} a devolver`
  if (cancelado) return 'Confira recebimentos e devoluções'
  if (saldo <= 0) return 'Pagamento completo'
  return destaque ? 'Receber o saldo e ver o extrato' : `${moeda(saldo)} a receber`
}

// Modal Pagamento: com o Extrato acima (modo API com pedido no EasyStok), o bloco
// Cobrança não repete o Total nem o "Recebido, saldo na entrega"; o Extrato já diz
// Total e A receber, e a linha da maquininha diz quanto cobrar.
export function cobrancaRepeteExtrato({ extratoAcima = false } = {}) {
  return !extratoAcima
}

// Modal Pedido e entrega: com o pedido criado no modo API, a linha "Janela de entrega"
// logo abaixo do papel mostra a janela e é quem troca. O cabeçalho do papel não a repete.
export function janelaNoCabecalhoDoPapel({ fonteApi = false, pedidoCriado = false, fechado = false } = {}) {
  return !(fonteApi && pedidoCriado && !fechado)
}

// --- #2232 (banca UX/UI do Atendimento, Tela 3) ----------------------------------------------------

// F3: o saldo em destaque já diz o valor; a linha de apoio não o repete. Se o total é igual ao saldo
// em destaque ele sai da linha (com sinal a receber, a linha diz o sinal); se difere, o total fica, que
// é outro número. Sem destaque (pago, pedido ainda em montagem) o total aparece só na linha.
const mesmoValor = (a, b) => Math.round(a * 100) === Math.round(b * 100)
export function linhaDoResumo({ estado, itens = 0, total = 0, saldo = 0, destaque = false, sinal = null } = {}) {
  const partes = [estado, `${itens} ${itens === 1 ? 'item' : 'itens'}`]
  const repete = destaque && mesmoValor(total, saldo)
  if (!repete) partes.push(moeda(total))
  else if (sinal > 0) partes.push(`sinal de ${moeda(sinal)}`)
  return partes.join(' · ')
}

// F4: a linha Entrega diz quando e para onde, com o que existir. Sem endereço, o convite de sempre;
// com endereço e sem janela nem bairro, "a combinar", nunca vazia.
const FAIXA_COM_HORA = /^(.*?)\s*·\s*(\d{1,2}):(\d{2})\s+às\s+(\d{1,2}):(\d{2})(?:\s*·.*)?$/
const hora = (h, m) => `${Number(h)}h${m === '00' ? '' : m}`
// "qui 05/11 · 14:00 às 17:00 · Tarde" -> "qui 05/11, 14h–17h"; "19h30 às 20h30" -> "19h30–20h30".
export function faixaCurta(faixa) {
  if (!faixa) return null
  const texto = String(faixa).trim()
  const comDia = FAIXA_COM_HORA.exec(texto)
  if (comDia) return `${comDia[1] ? `${comDia[1]}, ` : ''}${hora(comDia[2], comDia[3])}–${hora(comDia[4], comDia[5])}`
  return texto.replace(/\s+às\s+/, '–')
}
// Bairro de um endereço em texto livre. "Rua A, 10" não tem bairro (o "10" é o número); sem rua
// antes dele, o único pedaço também não é bairro.
export function bairroDoEndereco(endereco) {
  const { rua, bairro } = partesDoEndereco(endereco)
  return rua && bairro && !/^\d/.test(bairro) ? bairro : null
}
export function resumoDaEntrega({ faixa = null, endereco = null, bairro = null } = {}) {
  if (!endereco) return 'Adicionar endereço'
  const partes = [faixaCurta(faixa), bairro || null].filter(Boolean)
  return partes.length ? partes.join(' · ') : 'Entrega a combinar'
}

// F2: "Abrir no mapa" e "Copiar" do endereço. A busca do Google Maps aceita o texto cru.
export const linkDoMapa = (endereco) =>
  `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(String(endereco ?? '').trim())}`
// Devolve se copiou; sem permissão ou sem API, quem chama mostra a falha em vez de um botão morto.
export async function copiarTexto(texto, areaDeTransferencia = globalThis.navigator?.clipboard) {
  try {
    if (!areaDeTransferencia?.writeText) return false
    await areaDeTransferencia.writeText(texto)
    return true
  } catch { return false }
}

// F6: a confirmação de "Marcar pago" diz o valor que vai ser lançado.
export const perguntaDaBaixa = (valor, { sinal = false } = {}) =>
  sinal ? `Registrar sinal de ${moeda(valor)}?` : `Marcar ${moeda(valor)} como pago?`

// F1 e F7: um menu só no cabeçalho da Ficha ("Ações da ficha"), com o cadastro, o PDF e as ações do
// pedido que ficavam num segundo ⋮ ao lado de "Marcar pago".
export const ROTULO_DO_MENU_DA_FICHA = 'Ações da ficha'
export function acoesDoPedido({ pedido, fonteApi = false, disponivel = () => true, capacidades = null } = {}) {
  const nenhuma = { podeCancelar: false, podeEstornar: false, podeVoltarEtapa: false, podeDesfazerPagamento: false }
  if (!pedido || pedido.estado === 'cancelado') return nenhuma
  const alocado = pedido.alocadoApi ?? pedido.totalPagoApi
  const temPagamento = alocado > 0 || pedido.totalPagoApi > 0 || Boolean(pedido.cobranca?.pagaEm)
  return {
    podeCancelar: disponivel('cancelarPedido') && (fonteApi
      ? Boolean(pedido.pedidoId) && (!temPagamento || capacidades?.cancelarPedidoPago === true)
      : pedido.estado === 'aguardando'),
    // Estornar é devolver dinheiro que entrou: sem cobrança paga não há o que devolver (QA7, achado 1).
    podeEstornar: pedido.estado !== 'aguardando' && Boolean(pedido.cobranca?.pagaEm) && disponivel('marcarEstorno'),
    podeVoltarEtapa: Boolean(passoAnterior(pedido.estado)) && disponivel('corrigirPasso'),
    podeDesfazerPagamento: podeDesfazerPagamento(pedido),
  }
}
export function itensDoMenuDaFicha({ acoes = {}, comPedido = false } = {}) {
  return [
    { chave: 'editar', rotulo: 'Editar cadastro', icone: 'lapis' },
    { chave: 'baixar', rotulo: 'Baixar ficha em PDF', icone: 'download' },
    ...(comPedido ? [
      acoes.podeCancelar && { chave: 'cancelar', rotulo: 'Cancelar pedido', icone: 'circle-x' },
      acoes.podeDesfazerPagamento && { chave: 'desfazer', rotulo: 'Desfazer pagamento', icone: 'undo-2' },
      acoes.podeEstornar && { chave: 'estornar', rotulo: 'Marcar estorno', icone: 'undo-2' },
      acoes.podeVoltarEtapa && { chave: 'voltar', rotulo: 'Voltar etapa', icone: 'arrow-left' },
      // #2185: o mesmo nome e ícone do ⋮ da conversa. O item só aparece com a conversa aberta (`editavel`).
      { chave: 'encerrar', rotulo: 'Concluir conversa', icone: 'circle-check' },
    ].filter(Boolean) : []),
  ]
}
