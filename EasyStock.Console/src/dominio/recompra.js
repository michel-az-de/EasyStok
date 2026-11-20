import { diasDeCalendarioEntre, plural } from './formato'

// #2946: lista de recompra. A tag marca quem já foi convidado; a API a esconde da lista por 30 dias.
export const TAG_RECOMPRA_CONVIDADA = 'recompra-convidada'

// wa.me quer só dígitos com DDI. Número fora do padrão do Brasil vira null: a linha o trata como sem telefone
// em vez de abrir uma conversa com a pessoa errada.
export function telefoneParaWhatsapp(telefone) {
  const digitos = String(telefone ?? '').replace(/\D/g, '')
  if ([10, 11].includes(digitos.length)) return `55${digitos}`
  if (digitos.startsWith('55') && [12, 13].includes(digitos.length)) return digitos
  return null
}

const primeiroNome = (nome) => String(nome ?? '').trim().split(/\s+/)[0] || null

// O sistema só abre a conversa: não sabe se a dona enviou. Sem emoji e com saída fácil para quem não quer receber.
export function textoDoConvite(nome, nomeDaLoja) {
  const primeiro = primeiroNome(nome)
  return `${primeiro ? `Oi, ${primeiro}!` : 'Oi!'} Aqui é ${nomeDaLoja ? `da ${nomeDaLoja}` : 'da loja'}. Faz um tempo que você não pede e lembrei de você. `
    + 'Quer que eu separe sua massa essa semana? Se preferir que eu não escreva, é só me dizer.'
}

export function linkDoConvite(telefone, nome, nomeDaLoja) {
  const numero = telefoneParaWhatsapp(telefone)
  return numero ? `https://wa.me/${numero}?text=${encodeURIComponent(textoDoConvite(nome, nomeDaLoja))}` : null
}

// "3 pedidos · último há 41 dias". Sem data válida diz só os pedidos: melhor calar do que inventar um prazo.
export function resumoDaRecompra(pedidos, ultimoPedidoEm, agora = Date.now()) {
  const total = plural(pedidos ?? 0, 'pedido', 'pedidos')
  const quando = ultimoPedidoEm ? Date.parse(ultimoPedidoEm) : NaN
  if (Number.isNaN(quando)) return total
  const dias = Math.max(0, diasDeCalendarioEntre(quando, agora))
  return `${total} · último ${dias === 0 ? 'hoje' : `há ${plural(dias, 'dia', 'dias')}`}`
}
