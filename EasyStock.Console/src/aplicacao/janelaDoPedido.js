import { disponivelHoje, itemPorSku } from '../dominio/cardapio'
import { identidadeDaEdicao } from '../dominio/identidadeDaEdicao'
import { temRascunhoDoPedido } from '../infra/api/rascunhosDoPedido'

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
  if (resposta?.erro) return { erro: faltaEndereco({ status: 409, message: resposta.erro }) ? `${resposta.erro} Volte ao passo Entrega e pagamento e cadastre o endereço.` : resposta.erro }
  // `criarPedido` devolve undefined quando não há pedido a criar ou a conversa já tem um: nada foi registrado.
  if (!resposta) return { erro: 'Não consegui confirmar o pedido. Confira em Pedidos antes de tentar de novo; a montagem foi mantida.' }
  aoRegistrar?.(resposta)
  return { ok: true, pedido: resposta }
}

// Conversa que já virou pedido: o modo API ainda não começa outra comanda nela (`novaComanda` é não ligada), então a
// janela avisa na abertura em vez de deixar montar algo que o Registrar não criaria.
export const pedidoJaCriadoNaConversa = (conversa) => Boolean(conversa?.pedido?.pedidoId)
// Comanda em montagem que já estava na conversa (Atendimento/Balcão): não é rascunho da janela.
export const comandaEmAndamento = (conversa) => (conversa?.pedido?.itens?.length ?? 0) > 0
// Sem cardápio carregado todo item pareceria "fora do cardápio".
export const cardapioPronto = (cardapio) => Array.isArray(cardapio) && cardapio.length > 0

// Entrega e endereço (como a comanda): a API usa o endereço do cadastro do cliente. Sem ele, 409 na simulação e no
// Registrar. O caminho é cadastrar o endereço aqui, pelo mesmo `editarDadoCliente('endereco')` da Ficha.
export const faltaEndereco = (erro) => erro?.status === 409 && /endere[cç]o/i.test(erro.message ?? '')

export const podeAvancarDaEntrega = ({ pedido, entrega = 'entrega', erroSimulacao = null }) =>
  Boolean(pedido?.janela && pedido?.meio) && !(entrega !== 'retirada' && faltaEndereco(erroSimulacao))

// Texto claro para qualquer recusa da API; a de endereço diz onde resolver.
export function textoDoErro(erro) {
  const texto = erro?.message || 'Não foi possível calcular o pedido. Tente de novo.'
  return faltaEndereco(erro) ? `${texto} Cadastre o endereço no passo Entrega e pagamento.` : texto
}

export async function cadastrarEnderecoDaJanela({ acoes, conversa, texto }) {
  const endereco = (texto ?? '').trim()
  if (!endereco) return { erro: 'Escreva o endereço de entrega.' }
  try {
    const r = await acoes.editarDadoCliente(conversa.id, 'endereco', endereco, undefined, identidadeDaEdicao(conversa))
    return r?.erro ? { erro: r.erro } : { ok: true }
  } catch (falha) {
    return { erro: falha.message || 'Não foi possível cadastrar o endereço. Tente de novo.' }
  }
}

export function rascunhoDaMontagem(janela, pedido, passo = 'itens', cardapio = [], { entrega, endereco } = {}) {
  if (!janela?.clienteId || !pedido?.itens?.length) return null
  return {
    clienteId: janela.clienteId,
    nome: janela.nome ?? '',
    passo,
    itens: pedido.itens.map((l) => ({ sku: l.sku, qtd: l.qtd, obs: l.obs ?? '', nome: itemPorSku(cardapio, l.sku)?.nome ?? null })),
    janela: pedido.janela ?? null,
    meio: pedido.meio ?? null,
    entrega: entrega ?? pedido.entrega ?? 'entrega',
    endereco: endereco ?? null,
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
  return { itens, avisos, passo: rascunho?.passo ?? 'itens', janela: rascunho?.janela ?? null, meio: rascunho?.meio ?? null,
    entrega: rascunho?.entrega ?? 'entrega', endereco: rascunho?.endereco ?? null }
}

export const novoPedidoDisponivel = ({ abrirConversaDoCliente, permite, acoes }) =>
  Boolean(abrirConversaDoCliente) && permite('atendimento') && acoes?.atenderConversas === true

// Rótulo do botão de entrada: com rascunho guardado da cliente, "Continuar pedido"; sem cliente conhecida, "Novo pedido".
export const rotuloDoNovoPedido = (sessao, clienteId) => (temRascunhoDoPedido(sessao, clienteId) ? 'Continuar pedido' : 'Novo pedido')
