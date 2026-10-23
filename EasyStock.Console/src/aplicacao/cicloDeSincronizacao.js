import * as acao from './acoes'
import { listarInbox, listarMensagens, marcarLida } from '../infra/api/conversasApi'
import { conversaDaApi } from '../infra/api/traducaoConversas'
import { obterPedido, pedidoDaApi } from '../infra/api/comandaApi'
import { abaVisivel, deveRelerMensagens, deveRelerPedido, emFila, fundirPagina, quedaDaSincronizacao } from './planoDeSincronizacao'

// Uma volta da polling da inbox (#1576), fora do React para ser provada sem tela: o hook
// (`useSincronizacaoApi.js`) só dá o ritmo. O que relê e em que ordem: `planoDeSincronizacao.js`.
const CONVERSAS_POR_VEZ = 4
// Voltas (5 s cada, 1 min) em que a conversa que esteve aberta segue com o resumo lido de fresco.
const VOLTAS_DE_FRESCOR = 12
// #1966: `conversa.atualizada` vale para a volta seguinte só (a conta desconta uma volta antes de ler).
const VOLTAS_DO_EVENTO = 2

// O que o laço lembra entre as voltas: os resumos da inbox (id -> resumo, na ordem da tela) e a
// última leitura de mensagens e de pedido de cada conversa. Criada acima do Balcão (App.jsx), não
// morre quando a dona vai ao Hall, à Cozinha ou às Entregas, e a volta não relê a inbox inteira.
export const criarMemoriaDoLaco = () => ({ dono: null, resumos: new Map(), mensagens: new Map(), pedidos: new Map() })

export function criarCicloDeSincronizacao({
  despachar, memoria = criarMemoriaDoLaco(), dono = null, comConversas = true,
  lerSelecionada = () => null, lerUsuario = () => null, estaVivo = () => true, lerAbaVisivel = abaVisivel,
}) {
  // Memória de outro usuário não serve a este: começa do zero.
  if (memoria.dono !== dono) {
    memoria.resumos = new Map()
    memoria.mensagens.clear()
    memoria.pedidos.clear()
    memoria.dono = dono
  }
  // A primeira volta de cada montagem varre a inbox inteira, e também a que vem depois de uma
  // volta que falhou: o que mudou fora da página 1 nesse meio-tempo só aparece na varredura.
  let devendoVarredura = true
  // É na conversa aberta que a dona age (assumir, encerrar, cadastrar o cliente), e a ação não mexe em
  // `ultimaMensagemEm`: fora da página 1, o resumo guardado desfaria na tela o que ela acabou de fazer.
  // Por isso a volta rápida lê até a página de quem está ou esteve aberta há pouco (id -> voltas que faltam).
  const frescas = new Map()
  const pedidosInvalidos = new Map()
  function conversasFrescas() {
    for (const [id, voltas] of frescas) {
      if (voltas > 1) frescas.set(id, voltas - 1)
      else frescas.delete(id)
    }
    const selecionada = lerSelecionada()
    if (selecionada) frescas.set(selecionada, VOLTAS_DE_FRESCOR)
    return [...frescas.keys()].filter((id) => memoria.resumos.has(id))
  }

  // #1966: a API marca como lida na Meta só pelo `marcar-lida`, e a seleção chama ao abrir. Com a conversa
  // já aberta e a aba visível, a entrada nova do cliente (não lidas e `ultimaMensagemEm` novos) é marcada
  // uma vez por lote. Aba oculta não marca nem esquece: ao voltar, o lote que chegou é marcado.
  let vista = { id: null, ultima: null }
  function marcarEntradaNaAberta() {
    const id = lerSelecionada()
    const resumo = id ? memoria.resumos.get(id) : null
    if (!resumo) return
    if (vista.id !== id) {
      vista = { id, ultima: resumo.ultimaMensagemEm }
      return
    }
    if (!(resumo.naoLidas > 0) || resumo.ultimaMensagemEm === vista.ultima || !lerAbaVisivel()) return
    vista = { id, ultima: resumo.ultimaMensagemEm }
    marcarLida(id).catch(() => {})
  }

  // `null` é "não li" (a leitura falhou e não há cache); lista vazia é "a conversa não tem mensagens".
  async function mensagensDe(resumo, cicloLento) {
    const guardado = memoria.mensagens.get(resumo.id)
    if (!deveRelerMensagens(resumo, guardado, lerSelecionada(), { cicloLento })) return guardado?.mensagens ?? []
    try {
      const mensagens = await listarMensagens(resumo.id)
      memoria.mensagens.set(resumo.id, { ultima: resumo.ultimaMensagemEm, mensagens })
      return mensagens
    } catch {
      return guardado?.mensagens ?? null
    }
  }

  async function pedidoDe(resumo, cicloLento) {
    if (!resumo.pedidoEmAndamentoId) return null
    const guardado = memoria.pedidos.get(resumo.id)
    const invalidacao = pedidosInvalidos.get(resumo.id)
    if (!invalidacao && !deveRelerPedido(resumo, guardado, { selecionadaId: lerSelecionada(), cicloLento })) {
      return guardado.dados
    }
    try {
      const dados = await obterPedido(resumo.id)
      // Um evento que chegou durante o GET exige outra leitura: a resposta anterior não o consome.
      if (!estaVivo() || pedidosInvalidos.get(resumo.id) !== invalidacao) return guardado?.dados ?? null
      memoria.pedidos.set(resumo.id, {
        pedidoId: resumo.pedidoEmAndamentoId, ultima: resumo.ultimaMensagemEm, statusDoResumo: resumo.pedidoStatus ?? null, dados,
      })
      pedidosInvalidos.delete(resumo.id)
      return dados
    } catch {
      return guardado?.dados ?? null
    }
  }

  function guardarInbox({ itens, completa }) {
    memoria.resumos = fundirPagina(memoria.resumos, itens, { completa })
    if (!completa) return
    devendoVarredura = false
    // Conversa que saiu da inbox sai dos caches: a memória agora dura o dia inteiro.
    for (const guardados of [memoria.mensagens, memoria.pedidos, pedidosInvalidos]) {
      for (const id of guardados.keys()) if (!memoria.resumos.has(id)) guardados.delete(id)
    }
  }

  async function volta({ cicloLento = false, somentePedidos = false } = {}) {
    try {
      if (somentePedidos) {
        const afetados = [...memoria.resumos.values()].filter(r => pedidosInvalidos.has(r.id))
        await emFila(afetados, CONVERSAS_POR_VEZ, async resumo => {
          const pedido = await pedidoDe(resumo, false)
          if (estaVivo()) despachar({ tipo: acao.SINCRONIZAR_PEDIDO, id: resumo.id, pedido: pedidoDaApi(pedido) })
        })
        return
      }
      const inbox = comConversas
        ? await listarInbox({ completa: devendoVarredura || cicloLento, ate: conversasFrescas() })
        : { itens: [], completa: true }
      // Quem saiu do Balcão no meio da volta não mexe na memória nem segue lendo conversa por conversa.
      if (!estaVivo()) return
      guardarInbox(inbox)
      marcarEntradaNaAberta()
      const semMensagens = []
      const conversas = await emFila([...memoria.resumos.values()], CONVERSAS_POR_VEZ, async (resumo) => {
        const [mensagens, pedido] = await Promise.all([mensagensDe(resumo, cicloLento), pedidoDe(resumo, cicloLento)])
        if (mensagens === null) semMensagens.push(resumo.id)
        return { ...conversaDaApi(resumo, mensagens, lerUsuario()), pedido: pedidoDaApi(pedido) }
      })
      if (estaVivo()) {
        // `semMensagens`: quem a leitura não trouxe e não estava em cache; o reducer mantém o que está na tela.
        despachar({ tipo: acao.SINCRONIZAR_CONVERSAS, conversas, ...(semMensagens.length ? { semMensagens } : {}) })
        // #1241: a volta da rede abre o lançamento do papel (o reducer ignora se já estava online).
        despachar({ tipo: acao.CONEXAO_VOLTOU, agora: Date.now() })
      }
    } catch (erro) {
      devendoVarredura = true
      if (estaVivo()) {
        despachar({ tipo: acao.SINCRONIZACAO_FALHOU, mensagem: erro.message })
        // #1241: sem rede, a faixa do papel liga e guarda quem estava aberto.
        if (quedaDaSincronizacao(erro)) despachar({ tipo: acao.CONEXAO_CAIU, agora: Date.now() })
      }
    }
  }
  volta.invalidarPedidos = (pedidoId, conversaId = null) => {
    let afetou = false
    for (const resumo of memoria.resumos.values()) {
      if (resumo.pedidoEmAndamentoId && (!pedidoId || resumo.pedidoEmAndamentoId === pedidoId)
        && (!conversaId || resumo.id === conversaId)) {
        pedidosInvalidos.set(resumo.id, {})
        afetou = true
      }
    }
    return afetou
  }
  // #1966: `conversa.atualizada` (nome ou contato mudou). A volta seguinte lê até a página dela, sem esperar a
  // varredura do minuto; conversa que o laço ainda não conhece chega pela página 1.
  volta.atualizarConversa = (conversaId) => {
    if (conversaId) frescas.set(conversaId, Math.max(frescas.get(conversaId) ?? 0, VOLTAS_DO_EVENTO))
  }
  return volta
}
