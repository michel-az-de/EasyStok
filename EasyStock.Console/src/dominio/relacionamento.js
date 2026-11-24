// #3254 (S2): a relação do cliente no grid de Cadastros. O estágio vem pronto da API (#3244);
// aqui só se traduz o código, nunca se recalcula por compras. A prova carrega o domínio pelo runner, que resolve o import sem extensão (./recompra).
import { diasDeCalendarioEntre } from './formato.js'
import { TAG_RECOMPRA_CONVIDADA } from './recompra'

const ESTAGIOS = ['sem_pedido', 'primeira_compra', 'recorrente', 'fiel']
const ROTULOS = { sem_pedido: 'Sem pedido', primeira_compra: 'Primeira compra', recorrente: 'Recorrente', fiel: 'Fiel' }
// A API normaliza tag (minúscula, sem acento, _ no lugar de - e espaço): compara-se na forma normalizada, da fonte única.
const TAGS_ESCONDIDAS = new Set([TAG_RECOMPRA_CONVIDADA.replace(/-/g, '_')])

export const estagioDoCliente = (codigo) => ROTULOS[codigo] ?? null

// Posição na barra de 4 passos (0 a 3); código ausente ou desconhecido não tem posição.
export const posicaoDoEstagio = (codigo) => { const i = ESTAGIOS.indexOf(codigo); return i < 0 ? null : i }

export function percentualDoGasto(gasto, totalDaCarteira) {
  const g = Number(gasto), t = Number(totalDaCarteira)
  if (!Number.isFinite(t) || t <= 0 || !Number.isFinite(g)) return 0
  return Math.min(100, Math.max(0, g / t * 100))
}

export const tagsVisiveis = (tags) => (Array.isArray(tags) ? tags : []).filter((t) => !TAGS_ESCONDIDAS.has(t))

// Dias de calendário (fuso da loja) entre a data e hoje; `agora` em ms é injetável. Sem data válida: null.
export function diasDesde(iso, agora = Date.now()) {
  const ms = iso ? new Date(iso).getTime() : NaN
  return Number.isFinite(ms) ? Math.max(0, diasDeCalendarioEntre(ms, agora)) : null
}
