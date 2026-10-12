import { CONVERSAS_SEMENTE } from './conversasSemente'
import { carregarMassa } from './massaConversas'
import { bloqueioDeSemente } from '../dominio/conversa'
import { FONTE_API } from './fonteDados'

// Porta de entrada das conversas. A aplicação depende desta função, nunca do
// array. Trocar por uma chamada HTTP mais tarde não toca em componente nenhum.
//
// Padrão: a massa de 30 conversas (dados/massa-conversas.json). Com ?massa=0
// na URL volta a semente curta de 5, útil para demonstração rápida.
const querSemente = () => {
  if (typeof window === 'undefined') return false
  return new URL(window.location.href).searchParams.get('massa') === '0'
}

// O campo `bloqueio` é normalizado na entrada: quem traz o campo (ou a etiqueta
// antiga da massa) nasce bloqueada, o resto nasce livre. Assim nenhum
// componente precisa adivinhar o que significa a ausência do campo.
export function carregarConversas() {
  // Modo API (F01): as conversas chegam pelo polling, nunca da massa.
  if (FONTE_API) return []
  const origem = querSemente() ? CONVERSAS_SEMENTE : carregarMassa()
  iniciarContadorPedido(origem)
  return origem.map((conversa) => ({ ...conversa, bloqueio: bloqueioDeSemente(conversa) }))
}

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

function iniciarContadorPedido(conversas) {
  contadorPedido = maiorNumeroDePedido(conversas)
}

// Chamado pelo criador de ação de `adicionarItem` só quando a conversa ainda
// não tem pedido: o reducer usa este número para o `novoPedido` que nasce ali.
export function proximoNumeroPedido() {
  if (contadorPedido == null) contadorPedido = 0
  contadorPedido += 1
  return `${ANO_PEDIDO}-${String(contadorPedido).padStart(4, '0')}`
}
