import { MODULOS } from './modulos'
import { FOCOS_DO_HALL, normalizarPreferenciasHall } from './preferenciasHall'
import { indicadoresDoHall, pendenciaDoModulo } from './pulsoDoHall'

// #2232 (banca UX/UI, Início): regras puras do hall que a tela só desenha.

const idsDoCatalogo = MODULOS.map((modulo) => modulo.id)

// Módulos com leitura própria no hall: sem dado lido não há o que afirmar no cartão.
const MODULOS_MEDIDOS = ['atendimento', 'cozinha', 'entregas', 'financeiro', 'cardapio']

// H10: Configurações não é uma área de trabalho do dia; sai da grade e vira link de rodapé.
export const MODULOS_NO_RODAPE = ['configuracoes']

// H8: todo cartão tem linha de estado. A leitura que falhou ou ainda não chegou fica sem linha
// (dizer "Tudo em dia" sem ter lido seria mentir); o módulo sem leitura própria fica neutro.
export function estadoDoCartao(id, fontes) {
  const pendencia = pendenciaDoModulo(id, fontes)
  if (pendencia) return pendencia
  return MODULOS_MEDIDOS.includes(id) ? null : { texto: 'Tudo em dia', atencao: false }
}

// H10: a ordem de fábrica é a ordem de uso do foco escolhido (por padrão, "Cliente primeiro").
// A ordem do catálogo (número do módulo) só vale como "não mexi", e aí a tela usa a de uso.
export function ordemOriginalDoHall(foco = 'atendimento') {
  const modelo = FOCOS_DO_HALL.find((item) => item.id === foco) ?? FOCOS_DO_HALL[0]
  return [...modelo.ordem, ...idsDoCatalogo.filter((id) => !modelo.ordem.includes(id))]
}

export function comOrdemPorUso(preferencia) {
  const atual = normalizarPreferenciasHall(preferencia)
  const intocada = atual.ordem.every((id, indice) => id === idsDoCatalogo[indice])
  return intocada ? { ...atual, ordem: ordemOriginalDoHall(atual.foco) } : atual
}

export function favoritosPrimeiroPorUso(preferencia) {
  const atual = normalizarPreferenciasHall(preferencia)
  const base = ordemOriginalDoHall(atual.foco)
  return { ...atual, ordem: [...base.filter((id) => atual.favoritos.includes(id)), ...base.filter((id) => !atual.favoritos.includes(id))] }
}

// H6/H9: o que pede atenção vira estado do cartão, na cor de severidade e nunca na da marca.
export function severidadeDoIndicador(item) {
  if (item.estado === 'erro') return item.em ? 'alerta' : 'erro'
  return item.alerta ? 'alerta' : null
}

// H6: o alerta do caixa de outro dia traz a ação que resolve, no próprio cartão.
export const acaoDoIndicador = (item) => (item.id === 'financeiro' && item.alerta ? 'Fechar caixa' : null)

// M3: "Hoje · N em andamento · N na fila", com as mesmas leituras que alimentam os números do
// hall. Em andamento: conversa não encerrada. Na fila: pedido esperando ou em preparo (a fila da
// Cozinha). Leitura que falhou ou não chegou fica de fora: o resumo nunca chuta.
const lida = (fonte) => (fonte?.estado === 'erro' ? undefined : fonte)
export function resumoDoDia(fontes) {
  const conversas = lida(fontes?.atendimento)?.dados
  const pedidos = lida(fontes?.pedidos)
  const emAndamento = conversas ? conversas.itens.filter((conversa) => conversa.situacao !== 'Encerrada').length : null
  const naFila = pedidos?.dados ? indicadoresDoHall({ pedidos }, ['cozinha'])[0].valor : null
  const partes = [
    emAndamento !== null && `${emAndamento}${conversas.parcial ? '+' : ''} em andamento`,
    naFila !== null && `${naFila}${pedidos.dados.parcial ? '+' : ''} na fila`,
  ].filter(Boolean)
  return { emAndamento, naFila, texto: partes.length ? `Hoje · ${partes.join(' · ')}` : null }
}
