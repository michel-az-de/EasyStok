// O que mudou entre dois retratos do balcão, para o aviso sonoro (`aplicacao/useAvisoSonoro.js`).
// Retrato: Map de id da conversa -> { pago, precisa, ultimaMensagemClienteId }.
//
// Sem retrato anterior, ou com ele vazio, é só retrato: o balcão inteiro não vira trinta
// avisos. O vazio conta porque no modo API a lista nasce vazia e o primeiro sync traz
// todas as conversas de uma vez (#1287).
export function mudancasDoRetrato(antes, retrato) {
  if (!antes || antes.size === 0) return []
  const mudancas = []
  for (const [id, atual] of retrato) {
    const velho = antes.get(id)
    // Conversa nova na lista só conta como "mensagem nova" se já chegou com fala do
    // cliente (evita som em conversa criada vazia); pago e precisa nunca disparam na
    // primeira aparição, para não confundir o estado de nascença com uma mudança de agora.
    const mensagemNova = velho
      ? Boolean(atual.ultimaMensagemClienteId) && atual.ultimaMensagemClienteId !== velho.ultimaMensagemClienteId
      : Boolean(atual.ultimaMensagemClienteId)
    const pagouAgora = Boolean(velho) && atual.pago && !velho.pago
    const passouAgora = Boolean(velho) && atual.precisa && !velho.precisa
    if (mensagemNova || pagouAgora || passouAgora) mudancas.push({ id, mensagemNova, pagouAgora, passouAgora })
  }
  return mudancas
}
