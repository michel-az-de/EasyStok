import { disponivelHoje, itemPorSku } from '../dominio/cardapio'

// #2921: regras puras da janela "Novo pedido" na tela. A montagem mora em `conversas[].pedido` do Provider (o mesmo
// lugar da comanda do Balcão), na conversa da janela: o "+" soma sempre nela, qualquer que seja a `selecionada`.

export const somarNaJanela = (acoes, janela, item) => {
  if (!janela?.conversaId || !item) return false
  acoes.adicionarItem(janela.conversaId, item)
  return true
}

export const ajustarNaJanela = (acoes, janela, sku, delta) => {
  if (!janela?.conversaId) return false
  acoes.ajustarQuantidade(janela.conversaId, sku, delta)
  return true
}

// Registrar: `gerarPedido` devolve `{ erro }` na recusa. Sucesso fecha a janela e apaga o rascunho; falha mantém tudo.
export async function registrarDaJanela({ acoes, janela, meio, enviarAoCliente = true, aoRegistrar }) {
  if (!janela?.conversaId) return { erro: 'Abra o Novo pedido de novo.' }
  const resposta = await acoes.gerarPedido(janela.conversaId, meio ?? null, enviarAoCliente)
  if (resposta?.erro) return { erro: resposta.erro }
  aoRegistrar?.(resposta)
  return { ok: true, pedido: resposta ?? null }
}

export function rascunhoDaMontagem(janela, pedido, passo = 'itens', cardapio = []) {
  if (!janela?.clienteId || !pedido?.itens?.length) return null
  return {
    clienteId: janela.clienteId,
    nome: janela.nome ?? '',
    passo,
    itens: pedido.itens.map((l) => ({ sku: l.sku, qtd: l.qtd, obs: l.obs ?? '', nome: itemPorSku(cardapio, l.sku)?.nome ?? null })),
    janela: pedido.janela ?? null,
    meio: pedido.meio ?? null,
    entrega: pedido.entrega ?? 'entrega',
  }
}

// Reabrir: refaz a montagem com o cardápio do dia. Item que saiu do cardápio (ou ficou indisponível) sai com aviso.
export function restaurarRascunho(rascunho, cardapio) {
  const itens = []
  const avisos = []
  for (const linha of rascunho?.itens ?? []) {
    const prato = itemPorSku(cardapio, linha.sku)
    if (!prato || prato.removidoEm || !disponivelHoje(prato)) {
      avisos.push(`${prato?.nome ?? linha.nome ?? 'Um item'} saiu do cardápio e foi tirado do pedido`)
    } else itens.push(linha)
  }
  return { itens, avisos, passo: rascunho?.passo ?? 'itens', janela: rascunho?.janela ?? null, meio: rascunho?.meio ?? null }
}

export const novoPedidoDisponivel = ({ abrirConversaDoCliente, permite, acoes }) =>
  Boolean(abrirConversaDoCliente) && permite('atendimento') && acoes?.atenderConversas === true
