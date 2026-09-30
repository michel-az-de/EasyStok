import { chamarApi } from './cliente'
import { DIAS_DA_SEMANA } from '../../dominio/funcionamento'

// Expediente da loja (S40): `api/atendimento/expediente`. Horário por dia, mensagens de
// fora do horário e loja fechada, e o controle manual que vence o relógio.
const BASE = '/api/atendimento/expediente'

export const obterExpediente = () => chamarApi(BASE)

// Horários omitidos mantêm os atuais na API; por isso as mensagens sozinhas não mexem na semana.
export const atualizarExpediente = ({ horarios, mensagemForaDoHorario, mensagemLojaFechada }) =>
  chamarApi(BASE, { metodo: 'PUT', corpo: { horarios, mensagemForaDoHorario, mensagemLojaFechada } })

export const definirControleExpediente = (controle) =>
  chamarApi(`${BASE}/controle`, { metodo: 'POST', corpo: { controle } })

// O console guarda o controle como o protótipo: `null` segue o horário, `true`/`false` é
// a dona decidindo na mão (dominio/funcionamento.js). A API usa ControleManualLoja.
const LOJA_ABERTA_DO_CONTROLE = { Automatico: null, ForcarAberta: true, ForcarFechada: false }
export const CONTROLE = { AUTOMATICO: 'Automatico', ABRIR: 'ForcarAberta', FECHAR: 'ForcarFechada' }

// TimeOnly sai da API como "08:00:00"; o <input type="time"> e a API na entrada usam "HH:mm".
const horaCurta = (valor) => (valor ? valor.slice(0, 5) : null)

// Dia sem turno na API é dia fechado no console.
export function expedienteDaApi(e) {
  const porDia = new Map((e.horarios ?? []).map((h) => [h.diaDaSemana, h]))
  const funcionamento = Object.fromEntries(DIAS_DA_SEMANA.map((dia) => {
    const turno = porDia.get(dia.indice)
    return [dia.chave, turno
      ? { abre: horaCurta(turno.abre), fecha: horaCurta(turno.fecha), fechado: false }
      : { abre: null, fecha: null, fechado: true }]
  }))
  return {
    funcionamento,
    lojaAberta: LOJA_ABERTA_DO_CONTROLE[e.controleManual] ?? null,
    mensagemForaDoHorario: e.mensagemForaDoHorario ?? '',
    mensagemLojaFechada: e.mensagemLojaFechada ?? '',
  }
}

// `null` quando algum dia aberto ainda está sem hora: a dona está no meio da edição.
export function horariosParaApi(funcionamento) {
  const horarios = []
  for (const dia of DIAS_DA_SEMANA) {
    const valor = funcionamento[dia.chave]
    if (!valor || valor.fechado) continue
    if (!valor.abre || !valor.fecha) return null
    horarios.push({ diaDaSemana: dia.indice, abre: valor.abre, fecha: valor.fecha })
  }
  return horarios
}
