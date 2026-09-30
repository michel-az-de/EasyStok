import * as acao from './acoes'
import {
  avisoDoPasso, indiceDoPasso, motivoParaNaoAvancar, passoAnterior,
} from '../dominio/esteira'
import { entregadorResolvido } from '../dominio/viagem'
import {
  comItem, comObservacao, novoPedido, pedidoEncerrado, semItem,
} from '../dominio/pedido'
import { resumoParaCliente } from '../dominio/resumoPedido'
import {
  ajustarSaldo, alternarDisponibilidade, baixarSaldo, devolverSaldo,
  itemPorSku, situacaoDeEstoque, textoDoAlerta,
} from '../dominio/cardapio'
import { baixarLotesFifo, devolverLotesFifo, registrarDescoberto } from '../dominio/producao'
import { GATILHOS, contextoDePrevia, regraDoGatilho, textoDaRegra } from '../dominio/automacao'
import { PAUSA_POR_ASSUMIR } from '../dominio/automatico'
import { canalDaConversa } from '../dominio/canal'
import { envioBloqueado } from '../dominio/janela'
import { ORIGENS } from '../dominio/lembrete'
import { dataHora, mesPorExtenso, moeda } from '../dominio/formato'
import { faixaParaCliente, janelaPorId } from '../dominio/entrega'
import {
  TEXTO_DE_CONFERENCIA, aceitarDivergencia, aplicarPagamento, cancelarCobranca, criarCobranca,
  estornarCobranca, liberaEsteira, marcarComprovante, reemitirCobranca, textoDaCobranca,
} from '../dominio/cobranca'
import { registroDaCobranca } from '../dominio/pagamento'
import {
  bloquearCadastro, conversasDoCadastro, desbloquearCadastro, ehLead, estaBloqueada, novoBloqueio,
} from '../dominio/conversa'
import { MARCA_DE_CONVERSAO } from '../dominio/resumoAtendimento'
import {
  abrirOcorrencia, apurar as apurarOcorrenciaDominio, encerrarComEstorno, encerrarSemEstorno,
} from '../dominio/ocorrencia'
import { casosCardapio } from './casos/cardapio'
import { casosCardapioLink } from './casos/cardapioLink'
import { casosCliente } from './casos/cliente'
import { casosComanda } from './casos/comanda'
import { casosCobranca } from './casos/cobranca'
import { casosEncerramento } from './casos/encerramento'
import { casosEntregas } from './casos/entregas'
import { casosSimulacao } from './casos/simulacao'
import { casosAreaEntrega } from './casos/areaEntrega'
import { casosFuncionamento } from './casos/funcionamento'
import { casosAnexos } from './casos/anexos'
import { casosRespostas } from './casos/respostas'
import { casosLote } from './casos/lote'
import { casosJanelas } from './casos/janelas'
import { casosProducao } from './casos/producao'
import { casosCaixa } from './casos/caixa'
import { casosIntegracoes } from './casos/integracoes'
import { casosFidelidade } from './casos/fidelidade'
import { FUNCIONAMENTO_PADRAO } from '../dominio/funcionamento'
import { REGRA_FIDELIDADE_PADRAO, pontosGanhosNoPedido } from '../dominio/fidelidade'
import { FIDELIDADE_SEMENTE } from '../infra/fidelidadeSemente'

export const ATENDENTE = 'Thatiane'

export function estadoInicial({
  conversas, catalogo, regras, modoAgente, som = true,
  funcionamento = FUNCIONAMENTO_PADRAO, lojaAberta = null,
}) {
  return {
    conversas,
    catalogo,
    regras,
    modoAgente,
    // Horário de funcionamento por dia e estado manual da loja (pedido do
    // dono, 24/09/2026): `lojaAberta` nasce `null` (segue o horário) até o
    // primeiro toque no controle do topo.
    funcionamento,
    lojaAberta,
    selecionadaId: conversas[0]?.id ?? null,
    // Balcão (seção 1, rodada 5): duas abas, canais que ligam mais de um ao
    // mesmo tempo e uma ordem só. `MUDAR_FILTRO` é genérico (chave/valor), a
    // forma nova só muda o que mora em cada chave.
    filtros: { busca: '', aba: 'precisa', canais: [], ordenacao: 'urgencia' },
    rascunhos: {},
    automaticoPausado: {},
    // Lista, não campo único: dois desacertos de saldo antes dela clicar
    // "Entendi" no primeiro não podem apagar um ao outro (QA2-16).
    alertasEstoque: [],
    agente: { estado: 'ocioso', sugestao: null, conversaId: null },
    ultimoAvanco: null,
    // Preferência de "Som da cozinha" (US-010): quem chama `estadoInicial`
    // decide o valor de partida; lida do localStorage em AtendimentoProvider.
    som,
    lembretes: [],
    lembretesConcluidos: {},
    // Visto do sininho, guardado por `id`+`quando` (seção 8): assim um
    // lembrete automático (id fixo) que volta adiado, em horário novo, conta
    // como novo de novo. A Frente B decide a chave e quando marcar.
    vistos: {},
    // Rodada 5 · passo zero (seção 8). `ui.encerrando` guarda o id da
    // conversa cuja modal de encerramento (seção 5) está aberta, ou `null`;
    // abre e fecha de QUALQUER lugar da tela (cabeçalho, menu do pedido,
    // barra do Pedido), então mora no estado, não num `useState` de um
    // componente só. `relogio.deslocamentoMs` é o painel de simulações
    // (seção 7, "+10 min", "+30 min", "Agora") empurrando o relógio da tela
    // sem esperar de verdade; zero não muda nada hoje.
    ui: { encerrando: null },
    relogio: { deslocamentoMs: 0 },
    // Lote de papel (rodada 8, US-042, D6, UC-04 E1): `pedidosAbertos` é a
    // foto (números de pedido) tirada no instante em que caiu, não o estado
    // atual — casos/lote.js e dominio/loteDePapel.js decidem sozinhos, este
    // arquivo só dá o valor de partida.
    conexao: { online: true, offlineDesde: null, pedidosAbertos: [] },
    // Produção (rodada 13, issue #43): lotes por sku (RN-44 a RN-47),
    // descoberto por sku (RN-48/RN-49) e o histórico de ajustes com motivo
    // (UC-09). Sku sem lote nenhum aqui continua no `estoque` plano de
    // sempre — nada muda para quem não usa a aba nova.
    producao: { lotes: [], descobertos: {}, ajustes: [] },
    // Caixa (rodada 13, issue #44): uma lista só de lançamentos MovimentoCaixa
    // (abertura/entrada/saida/fechamento), igual ao EasyStok — nada de agregado
    // "sessão de caixa" à parte. `aplicacao/casos/caixa.js` é quem lê e escreve.
    caixa: { movimentos: [] },
    // Frente Fidelidade e cupons (rodada 13, issue #45, registro 107): cupons
    // e catálogo de recompensas/sorteios cadastrados na Gestão; resgate por
    // cadastro (pontos GANHOS são derivados do histórico, não moram aqui, ver
    // dominio/fidelidade.js).
    fidelidade: {
      cupons: FIDELIDADE_SEMENTE.cupons,
      config: REGRA_FIDELIDADE_PADRAO,
      recompensas: FIDELIDADE_SEMENTE.recompensas,
      sorteios: FIDELIDADE_SEMENTE.sorteios,
      resgatesPorCadastro: {},
    },
  }
}

const mapear = (estado, id, transformar) => ({
  ...estado,
  conversas: estado.conversas.map((c) => (c.id === id ? transformar(c) : c)),
})

// Rodada 13 (issue #43): sku sem lote nenhum não muda em nada — continua só
// no `estoque` plano de `dominio/cardapio.js`, comportamento herdado. Sku com
// produção lançada baixa do lote mais antigo (RN-50); o que não coube vira
// descoberto persistente (RN-48/RN-49).
const temLoteDoSku = (producao, sku) => producao.lotes.some((l) => l.sku === sku && !l.insumo)

function comBaixaDeProducao(producao, sku, quantidade, agora) {
  if (!temLoteDoSku(producao, sku)) return producao
  const { lotes, descoberto } = baixarLotesFifo(producao.lotes, sku, quantidade)
  return {
    ...producao,
    lotes,
    descobertos: descoberto > 0
      ? registrarDescoberto(producao.descobertos, sku, descoberto, new Date(agora).toISOString())
      : producao.descobertos,
  }
}

function comDevolucaoDeProducao(producao, sku, quantidade) {
  if (!temLoteDoSku(producao, sku)) return producao
  return { ...producao, lotes: devolverLotesFifo(producao.lotes, sku, quantidade).lotes }
}

// Cadastro bloqueado não tem caminho de envio nenhum: nem composer, nem painel
// do agente, nem regra automática, nem mensagem programada. Quem decide é a
// MESMA permissão de escrita que o composer consulta, e não uma segunda conta
// feita aqui dentro.
const recusaEnvio = (estado, id, agora) => {
  const conversa = estado.conversas.find((c) => c.id === id)
  if (!conversa) return true
  return envioBloqueado(conversa, agora, canalDaConversa(estado.catalogo.canais, conversa))
}

// Mensagem que ela manda ao cliente PARA o automático e grava o dono (RN-04).
// Vale para texto, mídia e programada: as três são a casa falando por ela.
// `aguardandoAbertura` (Frente Horário e loja) some quando ela mesma
// escreve: a fila era "ninguém olhou ainda", e agora alguém olhou.
const comEscritaDaCasa = (estado, id) => ({
  ...mapear(estado, id, (c) => ({
    ...c,
    estado: c.estado === 'Aberto' ? 'Em atendimento' : c.estado,
    responsavel: c.responsavel ?? ATENDENTE,
    aguardandoAbertura: false,
  })),
  automaticoPausado: { ...estado.automaticoPausado, [id]: true },
})

// Mensagem que sai para o cliente mexe no relógio da conversa. Registro de
// sistema não mexe, porque ninguém respondeu nada.
function comMensagem(conversa, mensagem, { id, agora }) {
  const paraOCliente = mensagem.dir !== 'sistema'
  return {
    ...conversa,
    atrasada: paraOCliente ? false : conversa.atrasada,
    ultimaEm: paraOCliente ? new Date(agora).toISOString() : conversa.ultimaEm,
    mensagens: [...conversa.mensagens, { id, em: new Date(agora).toISOString(), ...mensagem }],
  }
}

// Modo API (F01). A conversa do servidor manda; do lado de cá só sobrevivem os
// balões da dona ainda sem resposta da API (enviando) ou recusados (falhou), para
// ela ver o que não saiu. O resto do estado local (rascunhos, filtros) fica.
const mensagemSoLocal = (m) => m.status === 'enviando' || m.status === 'falhou'

function mesclarDoServidor(local, doServidor) {
  if (!local) return doServidor
  const doServidorIds = new Set(doServidor.mensagens.map((m) => m.id))
  const soLocais = local.mensagens.filter((m) => mensagemSoLocal(m) && !doServidorIds.has(m.id))
  return { ...local, ...doServidor, mensagens: [...doServidor.mensagens, ...soLocais] }
}

const CASOS_API = {
  [acao.SINCRONIZAR_CONVERSAS]: (estado, { conversas }) => {
    const locais = new Map(estado.conversas.map((c) => [c.id, c]))
    const mescladas = conversas.map((c) => mesclarDoServidor(locais.get(c.id), c))
    const aindaExiste = mescladas.some((c) => c.id === estado.selecionadaId)
    return {
      ...estado,
      conversas: mescladas,
      selecionadaId: aindaExiste ? estado.selecionadaId : (mescladas[0]?.id ?? null),
      automaticoPausado: Object.fromEntries(
        mescladas.map((c) => [c.id, c.situacaoApi === 'Assumida' ? PAUSA_POR_ASSUMIR : false]),
      ),
      sincronizacao: { estado: 'ok', mensagem: null, em: Date.now() },
    }
  },

  [acao.SINCRONIZACAO_FALHOU]: (estado, { mensagem }) => ({
    ...estado, sincronizacao: { ...estado.sincronizacao, estado: 'erro', mensagem },
  }),

  [acao.CONFIRMAR_ENVIO_API]: (estado, { id, mensagemId, mensagem }) =>
    mapear(estado, id, (c) => ({
      ...c, mensagens: c.mensagens.map((m) => (m.id === mensagemId ? mensagem : m)),
    })),

  [acao.FALHAR_ENVIO_API]: (estado, { id, mensagemId, erro }) =>
    mapear(estado, id, (c) => ({
      ...c, mensagens: c.mensagens.map((m) => (m.id === mensagemId ? { ...m, status: 'falhou', erro } : m)),
    })),

  [acao.AVISO_API]: (estado, { mensagem }) => ({
    ...estado, sincronizacao: { ...estado.sincronizacao, aviso: mensagem },
  }),

  // A API é a verdade do expediente: horário e controle manual substituem o local.
  [acao.SINCRONIZAR_EXPEDIENTE]: (estado, { funcionamento, lojaAberta, mensagemForaDoHorario, mensagemLojaFechada }) => ({
    ...estado,
    funcionamento,
    lojaAberta,
    expediente: { carregado: true, mensagemForaDoHorario, mensagemLojaFechada },
  }),
}

const CASOS = {
  [acao.SELECIONAR_CONVERSA]: (estado, { id }) => ({
    ...estado,
    selecionadaId: id,
    agente: estado.agente.conversaId === id ? estado.agente : { estado: 'ocioso', sugestao: null, conversaId: null },
  }),

  [acao.MUDAR_FILTRO]: (estado, { chave, valor }) => ({
    ...estado, filtros: { ...estado.filtros, [chave]: valor },
  }),

  // O rascunho vive no estado da conversa, não dentro do componente. Assim o
  // agente e as respostas rápidas escrevem nele sem truque de remontagem.
  [acao.DEFINIR_RASCUNHO]: (estado, { id, texto }) => ({
    ...estado, rascunhos: { ...estado.rascunhos, [id]: texto },
  }),

  [acao.ENVIAR_MENSAGEM]: (estado, { id, texto, agora, mensagemId, modelo = false, automatica = false, regra = null }) => {
    if (recusaEnvio(estado, id, agora)) return estado
    const base = mapear(estado, id, (c) => ({
      ...comMensagem(c, {
        dir: 'out', texto, automatica, modelo, regra,
        status: automatica ? 'lida' : 'enviando',
      }, { id: mensagemId, agora }),
      estado: c.estado === 'Aberto' ? 'Em atendimento' : c.estado,
      responsavel: c.responsavel ?? ATENDENTE,
    }))
    const comRascunhoLimpo = { ...base, rascunhos: { ...base.rascunhos, [id]: '' } }
    if (automatica) return comRascunhoLimpo
    return {
      ...comRascunhoLimpo,
      automaticoPausado: { ...comRascunhoLimpo.automaticoPausado, [id]: true },
    }
  },

  [acao.CONFIRMAR_ENTREGA]: (estado, { id, mensagemId }) =>
    mapear(estado, id, (c) => ({
      ...c,
      mensagens: c.mensagens.map((m) => (m.id === mensagemId ? { ...m, status: 'lida' } : m)),
    })),

  // Assumir e devolver ao automático moram em ASSUMIR_ATENDIMENTO e
  // DEVOLVER_AUTOMATICO, lá embaixo. As versões antigas destes dois nomes
  // saíram: nenhuma tela despachava, e caso sem consumidor diverge calado.
  // Integração 48: `reabertaNaMao` segura o encerramento por janela (46). Sem
  // ela, conversa de janela vencida reabria e fechava de novo no mesmo tique,
  // e "Reabrir conversa" não fazia nada na tela.
  [acao.REABRIR_CONVERSA]: (estado, { id }) =>
    mapear(estado, id, (c) => ({
      ...c, estado: 'Em atendimento', responsavel: c.responsavel ?? ATENDENTE, reabertaNaMao: true,
    })),

  // Fecha o ciclo depois de "Entregue" ou "Cancelado" (seção 2, ações de
  // pedido). Copy da confirmação em linha é da Frente A.
  [acao.ENCERRAR_ATENDIMENTO]: (estado, { id }) =>
    mapear(estado, id, (c) => ({ ...c, estado: 'Encerrado' })),

  // O cliente só é avisado quando a dona marca. O sistema não adivinha.
  [acao.AVANCAR_ESTEIRA]: (estado, {
    id, passo, agora, mensagemId, posEntregaId, entregador: entregadorEscolhido,
  }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    // Pedido cancelado não anda na esteira (dominio/pedido.js, pedidoEncerrado):
    // sem esta trava, avançar um passo nele ressuscitava o pedido calado.
    if (!conversa?.pedido || pedidoEncerrado(conversa.pedido)) return estado
    // RN-14 / US-019: com o cadastro bloqueado o pedido não anda para preparo,
    // embalado nem entrega, venha o toque da ficha, da cozinha ou de Entregas.
    if (motivoParaNaoAvancar(passo, { bloqueado: estaBloqueada(conversa) })) return estado
    const janela = janelaPorId(estado.catalogo.janelas, conversa.pedido.janela)
    // US-040: por ficha, cozinha ou o cartão solto de Entregas, "quem leva"
    // é o que a dona acabou de escolher no despacho (`entregadorEscolhido`,
    // `componentes/EscolhaDeEntregador.jsx`) ou o que já está resolvido
    // (viagem com chamado achado, ou já escolhido antes). Ponto único de
    // leitura, mesmo `entregadorResolvido` que `casos/entregas.js` usa.
    const entregador = passo === 'entrega'
      ? (entregadorEscolhido ?? entregadorResolvido(conversa.pedido))
      : undefined
    // RN-06 e RN-22 (US-028): respiro antes de virar texto, ponto único em
    // dominio/entrega.js. Vale sobretudo para o aviso de "em preparo". Rodada
    // 13 (issue #42): respiro passou a ser ajustável na tela de Janelas
    // (`estado.catalogo.respiroMinutos`), sem nunca descer do chão do RN-22.
    const aviso = avisoDoPasso(passo, {
      faixa: janela?.faixa
        ? faixaParaCliente(janela.faixa, agora, estado.catalogo.respiroMinutos)
        : janela?.faixa,
      entregador,
    })
    // Pós-entrega: mesmo padrão de PAGAMENTO_CONFIRMADO (regraDoGatilho +
    // textoDaRegra + contextoDePrevia), disparado quando o passo marcado é
    // "Entregue". É a automática "Agradecimento e avaliação" da tela virando
    // verdade: hoje ela só saía pelo botão de teste do modal (QA3-17).
    // Só dispara uma vez por pedido: CORRIGIR_PASSO volta o passo sem apagar
    // o agradecimento já lido pelo cliente, então marcar Entregue de novo não
    // pode mandar um segundo (achado da auditoria cruzada QA5).
    const agradecimento = passo === 'entregue' && !conversa.pedido.agradecimentoEnviado
      ? regraDoGatilho(estado.regras, GATILHOS.POS_ENTREGA)
      : null
    // O agradecimento automático já avisa que chegou e agradece: mandar o
    // aviso genérico do passo "Entregue" (que também tem "obrigada" escrito)
    // saía como um segundo agradecimento colado no primeiro (achado do
    // arquiteto, 23/09/2026; a guarda de não duplicar cobria remarcar, não
    // esta dobra na mesma marcação). Com o agradecimento ativo, o aviso vira
    // só o fato; sem ele (regra desligada ou já enviado), o aviso completo
    // continua sendo a única mensagem.
    const textoDoAviso = agradecimento && aviso ? 'Pedido entregue.' : aviso
    const proximo = mapear(estado, id, (c) => {
      const comPasso = {
        ...c,
        pedido: {
          ...c.pedido,
          estado: passo,
          ...(entregador !== undefined ? { entregador } : {}),
          ...(agradecimento ? { agradecimentoEnviado: true } : {}),
        },
      }
      const comAviso = aviso
        ? comMensagem(
          comPasso,
          // Item D (banca 10): "esteira" sozinho não dizia QUAL passo mandou a
          // mensagem; com o passo no valor, a etiqueta "automática" do balão
          // (Balao.jsx) sabe abrir a definição certa (dominio/respostas.js,
          // itemDaBibliotecaPelaRegra) em vez de nenhuma.
          { dir: 'out', texto: textoDoAviso, status: 'lida', automatica: true, regra: `esteira-${passo}` },
          { id: mensagemId, agora },
        )
        : comPasso
      if (!agradecimento) return comAviso
      return comMensagem(
        comAviso,
        {
          dir: 'out', status: 'lida', automatica: true, regra: agradecimento.id,
          texto: textoDaRegra(agradecimento, contextoDePrevia(c, janela?.faixa)),
        },
        { id: posEntregaId, agora },
      )
    })
    return {
      ...proximo,
      ultimoAvanco: {
        conversaId: id, mensagemId, em: agora, posEntregaId: agradecimento ? posEntregaId : null,
      },
    }
  },

  // Desfazer de verdade: volta o passo e apaga a mensagem que a casa mandou.
  // Sem isto, marcar por engano é irreversível e o cliente já leu. Quando o
  // passo desfeito é "Entregue", o agradecimento automático some junto: senão
  // sobra um "obrigada pela preferência" sem entrega nenhuma por trás.
  [acao.DESFAZER_ESTEIRA]: (estado, { id, mensagemId, posEntregaId }) =>
    mapear(estado, id, (c) => {
      if (!c.pedido) return c
      const anterior = passoAnterior(c.pedido.estado)
      if (!anterior) return c
      return {
        ...c,
        pedido: {
          ...c.pedido,
          estado: anterior,
          // Aqui a mensagem some de verdade (apagada duas linhas abaixo), diferente
          // de CORRIGIR_PASSO: então a marca de agradecimento também some, senão o
          // próximo "Entregue" de verdade fica mudo.
          ...(c.pedido.estado === 'entregue' ? { agradecimentoEnviado: false } : {}),
        },
        mensagens: c.mensagens.filter((m) => m.id !== mensagemId && m.id !== posEntregaId),
      }
    }),

  // Correção deliberada, fora da janela de 10s do desfazer: ela percebe o
  // erro depois, com a mensagem automática já lida pelo cliente. Só volta o
  // passo, nunca apaga mensagem, diferente do DESFAZER_ESTEIRA (P1, QA2-13).
  [acao.CORRIGIR_PASSO]: (estado, { id }) =>
    mapear(estado, id, (c) => {
      if (!c.pedido) return c
      const anterior = passoAnterior(c.pedido.estado)
      if (!anterior) return c
      return { ...c, pedido: { ...c.pedido, estado: anterior } }
    }),

  [acao.TROCAR_JANELA]: (estado, { id, janela }) =>
    mapear(estado, id, (c) => (c.pedido ? { ...c, pedido: { ...c.pedido, janela } } : c)),

  // P0 (banca3/e1-e3, achado 1): o telefone é o próprio identificador do
  // canal no WhatsApp. Sem copiar `telefoneCanal` aqui, o lead virava cliente
  // sem telefone e RN-02/US-002 (saudar pelo nome na próxima) nunca disparava.
  //
  // RN-03/UC-01 passo 10 (rodada 10, item P1.4): confirmar endereço grava o
  // cadastro, mas NÃO promove a lead a cliente. "Lead é quem ainda não
  // comprou" (RN-03); quem promove é a primeira compra paga, em
  // `comPagamentoReconhecido` mais abaixo.
  [acao.CADASTRAR_ENDERECO]: (estado, { id, agora, mensagemId }) =>
    mapear(estado, id, (c) => comMensagem({
      ...c,
      cliente: {
        ...c.cliente,
        endereco: c.cliente.enderecoCapturado,
        enderecoCapturado: null,
        telefone: c.cliente.telefone || (c.canal === 'WhatsApp' ? c.cliente.telefoneCanal : null) || null,
      },
    }, {
      dir: 'sistema',
      texto: 'Cadastro criado a partir do endereço confirmado na conversa.',
    }, { id: mensagemId, agora })),

  // Saldo zero avisa, nunca trava a venda.
  [acao.ADICIONAR_ITEM]: (estado, {
    id, item, numeroPedido, alertaId, agora,
  }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    // Pedido cancelado não edita comanda (dominio/pedido.js, pedidoEncerrado):
    // sem esta trava, um item novo entrava numa comanda que já morreu.
    if (pedidoEncerrado(conversa?.pedido)) return estado
    const situacao = situacaoDeEstoque(item)
    const comProduto = mapear(estado, id, (c) => ({
      ...c,
      pedido: comItem(c.pedido ?? novoPedido(numeroPedido), item.sku),
    }))
    return {
      ...comProduto,
      catalogo: { ...estado.catalogo, cardapio: baixarSaldo(estado.catalogo.cardapio, item.sku) },
      // Rodada 13 (issue #43, RN-50): sku com lote lançado baixa dele; sem
      // lote, `producao` não muda (comBaixaDeProducao é no-op).
      producao: comBaixaDeProducao(estado.producao, item.sku, 1, agora),
      alertasEstoque: situacao.alerta
        ? [...estado.alertasEstoque, { id: alertaId, texto: textoDoAlerta(item) }]
        : estado.alertasEstoque,
    }
  },

  [acao.REMOVER_ITEM]: (estado, { id, sku }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    const linha = conversa?.pedido?.itens.find((l) => l.sku === sku)
    if (!linha) return estado
    return {
      ...mapear(estado, id, (c) => ({ ...c, pedido: semItem(c.pedido, sku) })),
      catalogo: { ...estado.catalogo, cardapio: devolverSaldo(estado.catalogo.cardapio, sku, linha.qtd) },
      producao: comDevolucaoDeProducao(estado.producao, sku, linha.qtd),
    }
  },

  // O +/- da comanda soma ou tira saldo igual ao caminho de ADICIONAR_ITEM,
  // então a mesma checagem de saldo zero roda aqui: aumentar quantidade pelo
  // +/- também pode zerar o item, e antes disso não nascia alerta nenhum
  // (QA2-18).
  [acao.AJUSTAR_QUANTIDADE]: (estado, {
    id, sku, delta, alertaId, agora,
  }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    const linha = conversa?.pedido?.itens.find((l) => l.sku === sku)
    if (!linha) return estado
    const nova = linha.qtd + delta
    if (nova <= 0) return CASOS[acao.REMOVER_ITEM](estado, { id, sku })
    const item = itemPorSku(estado.catalogo.cardapio, sku)
    const situacao = delta > 0 && item ? situacaoDeEstoque(item) : null
    return {
      ...mapear(estado, id, (c) => ({
        ...c,
        pedido: {
          ...c.pedido,
          itens: c.pedido.itens.map((l) => (l.sku === sku ? { ...l, qtd: nova } : l)),
        },
      })),
      catalogo: {
        ...estado.catalogo,
        cardapio: delta > 0
          ? baixarSaldo(estado.catalogo.cardapio, sku)
          : devolverSaldo(estado.catalogo.cardapio, sku),
      },
      // Rodada 13 (issue #43): mesmo caminho de ADICIONAR_ITEM/REMOVER_ITEM.
      producao: delta > 0
        ? comBaixaDeProducao(estado.producao, sku, Math.abs(delta), agora)
        : comDevolucaoDeProducao(estado.producao, sku, Math.abs(delta)),
      alertasEstoque: situacao?.alerta
        ? [...estado.alertasEstoque, { id: alertaId, texto: textoDoAlerta(item) }]
        : estado.alertasEstoque,
    }
  },

  // Texto livre por item, sem checagem de saldo nem toque em estoque: é
  // anotação da comanda, não mudança de quantidade (QA2-01).
  [acao.AJUSTAR_OBSERVACAO]: (estado, { id, sku, texto }) =>
    mapear(estado, id, (c) => (c.pedido ? { ...c, pedido: comObservacao(c.pedido, sku, texto) } : c)),

  // Gerar o pedido manda o resumo e a cobrança de verdade, no mesmo despacho.
  // Antes saía a regra "cobranca", com o texto "Segue o link de pagamento" e
  // link nenhum: o cliente lia a promessa de um link que não existia. Agora a
  // emissão do Pix chega pronta na ação (o provider emite, RN-24) e o texto sai
  // de textoDaCobranca, que é o mesmo do botão Gerar cobrança. Uma mensagem só.
  [acao.GERAR_PEDIDO]: (estado, { id, agora, mensagemId, cobrancaId, emissao }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido) return estado
    if (recusaEnvio(estado, id, agora)) return estado
    const janela = janelaPorId(estado.catalogo.janelas, conversa.pedido.janela)
    const resumo = resumoParaCliente(conversa.pedido, estado.catalogo.cardapio, janela)
    // Cupom aplicado (frente Fidelidade, issue #45) desconta sozinho aqui:
    // `criarCobranca` já subtrai `pedido.valorDesconto` do total bruto (ver
    // dominio/cobranca.js). Sem `valorForcado`: esse parâmetro é de OUTRO
    // conceito (cobrança de diferença/complemento) e marcaria esta cobrança
    // normal como `cobranca.diferenca`, trocando a mensagem ao cliente.
    const cobranca = criarCobranca(conversa.pedido, estado.catalogo.cardapio, agora, emissao)
    const texto = textoDaCobranca(cobranca, conversa.pedido, estado.catalogo.cardapio)

    return mapear(estado, id, (c) => {
      const comResumo = comMensagem(
        { ...c, pedido: { ...c.pedido, cobranca } },
        { dir: 'out', texto: resumo, status: 'lida', automatica: true, regra: 'resumo' },
        { id: mensagemId, agora },
      )
      return comMensagem(
        comResumo,
        { dir: 'out', texto, status: 'lida', automatica: true, regra: 'cobranca-pix' },
        { id: cobrancaId, agora },
      )
    })
  },

  [acao.SALVAR_NOTA]: (estado, { id, texto, agora, notaId }) =>
    mapear(estado, id, (c) => ({
      ...c,
      cliente: {
        ...c.cliente,
        notas: [{ id: notaId, autor: ATENDENTE, em: dataHora(agora), texto }, ...c.cliente.notas],
      },
    })),

  // Figurinha, carta de cardápio, e (rodada 7, frente Anexos) arquivo, áudio
  // gravado e peça da galeria: mensagem como qualquer outra, com tipo próprio.
  // Saem na hora, sem rascunho, porque é resposta de mão ocupada. Pausam o
  // automático e gravam o dono igual ao texto: quem mandou foi ela, e a
  // conversa passou a ser dela. `...extra` é só espalhado (nunca lido aqui):
  // cada formato novo carrega os campos que o molde de `dominio/anexos.js`
  // decidiu (duracaoMs do áudio, nome/descrição da peça etc.), sem este case
  // precisar conhecer formato nenhum por nome.
  [acao.ENVIAR_MIDIA]: (estado, {
    id, formato, arte, texto, agora, mensagemId, ...extra
  }) => {
    if (recusaEnvio(estado, id, agora)) return estado
    return mapear(comEscritaDaCasa(estado, id), id, (c) => comMensagem(c, {
      dir: 'out', formato, arte, texto, status: 'lida', ...extra,
    }, { id: mensagemId, agora }))
  },

  [acao.FECHAR_ALERTA]: (estado, { alertaId }) => ({
    ...estado, alertasEstoque: estado.alertasEstoque.filter((a) => a.id !== alertaId),
  }),

  [acao.ALTERNAR_REGRA]: (estado, { regraId }) => ({
    ...estado,
    regras: estado.regras.map((r) => (r.id === regraId ? { ...r, ativa: !r.ativa } : r)),
  }),

  [acao.TROCAR_MODO_AGENTE]: (estado, { modo }) => ({ ...estado, modoAgente: modo }),

  [acao.AGENTE_PEDINDO]: (estado, { id }) => ({
    ...estado, agente: { estado: 'pensando', sugestao: null, conversaId: id },
  }),

  [acao.AGENTE_RESPONDEU]: (estado, { id, sugestao }) => ({
    ...estado, agente: { estado: 'pronto', sugestao, conversaId: id },
  }),

  [acao.AGENTE_LIMPOU]: (estado) => ({
    ...estado, agente: { estado: 'ocioso', sugestao: null, conversaId: null },
  }),

  // Transporte falhou (servidor local fora, sem credencial, modelo recusou).
  // A falha fica visível na tela em vez de deixar o painel em "Lendo" para sempre.
  [acao.AGENTE_FALHOU]: (estado, { id, erro }) => ({
    ...estado, agente: { estado: 'erro', sugestao: null, conversaId: id, erro },
  }),

  // ---------------------------------------------------------------------------
  // Rodada 2 · cobrança por Pix. Casos acrescentados no fim do objeto de
  // propósito, para o merge com a outra árvore não disputar linha nenhuma.
  // ---------------------------------------------------------------------------

  // A cobrança nasce junto com a mensagem que o cliente lê, no mesmo despacho.
  // Separar as duas coisas criaria cobrança que ninguém recebeu, e é justamente
  // esse silêncio que faz a dona ligar para o cliente perguntando se chegou.
  [acao.GERAR_COBRANCA]: (estado, { id, agora, mensagemId, emissao }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido || conversa.pedido.itens.length === 0) return estado
    // Ver nota de GERAR_PEDIDO acima: cupom desconta sozinho dentro de
    // `criarCobranca`.
    const cobranca = criarCobranca(conversa.pedido, estado.catalogo.cardapio, agora, emissao)
    const texto = textoDaCobranca(cobranca, conversa.pedido, estado.catalogo.cardapio)
    return mapear(estado, id, (c) => comMensagem(
      { ...c, pedido: { ...c.pedido, cobranca } },
      { dir: 'out', texto, status: 'lida', automatica: true, regra: 'cobranca-pix' },
      { id: mensagemId, agora },
    ))
  },

  // Reenvio depois de vencer. Conta tentativa e manda código novo: código velho
  // que o cliente guardou não paga mais, e ele precisa saber disso.
  [acao.REENVIAR_COBRANCA]: (estado, { id, agora, mensagemId, emissao, valorForcado = null }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido?.cobranca) return estado
    const cobranca = reemitirCobranca(
      conversa.pedido.cobranca, conversa.pedido, estado.catalogo.cardapio,
      agora, emissao, valorForcado,
    )
    const texto = textoDaCobranca(cobranca, conversa.pedido, estado.catalogo.cardapio)
    // Cobrança paga que sai de cena (complemento, diferença) vai para o
    // arquivo `pedido.pagamentos`: é o que mantém "quanto já foi pago" certo
    // depois da troca (dominio/pagamento.js, fonte única, registro 38).
    const anterior = conversa.pedido.cobranca
    const pagamentos = anterior.pagaEm
      ? [...(conversa.pedido.pagamentos ?? []), registroDaCobranca(anterior)]
      : (conversa.pedido.pagamentos ?? [])
    return mapear(estado, id, (c) => comMensagem(
      { ...c, pedido: { ...c.pedido, cobranca, pagamentos } },
      { dir: 'out', texto, status: 'lida', automatica: true, regra: 'cobranca-pix' },
      { id: mensagemId, agora },
    ))
  },

  // Simula o webhook do provedor (RN-24: a conciliação não é trabalho dela). O
  // botão na tela existe porque no protótipo não há provedor do outro lado, e
  // porque cliente que paga por fora acontece. O recibo ao cliente reusa a regra
  // automática de pagamento confirmado, que já existia e nunca tinha disparado.
  [acao.CONFIRMAR_PAGAMENTO]: (estado, { id, agora, mensagemId, valorPago = null }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido?.cobranca) return estado
    const cobranca = aplicarPagamento(conversa.pedido.cobranca, agora, valorPago)
    return comPagamentoReconhecido(estado, id, cobranca, { agora, mensagemId })
  },

  // O cliente mandou o print. Registra a espera e responde a ele, e só. Liberar
  // pedido por comprovante é o vetor do comprovante falso: a esteira continua
  // fechada até a baixa chegar.
  [acao.MARCAR_COMPROVANTE]: (estado, { id, agora, mensagemId }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido?.cobranca) return estado
    const cobranca = marcarComprovante(conversa.pedido.cobranca, agora)
    return mapear(estado, id, (c) => comMensagem(
      { ...c, pedido: { ...c.pedido, cobranca } },
      { dir: 'out', texto: TEXTO_DE_CONFERENCIA, status: 'lida', automatica: true, regra: 'cobranca-pix' },
      { id: mensagemId, agora },
    ))
  },

  // Caiu menos do que o cobrado e ela decidiu deixar passar. Quem escolhe é ela,
  // o sistema só registra quem liberou e quando.
  [acao.ACEITAR_DIVERGENCIA]: (estado, { id, agora, mensagemId }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido?.cobranca) return estado
    const cobranca = aceitarDivergencia(conversa.pedido.cobranca, agora)
    return comPagamentoReconhecido(estado, id, cobranca, { agora, mensagemId })
  },

  // ---------------------------------------------------------------------------
  // Rodada 3 · ações de pedido (passo zero da direção visual, seção 2 e 5).
  // O texto que o cliente lê e a confirmação em linha são da Frente A; aqui só
  // o estado que muda.
  // ---------------------------------------------------------------------------

  // Cancelar sempre avisa o cliente, com o texto pronto que a Frente A monta
  // (mesmo padrão de ENVIAR_MIDIA/GERAR_PEDIDO: o dominio não escreve copy).
  [acao.CANCELAR_PEDIDO]: (estado, { id, agora, mensagemId, texto }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido || pedidoEncerrado(conversa.pedido)) return estado
    // Fecha a cobrança em aberto junto (bug do dono, 24/09/2026: cancelar com
    // a cobrança vencida não fazia o relógio sumir). Cobrança já paga não é
    // tocada: só some quem ainda não recebeu nada.
    const cobranca = cancelarCobranca(conversa.pedido.cobranca, agora)
    return mapear(estado, id, (c) => comMensagem(
      { ...c, pedido: { ...c.pedido, estado: 'cancelado', cobranca } },
      { dir: 'out', texto, status: 'lida', automatica: true, regra: 'cancelamento' },
      { id: mensagemId, agora },
    ))
  },

  // Antes de "Em entrega" o estorno cancela o pedido junto, porque a massa
  // ainda não saiu da cozinha; depois ele só ganha a marca (cobranca.estornadaEm),
  // porque a Frente A lê esse campo para mostrar "Estornado" ao lado do passo.
  //
  // UC-06 passo 5-6: motivo obrigatório (barrado dentro de `estornarCobranca`
  // quando falta), valor pode ser parcial. O motivo grava em dois lugares: na
  // ocorrência, se o pedido tiver uma aberta, e no cadastro (nota interna).
  [acao.MARCAR_ESTORNO]: (estado, { id, agora, motivo, valor, notaId }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido?.cobranca) return estado
    const cobranca = estornarCobranca(conversa.pedido.cobranca, agora, { motivo, valor })
    if (cobranca === conversa.pedido.cobranca) return estado
    const antesDaEntrega = indiceDoPasso(conversa.pedido.estado) < indiceDoPasso('entrega')
    const ocorrencia = conversa.pedido.ocorrencia
      ? encerrarComEstorno(conversa.pedido.ocorrencia, { motivo, valor: cobranca.valorEstornado }, agora)
      : conversa.pedido.ocorrencia
    const nota = {
      id: notaId,
      autor: ATENDENTE,
      em: dataHora(agora),
      texto: `Estorno de ${moeda(cobranca.valorEstornado)} no pedido ${conversa.pedido.numero}. Motivo: ${motivo}`,
    }
    return mapear(estado, id, (c) => ({
      ...c,
      cliente: { ...c.cliente, notas: [nota, ...c.cliente.notas] },
      pedido: {
        ...c.pedido,
        cobranca,
        ocorrencia,
        estado: antesDaEntrega ? 'cancelado' : c.pedido.estado,
      },
    }))
  },

  // ---------------------------------------------------------------------------
  // Frente Reclamação · ocorrência de pedido entregue (RN-34 a RN-38, UC-06).
  // ---------------------------------------------------------------------------

  // US-049: abertura automática, disparada de `consultarAgente`
  // (AtendimentoProvider) quando o classificador reconhece reclamação de
  // produto entregue. Uma ocorrência aberta por pedido: uma segunda tentativa
  // de abrir não duplica.
  // `origem` é novo na rodada 10 (item 4, ocorrência de entrega): default
  // preserva quem já chamava isto sem saber do motivo novo (reclamação).
  [acao.ABRIR_OCORRENCIA]: (estado, { id, agora, ocorrenciaId, relatoCliente, origem }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido || conversa.pedido.ocorrencia) return estado
    const ocorrencia = abrirOcorrencia({
      id: ocorrenciaId, pedidoNumero: conversa.pedido.numero, relatoCliente, agora, ...(origem ? { origem } : {}),
    })
    return mapear(estado, id, (c) => ({ ...c, pedido: { ...c.pedido, ocorrencia } }))
  },

  // UC-06 passos 3-4: a dona entra e apura.
  [acao.APURAR_OCORRENCIA]: (estado, { id, agora }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido?.ocorrencia) return estado
    const ocorrencia = apurarOcorrenciaDominio(conversa.pedido.ocorrencia, agora)
    return mapear(estado, id, (c) => ({ ...c, pedido: { ...c.pedido, ocorrencia } }))
  },

  // Alternativo A: não era defeito, é preferência. Fecha sem mexer na
  // cobrança e a preferência vira nota interna no cadastro.
  [acao.ENCERRAR_OCORRENCIA_SEM_ESTORNO]: (estado, { id, agora, preferencia, notaId }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido?.ocorrencia) return estado
    const ocorrencia = encerrarSemEstorno(conversa.pedido.ocorrencia, { preferencia }, agora)
    if (ocorrencia === conversa.pedido.ocorrencia) return estado
    const nota = { id: notaId, autor: ATENDENTE, em: dataHora(agora), texto: `Sem estorno. ${preferencia}` }
    return mapear(estado, id, (c) => ({
      ...c,
      cliente: { ...c.cliente, notas: [nota, ...c.cliente.notas] },
      pedido: { ...c.pedido, ocorrencia },
    }))
  },

  // ---------------------------------------------------------------------------
  // Rodada 2 · cardápio do dia.
  // ---------------------------------------------------------------------------

  // Ligar e desligar item do dia é a cozinha dizendo o que existe, não venda.
  // O item sai do DIA, nunca da lista (RN-16): quem procura continua achando,
  // com o estado escrito.
  [acao.ALTERNAR_DISPONIBILIDADE]: (estado, { sku }) => ({
    ...estado,
    catalogo: {
      ...estado.catalogo,
      cardapio: alternarDisponibilidade(estado.catalogo.cardapio, sku),
    },
  }),

  // Acerto de saldo na mão. É ela contando o congelador, e o número dela vence o
  // do sistema. Era a frustração do áudio 14: produzir um quilo, vender 500 g e
  // o sistema dizer que não tem.
  [acao.AJUSTAR_SALDO]: (estado, { sku, delta }) => ({
    ...estado,
    catalogo: {
      ...estado.catalogo,
      cardapio: ajustarSaldo(estado.catalogo.cardapio, sku, delta),
    },
  }),

  // ---------------------------------------------------------------------------
  // Rodada 2 · janelas de entrega com capacidade.
  // ---------------------------------------------------------------------------

  // Escolher janela ocupa vaga e trocar devolve, sem ninguém somar nada: a
  // ocupação é contada dos pedidos (dominio/entrega.js), então mover o pedido JÁ
  // é mover a vaga. Trocar de janela nunca exige cancelar e refazer o pedido,
  // erro que o Zé Delivery comete e que a Baymard mede em mercado online.
  //
  // Encaixe forçado de antes é apagado aqui: se ela voltou para uma janela com
  // vaga, o estouro daquele pedido deixou de existir.
  [acao.ESCOLHER_JANELA]: (estado, { id, janela }) =>
    mapear(estado, id, (c) => (c.pedido
      ? { ...c, pedido: { ...c.pedido, janela, encaixeForcado: null } }
      : c)),

  // Encaixe fora de vaga. No automático isso não existe: o agente passa para a
  // dona (D7 e RN-53). Aqui ela força, com confirmação, e fica registrado o que
  // estourou, quando e por quê. Registro na conversa também, porque quem vê a
  // conversa depois precisa entender por que aquela janela tem gente demais.
  [acao.FORCAR_ENCAIXE]: (estado, { id, janela, faixa, motivo, agora, mensagemId }) =>
    mapear(estado, id, (c) => (c.pedido
      ? comMensagem(
        {
          ...c,
          pedido: {
            ...c.pedido,
            janela,
            encaixeForcado: { em: agora, faixa, motivo },
          },
        },
        {
          dir: 'sistema',
          texto: `Encaixe forçado na janela de ${faixa}. ${motivo}`,
        },
        { id: mensagemId, agora },
      )
      : c)),

  // O agente devolveu a conversa (acao passar_para_dona). A marca fica NA
  // conversa, com o motivo, para o cartão do balcão contar a mesma história do
  // painel. Passagem ABERTA não é sobrescrita: o relógio da espera é o da
  // primeira devolução, senão o lembrete de 10 min nunca vence. Passagem já
  // ASSUMIDA é passado, e a devolução nova toma o lugar dela: senão o motivo
  // novo era descartado calado e o cartão contava a história errada.
  [acao.PASSAR_PARA_DONA]: (estado, { id, motivo, agora }) =>
    mapear(estado, id, (c) => (c.passagem && !c.passagem.assumida ? c : {
      ...c, passagem: { motivo, em: new Date(agora).toISOString(), assumida: false },
    })),

  // Assumir é um toque: a conversa fica com ela e o automático para na hora
  // (RN-04). Aviso de esteira continua saindo, que é a RN-05.
  [acao.ASSUMIR_ATENDIMENTO]: (estado, { id }) => ({
    ...mapear(estado, id, (c) => ({
      ...c,
      responsavel: ATENDENTE,
      estado: c.estado === 'Encerrado' ? c.estado : 'Em atendimento',
      passagem: c.passagem ? { ...c.passagem, assumida: true } : null,
    })),
    automaticoPausado: { ...estado.automaticoPausado, [id]: PAUSA_POR_ASSUMIR },
  }),

  // Devolver ao automático é decisão dela, nunca do relógio (D4).
  [acao.DEVOLVER_AUTOMATICO]: (estado, { id }) => ({
    ...mapear(estado, id, (c) => ({ ...c, passagem: null })),
    automaticoPausado: { ...estado.automaticoPausado, [id]: false },
  }),

  [acao.ALTERNAR_SOM]: (estado, { ligado }) => ({ ...estado, som: ligado }),

  // Regra é dado: mudar o texto ou a espera é trocar um campo da entrada, e
  // nenhum componente precisa saber disso.
  [acao.EDITAR_REGRA]: (estado, { regraId, campos }) => ({
    ...estado,
    regras: estado.regras.map((r) => (r.id === regraId ? { ...r, ...campos } : r)),
  }),

  [acao.CRIAR_LEMBRETE]: (estado, { lembrete }) => ({
    ...estado, lembretes: [...estado.lembretes, lembrete],
  }),

  // Concluído sai da lista e fica no histórico da conversa como mensagem de
  // sistema. Ela precisa enxergar depois que aquilo foi feito, e onde.
  [acao.CONCLUIR_LEMBRETE]: (estado, { lembreteId, titulo, conversaId, agora, mensagemId }) => {
    const semEle = {
      ...estado,
      lembretes: estado.lembretes.filter((l) => l.id !== lembreteId),
      lembretesConcluidos: { ...estado.lembretesConcluidos, [lembreteId]: true },
    }
    if (!conversaId) return semEle
    return mapear(semEle, conversaId, (c) => comMensagem(c, {
      dir: 'sistema', texto: 'Lembrete concluído: ' + titulo,
    }, { id: mensagemId, agora }))
  },

  // Os lembretes automáticos são retrato do estado das conversas: o que não
  // vale mais sai sozinho, o que foi adiado mantém a hora nova, e o que ela
  // concluiu não volta.
  [acao.SINCRONIZAR_LEMBRETES]: (estado, { automaticos }) => {
    const manuais = estado.lembretes.filter((l) => l.origem === ORIGENS.MANUAL)
    const antigos = new Map(estado.lembretes.map((l) => [l.id, l]))
    const vivos = automaticos
      .filter((l) => !estado.lembretesConcluidos[l.id])
      .map((l) => antigos.get(l.id) ?? l)
    const proximos = [...manuais, ...vivos]
    const igual = proximos.length === estado.lembretes.length
      && proximos.every((l, i) => l === estado.lembretes[i])
    return igual ? estado : { ...estado, lembretes: proximos }
  },

  // Visto estilo Instagram (seção 8): a Frente B manda as chaves `id+quando`
  // dos itens de "Novos" quando o painel fecha, e elas somam ao conjunto.
  [acao.MARCAR_LEMBRETES_VISTOS]: (estado, { chaves }) => {
    if (!chaves?.length) return estado
    const vistos = { ...estado.vistos }
    for (const chave of chaves) vistos[chave] = true
    return { ...estado, vistos }
  },

  // RN-14: bloquear é do cadastro, não da conversa. Toda conversa do mesmo
  // cadastro, em qualquer canal, fica bloqueada junto e com o automático
  // desligado. Rodada 5 (seção 2, passo zero): o alvo é `cadastroId`, não
  // mais `nome` (renomear na edição em linha não pode quebrar o bloqueio).
  [acao.BLOQUEAR_CLIENTE]: (estado, { cadastroId, motivo, em, por, agora, mensagemId }) => {
    const alvos = conversasDoCadastro(estado.conversas, cadastroId)
    if (alvos.length === 0) return estado
    const idsAlvo = new Set(alvos.map((c) => c.id))
    const bloqueio = novoBloqueio({ motivo, em, por })
    const texto = `Cadastro bloqueado por ${por}. Motivo: ${bloqueio.motivo} `
      + 'Atendimento automático suspenso em todos os canais.'
    const conversas = bloquearCadastro(estado.conversas, cadastroId, bloqueio).map((c) =>
      (idsAlvo.has(c.id)
        ? comMensagem(c, { dir: 'sistema', texto }, { id: `${mensagemId}-${c.id}`, agora })
        : c))
    return {
      ...estado,
      conversas,
      automaticoPausado: {
        ...estado.automaticoPausado,
        ...Object.fromEntries(alvos.map((c) => [c.id, true])),
      },
    }
  },

  // Desbloquear devolve a escrita, nunca o automático: quem religa o robô para
  // um cadastro que já deu problema é ela, num segundo gesto.
  [acao.DESBLOQUEAR_CLIENTE]: (estado, { cadastroId, por, agora, mensagemId }) => {
    const alvos = conversasDoCadastro(estado.conversas, cadastroId)
    if (alvos.length === 0) return estado
    const idsAlvo = new Set(alvos.map((c) => c.id))
    const texto = `Bloqueio removido por ${por}. `
      + 'O atendimento automático segue desligado até você retomar.'
    const conversas = desbloquearCadastro(estado.conversas, cadastroId).map((c) =>
      (idsAlvo.has(c.id)
        ? comMensagem(c, { dir: 'sistema', texto }, { id: `${mensagemId}-${c.id}`, agora })
        : c))
    return { ...estado, conversas }
  },

  // ---------------------------------------------------------------------------
  // Rodada 5 · passo zero da direção visual (seção 8). `ui.encerrando` abre a
  // modal de encerramento (seção 5) de qualquer lugar: cabeçalho da conversa,
  // menu do pedido e barra do Pedido. A F5 só constrói o CONTEÚDO da modal,
  // em `casos/encerramento.js`; abrir e fechar já são do passo zero, porque a
  // rodada 4 já tem botões que precisam chamar "Encerrar" hoje.
  // ---------------------------------------------------------------------------

  // Ponta (d) da integração: com `numero`, abre em leitura o resumo daquele
  // atendimento (modal Histórico, aba Atendimentos).
  [acao.ABRIR_ENCERRAMENTO]: (estado, { id, numero = null }) => ({
    ...estado, ui: { ...estado.ui, encerrando: id, encerrandoNumero: numero },
  }),

  [acao.FECHAR_ENCERRAMENTO]: (estado) => ({ ...estado, ui: { ...estado.ui, encerrando: null, encerrandoNumero: null } }),
}

// Objeto composto por tema (seção 8, passo zero): cada frente (F2 a F7) só
// edita o próprio `casos/<tema>.js`, nunca este arquivo. `CASOS` acima é a
// base que já existia antes desta rodada mais os dois casos de
// `ui.encerrando`, que são do passo zero.
const CASOS_COMPOSTOS = {
  ...CASOS_API,
  ...CASOS,
  ...casosCardapio,
  ...casosCardapioLink,
  ...casosCliente,
  ...casosComanda,
  ...casosCobranca,
  ...casosEncerramento,
  ...casosEntregas,
  ...casosSimulacao,
  ...casosAreaEntrega,
  ...casosFuncionamento,
  ...casosAnexos,
  ...casosRespostas,
  ...casosLote,
  ...casosJanelas,
  ...casosProducao,
  ...casosCaixa,
  ...casosIntegracoes,
  ...casosFidelidade,
}

// Dinheiro reconhecido em um lugar só: a baixa exata, a marcação à mão e a
// diferença aceita terminam aqui. Enquanto a cobrança não libera (valor diferente
// sem decisão dela), o pedido NÃO anda e o recibo não sai.
function comPagamentoReconhecido(estado, id, cobranca, { agora, mensagemId }) {
  const conversa = estado.conversas.find((c) => c.id === id)
  const janela = janelaPorId(estado.catalogo.janelas, conversa.pedido.janela)
  const recibo = regraDoGatilho(estado.regras, GATILHOS.PAGAMENTO_CONFIRMADO)
  const libera = liberaEsteira(cobranca)
  // Reconhecer o dinheiro ABRE a esteira, nunca anda para trás: aceitar a
  // diferença de um pedido que já está em preparo não pode devolver ele para
  // "pago" e obrigar a marcar o preparo de novo.
  const paraPago = (pedido) =>
    (libera && indiceDoPasso(pedido.estado) < indiceDoPasso('pago') ? 'pago' : pedido.estado)
  // O mesmo contexto da prévia das automáticas: {nome}, {faixa} e {pedido}.
  // Com só { faixa }, a regra que usa {nome} chegava crua ao cliente.
  // `agora` aplica o respiro (RN-06, RN-22, US-028) dentro de contextoDePrevia.
  const contexto = contextoDePrevia(conversa, janela?.faixa, agora)
  // RN-03/UC-01 passo 10 (rodada 10, item P1.4): a primeira compra paga é
  // quem promove lead a cliente, nunca a confirmação de endereço
  // (CADASTRAR_ENDERECO, mais acima). A marca de sistema é o mesmo sinal que
  // dominio/resumoAtendimento.js lê para calcular "Converteu" no resumo.
  const converte = libera && ehLead(conversa)
  return mapear(estado, id, (c) => {
    let comConversao = c
    if (converte) {
      comConversao = comMensagem({
        ...c, conta: 'cliente', cliente: { ...c.cliente, desde: mesPorExtenso(agora) },
      }, { dir: 'sistema', texto: MARCA_DE_CONVERSAO }, { id: `${mensagemId}-conversao`, agora })
    }
    const comCobranca = {
      ...comConversao,
      pedido: { ...comConversao.pedido, cobranca, estado: paraPago(comConversao.pedido) },
    }
    if (!libera || !recibo) return comCobranca
    // Frente Fidelidade e cupons (rodada 13, issue #45, registro 107; RN-40
    // anti-spam): pontos ganhos entram como LINHA a mais na mensagem
    // automática de pagamento confirmado que já ia sair, nunca uma mensagem
    // própria. `cobranca.valor` é o que o cliente pagou de fato (já líquido
    // de cupom, se teve).
    const pontos = pontosGanhosNoPedido(cobranca.valor, estado.fidelidade.config)
    const textoRecibo = textoDaRegra(recibo, contexto)
    const texto = pontos > 0 ? `${textoRecibo}\n\nVocê ganhou ${pontos} pontos de fidelidade.` : textoRecibo
    return comMensagem(comCobranca, {
      dir: 'out', status: 'lida', automatica: true, regra: recibo.id, texto,
    }, { id: mensagemId, agora })
  })
}

export function reducer(estado, despacho) {
  const caso = CASOS_COMPOSTOS[despacho.tipo]
  return caso ? caso(estado, despacho) : estado
}
