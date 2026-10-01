import { ATENDENTE } from './reducer'
import * as acao from './acoes'
import { proximoId, proximoNumeroPedido } from '../infra/repositorioConversas'
import { totalDoPedido } from '../dominio/pedido'
import { totalComDesconto } from '../dominio/fidelidade'
import { emitirCobranca } from '../infra/provedoresDeCobranca'
import { podeAlterarMeio } from '../dominio/cobranca'
import { tocarAmostra } from '../infra/som'
import { dataHora } from '../dominio/formato'
import { criarAcoesCardapio } from './acoes/cardapio'
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

// Todas as ações do modo demonstração, fora do React (F06, #1236): o Provider
// chama isto dentro de um `useMemo`, e a prova `ferramentas/prova-f06-honestidade.mjs`
// monta o mesmo objeto no node para conferir que cada chave tem dono no modo API.
// `pendentes` guarda os timers da entrega simulada, para o Provider limpar ao desmontar.
export function criarAcoes({
  despachar, agoraRef, estadoRef, pendentes, consultarAgente, perguntarAssistente, pedirNotificacaoDoNavegador,
}) {
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
}
