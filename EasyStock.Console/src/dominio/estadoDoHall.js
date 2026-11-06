import { MODULOS, ORDEM_DOS_MODULOS } from './modulos'
import { FOCOS_DO_HALL, normalizarPreferenciasHall } from './preferenciasHall'
import { indicadoresDoHall, pendenciaDoModulo, precisaDeVoce } from './pulsoDoHall'

// #2232 (banca UX/UI, Início): regras puras do hall que a tela só desenha.

const idsDoCatalogo = MODULOS.map((modulo) => modulo.id)

// H10: Configurações não é uma área de trabalho do dia; sai da grade e vira link de rodapé.
export const MODULOS_NO_RODAPE = ['configuracoes']

// H8: todo cartão tem linha de estado. A leitura que falhou ou ainda não chegou fica sem linha
// (dizer "Tudo em dia" sem ter lido seria mentir); o módulo sem leitura própria fica neutro.
export function estadoDoCartao(id, fontes) {
  const pendencia = pendenciaDoModulo(id, fontes)
  if (pendencia) return pendencia
  // #2258 (P1): "Tudo em dia" é afirmação sobre uma leitura. Módulo medido sem leitura fica sem linha, e o
  // que não tem número a medir (Configurações, Campanhas em breve) não afirma nada.
  return null
}

// H10: a ordem de fábrica é a ordem de uso do foco escolhido (por padrão, "Cliente primeiro").
// A ordem do catálogo (número do módulo) só vale como "não mexi", e aí a tela usa a de uso.
export function ordemOriginalDoHall(foco = 'atendimento') {
  const modelo = FOCOS_DO_HALL.find((item) => item.id === foco) ?? FOCOS_DO_HALL[0]
  // #2258 (A2): o que o foco não cita segue a ordem única dos módulos (`ORDEM_DOS_MODULOS`).
  const posicao = (id) => (ORDEM_DOS_MODULOS.indexOf(id) < 0 ? ORDEM_DOS_MODULOS.length : ORDEM_DOS_MODULOS.indexOf(id))
  return [...modelo.ordem, ...idsDoCatalogo.filter((id) => !modelo.ordem.includes(id)).sort((a, b) => posicao(a) - posicao(b))]
}

export function comOrdemPorUso(preferencia) {
  const atual = normalizarPreferenciasHall(preferencia)
  const intocada = atual.ordem.every((id, indice) => id === idsDoCatalogo[indice])
  return intocada ? { ...atual, ordem: ordemOriginalDoHall(atual.foco) } : atual
}

// "Restaurar ordem original": volta à ordem de uso do foco e mantém os favoritos.
export function restaurarOrdemPorUso(preferencia) {
  const atual = normalizarPreferenciasHall(preferencia)
  return { ...atual, ordem: ordemOriginalDoHall(atual.foco) }
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

// #2258 (H1): o resumo é a SOMA dos números que o hall mostra nos cartões que pedem a dona (conversas
// que precisam dela, pedidos na fila, pedidos prontos para sair), e separa o que pede ação (conversas, "precisam de você") do que só está em andamento.
// Antes contava "conversas não encerradas", outra conta que não batia com nenhum cartão. Cartão que não
// está na tela não entra na soma; leitura que falhou ou não chegou tira o resumo: ele nunca chuta.
export const MODULOS_QUE_PEDEM_A_DONA = ['atendimento', 'cozinha', 'entregas']
export function resumoDoDia(fontes, ids = MODULOS_QUE_PEDEM_A_DONA) {
  const contados = MODULOS_QUE_PEDEM_A_DONA.filter((id) => ids.includes(id))
  const itens = indicadoresDoHall(fontes ?? {}, contados)
  const lidos = itens.every((item) => item.estado !== 'erro' && item.valor !== null)
  if (!contados.length || !lidos) return { total: null, parcial: false, texto: null }
  const total = itens.reduce((soma, item) => soma + item.valor, 0)
  const parcial = itens.some((item) => item.parcial)
  // #2258 (H1, banca): só conversa esperando resposta pede ação da dona; pedido na fila ou pronto para
  // sair está em andamento. O texto separa as duas contas em vez de chamar tudo de "precisam de você".
  const precisam = itens.filter((item) => item.id === 'atendimento').reduce((soma, item) => soma + item.valor, 0)
  const andamento = total - precisam
  const parteAcao = precisam || parcial ? precisaDeVoce(precisam, parcial ? '+' : '') : 'nada precisa de você'
  const parteAndamento = andamento ? ` · ${andamento} em andamento` : ''
  return { total, precisam, andamento, parcial, texto: `Hoje · ${parteAcao}${parteAndamento}` }
}

// #2256 (auditoria de UX, Onda 1): 6 módulos em 4 colunas deixavam 2 vagas vazias.
// Escolhe 3 ou 4 colunas, a que deixa menos vagas na última linha; no empate, e com 1 ou 2, fica 4.
export function colunasDaGrade(total) {
  const vagas = (colunas) => (colunas - (total % colunas)) % colunas
  return total >= 3 && vagas(3) < vagas(4) ? 3 : 4
}
