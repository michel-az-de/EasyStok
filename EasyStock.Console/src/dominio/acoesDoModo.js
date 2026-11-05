// #2232 (A3, A4): o que se pode fazer com "quem responde" a conversa. Uma conta só para a faixa (que
// mostra o Assumir) e para o menu ⋮ do cabeçalho (que guarda o resto), em vez de dois menus.
import { MODOS } from './automatico.js'
import { resumoDoModo } from './resumoDoModo.js'
import { automaticoDesligadoNaConversa } from './motivoDeEscalada.js'

export function acoesDoModo(conversa, pausado = false, bloqueada = false) {
  const resumo = resumoDoModo(conversa, pausado, bloqueada)
  const encerrada = resumo.chave === MODOS.ENCERRADO
  const semAutomatico = automaticoDesligadoNaConversa(conversa)
  const ativa = !encerrada && !bloqueada
  return {
    // Assumir: conversa que ainda está no automático ou que acabou de ser devolvida.
    podeAssumir: ativa && (resumo.chave === MODOS.LIGADO || resumo.chave === MODOS.COM_VOCE),
    // Devolver: só onde o automático está parado, e se a loja tem automático para quem devolver.
    podeDevolver: ativa && (resumo.chave === MODOS.PAUSADO || resumo.chave === MODOS.COM_VOCE) && !semAutomatico,
    semAutomatico,
  }
}
