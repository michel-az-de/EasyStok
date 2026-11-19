// #2853: regras puras do painel do Assistente (Gestão › Atendimento). O dado vem de
// GET api/atendimento/assistente/{uso,fluxo,historico}; nada aqui fala com a rede.

import { dolar } from './formato'

export const PERCENTUAL_ALERTA = 80

// Limites que espelham o servidor (RoteiroAtendimento): passar deles trava o Salvar.
export const ROTEIRO_ETAPAS_MAXIMO = 20
export const ROTEIRO_NOME_MAXIMO = 40
export const ROTEIRO_INSTRUCAO_MAXIMO = 600

const numero = new Intl.NumberFormat('pt-BR')
export const formatarTokens = (valor) => numero.format(Math.max(0, Math.round(Number(valor) || 0)))

// Tokens em milhar ou milhão, para a barra e os cartões ("1,2 mi").
export function tokensCurtos(valor) {
  const n = Math.max(0, Number(valor) || 0)
  if (n >= 1_000_000) return `${(n / 1_000_000).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} mi`
  if (n >= 1_000) return `${(n / 1_000).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} mil`
  return numero.format(n)
}

// A frase e o tom da barra de consumo. Sem cota a barra não existe: só o gasto.
export function resumoDoUso(uso) {
  if (!uso) return null
  const total = Number(uso.totalTokens) || 0
  const cota = uso.cotaTokensMes ? Number(uso.cotaTokensMes) : null
  if (!cota) {
    return { comCota: false, total, cota: null, restante: null, percentual: null, tom: 'neutro',
      titulo: `${tokensCurtos(total)} tokens usados no mês`, detalhe: 'Sem cota mensal: o Assistente não para por consumo.' }
  }
  const percentual = uso.percentual ?? Math.floor((total * 100) / cota)
  const restante = Math.max(0, cota - total)
  const esgotada = total >= cota
  const tom = esgotada ? 'perigo' : percentual >= PERCENTUAL_ALERTA ? 'aviso' : 'ok'
  const detalhe = esgotada
    ? 'Cota esgotada: até virar o mês, toda mensagem vai para a equipe.'
    : percentual >= PERCENTUAL_ALERTA
      ? `Passou de ${PERCENTUAL_ALERTA}%. Ao chegar em 100%, o Assistente passa as conversas para a equipe.`
      : `Restam ${tokensCurtos(restante)} tokens neste mês.`
  return { comCota: true, total, cota, restante, percentual, tom,
    titulo: `${tokensCurtos(total)} de ${tokensCurtos(cota)} tokens (${percentual}%)`, detalhe }
}

// Uma linha só para a aba Uso (#2855): o que resta, os turnos e o custo.
export function linhaDoUso(uso) {
  const resumo = resumoDoUso(uso)
  const custo = custoDoUso(uso)
  const partes = []
  if (resumo.comCota) {
    if (resumo.restante <= 0) partes.push('Cota esgotada')
    else partes.push(`${resumo.tom === 'aviso' ? `Passou de ${PERCENTUAL_ALERTA}%, r` : 'R'}estam ${tokensCurtos(resumo.restante)}`)
  }
  partes.push(`${formatarTokens(uso.turnos)} turnos`)
  partes.push(custo.valor ? `custo estimado ${custo.valor}` : 'custo sem preço configurado')
  return partes.join(' · ')
}

// Custo: a API soma só turnos com preço configurado. Sem nenhum preço, não inventa R$ 0,00.
export function custoDoUso(uso) {
  if (!uso) return null
  const semPreco = Number(uso.turnosSemPreco) || 0
  const turnos = Number(uso.turnos) || 0
  if (turnos > 0 && semPreco === turnos) return { valor: null, nota: 'Sem preço configurado para o modelo: o custo não é calculado.' }
  const valor = Number(uso.custoEstimado) || 0
  const nota = semPreco > 0 ? `${semPreco} de ${turnos} turnos ficaram fora da conta por falta de preço.` : null
  return { valor: dolar(valor), nota }
}

// Campo da cota em milhares de tokens; vazio ou 0 tira a cota.
export function cotaDoCampo(texto) {
  const limpo = String(texto ?? '').replace(/\D/g, '')
  if (!limpo) return { valor: 0, erro: null }
  const mil = Number(limpo)
  if (!Number.isSafeInteger(mil * 1000)) return { valor: null, erro: 'Valor alto demais.' }
  return { valor: mil * 1000, erro: null }
}
export const cotaParaCampo = (cota) => (cota ? String(Math.round(Number(cota) / 1000)) : '')

export function moverEtapa(roteiro, indice, passo) {
  const destino = indice + passo
  if (destino < 0 || destino >= roteiro.length) return roteiro
  const copia = [...roteiro]
  const [etapa] = copia.splice(indice, 1)
  copia.splice(destino, 0, etapa)
  return copia
}

export function erroDoRoteiro(roteiro) {
  if (roteiro.length > ROTEIRO_ETAPAS_MAXIMO) return `O roteiro aceita até ${ROTEIRO_ETAPAS_MAXIMO} etapas.`
  for (const etapa of roteiro) {
    if (!etapa.nome?.trim() || !etapa.instrucao?.trim()) return 'Toda etapa precisa de nome e instrução.'
    if (etapa.nome.trim().length > ROTEIRO_NOME_MAXIMO || etapa.instrucao.trim().length > ROTEIRO_INSTRUCAO_MAXIMO) {
      return `Cada etapa tem nome de até ${ROTEIRO_NOME_MAXIMO} e instrução de até ${ROTEIRO_INSTRUCAO_MAXIMO} caracteres.`
    }
  }
  return null
}

export const mesmoRoteiro = (a, b) => JSON.stringify(a ?? []) === JSON.stringify(b ?? [])

export const desligadasDoFluxo = (ferramentas = []) => ferramentas.filter((f) => !f.ligada).map((f) => f.nome).sort()

const DESFECHOS = Object.freeze({
  Respondeu: Object.freeze({ rotulo: 'Respondeu', tom: 'ok' }),
  Escalou: Object.freeze({ rotulo: 'Passou à equipe', tom: 'marca' }),
  Cota: Object.freeze({ rotulo: 'Cota esgotada', tom: 'aviso' }),
  Erro: Object.freeze({ rotulo: 'Erro', tom: 'perigo' }),
  Interrompido: Object.freeze({ rotulo: 'Interrompido', tom: 'neutro' }),
  SemIa: Object.freeze({ rotulo: 'Sem IA', tom: 'neutro' }),
})
export const desfechoDoTurno = (desfecho) => DESFECHOS[desfecho] ?? Object.freeze({ rotulo: String(desfecho ?? '?'), tom: 'neutro' })
