// Ocorrência de reclamação, vinculada ao pedido (RN-35), nunca à conversa
// solta: quando o pedido muda de dono ou de canal, a ocorrência viaja junto
// porque mora dentro dele (`pedido.ocorrencia`), igual à cobrança.
//
// UC-06 (tratar reclamação e devolver o valor). Quatro estados, na ordem da
// vida real:
//
//   aberta                  Sistema identificou a reclamação, ninguém entrou ainda.
//   em-apuracao              A dona entrou e está conversando para entender o caso.
//   encerrada-com-estorno     Comida não volta (fala da dona, áudio 07): sem
//                             consumo possível, o dinheiro volta.
//   encerrada-sem-estorno     Alternativo A: não era defeito, é preferência.
//                             Vira nota interna, nunca estorno.
//
// A abertura é automática (US-049, RN-35): "não é botão, é entrar e ver o que
// aconteceu". Puro, sem rede: quem decide `agora` e persiste é a aplicação.

import { faixaDaJanela } from './entrega.js'

// Mesmo cálculo de `dominio/viagem.js: fimDaFaixa`, copiado em vez de
// importado: `viagem.js` puxa `esteira.js`/`entrega.js` sem extensão, o que
// quebra os testes node puro (`teste-assistente.mjs` chega aqui por
// `dominio/assistente.js`, que só sabe carregar módulo com import de
// extensão fechada). Duas linhas de regex, o mesmo espírito de
// `dominio/cozinha.js: ICONE_DO_PASSO` (duplicado de propósito, comentado).
function fimDaFaixaSimples(faixa, agora) {
  const casado = /às\s*(\d{1,2})h(\d{2})?/.exec(faixa ?? '')
  if (!casado) return null
  const dia = new Date(agora)
  dia.setHours(Number(casado[1]), Number(casado[2] ?? 0), 0, 0)
  return dia.getTime()
}

export const ESTADOS_OCORRENCIA = {
  ABERTA: 'aberta',
  EM_APURACAO: 'em-apuracao',
  ENCERRADA_COM_ESTORNO: 'encerrada-com-estorno',
  ENCERRADA_SEM_ESTORNO: 'encerrada-sem-estorno',
}

// Rodada 10, achado 7 / item "falta construir" 4: a ocorrência não nascia só
// de reclamação de cliente. `origem` distingue quem abriu (o cliente
// reclamando, ou o sistema sozinho ao ver uma entrega passar do prometido),
// sem duplicar o resto do fluxo (apurar/encerrar continuam os mesmos).
export const ORIGENS_OCORRENCIA = {
  RECLAMACAO: 'reclamacao',
  ATRASO_ENTREGA: 'atraso-entrega',
}

const comHistorico = (ocorrencia, entrada) => ({
  ...ocorrencia,
  historico: [...ocorrencia.historico, entrada],
})

// US-049: nasce vinculada ao pedido, com o relato do cliente já no histórico.
// A resolução é humana, então nasce sempre "aberta", nunca resolvida sozinha.
// `origem` default mantém quem já chamava isto sem saber do motivo novo
// (reclamação, autor sempre 'cliente'); a entrega atrasada chama com
// `origem: ATRASO_ENTREGA` e um relato de autoria do próprio sistema.
export function abrirOcorrencia({
  id, pedidoNumero, relatoCliente, agora, origem = ORIGENS_OCORRENCIA.RECLAMACAO,
}) {
  const autor = origem === ORIGENS_OCORRENCIA.ATRASO_ENTREGA ? 'sistema' : 'cliente'
  return {
    id,
    pedidoNumero,
    origem,
    estado: ESTADOS_OCORRENCIA.ABERTA,
    abertaEm: agora,
    encerradaEm: null,
    estorno: null,
    notaInterna: null,
    historico: [{ em: agora, autor, texto: relatoCliente }],
  }
}

export const ehAtrasoDeEntrega = (ocorrencia) => ocorrencia?.origem === ORIGENS_OCORRENCIA.ATRASO_ENTREGA

// Achado 7 / item 4: parada já em rota (`entrega`, entregador a caminho) cujo
// fim da janela prometida já passou. Atraso ANTES de sair é atraso de
// cozinha (`dominio/cozinha.js: pedidoAtrasado`), motivo diferente.
export function entregaPassouDoPrazo(conversa, janelas, agora) {
  const pedido = conversa?.pedido
  if (!pedido || pedido.estado !== 'entrega') return false
  const faixa = faixaDaJanela(janelas, pedido.janela)
  const prazo = faixa ? fimDaFaixaSimples(faixa, agora) : null
  return prazo != null && agora > prazo
}

// UC-06 passos 3-4: a dona abre e apura. Idempotente: apurar de novo uma
// ocorrência que já está em apuração ou encerrada não desfaz o que já andou.
export function apurar(ocorrencia, agora) {
  if (!ocorrencia || ocorrencia.estado !== ESTADOS_OCORRENCIA.ABERTA) return ocorrencia
  return comHistorico(
    { ...ocorrencia, estado: ESTADOS_OCORRENCIA.EM_APURACAO },
    { em: agora, autor: 'dona', texto: 'Entrou para apurar o que aconteceu.' },
  )
}

// UC-06 passos 5-6: motivo é obrigatório (passo 5). Sem motivo a ocorrência
// não muda, do mesmo jeito que `estornarCobranca` (dominio/cobranca.js) barra
// o estorno sem motivo: as duas travas protegem a mesma regra de negócio.
export function encerrarComEstorno(ocorrencia, { motivo, valor }, agora) {
  const motivoLimpo = (motivo ?? '').trim()
  if (!ocorrencia || ocorrencia.encerradaEm || !motivoLimpo) return ocorrencia
  return comHistorico(
    {
      ...ocorrencia,
      estado: ESTADOS_OCORRENCIA.ENCERRADA_COM_ESTORNO,
      encerradaEm: agora,
      estorno: { motivo: motivoLimpo, valor, em: agora },
    },
    { em: agora, autor: 'dona', texto: `Estornou com o motivo: ${motivoLimpo}` },
  )
}

// Alternativo A (UC-06): não era defeito do produto, é preferência. Fecha sem
// estorno e a preferência fica registrada como nota interna, nunca como
// devolução de dinheiro.
export function encerrarSemEstorno(ocorrencia, { preferencia }, agora) {
  const preferenciaLimpa = (preferencia ?? '').trim()
  if (!ocorrencia || ocorrencia.encerradaEm || !preferenciaLimpa) return ocorrencia
  return comHistorico(
    {
      ...ocorrencia,
      estado: ESTADOS_OCORRENCIA.ENCERRADA_SEM_ESTORNO,
      encerradaEm: agora,
      notaInterna: { texto: preferenciaLimpa, em: agora },
    },
    { em: agora, autor: 'dona', texto: `Encerrou sem estorno. Nota: ${preferenciaLimpa}` },
  )
}

export const ocorrenciaAberta = (ocorrencia) => Boolean(ocorrencia) && !ocorrencia.encerradaEm

// Motivo curto para o cartão de "Precisa de você" (dominio/automatico.js):
// mesma fala do cliente que abriu a ocorrência, resumida em uma linha.
export function motivoParaAutomatico(ocorrencia) {
  if (!ocorrenciaAberta(ocorrencia)) return null
  return ocorrencia.historico[0]?.texto ?? 'Reclamação aberta neste pedido'
}

// Modo API (F09): as ocorrências do cliente vêm do EasyStok, uma lista por cadastro. A do
// pedido em tela é a aberta; sem aberta, a mais recente; sem nenhuma, nada.
export function ocorrenciaDoPedido(ocorrencias, pedidoId) {
  if (!pedidoId) return null
  const doPedido = (ocorrencias ?? []).filter((o) => o.pedidoId === pedidoId)
  const maisRecente = (a, b) => (b.abertaEm ?? 0) - (a.abertaEm ?? 0)
  return doPedido.filter(ocorrenciaAberta).sort(maisRecente)[0]
    ?? doPedido.sort(maisRecente)[0]
    ?? null
}
