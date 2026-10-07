// Horário de funcionamento da casa, configurável por dia da semana (pedido do
// dono, 24/09/2026). Puro: nenhum import fora de `dominio`, "agora" sempre
// chega por parâmetro (mesma regra de fronteira de `dominio/simulacoes.js`).

import { horaCurta, partesNoFuso } from './formato.js'

export const DIAS_DA_SEMANA = [
  { chave: 'domingo', indice: 0, rotulo: 'Domingo' },
  { chave: 'segunda', indice: 1, rotulo: 'Segunda' },
  { chave: 'terca', indice: 2, rotulo: 'Terça' },
  { chave: 'quarta', indice: 3, rotulo: 'Quarta' },
  { chave: 'quinta', indice: 4, rotulo: 'Quinta' },
  { chave: 'sexta', indice: 5, rotulo: 'Sexta' },
  { chave: 'sabado', indice: 6, rotulo: 'Sábado' },
]

const diaPorIndice = (indice) => DIAS_DA_SEMANA[indice]

// Mesmo par que `HORARIO` tinha em `dominio/automacao.js` (8h-22h), agora
// repetido por dia porque cada um pode mudar sozinho. Nenhum dia fechado por
// padrão: quem quiser folga marca "fechado" na tela.
export const FUNCIONAMENTO_PADRAO = Object.fromEntries(
  DIAS_DA_SEMANA.map((dia) => [dia.chave, { abre: '08:00', fecha: '22:00', fechado: false }]),
)

const paraMinutos = (hhmm) => {
  const [h, m] = hhmm.split(':').map(Number)
  return h * 60 + m
}

// Janela de UM dia, testada contra `minutos` (0-1439). `comoOntem` é a
// segunda passada de `dentroDoHorario`: quando a janela vira a meia-noite
// (fecha <= abre, ex. abre 20h fecha 2h), a fatia de HOJE vale de abre até
// 24h e a fatia de ONTEM (o mesmo dia configurado, olhado do dia seguinte)
// vale de 0h até fecha.
function dentroDaJanelaDoDia(dia, minutos, comoOntem) {
  if (!dia || dia.fechado || !dia.abre || !dia.fecha) return false
  const abre = paraMinutos(dia.abre)
  const fecha = paraMinutos(dia.fecha)
  if (fecha > abre) return !comoOntem && minutos >= abre && minutos < fecha
  if (comoOntem) return minutos < fecha
  return minutos >= abre
}

// Dentro do horário configurado, com a virada da meia-noite contando dois
// dias: o de hoje (que pode abrir à noite e fechar de madrugada) e o de
// ontem (cuja fatia da madrugada ainda vale agora).
export function dentroDoHorario(agora, funcionamento = FUNCIONAMENTO_PADRAO) {
  // Hora e dia da loja (F07, item 8), não os da máquina.
  const { horas, minutos: doRelogio, diaDaSemana } = partesNoFuso(agora)
  const minutos = horas * 60 + doRelogio
  const hoje = funcionamento[diaPorIndice(diaDaSemana).chave]
  const ontem = funcionamento[diaPorIndice((diaDaSemana + 6) % 7).chave]
  return dentroDaJanelaDoDia(hoje, minutos, false) || dentroDaJanelaDoDia(ontem, minutos, true)
}

export const foraDoHorario = (agora, funcionamento = FUNCIONAMENTO_PADRAO) =>
  !dentroDoHorario(agora, funcionamento)

// Estado da loja "vale por cima do horário" (pedido do dono): `lojaAberta`
// chega em três valores. `null`/`undefined` é o padrão, ninguém tocou o
// controle do topo, o horário sozinho decide. `true`/`false` é ela decidindo
// na mão — fecha no meio do dia se a massa acabou, abre fora do horário se
// quiser — e essa decisão nunca volta a seguir o relógio sozinha (mesma
// regra de `dominio/automatico.js`: "a volta é decisão dela, nunca do
// relógio").
export function estaAberta(agora, { funcionamento = FUNCIONAMENTO_PADRAO, lojaAberta = null } = {}) {
  if (lojaAberta === true) return true
  if (lojaAberta === false) return false
  return dentroDoHorario(agora, funcionamento)
}

// Minutos com a loja aberta entre `inicioMs` e `fimMs` (#1427: o SLA de
// primeira resposta pausa fora do expediente). Anda de fronteira em fronteira
// (abre, fecha e meia-noite, no relógio da loja), porque só nelas o estado
// muda: uma noite fechada é um passo, não 600. `limite` para a conta assim
// que passa dele (quem pergunta só quer saber se estourou).
//
// Controle manual: aberta na mão conta o relógio inteiro; fechada na mão
// conta zero, porque o histórico do controle não existe e a loja fechada é,
// por definição, fora do expediente.
export function minutosAbertos(inicioMs, fimMs, { funcionamento = FUNCIONAMENTO_PADRAO, lojaAberta = null } = {}, limite = Infinity) {
  if (!(fimMs > inicioMs)) return 0
  if (lojaAberta === true) return (fimMs - inicioMs) / 60000
  if (lojaAberta === false) return 0
  const fronteiras = [...new Set(
    Object.values(funcionamento)
      .filter((dia) => dia && !dia.fechado && dia.abre && dia.fecha)
      .flatMap((dia) => [paraMinutos(dia.abre), paraMinutos(dia.fecha)]),
  )].sort((a, b) => a - b)
  if (fronteiras.length === 0) return 0
  fronteiras.push(24 * 60)

  let instante = inicioMs
  let total = 0
  // Teto de passos: oito dias de fronteiras sobram para qualquer SLA (até 240 min).
  for (let passos = 0; instante < fimMs && total <= limite && passos < 8 * fronteiras.length; passos += 1) {
    const { horas, minutos } = partesNoFuso(instante)
    const minutoDoDia = horas * 60 + minutos
    const proxima = fronteiras.find((f) => f > minutoDoDia)
    const ate = Math.min(fimMs, instante - (instante % 60000) + (proxima - minutoDoDia) * 60000)
    if (dentroDoHorario(instante, funcionamento)) total += (ate - instante) / 60000
    instante = ate
  }
  return total
}

const inicioDoDia = (ms) => {
  const data = new Date(ms)
  data.setHours(0, 0, 0, 0)
  return data.getTime()
}

// Próxima abertura pelo HORÁRIO CONFIGURADO (ignora o estado manual da loja
// de propósito: é o que a regra "Fora do horário" promete ao cliente, e o
// dono pode abrir ou fechar na mão antes disso, mas o texto fala do
// horário). Varre até 8 dias; `null` quando a semana inteira está fechada.
export function proximaAbertura(agora, funcionamento = FUNCIONAMENTO_PADRAO) {
  for (let offset = 0; offset <= 7; offset += 1) {
    const data = new Date(agora)
    data.setDate(data.getDate() + offset)
    const dia = funcionamento[diaPorIndice(data.getDay()).chave]
    if (!dia || dia.fechado || !dia.abre) continue
    const [hora, minuto] = dia.abre.split(':').map(Number)
    data.setHours(hora, minuto, 0, 0)
    if (data.getTime() > agora) return data.getTime()
  }
  return null
}

// Texto curto para a regra "Fora do horário" (dono, 24/09: "a de fora do
// horário diz quando abre"): "hoje às 20h", "amanhã às 8h" ou o dia por
// extenso, sempre em pt-BR.
export function descreverProximaAbertura(agora, funcionamento = FUNCIONAMENTO_PADRAO) {
  const abertura = proximaAbertura(agora, funcionamento)
  if (abertura == null) return 'ainda não temos o próximo horário configurado'
  const diffDias = Math.round((inicioDoDia(abertura) - inicioDoDia(agora)) / 86400000)
  const hora = horaCurta(abertura)
  if (diffDias === 0) return `hoje às ${hora}`
  if (diffDias === 1) return `amanhã às ${hora}`
  return `${diaPorIndice(new Date(abertura).getDay()).rotulo} às ${hora}`
}
