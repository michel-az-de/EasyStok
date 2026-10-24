import { linkDoCardapio } from '../../dominio/cardapioLink'
import { totalComDesconto } from '../../dominio/fidelidade'
import { dataHora } from '../../dominio/formato'
import { totalDoPedido } from '../../dominio/pedido'
import { proximoId } from '../../infra/numeracao'
import { emitirCobranca } from '../../infra/provedoresDeCobranca'
import * as acao from '../acoes'
import { ATENDENTE } from '../reducer/estadoInicial'
import { criarAcoesAnexos } from './anexos'
import { criarAcoesAreaEntrega } from './areaEntrega'
import { criarAcoesCaixa } from './caixa'
import { criarAcoesCardapio } from './cardapio'
import { criarAcoesCliente } from './cliente'
import { criarAcoesCobranca } from './cobranca'
import { criarAcoesEncerramento } from './encerramento'
import { criarAcoesEntregas } from './entregas'
import { criarAcoesFidelidade } from './fidelidade'
import { criarAcoesFuncionamento } from './funcionamento'
import { criarAcoesIntegracoes } from './integracoes'
import { criarAcoesJanelas } from './janelas'
import { criarAcoesLote } from './lote'
import { criarAcoesProducao } from './producao'
import { criarAcoesRespostas } from './respostas'
import { criarAcoesSimulacao } from './simulacao'

// SÓ DA DEMONSTRAÇÃO (carregado por `src/demonstracao/`, fora do build de produção): as ações que escrevem no
// estado do navegador. No modo API cada uma delas é trocada por uma que fala com o EasyStok
// (`aplicacao/acoesApi.js`) ou por um aviso de "indisponível" (`api/naoLigadas.js`).
// A prova `ferramentas/prova-f06-honestidade.mjs` monta o objeto inteiro (estas mais as locais) e confere que
// cada chave tem dono no modo API.
const PROGRAMADA_SO_NA_API = 'Mensagem programada só sai pelo EasyStok: na demonstração não há disparo.'

const mesPorExtenso = (ms) =>
  new Date(ms).toLocaleDateString('pt-BR', { month: 'long', year: 'numeric' })

export function criarAcoesDaDemonstracao({
  despachar, agoraRef, estadoRef, pendentes, consultarAgente, perguntarAssistente,
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
    // "Enviar cardápio" (#1353): na demonstração o link é a tela do próprio console
    // (useEspelhoCardapioLink.js); no modo API vem da loja (acoesApi.js).
    obterLinkCardapio: async (id) => ({
      url: linkDoCardapio(window.location.origin + window.location.pathname, id), daLoja: false,
    }),
    // Mensagem programada (#1424): quem agenda e dispara é o EasyStok (acoesApi.js). Na
    // demonstração não há disparo, então não finge: a modal mostra este motivo.
    programarMensagem: async () => { throw new Error(PROGRAMADA_SO_NA_API) },
    listarProgramadas: async () => { throw new Error(PROGRAMADA_SO_NA_API) },
    cancelarProgramada: async () => { throw new Error(PROGRAMADA_SO_NA_API) },
    listarModelos: async () => { throw new Error('Modelos aprovados exigem a conexão real com o EasyStok.') },
    enviarModelo: async () => { throw new Error('Modelos aprovados exigem a conexão real com o EasyStok.') },
    prepararOutroModelo: () => {},
    listarAtendentes: async () => [{ nome: ATENDENTE }],
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
    reabrir: (id) => despachar({ tipo: acao.REABRIR_CONVERSA, id }),
    trocarJanela: (id, janela) => despachar({ tipo: acao.TROCAR_JANELA, id, janela }),
    alternarRegra: (regraId) => despachar({ tipo: acao.ALTERNAR_REGRA, regraId }),
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
    assumirAtendimento: (id) => despachar({ tipo: acao.ASSUMIR_ATENDIMENTO, id }),
    devolverAutomatico: (id) => despachar({ tipo: acao.DEVOLVER_AUTOMATICO, id }),
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
    // Seis frentes (F2 a F7), seis arquivos: cada uma só mexe no próprio
    // `aplicacao/acoes/<tema>.js` daqui para frente.
    ...criarAcoesCardapio(despachar),
    ...criarAcoesCliente(despachar),
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
  }
}
