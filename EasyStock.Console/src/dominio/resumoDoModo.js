// Banner "quem responde esta conversa" em UMA linha (#1442). Homologação de
// 07/10: "Pausado porque você assumiu... Aviso de esteira continua saindo...
// Você respondeu. Próximo passo sugerido: ..." funcionava, mas era prolixo.
// A regra continua em `modoDoAtendimento` e `proximoPassoDepoisDeResponder`
// (provadas em prova-f07 e prova-r12); aqui só muda o tamanho da frase. A
// frase inteira continua em `detalhe`, que a tela põe no `title`.

import { MODOS, PAUSA_POR_OUTRO, modoDoAtendimento, proximoPassoDepoisDeResponder, sinalUrgente, tomDoMotivo } from './automatico'
import { AGUARDANDO_EQUIPE, origemDaPassagem } from './passagem'
import { naFilaSemResponsavel } from './acoesDaConversa'
import { automaticoDesligadoNaConversa } from './motivoDeEscalada'
import { faixaDaTransferencia } from './faixaDeTransferencia'
import { ROTULOS } from './rotulos'

const ROTULO = {
  get [MODOS.LIGADO]() { return ROTULOS.ASSISTENTE_RESPONDENDO },
  [MODOS.PAUSADO]: 'Você respondendo',
  [MODOS.COM_VOCE]: 'Passou para você',
  [MODOS.ENCERRADO]: 'Concluída',
  [MODOS.BLOQUEADO]: 'Bloqueado',
}

const LINHA = {
  [MODOS.LIGADO]: 'Responde sozinho até você assumir.',
  [MODOS.PAUSADO]: 'Assistente pausado; os avisos do pedido continuam.',
  [MODOS.ENCERRADO]: 'Reabra a conversa para retomar o atendimento.',
  [MODOS.BLOQUEADO]: 'Envios bloqueados. A gerência desbloqueia na ficha.',
}

const LINHA_DO_PROXIMO = {
  'encerrar-ou-aguardar': 'Conclua a conversa ou devolva ao Assistente.',
  aguardar: 'Respondido. Pedido em andamento: aguarde o cliente.',
  'aguardar-venda': 'Respondido. Venda em andamento: monte o pedido ou aguarde o cliente.',
}

// #2185: "Aguardando equipe" vale para a barra e o cartão (`modo.rotulo`), e quem é o outro atendente
// aparece pelo nome. Sem responsável, a conversa nunca diz "Você respondendo".
function rotuloDoModo(modo, conversa, outro) {
  // #2944 (E1-16): com o Assistente desligado na loja ninguém responde sozinho: a conversa é da equipe.
  if (modo.chave === MODOS.LIGADO && automaticoDesligadoNaConversa(conversa)) return ROTULOS.CONVERSA_ASSUMIDA_POR_VOCE
  if (outro) return conversa?.responsavel && conversa.responsavel !== 'Outro atendente' ? `Com ${conversa.responsavel}` : 'Outro atendente'
  if (modo.chave === MODOS.COM_VOCE) return modo.rotulo === AGUARDANDO_EQUIPE ? AGUARDANDO_EQUIPE : ROTULO[modo.chave]
  if (modo.chave === MODOS.PAUSADO && naFilaSemResponsavel(conversa)) return AGUARDANDO_EQUIPE
  return ROTULO[modo.chave]
}

// #3339 (R1D-08): com o atendente conduzindo, janela fechada é o motivo da barra. Sem ele, a linha
// do próximo passo sugere concluir ou devolver, e a janela não dá nenhuma das duas saídas.
export function motivoDaBarra(motivoDaFila, motivoDeEscrita, pausado) {
  if (motivoDaFila || !pausado || motivoDeEscrita?.acao !== 'janela') return motivoDaFila ?? null
  return { chave: 'janela', texto: `${motivoDeEscrita.titulo} ${motivoDeEscrita.detalhe}`, tom: 'aviso' }
}

// #2185 (E22): quem não pode reabrir não recebe a ordem "reabra"; o botão nem existe para ela.
const LINHA_ENCERRADA_SEM_PERMISSAO = 'Peça a quem pode reabrir a conversa.'

export function resumoDoModo(conversa, pausado = false, bloqueada = false, { podeReabrir = true, agora = null, motivo: motivoDaFila } = {}) {
  const modo = modoDoAtendimento(conversa, pausado, bloqueada)
  const outro = modo.chave === MODOS.PAUSADO && pausado === PAUSA_POR_OUTRO
  // #2185 (E20): "Você respondeu" só vale quando a pausa é minha; a conversa de um colega não é.
  const proximo = !bloqueada && modo.chave === MODOS.PAUSADO && !outro
    ? proximoPassoDepoisDeResponder(conversa, pausado)
    : null
  const semReabrir = modo.chave === MODOS.ENCERRADO && !podeReabrir
  const detalhe = [modo.rotulo + '.', semReabrir ? LINHA_ENCERRADA_SEM_PERMISSAO : modo.detalhe, proximo?.texto].filter(Boolean).join(' ')
  const motivo = conversa?.passagem?.motivo ?? null

  let linha = semReabrir ? LINHA_ENCERRADA_SEM_PERMISSAO : LINHA[modo.chave]
  if (modo.chave === MODOS.COM_VOCE) linha = origemDaPassagem(conversa.passagem).motivo
  else if (outro) linha = 'Assistente pausado.'
  else if (proximo) linha = LINHA_DO_PROXIMO[proximo.chave] ?? linha
  else if (modo.chave === MODOS.PAUSADO && motivo) linha = motivo

  // #3318: a barra mostra o mesmo motivo do cartão (`motivoDePrecisar`, calculado por quem chama com os mesmos dados).
  // Sem o motivo pronto, vale a leitura das mensagens da #3300 (F1), só com o Assistente ligado.
  const ligado = modo.chave === MODOS.LIGADO && !automaticoDesligadoNaConversa(conversa)
  const emAndamento = modo.chave === MODOS.LIGADO || modo.chave === MODOS.PAUSADO
  const daFila = !emAndamento ? null
    : motivoDaFila !== undefined ? motivoDaFila
      : agora != null && ligado ? sinalUrgente(conversa, agora) : null
  // Ligado: o Assistente não resolve isso sozinho, então "Precisa de você" (#3300). Pausado: a conversa já é de
  // alguém; fica o rótulo ("Com Thatiane", "Você respondendo") e a linha diz o motivo, que vence o próximo passo.
  const urgente = ligado ? daFila : null
  if (daFila) linha = daFila.texto
  // O `title` da pílula e da linha e o leitor de tela leem o `detalhe`: ele começa pelo motivo.
  const detalheFinal = urgente ? `Precisa de você. ${urgente.detalhe ?? urgente.texto}.` : daFila ? `${daFila.detalhe ?? daFila.texto}. ${detalhe}` : detalhe

  return {
    chave: modo.chave,
    // #3384 (r4 P1-3): qual motivo a linha diz, para a barra dar à frase da restrição o tom de aviso.
    motivo: daFila?.chave ?? null,
    tom: daFila ? tomDoMotivo(daFila) : modo.tom,
    rotulo: urgente ? 'Precisa de você' : rotuloDoModo(modo, conversa, outro),
    urgente: Boolean(urgente),
    precisa: Boolean(daFila),
    linha,
    detalhe: detalheFinal,
    proximo,
    sugereEncerrar: proximo?.chave === 'encerrar-ou-aguardar',
  }
}

// O que a barra "Quem responde esta conversa" mostra: a faixa da transferência (quem passou e por quê), a frase
// (à vista ou só para o leitor de tela) e a pílula. Com o Assistente desligado na loja, "pausado" e "volta"
// prometem o que não acontece. #3234 (C5): sem faixa nem frase à vista sobra só a pílula (e o Assumir), que no
// desktop cabe no cabeçalho como selo, em vez de ocupar uma faixa de 40 px.
// #3386 (revisão): a frase da restrição vira uma parte essencial curta e inteira ("Sem lactose", "Restrição alimentar") e o
// resto (": confira os ingredientes", ", confira ... antes de responder"), que a barra mostra só se couber. Separa no primeiro ": "
// ou, sem ele, na primeira ", "; sem separador, a frase toda é o essencial.
export function partesDoAvisoDeRestricao(linha) {
  const texto = String(linha ?? '')
  const i = texto.includes(': ') ? texto.indexOf(': ') : texto.indexOf(', ')
  return i < 0 ? { essencial: texto, resto: '' } : { essencial: texto.slice(0, i), resto: texto.slice(i) }
}

export function apresentacaoDoModo(conversa, pausado = false, bloqueada = false, { podeReabrir = true, agora = null, motivo } = {}) {
  const resumo = resumoDoModo(conversa, pausado, bloqueada, { podeReabrir, agora, motivo })
  const faixa = resumo.chave === MODOS.COM_VOCE ? faixaDaTransferencia(conversa.passagem) : null
  const semAssistente = automaticoDesligadoNaConversa(conversa) && resumo.chave !== MODOS.ENCERRADO && resumo.chave !== MODOS.LIGADO
  // #3318: o motivo de precisar de você vence o aviso do Assistente desligado e fica à vista.
  const mostrarLinha = semAssistente || resumo.chave === MODOS.COM_VOCE || Boolean(resumo.precisa)
  return {
    resumo, faixa, mostrarLinha,
    linha: semAssistente && !resumo.precisa ? 'O Assistente está desligado.' : resumo.linha,
    noCabecalho: !faixa && !mostrarLinha,
  }
}

// #2134: o Assistente está de fato respondendo esta conversa. Responder por cima dele são duas
// vozes falando com o cliente, então quem escreve assume antes (convenção do Intercom e do Front).
// Automático desligado na loja não responde ninguém: não há o que assumir.
export function assistenteAtendendo(conversa, pausado = false, bloqueada = false) {
  return modoDoAtendimento(conversa, pausado, bloqueada).chave === MODOS.LIGADO
    && !automaticoDesligadoNaConversa(conversa)
}
