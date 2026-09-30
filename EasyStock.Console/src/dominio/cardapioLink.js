// Domínio da Frente Cardápio por link (rodada 7, US-021, RN-09/RN-10/RN-16/RN-19/RN-23).
// Puro, sem React e sem infra: reaproveita dominio/pedido.js, dominio/cardapio.js e
// dominio/areaEntrega.js em vez de duplicar a conta de item, total e CEP.
//
// Fala do dono (24/09/2026 04h12): "poder enviar cardápio pra pessoa fazer o pedido
// (link que vai pro site e no site a pessoa faz tudo, seleciona, compra, paga etc),
// quando ela volta do site aqui no atendimento a gente já sabe de tudo, já acompanha
// se a pessoa pagou". Este arquivo cobre o link, o carrinho e o texto que chega na
// conversa; o pagamento em si é o mesmo CONFIRMAR_PAGAMENTO que a ficha já usa.

// Extensão .js em toda importação daqui: este arquivo é alcançado direto pelo
// Node em teste-cardapio-link.mjs (mesmo motivo do cabeçalho de dominio/entrega.js),
// e o ESM do Node não resolve caminho sem extensão.
import { primeiroNome } from './mensagem.js'
import { disponivelHoje, estaEmValidacao, estaRemovido, situacaoDoItem } from './cardapio.js'
import {
  ORDEM_DAS_LINHAS, comItem, comObservacao, itensDetalhados, novoPedido, semItem, totalDoPedido,
} from './pedido.js'
import { nomeDoMeio } from './cobranca.js'
import { foraDaArea, situacaoDoCep } from './areaEntrega.js'
import { moeda } from './formato.js'

// Rota própria do protótipo (seção C da pesquisa): janela nova, sem
// AtendimentoProvider por perto, igual a `#/entregas` e `#/cozinha`.
export const PREFIXO_ROTA = '#/cardapio-link/'

export const rotaDoCardapioLink = (conversaId) => PREFIXO_ROTA + conversaId

// `origem` é `location.origin + location.pathname`, que chega de fora porque
// domínio não lê `window` (a mesma regra que mantém dominio/canal.js sem infra).
export const linkDoCardapio = (origem, conversaId) => origem + rotaDoCardapioLink(conversaId)

// Convite que sai na conversa (US-021, seção C): nome e link, sem travessão.
export function textoConviteCardapio(nomeCliente, link) {
  return `Oi ${primeiroNome(nomeCliente)}! Segue o cardápio de hoje, é só escolher, `
    + `pagar e a gente já recebe tudo por aqui: ${link}`
}

// RN-16: só o que a casa vende hoje entra no link. Mesmo filtro que o
// composer já usa para a foto do prato (dominio/cardapio.js, situacaoDoItem).
// Integração 50: item tirado do cardápio (43) nunca aparece, e item "Em
// validação" (RN-15) só depois que a dona confirma, a mesma régua do agente
// (`oferecivelPeloAutomatico`): o site é venda automática.
export const itensDoDia = (cardapio) => cardapio.filter(
  (item) => disponivelHoje(item) && !estaRemovido(item) && !estaEmValidacao(item),
)

// Mesmo agrupamento e mesma ordem da comanda (dominio/pedido.js,
// ORDEM_DAS_LINHAS), só que sobre o catálogo cru em vez de itens já
// lançados no pedido.
export function itensDoDiaPorLinha(cardapio, linhas) {
  return ORDEM_DAS_LINHAS
    .map((chave) => ({
      chave, rotulo: linhas?.[chave]?.rotulo ?? chave, itens: itensDoDia(cardapio).filter((i) => i.linha === chave),
    }))
    .filter((grupo) => grupo.itens.length > 0)
}

// RN-53: saldo zerado na venda AUTOMÁTICA (o agente, e o site é a mesma
// coisa: ninguém do balcão do outro lado) não vende e não promete. É mais
// estrito que RN-48 (venda manual da dona, que segue vendendo com alerta):
// aqui não tem dona para ver o alerta antes de aceitar o pedido.
export const podeAdicionarAoCarrinho = (item) => disponivelHoje(item) && item.estoque > 0

// ---------------------------------------------------------------------------
// Carrinho: mesma forma de `pedido.itens`, então dominio/pedido.js (comItem,
// semItem, itensDetalhados, totalDoPedido) já serve o carrinho sem cópia
// nenhuma. Só o ajuste de quantidade por +/- precisa de função própria: o
// reducer tem essa conta embutida em AJUSTAR_QUANTIDADE, mas ali ela não é
// reaproveitável fora de um despacho.
export const carrinhoVazio = () => ({ itens: [] })

// Rodada 12 (#19): o "+" do cartão soma na linha que já existe, mesmo que ela
// tenha ganho observação. Sem isto, anotar "sem manteiga" e tocar "+" de novo
// abria uma segunda linha sem anotação, que o cliente não vê no cartão.
export function carrinhoComItem(carrinho, sku) {
  if (carrinho.itens.some((l) => l.sku === sku)) return carrinhoComQuantidade(carrinho, sku, 1)
  return comItem(carrinho, sku)
}

// RN-20: observação mora na linha do item ("sem manteiga" junto do prato),
// a mesma função que a comanda da Ficha usa.
export const carrinhoComObservacao = (carrinho, sku, texto) => comObservacao(carrinho, sku, texto)

export const quantidadeNoCarrinho = (carrinho) => carrinho.itens.reduce((soma, l) => soma + l.qtd, 0)

export const subtotalDoCarrinho = (carrinho, cardapio) => totalDoPedido(carrinho, cardapio)

export function carrinhoComQuantidade(carrinho, sku, delta) {
  const linha = carrinho.itens.find((l) => l.sku === sku)
  if (!linha) return carrinho
  const nova = linha.qtd + delta
  if (nova <= 0) return semItem(carrinho, sku)
  return { ...carrinho, itens: carrinho.itens.map((l) => (l.sku === sku ? { ...l, qtd: nova } : l)) }
}

export const carrinhoVazioDeItens = (carrinho) => (carrinho?.itens?.length ?? 0) === 0

// ---------------------------------------------------------------------------
// Endereço e área de entrega (RN-09: CEP obrigatório antes de fechar pedido;
// RN-10: fora da área não promete entrega). Site sem a Thatiane no meio não
// tem a quem escalar (RN-11 é conversa, não formulário), então aqui só
// bloqueia com o mesmo aviso curto que o automático já dá no chat.
export function avisoDeEndereco(texto, prefixosCepAtendidos) {
  const limpo = (texto ?? '').trim()
  if (!limpo) return 'Informe o endereço de entrega.'
  const situacao = situacaoDoCep(limpo, prefixosCepAtendidos)
  if (situacao === 'sem-cep') return 'Inclua o CEP no endereço.'
  if (foraDaArea(situacao)) return 'Essa região ainda não é atendida por aqui. Fale com a gente pelo WhatsApp.'
  return null
}

export const enderecoValido = (texto, prefixosCepAtendidos) => avisoDeEndereco(texto, prefixosCepAtendidos) == null

// Só os meios que o Mercado Pago emite online (Pix, cartão por link): a
// maquininha e o vale só existem na entrega, não fazem sentido num
// checkout sem ninguém do outro lado do balcão (infra/provedoresDeCobranca.js).
export const meiosDePagamentoOnline = (meios) => meios.filter((m) => m.provedor === 'mercadopago')

// ---------------------------------------------------------------------------
// Pedido fechado no site: RN-23, o pedido nasce ANTES do pagamento. A cobrança
// (emissao) chega pronta por parâmetro, quem emite é infra/provedoresDeCobranca.js.
export function pedidoDoCarrinho(numero, carrinho, janelaId, meio) {
  return { ...novoPedido(numero), janela: janelaId, meio, itens: carrinho.itens }
}

export const pedidoLinkPodeSerConfirmado = ({ carrinho, janelaId, endereco, meio, prefixosCepAtendidos }) =>
  !carrinhoVazioDeItens(carrinho) && Boolean(janelaId) && Boolean(meio) && enderecoValido(endereco, prefixosCepAtendidos)

// Série própria do pedido (mesmo formato "AAAA-NNNN" de
// infra/repositorioConversas.js), mas calculada do estado que a janela do
// site já tem em mãos: ela é OUTRO documento, com o próprio contador
// zerado, então reaproveitar o contador de lá duplicaria número com a
// janela do Balcão (o mesmo risco que acoes/entregas.js resolve com UUID
// para viagem). Aqui o número precisa continuar a série real, então a
// conta é pura sobre `conversas`, não um contador à parte.
export function proximoNumeroDoPedido(conversas, ano = '2026') {
  const maior = (conversas ?? []).reduce((max, c) => {
    const numero = c.pedido?.numero
    if (!numero || !numero.startsWith(ano + '-')) return max
    const n = Number(numero.slice(ano.length + 1))
    return Number.isFinite(n) && n > max ? n : max
  }, 0)
  return `${ano}-${String(maior + 1).padStart(4, '0')}`
}

// Cartão de sistema na conversa (seção C da pesquisa: "Pedido #123
// confirmado, R$ 42,90, Pix pago"). Situação de pagamento aqui é sempre
// "aguardando" (Pix e cartão) ou "na entrega" (maquininha e vale): RN-23
// garante que o pedido chega ANTES de pagar, o pago de verdade quem mostra ao
// vivo é dominio/cobranca.js (situacaoDaCobranca) na Ficha.
// Rodada 12 (#19): a observação que o cliente escreveu vai junto, e o cartão
// pede conferência só quando ela existe.
export function textoCartaoPedidoLink(pedido, cardapio, meio) {
  const linhas = itensDetalhados(pedido, cardapio)
    .map((l) => `${l.qtd}× ${l.produto?.nome ?? l.sku}${l.obs ? ` (${l.obs})` : ''}`)
    .join(', ')
  const total = moeda(totalDoPedido(pedido, cardapio))
  const pagamento = meioTemLink(meio)
    ? `Aguardando pagamento no ${nomeDoMeio(meio)}.`
    : `Pagamento na entrega, por ${nomeDoMeio(meio)}.`
  const conferir = pedidoPrecisaConferir(pedido.itens) ? ' Tem observação: confira a comanda.' : ''
  return `Pedido ${pedido.numero} chegou pelo cardápio online: ${linhas}. Total ${total}. ${pagamento}${conferir}`
}

// Pix e cartão por link são pagos na própria página; maquininha e vale, na
// entrega (infra/provedoresDeCobranca.js: só os dois primeiros têm link).
const MEIOS_COM_LINK = new Set(['pix', 'cartao-link'])
export const meioTemLink = (meio) => MEIOS_COM_LINK.has(meio)

// Rodada 12 (#19, decisão revista no registro 98): o pedido do cardápio segue
// direto para a cobrança. Só sobe para "Precisa de você" quando o cliente
// escreveu observação, que é o único pedaço que ninguém confere sozinho.
export const pedidoPrecisaConferir = (itens) => (itens ?? []).some((l) => (l.obs ?? '').trim() !== '')

export const MOTIVO_CONFERIR_PEDIDO_LINK = 'Pedido do cardápio com observação, confira a comanda'

// ---------------------------------------------------------------------------
// Rodada 12 (#19): vitrine estilo iFood. Feedback da Thatiane (26/09/2026):
// "uma página mais robusta, com mais imagens, onde o cliente marca os itens
// e as quantidades".
//
// RN-16 (toda opção sempre visível): esgotado e fora do dia APARECEM, com o
// motivo escrito, e não entram no carrinho (RN-53, `podeAdicionarAoCarrinho`).
// Tirado do cardápio e em validação continuam fora, a mesma régua de
// `itensDoDia`: o site é venda automática e não oferece o que a dona ainda
// não confirmou (RN-15).
export function motivoIndisponivel(item) {
  if (podeAdicionarAoCarrinho(item)) return null
  const situacao = situacaoDoItem(item)
  return situacao.chave === 'fora-do-dia' ? situacao.rotulo : 'Esgotado hoje'
}

export function vitrineDoCardapio(cardapio, linhas) {
  const visiveis = cardapio.filter((item) => !estaRemovido(item) && !estaEmValidacao(item))
  return ORDEM_DAS_LINHAS
    .map((chave) => {
      const itens = visiveis
        .filter((item) => item.linha === chave)
        .map((item) => ({ ...item, motivoIndisponivel: motivoIndisponivel(item) }))
      // Disponível primeiro: o indisponível fica visível, mas no fim da categoria.
      const ordenados = [...itens.filter((i) => !i.motivoIndisponivel), ...itens.filter((i) => i.motivoIndisponivel)]
      return { chave, rotulo: linhas?.[chave]?.rotulo ?? chave, dica: linhas?.[chave]?.dica ?? null, itens: ordenados }
    })
    .filter((grupo) => grupo.itens.length > 0)
}

export const TODAS_AS_CATEGORIAS = 'todas'

// Busca sem acento e sem caixa: quem digita "ravioli" no celular acha o
// "Ravióli". Mesma normalização para o nome e a porção.
const semAcento = (texto) => String(texto ?? '').normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase()

export function filtrarVitrine(grupos, { busca = '', categoria = TODAS_AS_CATEGORIAS } = {}) {
  const termo = semAcento(busca).trim()
  return grupos
    .filter((g) => categoria === TODAS_AS_CATEGORIAS || g.chave === categoria)
    .map((g) => ({
      ...g,
      itens: termo ? g.itens.filter((i) => semAcento(`${i.nome} ${i.porcao}`).includes(termo)) : g.itens,
    }))
    .filter((g) => g.itens.length > 0)
}

// Rodada 12 (#19), alinhado à PR #18: Pix e cartão em destaque (pagos aqui
// mesmo); maquininha e vale numa segunda linha, "Na entrega". `destaque` vem
// do catálogo quando existir; sem ele, destaque é quem o provedor emite online.
export function meiosDaPagina(meios) {
  const ativos = meios.filter((m) => m.situacao === 'ativo')
  const emDestaque = (m) => m.destaque ?? m.provedor === 'mercadopago'
  return { principais: ativos.filter(emDestaque), naEntrega: ativos.filter((m) => !emDestaque(m)) }
}

// O convite na conversa é texto; o link dele vira trecho clicável na bolha
// para abrir a página do cliente (features/atendimento/Balao.jsx). Só o link
// do cardápio, nada de transformar qualquer URL.
const LINK_NO_TEXTO = /(\S*#\/cardapio-link\/\S+)/ // mesmo caminho de PREFIXO_ROTA

export function partesComLinkDoCardapio(texto) {
  return String(texto ?? '')
    .split(LINK_NO_TEXTO)
    .filter((parte) => parte !== '')
    .map((parte) => ({ tipo: LINK_NO_TEXTO.test(parte) ? 'link' : 'texto', texto: parte }))
}
