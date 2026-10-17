// Apresentação do Resumo e das modais da ficha (#1868). Puro e sem JSX, para a
// prova (`ferramentas/prova-1868-visual.mjs`) conferir a mesma regra que a tela usa.

import { moeda } from '../../dominio/formato'

// O saldo é o número que a atendente diz ao cliente. Estava a 15 px, cinza, como
// subtítulo do acesso Pagamento, enquanto o estado da cozinha tinha 20 px em negrito.
// Com pedido criado e saldo aberto ele ganha linha própria no topo do Resumo.
export function saldoEmDestaque({ editavel = true, pedidoCriado = false, saldo = 0, cancelado = false } = {}) {
  return editavel && pedidoCriado && !cancelado && saldo > 0
}

// Subtítulo do acesso Pagamento. Com o saldo em destaque logo acima, repetir o valor
// aqui era a terceira vez na mesma coluna: fica o que se faz lá dentro.
export function resumoDoPagamento({ cancelado = false, saldo = 0, destaque = false } = {}) {
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
