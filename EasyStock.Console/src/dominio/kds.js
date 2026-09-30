// KDS sobre Pedido (S19), usado pela cozinha no modo API (F05). Os status são os
// tokens da API (`StatusPedidoMapper`); a máquina de estados mora na API, aqui só
// fica o próximo toque de cada coluna, como na cozinha da demonstração.
export const COLUNAS_KDS = [
  { status: 'aguardando', rotulo: 'Aguardando', toque: 'Começar preparo' },
  { status: 'preparando', rotulo: 'Em preparo', toque: 'Marcar pronto' },
  { status: 'pronto', rotulo: 'Pronto', toque: 'Saiu para entrega' },
  { status: 'saiu_para_entrega', rotulo: 'Saiu para entrega', toque: 'Marcar entregue' },
]

const PROXIMO = {
  aguardando: 'preparando',
  preparando: 'pronto',
  pronto: 'saiu_para_entrega',
  saiu_para_entrega: 'entregue',
}

// `{ status, toque }` do próximo passo, ou `null` quando o pedido não anda mais.
export function proximoStatusKds(status) {
  const proximo = PROXIMO[status]
  if (!proximo) return null
  return { status: proximo, toque: COLUNAS_KDS.find((c) => c.status === status).toque }
}

const minutos = (ms) => Math.max(1, Math.round(Math.abs(ms) / 60000))

// S21: quando o preparo precisa começar e se já passou. `atrasado` vem da API
// (ela sabe também do pedido aberto de ontem); o texto só conta os minutos.
export function avisoDeInicio({ inicioPrevistoEm, atrasado }, agora) {
  if (!inicioPrevistoEm) return atrasado ? { atrasado: true, texto: 'Atrasado: pedido de outro dia' } : null
  const falta = Date.parse(inicioPrevistoEm) - agora
  if (atrasado) return { atrasado: true, texto: `Atrasado ${minutos(falta)} min: devia ter começado` }
  return falta > 0
    ? { atrasado: false, texto: `Começar em ${minutos(falta)} min` }
    : { atrasado: false, texto: 'Começar agora' }
}
