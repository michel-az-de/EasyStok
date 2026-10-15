import { normalizarPreferenciasImpressao } from '../dominio/impressos'

// Preferências de impressão DESTE aparelho (#1632): o tablet da cozinha tem a bobina, o
// computador do balcão tem a A4. Por isso moram no navegador e não na conta. Sem storage
// (aba anônima, navegador que nega), a escolha vale até fechar a aba.
export const CHAVE_PREFERENCIAS_IMPRESSAO = 'easystok.impressao.v1'
let daVisita = null

export function lerPreferenciasImpressao(armazenamento) {
  try {
    const guardado = (armazenamento ?? globalThis.localStorage).getItem(CHAVE_PREFERENCIAS_IMPRESSAO)
    if (guardado === null) return daVisita ?? normalizarPreferenciasImpressao(null)
    return normalizarPreferenciasImpressao(JSON.parse(guardado))
  } catch { return daVisita ?? normalizarPreferenciasImpressao(null) }
}

// Devolve se ficou gravado no aparelho; a escolha vale para a visita de qualquer jeito.
export function gravarPreferenciasImpressao(preferencias, armazenamento) {
  daVisita = normalizarPreferenciasImpressao(preferencias)
  try {
    (armazenamento ?? globalThis.localStorage).setItem(CHAVE_PREFERENCIAS_IMPRESSAO, JSON.stringify(daVisita))
    return true
  } catch { return false }
}
