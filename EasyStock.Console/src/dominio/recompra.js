import { diasDeCalendarioEntre, plural } from './formato'

// #2946: lista de recompra. A tag marca quem já foi convidado; a API a esconde da lista por 30 dias.
export const TAG_RECOMPRA_CONVIDADA = 'recompra-convidada'

// wa.me quer só dígitos com DDI. Fixo e número fora do padrão do Brasil viram null: a linha os trata como sem
// telefone em vez de marcar como convidado quem não tem WhatsApp naquele número.
export function telefoneParaWhatsapp(telefone) {
  const digitos = String(telefone ?? '').replace(/\D/g, '')
  const nacional = digitos.startsWith('55') && [12, 13].includes(digitos.length) ? digitos.slice(2) : digitos
  const celular = nacional.length === 11 ? nacional[2] === '9' : nacional.length === 10 && '6789'.includes(nacional[2])
  return celular ? `55${nacional}` : null
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
