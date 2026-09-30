// Modo do atendimento automático de UMA conversa. Três situações e um lugar só
// para mostrar: automático ligado, pausado porque a dona escreveu (RN-04) e
// passou para você, que é quando o agente devolve com acao passar_para_dona
// (RN-52 restrição alimentar, RN-53 saldo zerado).
//
// D4: a dona assume a qualquer momento e a volta ao automático é decisão dela,
// nunca do relógio. Por isso não existe aqui nenhuma regra de tempo que religue
// o automático sozinho.
//
// Puro. Conversa antiga, sem os campos novos, cai no padrão seguro: automático
// ligado e nada de "precisa de você".

import { situacaoDaCobranca } from './cobranca'
import { prazoDoPagamento } from './lembrete'
import { duracao } from './formato'
import { ehAtrasoDeEntrega, motivoParaAutomatico, ocorrenciaAberta } from './ocorrencia.js'
import { pedidoAtrasado } from './cozinha.js'
import { MOTIVO_PADRAO, origemDaPassagem } from './passagem.js'
import { pedidoEncerrado } from './pedido.js'
import { ehPerguntaSemResposta, mensagensSemRespostaReal } from './mensagem.js'

export const MODOS = {
  LIGADO: 'ligado',
  PAUSADO: 'pausado',
  COM_VOCE: 'com-voce',
  ENCERRADO: 'encerrado',
  BLOQUEADO: 'bloqueado',
}

// Marca guardada em `automaticoPausado[conversaId]` quando a pausa nasceu do
// botão Assumir. Escrever na conversa continua gravando `true`.
export const PAUSA_POR_ASSUMIR = 'assumiu'

// Os três momentos que pedem som na cozinha (D6). O nome mora aqui, no
// domínio, porque a tela precisa listar e infra precisa tocar, e nenhuma das
// duas pode depender da outra.
export const EVENTOS_DE_SOM = {
  NOVO_ATENDIMENTO: 'novo-atendimento',
  PAGAMENTO_CONFIRMADO: 'pagamento-confirmado',
  PASSOU_PARA_VOCE: 'passou-para-voce',
}

export const ROTULO_DO_SOM = {
  [EVENTOS_DE_SOM.NOVO_ATENDIMENTO]: 'novo atendimento',
  [EVENTOS_DE_SOM.PAGAMENTO_CONFIRMADO]: 'pagamento confirmado',
  [EVENTOS_DE_SOM.PASSOU_PARA_VOCE]: 'passou para você',
}

export const listaDeEventosDeSom = () => Object.values(EVENTOS_DE_SOM)

const DESCRICOES = {
  [MODOS.LIGADO]: {
    rotulo: 'Automático ligado',
    tom: 'ok',
    // Rodada 12 (issue #16): sem linha, "Automático ligado" não dizia o que
    // isso muda para ela.
    detalhe: 'O atendimento automático responde esta conversa sozinho. Toque em Assumir para responder você.',
  },
  [MODOS.PAUSADO]: {
    rotulo: 'Pausado porque você escreveu',
    tom: 'info',
    detalhe: 'Aviso de esteira continua saindo. O resto espera você devolver.',
  },
  // Mesma pausa, outra porta de entrada: ela apertou Assumir sem escrever nada.
  assumiu: {
    rotulo: 'Pausado porque você assumiu',
    tom: 'info',
    detalhe: 'Aviso de esteira continua saindo. O resto espera você devolver.',
  },
  [MODOS.COM_VOCE]: {
    rotulo: 'Passou para você',
    tom: 'aviso',
    detalhe: 'O agente não responde isso sozinho.',
  },
  [MODOS.ENCERRADO]: {
    rotulo: 'Conversa encerrada',
    tom: 'neutro',
    detalhe: 'Reabra a conversa para o automático voltar a valer.',
  },
  // Cadastro bloqueado não tem botão de reabrir nem de assumir: a única porta
  // é o desbloqueio na ficha. Mandar reabrir aqui era prometer um botão que a
  // barra esconde justamente por causa do bloqueio.
  [MODOS.BLOQUEADO]: {
    rotulo: 'Cadastro bloqueado',
    tom: 'perigo',
    detalhe: 'Nada sai em nenhum canal. Desbloqueie na ficha, bloco Cliente.',
  },
}

// Precedência: bloqueio cala todo o resto, depois conversa encerrada, depois
// passou para você, depois pausa, e por último o automático ligado. `bloqueada`
// chega por parâmetro para o domínio não fazer duas contas da mesma pergunta.
export function modoDoAtendimento(conversa, pausado = false, bloqueada = false) {
  if (!conversa) return { chave: MODOS.LIGADO, ...DESCRICOES[MODOS.LIGADO] }
  if (bloqueada) {
    return { chave: MODOS.BLOQUEADO, ...DESCRICOES[MODOS.BLOQUEADO] }
  }
  if (conversa.estado === 'Encerrado') {
    return { chave: MODOS.ENCERRADO, ...DESCRICOES[MODOS.ENCERRADO] }
  }
  const motivo = conversa.passagem?.motivo ?? null
  // Passou para você e ninguém pegou ainda: é o estado que grita. A linha
  // diz a origem inteira (quem, por quê, que horas), a mesma do cartão do
  // Balcão e do lembrete (dominio/passagem.js, rodada 12).
  if (conversa.passagem && !conversa.passagem.assumida) {
    const base = DESCRICOES[MODOS.COM_VOCE]
    return { chave: MODOS.COM_VOCE, ...base, detalhe: origemDaPassagem(conversa.passagem).longo }
  }
  if (pausado) {
    const base = pausado === PAUSA_POR_ASSUMIR ? DESCRICOES.assumiu : DESCRICOES[MODOS.PAUSADO]
    return {
      chave: MODOS.PAUSADO,
      ...base,
      detalhe: motivo ? `${motivo}. ${base.detalhe}` : base.detalhe,
    }
  }
  return { chave: MODOS.LIGADO, ...DESCRICOES[MODOS.LIGADO] }
}

// Motivo curto da passagem, na voz da casa. É o que aparece no cartão do
// balcão, então cabe em uma linha.
const MOTIVO_POR_INTENCAO = {
  restricao: 'Restrição alimentar, o automático não responde isso',
  'elogio-ou-queixa': 'Retorno sobre o pedido, precisa da sua voz',
  'pergunta-entrega': 'Pergunta de entrega fora do combinado',
  'fechar-pedido': 'Fechamento de pedido que o automático não conclui',
  acompanhar: 'Cobrança de prazo que só você pode confirmar',
}


// RN-11: motivo específico para a dona, sem depender da intenção classificada
// (o lead pode ter insistido com uma frase que não bate pista nenhuma).
const MOTIVO_AREA_FORA = 'Lead fora da área de entrega insistiu, decida a exceção'

export function motivoDaPassagem(intencaoChave, semSaldo = [], areaForaInsistida = false) {
  if (areaForaInsistida) return MOTIVO_AREA_FORA
  if (semSaldo.length > 0) {
    return `${semSaldo[0].nome} sem saldo, o automático não vende`
  }
  return MOTIVO_POR_INTENCAO[intencaoChave] ?? MOTIVO_PADRAO
}

// --- Próximo passo depois de ela responder -----------------------------------
// Rodada 12 (issue #16), dúvida da Thatiane: "respondi, e agora? encerro ou
// espero?". Só aparece quando é ELA quem conduz (automático pausado) e a
// última fala da conversa é dela. Nota interna e aviso do sistema não contam
// como fala. Pedido em andamento nunca sugere encerrar: a conversa ainda vai
// levar os avisos da esteira até a entrega.
const ultimaFala = (conversa) =>
  [...(conversa.mensagens ?? [])].reverse().find((m) => m.dir === 'in' || m.dir === 'out') ?? null

export function proximoPassoDepoisDeResponder(conversa, pausado) {
  if (!conversa || !pausado || conversa.estado === 'Encerrado') return null
  const fala = ultimaFala(conversa)
  if (!fala || fala.dir !== 'out') return null
  if (conversa.pedido && !pedidoEncerrado(conversa.pedido)) {
    return {
      chave: 'aguardar',
      texto: 'Você respondeu. Próximo passo sugerido: aguardar o cliente. O pedido ainda está em andamento, '
        + 'então não encerre; devolva ao automático se quiser que ele responda quando o cliente voltar.',
    }
  }
  return {
    chave: 'encerrar-ou-aguardar',
    texto: 'Você respondeu. Próximo passo sugerido: se o assunto acabou, encerre a conversa; '
      + 'se ainda espera o cliente, devolva ao automático para ele responder quando o cliente voltar.',
  }
}

// --- Precisa de você ---------------------------------------------------------
// TRÊS motivos, não um. Contar só a devolução do agente marcava 0 com quatro
// cobranças vencidas na fila e gente esperando resposta há nove minutos, e um
// filtro que marca zero com trabalho na mesa é um filtro que ninguém liga.
//
// Um motivo por conversa, por precedência: o cartão de triagem não comporta
// dois. O texto sai escrito no cartão, porque "precisa de você" sem o porquê
// obriga a abrir a conversa para descobrir o que era.

// Silêncio que já pede a voz dela. Abaixo disso o automático ainda tem chance.
export const MINUTOS_DE_ESPERA = 5

const minutosDesde = (iso, agora) => Math.floor((agora - new Date(iso).getTime()) / 60000)

// `bloqueio` chega normalizado do repositório: com o campo, a conversa está
// bloqueada. Conversa bloqueada sai da lista padrão do balcão, então contar ela
// aqui deixaria a contagem maior que a lista.
const foraDeAlcance = (conversa) =>
  Boolean(conversa.bloqueio) || conversa.estado === 'Encerrado'

// `pausado` é o automático desligado PARA ESTA conversa (RN-04). Default
// `true` porque a maioria dos chamadores ainda não carrega
// `automaticoPausado`; a Frente A do Balcão passa o valor real.
//
// Rodada 13 (issue #41, varredura): contar só quando pausado deixava de fora
// pergunta real que o automático nunca vai responder sozinho (fora do
// roteiro, RN correspondente) enquanto ele segue "ligado" na conversa — José
// Moretti perguntou "Consegue entregar na hora do almoço?" e ficou fora de
// "Precisa de você" até alguém pausar manualmente. `mensagensSemRespostaReal`
// (dominio/mensagem.js) já resolve a outra metade do achado: aviso de
// esteira que sai sozinho durante a espera não conta como resposta e não
// zera a pendência.
//
// Com o automático ligado (não pausado) só entra quando a última fala
// pendente tem cara de pergunta de verdade (`ehPerguntaSemResposta`, mesmo
// heurístico do fallback de `aplicacao/casos/simulacao.js`): ele ainda pode
// estar conduzindo um fluxo normal (captura de endereço, por exemplo), e um
// "obrigada" ou uma frase qualquer não pode virar pendência só por ele não
// ter respondido nada depois. Pausado (RN-04, ela escreveu ou assumiu) o
// silêncio já é dela: qualquer mensagem pendente conta, pergunta ou não.
function esperaDoCliente(conversa, agora, pausado = true) {
  const pendentes = mensagensSemRespostaReal(conversa)
  const ultima = pendentes.at(-1)
  if (!ultima) return null
  if (!pausado && !ehPerguntaSemResposta(ultima.texto)) return null
  const minutos = minutosDesde(ultima.em, agora)
  if (minutos <= MINUTOS_DE_ESPERA) return null
  return {
    chave: 'esperando',
    rotulo: 'Esperando você',
    tom: 'perigo',
    // Minuto cru acima de uma hora nunca mais (seção 1): "duracao" já sabe
    // virar "1 h 12", "23 h" ou "2 d".
    texto: `Cliente falou por último e espera ${duracao(agora - new Date(ultima.em).getTime())}`,
  }
}

// Ocorrência aberta no pedido (seção 1 e 7, RN-35/RN-36; rodada 10 achado 7):
// reclamação de cliente OU entrega que passou do prometido, mesmo slot (só
// uma ocorrência por pedido de cada vez), rótulo por origem. A ocorrência real
// mora em `pedido.ocorrencia` (dominio/ocorrencia.js), aberta automaticamente.
function ocorrenciaPendente(conversa) {
  const ocorrencia = conversa.pedido?.ocorrencia
  if (!ocorrenciaAberta(ocorrencia)) return null
  if (ehAtrasoDeEntrega(ocorrencia)) {
    return {
      chave: 'atraso-entrega',
      rotulo: 'Entrega atrasada',
      tom: 'perigo',
      texto: motivoParaAutomatico(ocorrencia),
    }
  }
  return {
    chave: 'reclamacao',
    rotulo: 'Reclamação',
    tom: 'perigo',
    texto: motivoParaAutomatico(ocorrencia),
  }
}

// Rodada 10, item "falta construir" 3 / achado 3: pedido atrasado (ainda na
// cozinha, antes de sair) é um dos 3 sons mínimos do US-010, e não dependia
// de o cliente escrever de novo para acender "Precisa de você". `janelas`
// chega por parâmetro (mesmo motivo de `dominio/areaEntrega.js`: domínio não
// lê catálogo sozinho).
function pedidoAtrasadoVisivel(conversa, janelas, agora) {
  if (!pedidoAtrasado(conversa.pedido, janelas, agora)) return null
  return {
    chave: 'pedido-atrasado',
    rotulo: 'Pedido atrasado',
    tom: 'perigo',
    texto: `Pedido ${conversa.pedido.numero} passou do horário calculado para começar o preparo`,
  }
}

// Cobrança vencida nos dois mundos: com Pix emitido vale o relógio dele, e sem
// Pix vale o prazo da comanda gerada, que é o mesmo do lembrete de pagamento.
function cobrancaVencida(conversa, agora) {
  const pedido = conversa.pedido
  if (!pedido) return null
  const expirou = pedido.cobranca
    ? situacaoDaCobranca(pedido.cobranca, agora).chave === 'expirada'
    : (prazoDoPagamento(conversa, agora) ?? Infinity) <= agora
  if (!expirou) return null
  return {
    chave: 'cobranca',
    rotulo: 'Cobrança vencida',
    tom: 'perigo',
    texto: `Pedido ${pedido.numero} gerado e o pagamento não caiu`,
  }
}

// Escreveu com a loja fechada ou fora do horário (dominio/simulacoes.js
// marca `conversa.aguardandoAbertura`, pedido do dono 24/09/2026): fica de
// fora de "Precisa de você" enquanto a loja segue fechada (é fila, não
// urgência) e sobe assim que `aberta` vira `true` de novo, sem precisar de
// nenhuma ação própria no reducer — o mesmo `agora`/estado da loja que já
// roda a cada tique decide.
function aguardandoAbertura(conversa, aberta) {
  if (!aberta || !conversa.aguardandoAbertura) return null
  return {
    chave: 'aguardando-abertura',
    rotulo: 'Loja abriu',
    tom: 'aviso',
    texto: 'Escreveu com a loja fechada, confira antes de responder',
  }
}

// `janelas` é novo na rodada 10 (achado 3): default `[]` para quem ainda não
// passa o catálogo continuar funcionando exatamente como antes (pedido
// atrasado simplesmente não entra na conta, nunca quebra).
export function motivoDePrecisar(conversa, agora, pausado = true, aberta = true, janelas = []) {
  if (!conversa) return null
  if (conversa.passagem && !conversa.passagem.assumida) {
    return {
      chave: 'passagem',
      rotulo: 'Precisa de você',
      tom: 'aviso',
      texto: conversa.passagem.motivo ?? MOTIVO_PADRAO,
    }
  }
  if (foraDeAlcance(conversa)) return null
  return aguardandoAbertura(conversa, aberta)
    ?? ocorrenciaPendente(conversa)
    ?? pedidoAtrasadoVisivel(conversa, janelas, agora)
    ?? esperaDoCliente(conversa, agora, pausado)
    ?? cobrancaVencida(conversa, agora)
}

export const precisaDeVoce = (conversa, agora, pausado = true, aberta = true, janelas = []) =>
  motivoDePrecisar(conversa, agora, pausado, aberta, janelas) != null

export const motivoVisivel = (conversa, agora, pausado = true, aberta = true, janelas = []) =>
  motivoDePrecisar(conversa, agora, pausado, aberta, janelas)?.texto ?? null

// A Frente A do Balcão (rodada 5) passa o valor real de `automaticoPausado`
// aqui: contar com o default (automático pausado) inflava "Precisa de você"
// com conversa que o automático ainda vai responder sozinho.
export const contarPrecisaDeVoce = (conversas, agora, automaticoPausado = {}, aberta = true, janelas = []) =>
  (conversas ?? []).filter((c) => precisaDeVoce(c, agora, automaticoPausado[c.id] ?? false, aberta, janelas)).length
