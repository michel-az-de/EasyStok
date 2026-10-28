import { legendaDoItem } from '../../dominio/vitrineCardapio'
import * as acao from '../acoes'
import { mesmoConteudo } from '../igualdade'
import { mapear } from '../reducer/auxiliares'
import { mesclarDoServidor, mesmaLista, pedidoMesclado } from '../reducer/mesclaDoServidor'

// Casos do modo API: o que o EasyStok devolveu entra no estado. Vale nos dois modos do build.
// Voltas da sincronização (5 s cada) em que a conversa recém-aberta pode demorar a aparecer na lista.
const VOLTAS_DA_SELECAO_PENDENTE = 3

export const casosApi = {
  [acao.CONFIRMAR_RESPONSAVEL_API]: (estado, { id, situacao, confirmadaEm }) => ({
    ...mapear(estado, id, (c) => ({ ...c, ...situacao, ...(confirmadaEm != null ? { confirmadaEm } : null) })),
    automaticoPausado: { ...estado.automaticoPausado, [id]: situacao.pausaApi ?? false },
  }),
  [acao.CONFIRMAR_REABERTURA_API]: (estado, { id, situacao, confirmadaEm }) => ({
    ...mapear(estado, id, (c) => ({ ...c, ...situacao, ...(confirmadaEm != null ? { confirmadaEm } : null) })),
    automaticoPausado: { ...estado.automaticoPausado, [id]: situacao.pausaApi ?? false },
  }),
  // `lidaEm`: id da conversa -> instante em que saiu a leitura do resumo dela (console-08).
  [acao.SINCRONIZAR_CONVERSAS]: (estado, { conversas, semMensagens, lidaEm }) => {
    const locais = new Map(estado.conversas.map((c) => [c.id, c]))
    const lidas = conversas.map((c) => mesclarDoServidor(locais.get(c.id), c, semMensagens?.includes(c.id), lidaEm?.[c.id]))
    const mescladas = mesmaLista(estado.conversas, lidas) ? estado.conversas : lidas
    const aindaExiste = mescladas.some((c) => c.id === estado.selecionadaId)
    // #2005 (console-07): "Chamar no WhatsApp" seleciona a conversa nova antes de a lista conhecê-la. A volta que
    // saiu antes dela existir não pode devolver a seleção à primeira da lista: espera algumas voltas pela conversa.
    const pendente = estado.selecaoPendente
    const esperando = !aindaExiste && pendente?.id === estado.selecionadaId && pendente.voltas < VOLTAS_DA_SELECAO_PENDENTE
    // #2135: sem a escolhida, nenhuma. A primeira do servidor não é a primeira da fila exibida.
    const selecionadaId = aindaExiste || esperando ? estado.selecionadaId : null
    const selecaoPendente = esperando ? { id: pendente.id, voltas: pendente.voltas + 1 } : null
    const pausas = Object.fromEntries(mescladas.map((c) => [c.id, c.pausaApi ?? false]))
    const automaticoPausado = mesmoConteudo(estado.automaticoPausado, pausas) ? estado.automaticoPausado : pausas
    // O aviso de ação recusada fica até a dona fechar (F06): o ciclo de 5 s não o apaga.
    const emDia = estado.sincronizacao?.estado === 'ok' && estado.sincronizacao.mensagem == null
    const sincronizacao = emDia ? estado.sincronizacao : {
      estado: 'ok', mensagem: null,
      aviso: estado.sincronizacao?.aviso ?? null, avisoFixo: estado.sincronizacao?.avisoFixo ?? null,
    }
    // #1565: volta sem novidade devolve o mesmo estado; o Provider não recalcula nem a tela re-renderiza.
    if (mescladas === estado.conversas && selecionadaId === estado.selecionadaId && selecaoPendente === (estado.selecaoPendente ?? null)
      && automaticoPausado === estado.automaticoPausado && sincronizacao === estado.sincronizacao) return estado
    return { ...estado, conversas: mescladas, selecionadaId, selecaoPendente, automaticoPausado, sincronizacao }
  },

  // #1276: cliente da conversa como o EasyStok tem (cadastro pelo console ou dossiê ao abrir).
  [acao.CLIENTE_DA_API]: (estado, { id, clienteId, nome, cliente, bloqueio }) =>
    mapear(estado, id, (c) => ({
      ...c,
      clienteId,
      conta: 'cliente',
      nome: nome || c.nome,
      // #1474: o bloqueio do cadastro vem do dossiê; sem o campo, fica o que estava.
      bloqueio: bloqueio === undefined ? c.bloqueio : bloqueio,
      cliente: { ...c.cliente, ...cliente, enderecoCapturado: null, daApi: true },
    })),

  [acao.CONTATO_INFORMADO_API]: (estado, { id, contato }) =>
    mapear(estado, id, (c) => ({ ...c, contatoInformado: contato })),

  [acao.RENOMEAR_LEAD_API]: (estado, { id, nome }) =>
    mapear(estado, id, (c) => ({ ...c, nome: nome?.trim() || c.nome, nomeDaDona: Boolean(nome?.trim()) })),

  // #2005 (console-09): a página mais antiga entra no topo do fio. `esgotou`: veio menos que uma página.
  [acao.ANTERIORES_CARREGADAS]: (estado, { id, mensagens, esgotou }) =>
    mapear(estado, id, (c) => {
      const jaTem = new Set(c.mensagens.map((m) => m.id))
      return {
        ...c,
        mensagens: [...mensagens.filter((m) => !jaTem.has(m.id)), ...c.mensagens],
        anterioresCarregadas: true,
        anterioresEsgotadas: esgotou,
      }
    }),

  [acao.SINCRONIZACAO_FALHOU]: (estado, { mensagem }) => ({
    ...estado, sincronizacao: { ...estado.sincronizacao, estado: 'erro', mensagem },
  }),

  // Sync que já trouxe a mensagem do servidor: o balão local sai, senão ficam dois com o
  // mesmo id (#1287).
  [acao.CONFIRMAR_ENVIO_API]: (estado, { id, mensagemId, mensagem }) =>
    mapear(estado, id, (c) => {
      const jaVeio = c.mensagens.some((m) => m.id === mensagem.id && m.id !== mensagemId)
      return {
        ...c,
        mensagens: jaVeio
          ? c.mensagens.filter((m) => m.id !== mensagemId)
          : c.mensagens.map((m) => (m.id === mensagemId ? { ...mensagem, aguardandoSync: true } : m)),
      }
    }),

  [acao.RECEBER_MODELO_API]: (estado, { id, mensagem }) => mapear(estado, id, c => ({
    ...c, mensagens: [...c.mensagens.filter(m => m.id !== mensagem.id), { ...mensagem, aguardandoSync: true }],
  })),

  // #1396: com a mensagem gravada pelo EasyStok, o balão local vira ela (id do servidor, que o Reenviar
  // usa); sem ela, fica marcado `semIdServidor` e não oferece Reenviar.
  [acao.FALHAR_ENVIO_API]: (estado, { id, mensagemId, erro, mensagem, semIdServidor }) =>
    mapear(estado, id, (c) => ({
      ...c,
      mensagens: c.mensagens.map((m) => {
        if (m.id !== mensagemId) return m
        if (mensagem) return { ...m, ...mensagem, status: 'falhou', erro }
        return { ...m, status: 'falhou', erro, ...(semIdServidor ? { semIdServidor: true } : {}) }
      }),
    })),

  // #1474: aviso de configuração (loja online desligada) é `persistente` e mora em `avisoFixo`:
  // só sai quando a dona fecha (`fixo: true`). A limpeza automática (trocar de tela, ação que
  // deu certo) apaga só o aviso de ação, que é o `aviso`.
  [acao.AVISO_API]: (estado, { mensagem, persistente }) => ({
    ...estado,
    sincronizacao: { ...estado.sincronizacao, ...(persistente ? { avisoFixo: mensagem } : { aviso: mensagem }) },
  }),

  [acao.FECHAR_AVISO_API]: (estado, { fixo } = {}) => (fixo ? {
    ...estado, sincronizacao: { ...estado.sincronizacao, aviso: null, avisoFixo: null },
  } : {
    ...estado, sincronizacao: { ...estado.sincronizacao, aviso: null },
  }),

  // F03: o cardápio da vitrine substitui o da massa. Adicional da massa não existe na API.
  // A peça guarda o id do item e o índice da foto: o envio vai pelo id (#1437), não pela URL.
  [acao.SINCRONIZAR_CARDAPIO]: (estado, { cardapio }) => ({
    ...estado,
    catalogo: {
      ...estado.catalogo, cardapio, adicionais: {},
      galeria: cardapio.flatMap((item) => (item.fotos ?? []).map((foto, indice) => ({
        id: `${item.sku}-foto-${indice}`, nome: item.nome,
        descricao: `${item.porcao} · Foto ${indice + 1}`, legenda: legendaDoItem(item), foto, doCardapio: true,
        cardapioItemId: item.sku, indice,
      }))),
    },
  }),

  // F03: pedido que a API acabou de criar ou trocar de forma, sem esperar o próximo ciclo.
  [acao.SINCRONIZAR_PEDIDO]: (estado, { id, pedido }) =>
    mapear(estado, id, (c) => ({ ...c, pedido: pedidoMesclado(c.pedido, pedido) })),

  // A API é a verdade do expediente: horário e controle manual substituem o local.
  [acao.SINCRONIZAR_EXPEDIENTE]: (estado, { funcionamento, lojaAberta, mensagemForaDoHorario, mensagemLojaFechada }) => ({
    ...estado,
    funcionamento,
    lojaAberta,
    expediente: { carregado: true, mensagemForaDoHorario, mensagemLojaFechada },
  }),

  // #1441: modo API. A lista lida do EasyStok substitui a da demonstração inteira.
  [acao.RESPOSTAS_DA_API]: (estado, { respostas }) => ({
    ...estado,
    catalogo: { ...estado.catalogo, respostasProntas: respostas },
  }),

  [acao.REGRAS_DA_API]: (estado, { regras }) => ({ ...estado, regras, cargaDasRegras: { estado: 'ok', mensagem: null } }),

  // #1474: a lista que não veio fica como erro, não como "carregando" para sempre.
  [acao.REGRAS_FALHARAM]: (estado, { mensagem }) => ({ ...estado, cargaDasRegras: { estado: 'erro', mensagem } }),

  // #1241 (modo API): o EasyStok já aplicou os passos e não avisa o cliente retroativo, então
  // nada entra na conversa. O pedido novo chega pela releitura; aqui só fecha a pendência.
  [acao.LOTE_PAPEL_LANCADO_API]: (estado) => ({
    ...estado, conexao: { ...estado.conexao, pedidosAbertos: [], offlineDesde: null },
  }),
}
