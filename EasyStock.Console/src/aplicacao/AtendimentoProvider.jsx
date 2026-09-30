import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react'
import { ATENDENTE, estadoInicial, reducer } from './reducer'
import * as acao from './acoes'
import { ContextoAcoes, ContextoCatalogo, ContextoEstado } from './contextos'
import { carregarConversas, proximoId, proximoNumeroPedido } from '../infra/repositorioConversas'
import { carregarCatalogo } from '../infra/repositorioCatalogo'
import { carregarHistorico } from '../infra/historicoPedidos'
import { pedirSugestao } from '../infra/conexaoAgente'
import { REGRAS_PADRAO } from '../dominio/automacao'
import { FUNCIONAMENTO_PADRAO, estaAberta } from '../dominio/funcionamento'
import {
  ACOES, acaoSugerida, classificarIntencao, confiancaDe, itensSemSaldoCitados,
  montarPrompt, rascunhoSugerido,
} from '../dominio/agente'
import { montarPromptLivre } from '../dominio/assistente'
import { historicoComPedidoVivo } from '../dominio/cliente'
import {
  conversasBloqueadasDoBalcao, conversasDoBalcao, conversasEncerradasDoBalcao, ordenarBalcao,
} from '../dominio/conversa'
import { conversasParaEncerrarPorJanela } from '../dominio/janela'
import { ocupacaoDeHoje } from '../dominio/entrega'
import { totalDoPedido } from '../dominio/pedido'
import { totalComDesconto } from '../dominio/fidelidade'
import { emitirCobranca } from '../infra/provedoresDeCobranca'
import { podeAlterarMeio } from '../dominio/cobranca'
import { EVENTOS_DE_SOM, motivoDaPassagem } from '../dominio/automatico'
import { precisaEscalarPorArea } from '../dominio/areaEntrega'
import { entregaPassouDoPrazo, ocorrenciaAberta, ORIGENS_OCORRENCIA } from '../dominio/ocorrencia'
import { pedirPermissaoDeNotificacao, permissaoDeNotificacao } from '../infra/notificacaoNavegador'
import { lembretesAutomaticos, listaDeLembretes } from '../dominio/lembrete'
import { useAvisoSonoro } from './useAvisoSonoro'
import { audioDestravado, destravarAudio, tocarAmostra } from '../infra/som'
import { gravarPreferenciaSom, lerPreferenciaSom } from '../infra/preferenciaSom'
import { dataHora } from '../dominio/formato'
import { conectarPrincipal } from '../infra/canalEntreJanelas'
import { criarAcoesCardapio } from './acoes/cardapio'
import { FONTE_API } from '../infra/fonteDados'
import { comApi } from './acoesApi'
import { useSincronizacaoApi } from './useSincronizacaoApi'
import { criarAcoesCliente } from './acoes/cliente'
import { criarAcoesComanda } from './acoes/comanda'
import { criarAcoesCobranca } from './acoes/cobranca'
import { criarAcoesEncerramento } from './acoes/encerramento'
import { criarAcoesEntregas } from './acoes/entregas'
import { criarAcoesSimulacao } from './acoes/simulacao'
import { criarAcoesAreaEntrega } from './acoes/areaEntrega'
import { criarAcoesFuncionamento } from './acoes/funcionamento'
import { criarAcoesAnexos } from './acoes/anexos'
import { criarAcoesRespostas } from './acoes/respostas'
import { criarAcoesLote } from './acoes/lote'
import { criarAcoesJanelas } from './acoes/janelas'
import { criarAcoesProducao } from './acoes/producao'
import { criarAcoesCaixa } from './acoes/caixa'
import { criarAcoesIntegracoes } from './acoes/integracoes'
import { criarAcoesFidelidade } from './acoes/fidelidade'

const mesPorExtenso = (ms) =>
  new Date(ms).toLocaleDateString('pt-BR', { month: 'long', year: 'numeric' })

const carregar = () => ({
  conversas: carregarConversas(),
  catalogo: carregarCatalogo(),
  regras: REGRAS_PADRAO,
  funcionamento: FUNCIONAMENTO_PADRAO,
  // Integração 48, decisão do gerente: a massa nasce com a loja ABERTA pela
  // Thatiane, no modo manual, coerente com os pedidos em preparo do dia. Sem
  // isso, relógio do Simular fora das 8h-22h fechava a loja sozinho e as
  // conversas saíam de "Precisa de você". Fechar é um toque no topo.
  // No modo API (F02) quem manda é o expediente da API; até ele chegar, segue o horário.
  lojaAberta: FONTE_API ? null : true,
  modoAgente: 'simulado',
  // Chave "Som da cozinha" (US-010): lembra a escolha entre uma visita e
  // outra, guardada em infra/preferenciaSom.js.
  som: lerPreferenciaSom(),
})

export function AtendimentoProvider({ agora, sessao = null, children }) {
  const [estado, despachar] = useReducer(reducer, null, () => ({
    ...estadoInicial(carregar()),
    // Modo API (F01): a lista nasce vazia e chega pelo polling.
    sincronizacao: { estado: FONTE_API ? 'carregando' : 'desligada', mensagem: null, aviso: null },
    // Modo API (F02): mensagens do expediente (S40); horário e controle vivem em
    // `funcionamento` e `lojaAberta`, os mesmos do modo demonstração.
    expediente: { carregado: false, mensagemForaDoHorario: '', mensagemLojaFechada: '' },
  }))
  useSincronizacaoApi({ ativo: FONTE_API, usuario: sessao?.usuario ?? null, despachar })

  // Relógio da tela mais o deslocamento do painel de simulações (seção 7,
  // "+10 min"/"+30 min"/"Agora"): as ações carimbam o mesmo instante que a
  // tela exibe, e os dois andam juntos. Deslocamento zero (padrão) é o
  // relógio de sempre, sem diferença nenhuma para quem não abriu o painel.
  const agoraEfetivo = agora + (estado.relogio?.deslocamentoMs ?? 0)
  const agoraRef = useRef(agoraEfetivo)
  useEffect(() => { agoraRef.current = agoraEfetivo }, [agoraEfetivo])

  // Loja aberta agora (Frente Horário e loja, pedido do dono 24/09/2026):
  // combina o horário configurado com o estado manual do topo, uma vez só,
  // porque quem lê "Precisa de você", o Balcão e o aviso sonoro precisam da
  // MESMA resposta.
  const aberta = estaAberta(agoraEfetivo, { funcionamento: estado.funcionamento, lojaAberta: estado.lojaAberta })

  const pendentes = useRef([])
  useEffect(() => () => pendentes.current.forEach(clearTimeout), [])

  // O estado mais recente fica num ref para os criadores de ação assíncronos
  // não precisarem de dependência e não recriarem o objeto de ações.
  const estadoRef = useRef(estado)
  useEffect(() => { estadoRef.current = estado }, [estado])

  // Sincronia com a janela de Entregas (seção 6): esta janela é sempre a
  // "principal" do canal. Sem nenhuma janela de Entregas aberta, o canal só
  // fica ouvindo, sem custo nenhum. `TelaEntregas.jsx` (F6) é quem conecta o
  // lado "espelho" e manda `pedir-estado`/`acao`.
  const canalPrincipalRef = useRef(null)
  useEffect(() => {
    canalPrincipalRef.current = conectarPrincipal({
      estadoAtual: () => estadoRef.current,
      aplicarAcao: (acaoRecebida) => despachar(acaoRecebida),
    })
    return () => canalPrincipalRef.current?.fechar()
  }, [])
  useEffect(() => { canalPrincipalRef.current?.mandarEstado() }, [estado])

  const consultarAgente = useCallback(async (id) => {
    const atual = estadoRef.current
    const conversa = atual.conversas.find((c) => c.id === id)
    if (!conversa) return
    despachar({ tipo: acao.AGENTE_PEDINDO, id })
    const leitura = classificarIntencao(conversa)
    // A ocupação das janelas é CONTADA dos pedidos aqui, uma vez, e entregue ao
    // agente. Sem isto ele lia o `ocupadas` fixo do catálogo e oferecia janela
    // que o painel de entregas mostrava cheia. Rodada 13 (issue #42, UC-03 E1):
    // `ocupacaoDeHoje` também esconde janela pausada/fora do dia e, com o
    // cardápio em mãos, aplica a proposta Q2 de capacidade por linha.
    const ocupacoes = ocupacaoDeHoje(
      atual.catalogo.janelas, atual.conversas, agoraRef.current, conversa.pedido?.janela ?? null,
      atual.catalogo.cardapio,
    )
    try {
      const sugestao = await pedirSugestao({
        modo: atual.modoAgente,
        prompt: montarPrompt(conversa, atual.catalogo, ocupacoes),
        rascunho: rascunhoSugerido(leitura, conversa, atual.catalogo, ocupacoes, agoraRef.current),
        acao: acaoSugerida(leitura, conversa, atual.catalogo),
        intencao: leitura.intencao,
        confianca: confiancaDe(leitura),
      })
      despachar({ tipo: acao.AGENTE_RESPONDEU, id, sugestao })
      // Devolveu para a dona: a conversa ganha a marca "Precisa de você" com o
      // motivo, e o lembrete de resposta nasce a partir dela.
      if (sugestao.acao === ACOES.PASSAR_PARA_DONA) {
        despachar({
          tipo: acao.PASSAR_PARA_DONA,
          id,
          agora: agoraRef.current,
          motivo: motivoDaPassagem(
            leitura.intencao.chave,
            itensSemSaldoCitados(conversa, atual.catalogo),
            precisaEscalarPorArea(conversa),
          ),
        })
      }
      // US-049/RN-35: abertura automática da ocorrência, não um botão. Só
      // para pedido já entregue (UC-06, pré-condição) e só uma vez por pedido.
      if (leitura.reclamacaoDeProduto && conversa.pedido?.estado === 'entregue'
        && !ocorrenciaAberta(conversa.pedido?.ocorrencia)) {
        despachar({
          tipo: acao.ABRIR_OCORRENCIA,
          id,
          agora: agoraRef.current,
          ocorrenciaId: proximoId('ocorrencia'),
          relatoCliente: leitura.trecho,
        })
      }
    } catch (erro) {
      despachar({ tipo: acao.AGENTE_FALHOU, id, erro: erro.message })
    }
  }, [])

  // Balão do assistente (rodada 7): pergunta livre da Thatiane, sempre pela IA
  // de verdade (`modo: 'cli'`, nunca o simulado do dev), com o contexto da
  // conversa no prompt. Sem `despachar`: a tela guarda a troca sozinha, não
  // faz sentido no estado global (não é rascunho, não é mensagem enviada).
  // O erro sobe cru para quem chamou; `remoto` (infra/conexaoAgente.js) já
  // devolve mensagem clara quando o servidor local não responde.
  const perguntarAssistente = useCallback(async (pergunta, conversa, historicoDoCliente) => {
    const resposta = await pedirSugestao({
      modo: 'cli',
      prompt: montarPromptLivre(pergunta, conversa, historicoDoCliente),
      intencao: null,
      confianca: null,
    })
    return resposta.texto
  }, [])

  // Item 6 (rodada 10): permissão de notificação do navegador. Estado do
  // Provider, não do reducer: `Notification.permission` não é dado do
  // atendimento, e a promessa do pedido resolve fora do fluxo de ações.
  const [permissaoNotificacao, setPermissaoNotificacao] = useState(() => permissaoDeNotificacao())
  const pedirNotificacaoDoNavegador = useCallback(async () => {
    setPermissaoNotificacao(await pedirPermissaoDeNotificacao())
  }, [])

  const acoes = useMemo(() => {
    const enviar = (id, texto, opcoes = {}) => {
      const mensagemId = proximoId('msg')
      despachar({
        tipo: acao.ENVIAR_MENSAGEM, id, texto, mensagemId, agora: agoraRef.current, ...opcoes,
      })
      if (opcoes.automatica) return
      const t = setTimeout(() => {
        despachar({ tipo: acao.CONFIRMAR_ENTREGA, id, mensagemId })
      }, 900)
      pendentes.current.push(t)
    }

    return {
      enviar,
      consultarAgente,
      perguntarAssistente,
      selecionar: (id) => despachar({ tipo: acao.SELECIONAR_CONVERSA, id }),
      // `...extra` (rodada 7, frente Anexos): campos que só alguns formatos
      // carregam (duracaoMs do áudio, nome/descrição da peça, tamanho do
      // arquivo). Ver o comentário do case ENVIAR_MIDIA em reducer.js.
      enviarMidia: (id, { formato, arte, texto, ...extra }) => despachar({
        tipo: acao.ENVIAR_MIDIA, id, formato, arte, texto, ...extra,
        agora: agoraRef.current, mensagemId: proximoId('mid'),
      }),
      desfazerEsteira: (id, mensagemId, posEntregaId) => despachar({
        tipo: acao.DESFAZER_ESTEIRA, id, mensagemId, posEntregaId,
      }),
      corrigirPasso: (id) => despachar({ tipo: acao.CORRIGIR_PASSO, id }),
      ajustarQuantidade: (id, sku, delta) => despachar({
        tipo: acao.AJUSTAR_QUANTIDADE, id, sku, delta, alertaId: proximoId('alerta'),
        agora: agoraRef.current,
      }),
      ajustarObservacao: (id, sku, texto) => despachar({ tipo: acao.AJUSTAR_OBSERVACAO, id, sku, texto }),
      mudarFiltro: (chave, valor) => despachar({ tipo: acao.MUDAR_FILTRO, chave, valor }),
      definirRascunho: (id, texto) => despachar({ tipo: acao.DEFINIR_RASCUNHO, id, texto }),
      reabrir: (id) => despachar({ tipo: acao.REABRIR_CONVERSA, id }),
      trocarJanela: (id, janela) => despachar({ tipo: acao.TROCAR_JANELA, id, janela }),
      removerItem: (id, sku) => despachar({ tipo: acao.REMOVER_ITEM, id, sku }),
      fecharAlerta: (alertaId) => despachar({ tipo: acao.FECHAR_ALERTA, alertaId }),
      alternarRegra: (regraId) => despachar({ tipo: acao.ALTERNAR_REGRA, regraId }),
      trocarModoAgente: (modo) => despachar({ tipo: acao.TROCAR_MODO_AGENTE, modo }),
      limparAgente: () => despachar({ tipo: acao.AGENTE_LIMPOU }),
      avancarEsteira: (id, passo, entregador) => despachar({
        tipo: acao.AVANCAR_ESTEIRA, id, passo, entregador,
        agora: agoraRef.current, mensagemId: proximoId('msg'), posEntregaId: proximoId('msg'),
      }),
      // Gerar o pedido manda o resumo E a cobrança de verdade. A emissão do Pix
      // é efeito e nasce aqui fora, igual à do botão Gerar cobrança: redutor
      // puro não inventa txid nem código copia e cola.
      //
      // Ponta (a) da integração: a cobrança sai no meio escolhido (o do chip,
      // gravado em `pedido.meio`), nunca Pix por baixo. Sem meio, não manda:
      // a tela pergunta antes (BlocoPedido). Maquininha e vale andam a
      // esteira igual ao "Gerar cobrança" do bloco.
      gerarPedido: (id, meio = null) => {
        const pedido = estadoRef.current.conversas.find((c) => c.id === id)?.pedido
        const meioEfetivo = meio ?? pedido?.meio
        if (!pedido || !meioEfetivo) return
        // Cupom aplicado no pedido (frente Fidelidade, issue #45): o valor que
        // vai no código Pix/copia-e-cola já é o líquido (`criarCobranca`
        // desconta sozinho do lado do reducer, ver dominio/cobranca.js).
        const emissao = {
          ...emitirCobranca(meioEfetivo, {
            numeroPedido: pedido.numero,
            valor: totalComDesconto(pedido, estadoRef.current.catalogo.cardapio),
          }),
          meio: meioEfetivo,
        }
        despachar({
          tipo: acao.GERAR_PEDIDO, id, agora: agoraRef.current,
          mensagemId: proximoId('msg'), cobrancaId: proximoId('msg'), emissao,
        })
        if (!emissao.link) {
          despachar({ tipo: acao.DESPACHAR_MESMO_ASSIM, id, agora: agoraRef.current })
        }
      },
      cadastrarEndereco: (id) => despachar({
        tipo: acao.CADASTRAR_ENDERECO, id,
        agora: agoraRef.current, mensagemId: proximoId('sis'),
        desde: mesPorExtenso(agoraRef.current),
      }),
      // numeroPedido vem de uma série própria (infra/repositorioConversas.js),
      // separada do contador de mensagem/nota/alerta: só entra no pedido
      // quando a conversa ainda não tem um (o reducer ignora o valor se já
      // existe `c.pedido`), então continua a série real da massa em vez de
      // nascer com o próximo id solto de qualquer coisa (achado do gerente,
      // 24/09: "Nº 1" no canhoto).
      adicionarItem: (id, item) => despachar({
        tipo: acao.ADICIONAR_ITEM, id, item, numeroPedido: proximoNumeroPedido(), alertaId: proximoId('alerta'),
        agora: agoraRef.current,
      }),
      salvarNota: (id, texto) => despachar({
        tipo: acao.SALVAR_NOTA, id, texto,
        agora: agoraRef.current, notaId: proximoId('nota'),
      }),

      // Rodada 2 · cobrança por Pix; rodada 5 (seção 4) acrescentou o `meio`,
      // que decide o que a emissão devolve (`infra/provedoresDeCobranca.js`):
      // Pix e cartão por link chegam com link e prazo, maquininha e vale sem
      // nenhum dos dois (cobram na entrega). Continua no Provider porque
      // precisa de `agoraRef`/`estadoRef`; a lógica de meio mora na
      // infraestrutura, não aqui.
      //
      // A emissão vem da infraestrutura aqui fora, nunca de dentro do reducer:
      // txid e código são efeito, e efeito em redutor puro não se repete igual.
      gerarCobranca: (id, pedido, meioEscolhido = null) => {
        const meio = meioEscolhido ?? pedido.meio
        if (!meio) return
        // Ver nota de `gerarPedido` acima: valor líquido só no código emitido,
        // o desconto em si já sai sozinho de dentro de `criarCobranca`.
        const emissao = {
          ...emitirCobranca(meio, {
            numeroPedido: pedido.numero,
            valor: totalComDesconto(pedido, estadoRef.current.catalogo.cardapio),
          }),
          meio,
        }
        despachar({
          tipo: acao.GERAR_COBRANCA, id, agora: agoraRef.current, mensagemId: proximoId('msg'), emissao,
        })
        // Maquininha e vale liberam a esteira sem esperar pagamento (seção 4,
        // decisão 19: "a esteira anda sem pagamento"). `emissao.link` é a
        // mesma marca que o domínio usa para saber se há prazo.
        if (!emissao.link) {
          despachar({ tipo: acao.DESPACHAR_MESMO_ASSIM, id, agora: agoraRef.current })
        }
      },
      reenviarCobranca: (id, pedido, valorForcado = null, meio = null) => {
        const meioEfetivo = meio ?? pedido.cobranca?.meio ?? 'pix'
        despachar({
          tipo: acao.REENVIAR_COBRANCA, id, agora: agoraRef.current, mensagemId: proximoId('msg'),
          valorForcado,
          emissao: {
            ...emitirCobranca(meioEfetivo, {
              numeroPedido: pedido.numero,
              valor: valorForcado ?? totalDoPedido(pedido, estadoRef.current.catalogo.cardapio),
            }),
            meio: meioEfetivo,
          },
        })
      },
      // Rodada 12 (issue #13): "Alterar forma de pagamento". Emite a cobrança
      // nova aqui fora (efeito, como gerarCobranca) e despacha uma ação só,
      // que cancela a pendente e manda a nova. Sem link, a esteira anda como
      // em qualquer cobrança de maquininha ou vale. A guarda é a mesma do
      // reducer: sem ela, DESPACHAR_MESMO_ASSIM andaria um pedido pago.
      alterarMeioPagamento: (id, meio) => {
        const pedido = estadoRef.current.conversas.find((c) => c.id === id)?.pedido
        if (!podeAlterarMeio(pedido) || !meio || meio === pedido.cobranca.meio) return
        const emissao = {
          ...emitirCobranca(meio, {
            numeroPedido: pedido.numero,
            valor: totalDoPedido(pedido, estadoRef.current.catalogo.cardapio),
          }),
          meio,
        }
        despachar({
          tipo: acao.ALTERAR_MEIO_PAGAMENTO, id, agora: agoraRef.current, emissao,
          mensagemId: proximoId('msg'), sistemaId: proximoId('sis'),
        })
        if (!emissao.link) {
          despachar({ tipo: acao.DESPACHAR_MESMO_ASSIM, id, agora: agoraRef.current })
        }
      },
      // Rodada 12 (issue #13): baixa marcada por engano. Motivo obrigatório.
      desfazerPagamento: (id, motivo) => despachar({
        tipo: acao.DESFAZER_PAGAMENTO, id, motivo, agora: agoraRef.current,
        mensagemId: proximoId('sis'), notaId: proximoId('nota'),
      }),
      marcarComprovante: (id) => despachar({
        tipo: acao.MARCAR_COMPROVANTE, id,
        agora: agoraRef.current, mensagemId: proximoId('msg'),
      }),
      aceitarDivergencia: (id) => despachar({
        tipo: acao.ACEITAR_DIVERGENCIA, id,
        agora: agoraRef.current, mensagemId: proximoId('msg'),
      }),
      confirmarPagamento: (id, valorPago = null) => {
        despachar({
          tipo: acao.CONFIRMAR_PAGAMENTO, id, valorPago,
          agora: agoraRef.current, mensagemId: proximoId('msg'),
        })
        // PONTO DE EXTENSÃO · evento "pagamento confirmado".
        // É aqui que o webhook do provedor chega no mundo real, e é daqui que
        // sairiam o som de caixa da RN-25 e o aviso no celular dela. O protótipo
        // não toca som de propósito: som que aparece sem ela pedir, em cozinha
        // com as mãos ocupadas, é pior que som nenhum. A decisão é dela.
      },

      // Rodada 2 · cardápio do dia.
      alternarDisponibilidade: (sku) => despachar({ tipo: acao.ALTERNAR_DISPONIBILIDADE, sku }),
      ajustarSaldo: (sku, delta) => despachar({ tipo: acao.AJUSTAR_SALDO, sku, delta }),

      // Rodada 2 · janelas de entrega com capacidade.
      escolherJanela: (id, janela) => despachar({ tipo: acao.ESCOLHER_JANELA, id, janela }),
      forcarEncaixe: (id, janela, faixa, motivo) => despachar({
        tipo: acao.FORCAR_ENCAIXE, id, janela, faixa, motivo,
        agora: agoraRef.current, mensagemId: proximoId('sis'),
      }),
      assumirAtendimento: (id) => despachar({ tipo: acao.ASSUMIR_ATENDIMENTO, id }),
      devolverAutomatico: (id) => despachar({ tipo: acao.DEVOLVER_AUTOMATICO, id }),
      alternarSom: (ligado) => despachar({ tipo: acao.ALTERNAR_SOM, ligado }),
      // Amostra sob demanda (US-010): não muda estado, só toca. Passa pela
      // camada de ações porque features não importa infra direto.
      ouvirAmostraDeSom: (evento) => tocarAmostra(evento),
      editarRegra: (regraId, campos) => despachar({ tipo: acao.EDITAR_REGRA, regraId, campos }),
      criarLembrete: (lembrete) => despachar({
        tipo: acao.CRIAR_LEMBRETE, lembrete: { ...lembrete, id: proximoId('lem') },
      }),
      concluirLembrete: (lembrete) => despachar({
        tipo: acao.CONCLUIR_LEMBRETE, lembreteId: lembrete.id, titulo: lembrete.titulo,
        conversaId: lembrete.conversaId, agora: agoraRef.current, mensagemId: proximoId('sis'),
      }),
      // Bloqueio é por cadastro, nunca por conversa: uma ação só alcança
      // WhatsApp, Instagram e chat do site da mesma pessoa. Quem chama ainda
      // manda `nome` (rodada 4, `BlocoCliente.jsx`); o passo zero traduz para
      // `cadastroId` aqui dentro, então nenhuma tela precisa saber da troca
      // (seção 2, "cadastroId no lugar do nome em mesmoCadastro").
      bloquearCliente: (nome, motivo) => {
        const cadastroId = estadoRef.current.conversas.find((c) => c.nome === nome)?.cadastroId ?? nome
        despachar({
          tipo: acao.BLOQUEAR_CLIENTE, cadastroId, motivo, por: ATENDENTE,
          em: dataHora(agoraRef.current), agora: agoraRef.current, mensagemId: proximoId('sis'),
        })
      },
      desbloquearCliente: (nome) => {
        const cadastroId = estadoRef.current.conversas.find((c) => c.nome === nome)?.cadastroId ?? nome
        despachar({
          tipo: acao.DESBLOQUEAR_CLIENTE, cadastroId, por: ATENDENTE,
          agora: agoraRef.current, mensagemId: proximoId('sis'),
        })
      },

      // Rodada 5 · passo zero da direção visual (seção 8). `ui.encerrando`
      // abre a modal de encerramento (seção 5) de qualquer lugar: cabeçalho
      // da conversa, menu do pedido e barra do Pedido.
      abrirEncerramento: (id, numero = null) => despachar({ tipo: acao.ABRIR_ENCERRAMENTO, id, numero }),
      fecharEncerramento: () => despachar({ tipo: acao.FECHAR_ENCERRAMENTO }),

      // Seis frentes (F2 a F7), seis arquivos: cada uma só mexe no próprio
      // `aplicacao/acoes/<tema>.js` daqui para frente.
      ...criarAcoesCardapio(despachar),
      ...criarAcoesCliente(despachar),
      ...criarAcoesComanda(despachar),
      ...criarAcoesCobranca(despachar),
      ...criarAcoesEncerramento(despachar),
      ...criarAcoesEntregas(despachar),
      ...criarAcoesSimulacao(despachar),
      ...criarAcoesAreaEntrega(despachar),
      ...criarAcoesFuncionamento(despachar),
      ...criarAcoesAnexos(despachar),
      ...criarAcoesRespostas(despachar),
      ...criarAcoesLote(despachar),
      ...criarAcoesJanelas(despachar),
      ...criarAcoesProducao(despachar),
      ...criarAcoesCaixa(despachar),
      ...criarAcoesIntegracoes(despachar),
      ...criarAcoesFidelidade(despachar),

      // Rodada 3 · passo zero da direção visual (seção 10). O texto que o
      // cliente lê em CANCELAR_PEDIDO é da Frente A: ela monta e passa pronto.
      cancelarPedido: (id, texto) => despachar({
        tipo: acao.CANCELAR_PEDIDO, id, texto,
        agora: agoraRef.current, mensagemId: proximoId('msg'),
      }),
      // UC-06 passo 5: motivo obrigatório, valor pode ser parcial (a Frente A
      // pré-preenche com o que foi pago e deixa editar).
      marcarEstorno: (id, motivo, valor) => despachar({
        tipo: acao.MARCAR_ESTORNO, id, motivo, valor, agora: agoraRef.current, notaId: proximoId('nota'),
      }),
      apurarOcorrencia: (id) => despachar({ tipo: acao.APURAR_OCORRENCIA, id, agora: agoraRef.current }),
      // Alternativo A (UC-06): não era defeito, vira nota interna com a preferência.
      encerrarOcorrenciaSemEstorno: (id, preferencia) => despachar({
        tipo: acao.ENCERRAR_OCORRENCIA_SEM_ESTORNO, id, preferencia,
        agora: agoraRef.current, notaId: proximoId('nota'),
      }),
      encerrarAtendimento: (id) => despachar({ tipo: acao.ENCERRAR_ATENDIMENTO, id }),
      marcarLembretesVistos: (chaves) => despachar({ tipo: acao.MARCAR_LEMBRETES_VISTOS, chaves }),
      pedirNotificacaoDoNavegador,
    }
  }, [consultarAgente, perguntarAssistente, pedirNotificacaoDoNavegador])

  // Item 7 (Sininho revê o que soou enquanto ela estava fora) e item 2 (selo
  // "novo, não visto" do pagamento, independente do prazo de 10 min do sinal
  // verde). Estado local do Provider, não do reducer: nenhuma das duas
  // precisa viajar pelo canal entre janelas (rodada 10, fronteira da 82).
  const [eventosSonoros, setEventosSonoros] = useState([])
  const [pagamentosNaoVistos, setPagamentosNaoVistos] = useState(() => new Set())

  // Som da cozinha (D6, US-010). O timbre de cada evento mora em
  // infra/som.js; aqui só o QUANDO, por diferença de estado. Rodada 10:
  // `janelas` liga o motivo "pedido atrasado" (achado 3); `aoTocar` alimenta
  // o registro de sons recentes do Sininho (achado 7, item 7) e a marca de
  // "pagamento não visto" independente do sinal verde (item 2).
  useAvisoSonoro({
    conversas: estado.conversas, som: estado.som, agora: agoraEfetivo, aberta,
    janelas: estado.catalogo.janelas,
    aoTocar: (evento, conversaId) => {
      setEventosSonoros((atual) => [
        ...atual, { id: proximoId('som'), evento, conversaId, quando: agoraRef.current },
      ].slice(-30))
      if (evento === EVENTOS_DE_SOM.PAGAMENTO_CONFIRMADO) {
        setPagamentosNaoVistos((atual) => new Set(atual).add(conversaId))
      }
    },
  })

  // "Visto" nasce de um jeito só: ela abriu a conversa. Passar o tempo ou
  // fechar a tela nunca conta como ter visto (é exatamente o que o achado 4
  // reclamava do sinal verde).
  useEffect(() => {
    if (!estado.selecionadaId) return
    setPagamentosNaoVistos((atual) => {
      if (!atual.has(estado.selecionadaId)) return atual
      const novo = new Set(atual)
      novo.delete(estado.selecionadaId)
      return novo
    })
  }, [estado.selecionadaId])

  // Lembra a escolha da chave entre uma visita e outra.
  useEffect(() => { gravarPreferenciaSom(estado.som) }, [estado.som])

  // O navegador bloqueia áudio até o primeiro gesto na página. Um clique ou
  // toque em qualquer lugar (o Provider envolve a tela inteira) tenta
  // destravar; enquanto não destravar, `audioBloqueado` liga o aviso de uma
  // linha no ModalAutomacoes, que some assim que o gesto vale.
  const [audioBloqueado, setAudioBloqueado] = useState(() => !audioDestravado())
  useEffect(() => {
    if (!audioBloqueado) return
    const aoGesto = () => {
      destravarAudio()
      if (audioDestravado()) setAudioBloqueado(false)
    }
    window.addEventListener('pointerdown', aoGesto)
    return () => window.removeEventListener('pointerdown', aoGesto)
  }, [audioBloqueado])

  // Lembrete que nasce sozinho: conversa devolvida sem resposta e pedido
  // gerado sem pagamento. O reducer devolve o mesmo estado quando nada muda,
  // então isto não repinta a tela a cada volta do relógio.
  const conversasAgora = estado.conversas
  useEffect(() => {
    despachar({
      tipo: acao.SINCRONIZAR_LEMBRETES,
      automaticos: lembretesAutomaticos(conversasAgora, agoraRef.current),
    })
  }, [conversasAgora, agoraEfetivo])

  // Encerramento automático por janela (decisão 46, "Encerrar" v2): mesmo
  // gatilho de tempo do efeito acima, para a mesma regra de 24h que já
  // bloqueia o composer. Só despacha quando existe alguma conversa para
  // fechar, para não repintar a cada volta do relógio sem motivo.
  useEffect(() => {
    const ids = conversasParaEncerrarPorJanela(conversasAgora, agoraRef.current, estado.catalogo.canais)
    if (ids.length > 0) despachar({ tipo: acao.ENCERRAR_POR_JANELA, ids, agora: agoraRef.current })
  }, [conversasAgora, agoraEfetivo, estado.catalogo.canais])

  // Ocorrência de entrega de verdade (rodada 10, achado 7 / item 4): parada
  // já em rota que passou do fim da janela prometida abre sozinha, do mesmo
  // jeito que a reclamação abre sozinha hoje (US-049). Mesmo guarda do
  // reducer (`ABRIR_OCORRENCIA` não duplica quando já existe uma ocorrência
  // no pedido) evita reabrir a cada volta do relógio.
  useEffect(() => {
    const paradasAtrasadas = conversasAgora.filter((c) =>
      entregaPassouDoPrazo(c, estado.catalogo.janelas, agoraRef.current) && !ocorrenciaAberta(c.pedido?.ocorrencia))
    paradasAtrasadas.forEach((c) => {
      despachar({
        tipo: acao.ABRIR_OCORRENCIA,
        id: c.id,
        agora: agoraRef.current,
        ocorrenciaId: proximoId('ocorrencia'),
        relatoCliente: `Entrega do pedido ${c.pedido.numero} passou do horário prometido`,
        origem: ORIGENS_OCORRENCIA.ATRASO_ENTREGA,
      })
    })
  }, [conversasAgora, agoraEfetivo, estado.catalogo.janelas])

  const acoesAtivas = useMemo(
    () => (FONTE_API ? comApi(acoes, { despachar, agoraRef, estadoRef }) : acoes),
    [acoes],
  )

  // Modo API (F02): o expediente vem da API uma vez ao entrar; depois, a cada gravação.
  useEffect(() => {
    if (FONTE_API) acoesAtivas.recarregarExpediente()
  }, [acoesAtivas])

  const valor = useMemo(() => {
    const selecionada = estado.conversas.find((c) => c.id === estado.selecionadaId) ?? null
    return {
      conversas: estado.conversas,
      selecionadaId: estado.selecionadaId,
      filtros: estado.filtros,
      regras: estado.regras,
      funcionamento: estado.funcionamento,
      lojaAberta: estado.lojaAberta,
      aberta,
      modoAgente: estado.modoAgente,
      agente: estado.agente,
      automaticoPausado: estado.automaticoPausado,
      alertasEstoque: estado.alertasEstoque,
      rascunho: estado.rascunhos[estado.selecionadaId] ?? '',
      // Ordenado aqui, uma vez só: quem lê `visiveis` (Balcão e o "abrir
      // primeira conversa" da ficha e do atendimento) precisa enxergar a
      // mesma ordem, senão o botão de estado vazio abre uma conversa
      // diferente da que está no topo da lista (achado da auditoria QA5).
      visiveis: ordenarBalcao(
        conversasDoBalcao(
          estado.conversas, estado.filtros, agoraEfetivo, estado.automaticoPausado, aberta, estado.catalogo.janelas,
        ),
        agoraEfetivo,
        {
          ordenacao: estado.filtros.ordenacao,
          automaticoPausado: estado.automaticoPausado,
          janelas: estado.catalogo.janelas,
          aberta,
        },
      ),
      // Os dois grupos recolhidos do fim de "Todas" (seção 1): mesmo canal e
      // busca da lista de cima, sem aba, porque nenhum dos dois existe em
      // "Precisa de você".
      encerradasDoBalcao: conversasEncerradasDoBalcao(estado.conversas, estado.filtros),
      bloqueadasDoBalcao: conversasBloqueadasDoBalcao(estado.conversas, estado.filtros),
      historico: selecionada ? historicoComPedidoVivo(carregarHistorico(selecionada.id), selecionada.pedido) : [],
      ultimoAvanco: estado.ultimoAvanco,
      // Lote de papel (US-042): banner de queda e a modal de lançamento leem
      // só este campo, tirado de `estado.conexao` sem transformação nenhuma.
      conexao: estado.conexao,
      selecionada,
      // Rodada 5 (seção 7, passo zero): a tela inteira lê o relógio JÁ com o
      // deslocamento do painel de simulações somado, então nenhuma feature
      // precisa somar `relogio.deslocamentoMs` por conta própria.
      agora: agoraEfetivo,
      som: estado.som,
      // Aviso "Toque para ligar o som" do ModalAutomacoes (US-010).
      audioBloqueado,
      lembretes: listaDeLembretes({ lembretes: estado.lembretes }),
      vistos: estado.vistos,
      // Rodada 10: sons das últimas horas para o Sininho rever (item 7) e
      // quem pagou e ainda não foi visto, independente do sinal verde
      // expirar (item 2).
      eventosSonoros,
      pagamentosNaoVistos,
      permissaoNotificacao,
      // `ui.encerrando` (seção 5): id da conversa com a modal de
      // encerramento aberta, ou `null`.
      encerrando: estado.ui.encerrando,
      encerrandoNumero: estado.ui.encerrandoNumero ?? null,
      // Rodada 13 (issue #43): lotes, descobertos e ajustes lidos pela aba
      // Produção e cardápio de Gestão.
      producao: estado.producao,
      // Caixa (rodada 13, issue #44): a aba de Gestão lê só isto, direto.
      caixa: estado.caixa,
      // Frente Fidelidade e cupons (rodada 13, issue #45, registro 107):
      // cupons, regra de pontos, catálogo de recompensas, sorteios e resgate
      // por cadastro. Pontos GANHOS não vêm daqui: são derivados do
      // `historico` (acima), ver dominio/fidelidade.js.
      fidelidade: estado.fidelidade,
      // Modo API (F01): estado do polling e quem está logado.
      fonteApi: FONTE_API,
      sincronizacao: estado.sincronizacao,
      expediente: estado.expediente,
      sessao,
    }
  }, [estado, sessao, agoraEfetivo, audioBloqueado, aberta, eventosSonoros, pagamentosNaoVistos, permissaoNotificacao])

  return (
    <ContextoCatalogo.Provider value={estado.catalogo}>
      <ContextoAcoes.Provider value={acoesAtivas}>
        <ContextoEstado.Provider value={valor}>{children}</ContextoEstado.Provider>
      </ContextoAcoes.Provider>
    </ContextoCatalogo.Provider>
  )
}
