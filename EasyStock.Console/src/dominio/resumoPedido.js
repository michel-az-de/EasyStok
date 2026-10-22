import { itensDetalhados, totalDoPedido } from './pedido'
import { dataIsoNoFuso, horaCurta, moeda } from './formato'
import { faixaDepoisDeEntre } from './entrega'
import { nomeDoMeio, sinalDeEncomenda } from './cobranca'

// Texto do pedido que vai para o cliente. Fica no domínio porque é regra de
// negócio, não formatação de tela: é o que a casa se compromete a entregar.
export function resumoParaCliente(pedido, cardapio, janela) {
  const linhas = itensDetalhados(pedido, cardapio)
    .map((l) => `• ${l.qtd}× ${l.produto?.nome} (${l.produto?.porcao})`
      + (l.obs ? ` — ${l.obs}` : ''))
  return [
    `Pedido ${pedido.numero}`,
    ...linhas,
    `Total: ${moeda(totalDoPedido(pedido, cardapio))}`,
    janela ? `Entrega entre ${faixaDepoisDeEntre(janela.faixa)}` : null,
  ].filter(Boolean).join('\n')
}

export const pedidoPodeSerGerado = (pedido) =>
  Boolean(pedido) && pedido.itens.length > 0 && pedido.estado === 'aguardando'

// Rodada 12 (issue #14, feedback da Thatiane no vídeo): "Enviar comanda" não
// reagia. O botão manda o resumo e a cobrança para o CLIENTE, na conversa; a
// cozinha recebe o pedido sozinha quando o pagamento entra (US-035). O rótulo
// passa a dizer o destino, e a tela nunca mais mostra o botão morto: ou ele
// envia, ou a linha diz por que não (enviada, já pago, vazia, bloqueado).
export const ROTULO_ENVIAR = 'Enviar ao cliente'

// #1474 (R4): no modo API "Enviar ao cliente", "Gerar cobrança" do bloco e o da barra criavam
// o mesmo pedido. Fica um botão só, o da barra fixa, e a comanda diz que vai junto.
export const ROTULO_GERAR_E_ENVIAR = 'Gerar cobrança e enviar'
export const LINHA_COMANDA_VAI_JUNTO = 'A comanda vai ao cliente junto com a cobrança.'
// #1587: pedido já criado (`comandaTravada`). O console não troca item de pedido criado; a frase
// diz só isso, sem prometer caminho que não existe.
export const LINHA_COMANDA_TRAVADA = 'O pedido já está no EasyStok: os itens não mudam mais por aqui.'
// #1686 (item 4): o caminho que existe para trocar item de pedido criado. O cliente altera pelo link
// da comanda e a loja aprova ("Confirmar alteração" na Ficha); sem pagamento, dá também para
// cancelar e montar de novo.
export function caminhoParaTrocarItem(pedido) {
  const pelaComanda = 'Para trocar um item, mande o link da comanda: o cliente altera e você aprova aqui.'
  return pedido?.estado === 'aguardando' ? `${pelaComanda} Ou cancele este pedido e monte outro.` : pelaComanda
}
export const podeCancelarParaRefazer = (pedido, { fonteApi = false } = {}) =>
  fonteApi && Boolean(pedido?.pedidoId) && pedido.estado === 'aguardando' && !pedido.cobranca?.pagaEm

// Pedido ainda não criado, no modo API, com janela de um dia depois de hoje: é encomenda e tem sinal.
function ehEncomendaDeOutroDia(pedido, { fonteApi, agora }) {
  if (!fonteApi || pedido?.pedidoId || pedido?.cobranca || pedido?.estado !== 'aguardando' || !pedido.itens?.length) return false
  const [janelaId, data] = String(pedido.janela ?? '').split('|')
  return Boolean(janelaId) && /^\d{4}-\d{2}-\d{2}$/.test(data) && Number.isFinite(Date.parse(data + 'T12:00:00Z'))
    && data > dataIsoNoFuso(agora)
}

export function avisoDeSinalAntesDeGerar(pedido, { fonteApi = false, agora = Date.now(), meio = pedido?.meio } = {}) {
  if ((meio && meio !== 'maquininha') || !ehEncomendaDeOutroDia(pedido, { fonteApi, agora })) return null
  return 'Encomenda para outro dia: sinal de 50% antes da produção. Restante na entrega.'
}

// #1875 ("Quanto fica?"): o que o console já sabe antes de gerar a cobrança. O frete depende da zona do
// endereço e só o servidor calcula, e o sinal da encomenda é metade do total COM o frete: aqui o sinal é o
// dos itens, um piso, e a tela diz isso.
// Vale para qualquer meio: o servidor cobra o sinal da encomenda também no Pix e no cartão por link.
export function previaDoSinal(pedido, { fonteApi = false, agora = Date.now(), total = 0 } = {}) {
  if (!ehEncomendaDeOutroDia(pedido, { fonteApi, agora })) return null
  const sinal = sinalDeEncomenda(total)
  return { sinal, restante: Math.round((total - sinal) * 100) / 100 }
}

// #1875: a barra só marca o meio; este resumo diz o que o botão de confirmação vai fazer.
export function resumoDaCobrancaEscolhida(pedido, { rotuloMeio, total, fonteApi = false, agora = Date.now(), simulacao = null }) {
  // #1884: com a simulação do servidor os números são os que vão ser cobrados (frete, total e sinal sobre o total com o frete).
  if (simulacao && !simulacao.freteACombinar) {
    const base = `Pedido de ${moeda(simulacao.total)} com o frete de ${moeda(simulacao.frete)}`
    if (simulacao.encomenda && simulacao.sinal != null)
      return `${base}. Encomenda para outro dia: sinal de ${moeda(simulacao.sinal)} por ${rotuloMeio}; ${moeda(simulacao.restante)} ficam para a entrega.`
    return `${base}, cobrança por ${rotuloMeio}.`
  }
  const itens = `Pedido de ${moeda(total)}${fonteApi && !pedido?.pedidoId ? ' nos itens (o frete entra ao gerar)' : ''}`
  const previa = previaDoSinal(pedido, { fonteApi, agora, total })
  if (!previa) return `${itens}, cobrança por ${rotuloMeio}.`
  return `${itens}. Encomenda para outro dia: sinal de 50% do total com o frete por ${rotuloMeio} (nos itens, ${moeda(previa.sinal)}); o restante fica para a entrega.`
}

// #1474 (R3): o que falta antes de gerar a cobrança, na ordem em que a dona resolve, para o
// botão nascer desabilitado com o motivo à vista em vez de falhar depois do clique. Cadastro,
// endereço e janela são exigências do EasyStok; na demonstração só a comanda vazia trava.
// #1847: a janela de mensagens não impede criar uma cobrança para copiar, sem envio.
// #1686 (item 9): cada motivo diz também onde se resolve (`alvo`): a barra o mostra como link que
// abre a seção e põe o foco no campo. `cardapio` abre o cardápio; `cadastro` e `endereco`, a seção
// Contato e endereço de "Cliente e histórico"; `janela`, a "Seleção do atendimento".
export const ALVOS_DO_MOTIVO = { CARDAPIO: 'cardapio', CADASTRO: 'cadastro', ENDERECO: 'endereco', JANELA: 'janela' }
export function pendenciaAntesDeCobrar({ pedido, clienteId, endereco }, { fonteApi = false } = {}) {
  if (!pedido?.itens?.length) return { motivo: 'A comanda está vazia.', alvo: ALVOS_DO_MOTIVO.CARDAPIO }
  if (!fonteApi) return null
  if (!clienteId) return { motivo: 'Salve o cadastro do cliente (Contato e endereço).', alvo: ALVOS_DO_MOTIVO.CADASTRO }
  if (!endereco) return { motivo: 'Falta o endereço de entrega (Contato e endereço).', alvo: ALVOS_DO_MOTIVO.ENDERECO }
  // #1865: a barra fica fora do modal; o seletor de janela está em "Seleção do atendimento".
  if (!pedido.janela) return { motivo: 'Escolha a janela de entrega em Seleção do atendimento.', alvo: ALVOS_DO_MOTIVO.JANELA }
  return null
}
export const motivoAntesDeCobrar = (dados, opcoes) => pendenciaAntesDeCobrar(dados, opcoes)?.motivo ?? null

// #1474 (R5): antes de o pedido existir o frete ainda não foi calculado (é da zona, no EasyStok).
export const rotuloDoTotal = (pedido, { fonteApi = false } = {}) =>
  (fonteApi && !pedido?.pedidoId ? 'Total dos itens (o frete entra ao gerar a cobrança)' : 'Total')

export function situacaoDoEnvio(pedido, { bloqueado = false } = {}) {
  // #1683: o EasyStok criou a cobrança mas o resumo não saiu (janela de 24 h, canal fora do ar).
  if (pedido?.cobranca && pedido.resumoNaoEnviado) {
    return {
      chave: 'nao-enviada', pode: false,
      texto: 'Cobrança criada, mas a mensagem não saiu. '
        + (pedido.cobranca.link ? 'Abra Pagamento no resumo, copie o link e envie pela conversa.' : 'Avise o cliente pela conversa.'),
    }
  }
  if (pedido?.cobranca) {
    return {
      chave: 'enviada', pode: false,
      texto: `Enviada ao cliente às ${horaCurta(pedido.cobranca.criadaEm)}. Para mudar a cobrança, abra Pagamento no resumo.`,
    }
  }
  if (pedido && pedido.estado !== 'aguardando') {
    return { chave: 'andou', pode: false, texto: 'Pedido já passou da cobrança. Não há o que enviar ao cliente.' }
  }
  if (!pedido || pedido.itens.length === 0) {
    return { chave: 'vazia', pode: false, texto: 'Comanda vazia. Abra o cardápio para montar o pedido.' }
  }
  if (bloqueado) {
    return { chave: 'bloqueado', pode: false, texto: 'Cliente bloqueado. Nada sai para ele.' }
  }
  return { chave: 'pode', pode: true, texto: null }
}

// Aviso que aparece na ficha logo depois do envio: o que saiu, para quem, e
// quando o pedido chega na cozinha. Mesma regra de `criarCobranca`: tem link,
// espera o pagamento; sem link (maquininha, vale), a esteira já andou.
export function avisoDoEnvio(cobranca, nomeCliente) {
  const nome = nomeDoMeio(cobranca?.meio)
  if (cobranca?.link) {
    return `Comanda enviada para ${nomeCliente} com o ${nome}. Entra na cozinha quando o pagamento cair.`
  }
  return `Comanda enviada para ${nomeCliente}. Pagamento na entrega (${nome}), o pedido já está na cozinha.`
}
