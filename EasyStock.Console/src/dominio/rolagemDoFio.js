// #1587: quando o fio da conversa rola sozinho até o fim. Antes rolava a cada mensagem nova, e
// quem estava lendo mais acima perdia o lugar. Domínio puro: a tela mede e rola, a decisão é daqui.

// Folga para "já estava no fim": perto de um balão curto, para valer com a última linha à vista.
export const FOLGA_DO_FIM = 96

export const pertoDoFim = ({ alturaTotal, topo, alturaVisivel }, folga = FOLGA_DO_FIM) =>
  alturaTotal - topo - alturaVisivel <= folga

// O que a decisão precisa saber do fio. "Da casa" é a mensagem que alguém da loja mandou por
// aqui; a automática chega sozinha e não é motivo para tirar a leitura de quem está acima.
export function retratoDoFio(conversaId, mensagens = []) {
  const ultima = mensagens.at(-1) ?? null
  return {
    conversaId,
    total: mensagens.length,
    ultimaId: ultima?.id ?? null,
    ultimaDaCasa: ultima?.dir === 'out' && !ultima.automatica,
  }
}

// Rola ao abrir ou trocar de conversa, quando quem lê já estava no fim, ou quando a mensagem
// que acabou de entrar no fim é da casa (quem enviou quer ver o que saiu). `total` e `ultimaId`
// juntos: histórico antigo que entra por cima não é mensagem nova no fim, e a confirmação do
// envio, que só troca o id, também não.
export function deveRolarParaOFim(antes, depois, estavaNoFim) {
  if (!antes || antes.conversaId !== depois.conversaId) return true
  if (estavaNoFim) return true
  const entrouNoFim = depois.total > antes.total && depois.ultimaId !== antes.ultimaId
  return entrouNoFim && depois.ultimaDaCasa
}
