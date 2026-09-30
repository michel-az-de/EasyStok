// Frente 7 · Menu de simulações (rodada 5, seção 7). Domínio puro: nenhum
// import fora de `dominio`, o dado sempre chega por parâmetro (regra da
// fronteira de camada, `ferramentas/verificar-camadas.mjs`).
//
// O CORAÇÃO DO PROBLEMA (achado da auditoria, decisão 22, item 1): o
// protótipo não tinha o evento "mensagem de cliente chega". Sem ele nenhum
// automático dispara, e por isso boas-vindas (US-001), recorrente (US-002),
// resposta do automático (US-003) e conciliação do Pix (US-032) nunca
// apareciam funcionando. Este arquivo decide O QUE fazer quando a mensagem
// chega; quem aplica a decisão no estado é `aplicacao/casos/simulacao.js`.

import { GATILHOS, regraDoGatilho, textoDaRegra } from './automacao'
import { motivoDaPassagem } from './automatico'
import {
  ACOES, INTENCOES, acaoSugerida, classificarIntencao, itensSemSaldoCitados,
} from './agente'
import { faixaDepoisDeEntre, faixaParaCliente, janelaPorId } from './entrega'
import {
  foraDaArea, precisaEscalarPorArea, respostaDeAreaFora, situacaoDoCep,
} from './areaEntrega'
import { FUNCIONAMENTO_PADRAO, descreverProximaAbertura, estaAberta } from './funcionamento'
import { linkDoCardapio } from './cardapioLink'

export const primeiroNome = (nome) => (nome ?? '').split(' ')[0] || 'você'

// --- Área de entrega (RN-10, RN-11, US-012, US-013) ------------------------
// A checagem é a mesma da frente Área de entrega (`dominio/areaEntrega.js`):
// CEP fora ou no limite recebe a resposta que pergunta se o lead quer seguir,
// e a insistência depois do aviso passa para a dona com o motivo de área.
// Integração da rodada 5: antes o Simular tinha uma cópia própria, só com
// prefixo exato e sem a escalada.

// --- Saudação a quem já tem cadastro (US-002, RN-02, RN-07) -----------------
// "Oferece favorito e novidade, sem afirmar hábito": nunca "você sempre
// pede", sempre uma pergunta em aberto.
const PADRAO_TAG_FAVORITO = /^gosta de (.+)$/i

export function favoritoDasTags(tags = []) {
  for (const tag of tags) {
    const achado = tag.match(PADRAO_TAG_FAVORITO)
    if (achado) return achado[1]
  }
  return null
}

export function textoSaudacaoRecorrente({ nome, favorito, novidade }) {
  // RN-07/US-005: oferece a escolha entre favorito e novidade, nunca afirma
  // hábito. "Separei de novo" dizia que a decisão já tinha sido tomada por
  // ela; a pergunta do fechamento é que precisa carregar a escolha.
  const oferta = favorito
    ? `Hoje tem ${favorito} e uma novidade: ${novidade}.`
    : `Hoje tem ${novidade} de novidade, além do resto do cardápio de sempre.`
  return `Oi ${primeiroNome(nome)}, que bom te ver de novo por aqui! ${oferta} `
    + 'Quer repetir ou prefere provar diferente hoje?'
}

// --- Relógio do painel de simulações (seção 7) ------------------------------
// Desloca até a próxima ocorrência da hora pedida (hoje ou amanhã), nunca
// para trás: "mensagem às 2h" precisa de um `agora` que VAI dar 2h da manhã,
// não um deslocamento negativo que confunde o resto da tela.
export function deslocamentoParaHora(agora, horaAlvo) {
  const alvo = new Date(agora)
  alvo.setHours(horaAlvo, 0, 0, 0)
  if (alvo.getTime() <= agora) alvo.setDate(alvo.getDate() + 1)
  return alvo.getTime() - agora
}

// --- O evento em si: MENSAGEM_DO_CLIENTE_CHEGOU -----------------------------
// `conversaComMensagem` já inclui a mensagem nova (dir "in") no fim de
// `mensagens`; quem chama fez o append antes de perguntar o que fazer com
// ela, porque classificarIntencao/itensSemSaldoCitados leem a conversa
// inteira. `primeiraMensagemDoCliente` é du cálculo do CHAMADOR (só ele sabe
// se há outras conversas do mesmo cadastro, dado que não cabe aqui dentro).
//
// Devolve:
//   respostaAutomatica: { texto, regraId } | null  → mensagem "out" a somar
//   passarParaDona: string | null                  → motivo de PASSAR_PARA_DONA
//   concilia: boolean                              → aplica pagamento sozinho
//
// Prioridade (RN-52 e RN-53 nunca perdem para nenhuma das de baixo):
//   1. bloqueada, ou já passou para ela, ou automático pausado → nada de novo
//   2. restrição alimentar, item sem saldo ou pista de "precisa da dona"
//   3. comprovante de Pix com cobrança pendente → concilia (e some com #4;
//      pagar um pedido já gerado não é "vender", conciliação vale a
//      qualquer hora)
//   4. loja fechada no controle do topo, ou fora do horário configurado
//      (dominio/funcionamento.js) → mensagem de Loja fechada/Fora do
//      horário, marca `aguardandoAbertura`, não vende nem passa para a área
//   5. fora da área atendida
//   6. primeira mensagem desta pessoa com a casa → boas-vindas
//   7. primeira mensagem desta conversa, mas o cadastro já existe → saudação
//   8. resto: sem resposta nova (fica para o agente, quando ela abrir)
export function respostaParaMensagemDeCliente({
  conversaComMensagem,
  primeiraMensagemDoCliente,
  cadastroConhecido,
  bloqueada = false,
  automaticoPausado = false,
  comprovante = false,
  catalogo,
  regras,
  agora,
  prefixosCepAtendidos = [],
  funcionamento = FUNCIONAMENTO_PADRAO,
  lojaAberta = null,
  // US-001 (regra de negócio)/UC-01 passo 2: origem (window.location.origin +
  // pathname) para montar o link real do cardápio, a mesma conta do botão
  // "Enviar cardápio" do composer. Vem por parâmetro porque este arquivo é
  // domínio puro e não lê `window` (ferramentas/verificar-camadas.mjs).
  origemCardapio = '',
}) {
  const nada = {
    respostaAutomatica: null, passarParaDona: null, concilia: false, aguardandoAbertura: false,
  }
  if (bloqueada) return nada
  if (conversaComMensagem.passagem && !conversaComMensagem.passagem.assumida) {
    return { ...nada, concilia: temCobrancaPendente(conversaComMensagem) && comprovante }
  }
  if (automaticoPausado) {
    return { ...nada, concilia: temCobrancaPendente(conversaComMensagem) && comprovante }
  }

  const leitura = classificarIntencao(conversaComMensagem)
  const semSaldo = itensSemSaldoCitados(conversaComMensagem, catalogo)
  const precisaDaDona = acaoSugerida(leitura, conversaComMensagem, catalogo) === ACOES.PASSAR_PARA_DONA
  if (precisaDaDona) {
    const areaInsistida = precisaEscalarPorArea(conversaComMensagem, prefixosCepAtendidos)
    return {
      respostaAutomatica: null,
      passarParaDona: motivoDaPassagem(leitura.intencao.chave, semSaldo, areaInsistida),
      concilia: temCobrancaPendente(conversaComMensagem) && comprovante,
    }
  }

  if (comprovante && temCobrancaPendente(conversaComMensagem)) {
    const recibo = regraDoGatilho(regras, GATILHOS.PAGAMENTO_CONFIRMADO)
    const janelaBruta = janelaPorId(catalogo.janelas, conversaComMensagem.pedido?.janela)?.faixa
    // RN-06 e RN-22 (US-028): respiro antes de virar texto, ponto único em
    // dominio/entrega.js.
    const faixa = janelaBruta ? faixaParaCliente(janelaBruta, agora) : 'o horário combinado'
    return {
      respostaAutomatica: recibo
        ? { texto: textoDaRegra(recibo, { nome: primeiroNome(conversaComMensagem.nome), faixa }), regraId: recibo.id }
        : null,
      passarParaDona: null,
      concilia: true,
    }
  }

  // Loja fechada ou fora do horário (pedido do dono, 24/09/2026): vale para
  // QUALQUER mensagem, não só a primeira, porque "o automático não atende
  // nem vende" enquanto a loja está fechada. `lojaAberta === false` é a
  // dona fechando na mão (vence o horário); os outros dois casos (`null`
  // seguindo o horário, ou `true` forçando aberta) caem no horário
  // configurado.
  if (!estaAberta(agora, { funcionamento, lojaAberta })) {
    const gatilho = lojaAberta === false ? GATILHOS.LOJA_FECHADA : GATILHOS.FORA_DO_HORARIO
    const regra = regraDoGatilho(regras, gatilho)
    const contexto = { nome: primeiroNome(conversaComMensagem.nome) }
    if (gatilho === GATILHOS.FORA_DO_HORARIO) contexto.abre = descreverProximaAbertura(agora, funcionamento)
    return {
      respostaAutomatica: regra ? { texto: textoDaRegra(regra, contexto), regraId: regra.id } : null,
      passarParaDona: null,
      concilia: false,
      aguardandoAbertura: true,
    }
  }

  const ultima = [...conversaComMensagem.mensagens].reverse().find((m) => m.dir === 'in')
  if (foraDaArea(situacaoDoCep(ultima?.texto, prefixosCepAtendidos))) {
    return {
      respostaAutomatica: { texto: respostaDeAreaFora(primeiroNome(conversaComMensagem.nome)), regraId: 'fora-de-area' },
      passarParaDona: null,
      concilia: false,
      aguardandoAbertura: false,
    }
  }

  if (primeiraMensagemDoCliente && !cadastroConhecido) {
    const regra = regraDoGatilho(regras, GATILHOS.PRIMEIRO_CONTATO)
    const linkCardapio = linkDoCardapio(origemCardapio, conversaComMensagem.id)
    return {
      respostaAutomatica: regra
        ? { texto: textoDaRegra(regra, { nome: primeiroNome(conversaComMensagem.nome), linkCardapio }), regraId: regra.id }
        : null,
      passarParaDona: null,
      concilia: false,
    }
  }

  // US-002/RN-02/D3: quem já é cliente é saudado pelo nome, mas a mensagem
  // dele PRECISA ser interpretada, não sempre respondida com a mesma oferta
  // de favorito/novidade (achado P0.2, banca10/simulacao). Reclamação vai
  // para a dona com o motivo certo (D2: cuidado nunca é automático); pergunta
  // sobre o pedido em andamento recebe o status, na mesma conta de
  // `dominio/agente.js` (rascunhoSugerido, caso ACOMPANHAR).
  if (primeiraMensagemDoCliente && cadastroConhecido) {
    if (leitura.intencao.chave === INTENCOES.ELOGIO_OU_QUEIXA.chave) {
      return {
        respostaAutomatica: null,
        passarParaDona: motivoDaPassagem(leitura.intencao.chave),
        concilia: false,
      }
    }

    if (leitura.intencao.chave === INTENCOES.ACOMPANHAR.chave && conversaComMensagem.pedido) {
      const nome = primeiroNome(conversaComMensagem.nome)
      const janelaDoPedido = conversaComMensagem.pedido.janela
        ? janelaPorId(catalogo.janelas, conversaComMensagem.pedido.janela)
        : null
      // RN-06/RN-22 (US-028): mesmo respiro do resto do arquivo antes de
      // prometer horário.
      const texto = janelaDoPedido
        ? `Oi ${nome}! Seu pedido está em preparo, chega entre ${faixaDepoisDeEntre(faixaParaCliente(janelaDoPedido.faixa, agora))}.`
        : `Oi ${nome}! Seu pedido está na fila, te aviso aqui assim que entrar no preparo.`
      return { respostaAutomatica: { texto, regraId: 'status-pedido' }, passarParaDona: null, concilia: false }
    }

    return {
      respostaAutomatica: {
        texto: textoSaudacaoRecorrente({
          nome: conversaComMensagem.nome,
          favorito: favoritoDasTags(conversaComMensagem.cliente?.tags),
          novidade: 'o pappardelle com ragu de costela',
        }),
        regraId: 'saudacao-recorrente',
      },
      passarParaDona: null,
      concilia: false,
    }
  }

  return nada
}

const temCobrancaPendente = (conversa) =>
  Boolean(conversa.pedido?.cobranca) && !conversa.pedido.cobranca.pagaEm

// --- Os 19 cenários da seção 7, com o corte das 3 perguntas -----------------
// Cortados desta rodada (registrados em auditoria/decisoes/24-f7-simulacoes.md):
//   9  Acrescenta item em pedido pago  → depende de F3+F4 (comanda/cobrança
//      ainda vazias); hoje só mostraria uma mensagem chegando, sem nenhuma
//      ação nova para provar. Sem valor extra sobre os cenários que já ficam.
//   16 Mesmo domicílio                 → depende de F2 (detecção de endereço
//      repetido ainda não existe); hoje seria idêntico ao cenário 1.
//   19 Internet cai                    → cortado na rodada 5 por não nascer
//      de "mensagem chegou" (fila offline era escopo novo grande demais para
//      aquela onda). Voltou na rodada 8 como cenário próprio, sem fila: só
//      marca `conexao.online` (decisão 70, US-042, D6, UC-04 E1).
export const GRUPOS = {
  CHEGANDO: 'Chegando',
  PEDIDO: 'Pedido e pagamento',
  ENTREGA: 'Entrega',
  PROBLEMAS: 'Problemas',
  VOLUME: 'Volume',
}

export const CENARIOS = [
  // --- Must, na ordem pedida pela sala de guerra --------------------------
  {
    id: 'lead-novo',
    grupo: GRUPOS.CHEGANDO,
    titulo: 'Cliente novo chega',
    resumo: 'Lead do Instagram manda oi; o automático responde sozinho, capta nome, telefone e endereço, cria o cadastro e anota o pedido.',
    ancora: 'US-001, US-011, UC-01 passos 2 a 8, issue #8',
    dependeDe: 'F2',
    // Rodada 11 (registro 92): cada fala espera a resposta "digitando" da
    // anterior sair (aplicacao/useDigitacaoAutomatica.js), para a conversa
    // se ler como conversa. Os intervalos cobrem o tempo de digitação de
    // `tempoDigitandoMs` (dominio/captura.js) com folga.
    roteiro: [
      { aposMs: 0, tipo: 'conversa' },
      { aposMs: 1200, tipo: 'mensagem', indice: 0 },
      { aposMs: 6400, tipo: 'mensagem', indice: 1 },
      { aposMs: 9600, tipo: 'mensagem', indice: 2 },
      { aposMs: 12800, tipo: 'mensagem', indice: 3 },
      { aposMs: 16800, tipo: 'mensagem', indice: 4 },
    ],
  },
  {
    id: 'recorrente',
    grupo: GRUPOS.CHEGANDO,
    titulo: 'Recorrente pede o de sempre',
    resumo: 'Cliente com histórico escreve; o automático saúda pelo nome e oferece favorito e novidade.',
    ancora: 'US-002, RN-02, RN-07',
    roteiro: [
      { aposMs: 0, tipo: 'conversa' },
      { aposMs: 1600, tipo: 'mensagem', indice: 0 },
    ],
  },
  {
    id: 'restricao',
    grupo: GRUPOS.CHEGANDO,
    titulo: 'Pergunta de restrição',
    resumo: 'Cliente pergunta sobre glúten; o automático nunca responde composição, passa para você.',
    ancora: 'RN-52',
    dependeDe: 'F1',
    roteiro: [
      { aposMs: 0, tipo: 'conversa' },
      { aposMs: 1600, tipo: 'mensagem', indice: 0 },
    ],
  },
  {
    id: 'pagamento-cai',
    grupo: GRUPOS.PEDIDO,
    titulo: 'Pagamento cai',
    resumo: 'O Pix de um pedido aguardando é pago; concilia sozinho, avisa o cliente e libera a esteira.',
    ancora: 'US-032, RN-24, RN-25, RN-27',
    dependeDe: 'F3',
    roteiro: [
      { aposMs: 0, tipo: 'conversa' },
      { aposMs: 1600, tipo: 'pagamento' },
    ],
  },
  {
    id: 'pix-vence',
    grupo: GRUPOS.PEDIDO,
    titulo: 'Pix vence',
    resumo: 'Conversa com Pix a segundos de vencer; o relógio avança e a cobrança expira sozinha.',
    ancora: 'UC-01 E2',
    roteiro: [
      { aposMs: 0, tipo: 'conversa' },
      { aposMs: 1600, tipo: 'relogio', incrementoMinutos: 1 },
    ],
  },
  {
    id: 'reclamacao',
    grupo: GRUPOS.PROBLEMAS,
    titulo: 'Reclamação',
    resumo: 'Pedido entregue; cliente reclama da lasanha fria; abre reclamação e sobe ao topo do Balcão.',
    ancora: 'UC-06, RN-35, RN-36',
    dependeDe: 'F1',
    roteiro: [
      { aposMs: 0, tipo: 'conversa' },
      { aposMs: 1600, tipo: 'mensagem', indice: 0 },
      { aposMs: 3400, tipo: 'reclamacao' },
    ],
  },
  {
    id: 'fora-de-area',
    grupo: GRUPOS.CHEGANDO,
    titulo: 'Cliente fora de área',
    resumo: 'CEP fora do alcance; o automático avisa e pergunta se quer seguir, o lead insiste e passa para você decidir na ficha.',
    ancora: 'UC-01 A, UC-02, RN-10, RN-11',
    dependeDe: 'F2',
    roteiro: [
      { aposMs: 0, tipo: 'conversa' },
      { aposMs: 1600, tipo: 'mensagem', indice: 0 },
      { aposMs: 4200, tipo: 'mensagem', indice: 1 },
    ],
  },

  // --- Os demais da seção 7, na ordem do documento ------------------------
  {
    id: 'dez-de-uma-vez',
    grupo: GRUPOS.VOLUME,
    titulo: 'Dez clientes de uma vez',
    resumo: '10 conversas em 20 s: 4 leads, 3 recorrentes, 2 perguntas de restrição, 1 reclamação.',
    ancora: 'UC-05',
    dependeDe: 'F1',
    roteiro: (() => {
      const passos = []
      let t = 0
      const cada = 2000
      const somar = (tipo, extra = {}) => { passos.push({ aposMs: t, tipo, ...extra }); t += cada }
      for (let i = 0; i < 4; i += 1) { somar('conversa', { variacao: `lead-${i}` }); somar('mensagem', { indice: 0 }) }
      for (let i = 0; i < 3; i += 1) { somar('conversa', { variacao: `recorrente-${i}` }); somar('mensagem', { indice: 0 }) }
      for (let i = 0; i < 2; i += 1) { somar('conversa', { variacao: `restricao-${i}` }); somar('mensagem', { indice: 0 }) }
      somar('conversa', { variacao: 'reclamacao-0' }); somar('mensagem', { indice: 0 }); somar('reclamacao')
      return passos
    })(),
  },
  {
    id: 'pagou-a-menos',
    grupo: GRUPOS.PEDIDO,
    titulo: 'Pagou a menos',
    resumo: 'Pix cai com R$ 50,00 de um pedido de R$ 68,00; a ficha mostra "Valor diferente".',
    ancora: 'rodada 4',
    roteiro: [
      { aposMs: 0, tipo: 'conversa' },
      { aposMs: 1600, tipo: 'pagamento', valorForcado: 50 },
    ],
  },
  {
    id: 'entrega-atrasa',
    grupo: GRUPOS.ENTREGA,
    titulo: 'Entrega atrasa',
    resumo: 'Pedido em preparo; cliente pergunta "cadê meu pedido?" e some sem resposta por minutos.',
    ancora: 'UC-04 E2, c30',
    dependeDe: 'F6',
    roteiro: [
      { aposMs: 0, tipo: 'conversa' },
      { aposMs: 1600, tipo: 'mensagem', indice: 0 },
      { aposMs: 3400, tipo: 'relogio', incrementoMinutos: 6 },
    ],
  },
  {
    id: 'bloqueado-insiste',
    grupo: GRUPOS.PROBLEMAS,
    titulo: 'Cliente bloqueado tenta de novo',
    resumo: 'Cadastro bloqueado escreve pelo Instagram; a casa não responde sozinha em canal nenhum.',
    ancora: 'RN-14',
    dependeDe: 'F1',
    roteiro: [
      { aposMs: 0, tipo: 'conversa' },
      { aposMs: 1600, tipo: 'mensagem', indice: 0 },
    ],
  },
  {
    id: 'saldo-zero',
    grupo: GRUPOS.PROBLEMAS,
    titulo: 'Saldo zero',
    resumo: 'Cliente pede lasanha verde (saldo 0); o automático não vende e passa para você.',
    ancora: 'UC-09, RN-48, RN-53',
    roteiro: [
      { aposMs: 0, tipo: 'conversa' },
      { aposMs: 1600, tipo: 'mensagem', indice: 0 },
    ],
  },
  {
    id: 'janela-lotada',
    grupo: GRUPOS.PEDIDO,
    titulo: 'Janela lotada',
    resumo: 'Cliente pede a janela das 11h30, cheia; fica esperando você, e ao consultar o agente ele já sugere só o que tem vaga.',
    ancora: 'UC-03 E1, RN-21',
    roteiro: [
      { aposMs: 0, tipo: 'conversa' },
      { aposMs: 1600, tipo: 'mensagem', indice: 0 },
    ],
  },
  {
    id: 'avaliacao-negativa',
    grupo: GRUPOS.ENTREGA,
    titulo: 'Avaliação chega',
    resumo: 'Pedido entregue; 30 minutos depois a casa pergunta e o cliente toca negativa, abrindo a ocorrência sozinha.',
    ancora: 'US-046, RN-34, RN-35',
    dependeDe: 'F5',
    // Rodada 10 (registro 79, achado P2.7): o roteiro só cria a conversa e
    // avança o relógio. O toque de avaliação (a pergunta da casa e a
    // resposta em um toque, distinta de texto livre) é reação do cliente
    // simulado (`aplicacao/useReacaoClienteSimulado.js`, ligada pelo campo
    // `avaliacaoPendente` do esqueleto em infra/roteirosSimulacao.js), não
    // mais um passo fixo de tempo.
    roteiro: [
      { aposMs: 0, tipo: 'conversa' },
      { aposMs: 1600, tipo: 'relogio', incrementoMinutos: 30 },
    ],
  },
  {
    id: 'desliga-avisos',
    grupo: GRUPOS.PROBLEMAS,
    titulo: 'Cliente desliga avisos',
    resumo: 'Cliente pede para parar de avisar cada passo; a casa passa para você em vez de prometer sozinha.',
    ancora: 'RN-37, c24',
    roteiro: [
      { aposMs: 0, tipo: 'conversa' },
      { aposMs: 1600, tipo: 'mensagem', indice: 0 },
    ],
  },
  {
    id: 'pedido-outro-dia',
    grupo: GRUPOS.CHEGANDO,
    titulo: 'Pedido para outro dia',
    resumo: 'Mensagem às 2h da manhã; sai a regra Fora do horário, com a hora em que reabre, e pede para deixar o pedido anotado.',
    ancora: 'D9, c21, c47',
    roteiro: [
      { aposMs: 0, tipo: 'conversa' },
      // Integração 48: a loja nasce aberta na mão; a madrugada do cenário é de
      // loja que ninguém deixou aberta, então ela volta a seguir o horário.
      { aposMs: 1600, tipo: 'relogio', paraHora: 2, lojaSegueHorario: true },
      { aposMs: 3200, tipo: 'mensagem', indice: 0 },
    ],
  },

  // Rodada 7 · frente Anexos (fala do dono, 24/09/2026 04h12: "falta poder
  // enviar arquivos também anexar, enviar áudio, receber áudio"; padrão de
  // mercado na seção B da pesquisa: "Tocar áudio recebido: bolha com forma de
  // onda, barra de progresso"). Sem este cenário o áudio recebido nunca
  // existia na tela: precisa de alguém mandando um para a bolha ter o que
  // tocar.
  {
    id: 'cliente-audio',
    grupo: GRUPOS.CHEGANDO,
    titulo: 'Cliente manda áudio',
    resumo: 'Lead manda um áudio perguntando da entrega; a bolha toca com play, pausa e progresso.',
    ancora: 'fala do dono 24/09/2026 04h12, pesquisa seção B',
    roteiro: [
      { aposMs: 0, tipo: 'conversa' },
      { aposMs: 1600, tipo: 'mensagem', indice: 0 },
    ],
  },

  // Rodada 8 · US-042, D6, UC-04 E1 (decisão 70): sem fila de mensagens, só
  // marca a loja offline. O roteiro de outros cenários (mensagem, pagamento)
  // fica mudo enquanto ela dura — ninguém aqui despacha nada nesse meio-tempo
  // de propósito, "não finge que mandou mensagem". Reconectar é um botão no
  // próprio banner (ModalLoteDePapel.jsx), não um passo com tempo fixo: dá
  // controle de verdade a quem está demonstrando, em vez de um timer solto.
  {
    id: 'internet-cai',
    grupo: GRUPOS.PROBLEMAS,
    titulo: 'Internet cai',
    resumo: 'A loja fica offline; ao reconectar, a tela oferece lançar em lote o que foi marcado no papel.',
    ancora: 'US-042, RN-27, RN-33, D6, UC-04 E1',
    roteiro: [
      { aposMs: 0, tipo: 'conexao', online: false },
    ],
  },
]

export const cenarioPorId = (id) => CENARIOS.find((c) => c.id === id) ?? null
