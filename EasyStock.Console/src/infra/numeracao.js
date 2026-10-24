// Numeração local: id de mensagem, nota, alerta e número de pedido. Mora aqui, e não em
// `repositorioConversas.js`, para a demonstração (que semeia o contador de pedido) importar sem
// passar pela porta que a carrega (`infra/demonstracao.js`).

let contador = 0

// Id monotônico. Chamado pelos criadores de ação, nunca dentro do reducer.
export const proximoId = (prefixo) => prefixo + '-' + (++contador)

// Número de pedido é uma série PRÓPRIA (achado do gerente, 24/09: "Nº 1" no
// canhoto em vez do número real). Antes, `adicionarItem` chamava
// `proximoId('2026')`, que soma no MESMO contador global de `msg-`, `nota-`,
// `alerta-` etc: o número de pedido virava "2026-1", "2026-2" — pequeno e sem
// relação com a série real da massa ("2026-0177" a "2026-0199"). Contador
// separado, semeado pelo maior número já visto na carga, para o próximo
// pedido continuar a série de verdade em vez de reaproveitar um id de outra
// coisa.
let contadorPedido = null
const ANO_PEDIDO = '2026'

function maiorNumeroDePedido(conversas) {
  let maior = 0
  for (const conversa of conversas) {
    const numero = conversa.pedido?.numero
    if (typeof numero !== 'string') continue
    const valor = Number(numero.split('-').at(-1))
    if (Number.isFinite(valor) && valor > maior) maior = valor
  }
  return maior
}

export function iniciarContadorPedido(conversas) {
  contadorPedido = maiorNumeroDePedido(conversas)
}

// Chamado pelo criador de ação de `adicionarItem` só quando a conversa ainda
// não tem pedido: o reducer usa este número para o `novoPedido` que nasce ali.
export function proximoNumeroPedido() {
  if (contadorPedido == null) contadorPedido = 0
  contadorPedido += 1
  return `${ANO_PEDIDO}-${String(contadorPedido).padStart(4, '0')}`
}
