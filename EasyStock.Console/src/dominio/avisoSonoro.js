// O que mudou entre dois retratos do balcão, para o aviso sonoro (`aplicacao/useAvisoSonoro.js`)
// e o aviso de tela e do navegador (`dominio/aviso.js`, #1807).
// Retrato: Map de id da conversa -> { pago, precisa, ultimaMensagemClienteId, etapa }.
//
// Sem retrato anterior, ou com ele vazio, é só retrato: o balcão inteiro não vira trinta
// avisos. O vazio conta porque no modo API a lista nasce vazia e o primeiro sync traz
// todas as conversas de uma vez (#1287).
export function mudancasDoRetrato(antes, retrato) {
  if (!antes || antes.size === 0) return []
  const mudancas = []
  for (const [id, atual] of retrato) {
    const velho = antes.get(id)
    // #1807: conversa nova na lista é "contato novo", e só conta se já chegou com fala do
    // cliente (evita aviso em conversa criada vazia pela equipe). Pago, precisa e etapa nunca
    // disparam na primeira aparição, para não confundir o estado de nascença com uma mudança.
    const contatoNovo = !velho && Boolean(atual.ultimaMensagemClienteId)
    const mensagemNova = Boolean(velho) && Boolean(atual.ultimaMensagemClienteId)
      && atual.ultimaMensagemClienteId !== velho.ultimaMensagemClienteId
    const pagouAgora = Boolean(velho) && Boolean(atual.pago) && !velho.pago
    // #2007 (item 8): o sinal da encomenda é aviso próprio; a quitação depois ainda avisa "pagou o pedido".
    // Pedido que estava quitado e teve o total aumentado (alteração aprovada) não "pagou o sinal".
    const sinalAgora = Boolean(velho) && Boolean(atual.sinal) && !velho.sinal && !velho.pago && !atual.pago
    const passouAgora = Boolean(velho) && Boolean(atual.precisa) && !velho.precisa
    // Etapa só muda entre duas etapas de verdade: pedido que nasce ou some não é mudança, e o
    // que virou pago já é `pagouAgora`.
    const etapaMudou = Boolean(velho?.etapa) && Boolean(atual.etapa) && atual.etapa !== velho.etapa && !pagouAgora && !sinalAgora
    if (contatoNovo || mensagemNova || pagouAgora || sinalAgora || passouAgora || etapaMudou) {
      mudancas.push({ id, contatoNovo, mensagemNova, pagouAgora, sinalAgora, passouAgora, etapaMudou })
    }
  }
  return mudancas
}

// #2007 (item 8): "pago" no balcão é o pedido coberto. A encomenda com só o sinal pago fica no estado "pago" do console
// (a cozinha já pode produzir), mas o aviso diz "pagou o sinal" e guarda o "pagou o pedido" para a quitação.
export function pagamentoDoRetrato(pedido) {
  if (pedido?.estado !== 'pago') return { pago: false, sinal: false }
  const total = pedido.totalApi
  const cobertura = pedido.alocadoApi ?? pedido.totalPagoApi
  const sinal = Boolean(pedido.sinalNaReserva) && typeof total === 'number' && typeof cobertura === 'number'
    && cobertura > 0 && cobertura < total
  return { pago: !sinal, sinal }
}
