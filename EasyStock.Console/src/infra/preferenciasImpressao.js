import { contagemDoDia } from '../dominio/impressaoAutomatica'
import { normalizarPreferenciasImpressao } from '../dominio/impressos'

// Preferências de impressão DESTE aparelho (#1632): o tablet da cozinha tem a bobina, o
// computador do balcão tem a A4. Por isso moram no navegador e não na conta. Sem storage
// (aba anônima, navegador que nega), a escolha vale até fechar a aba.
export const CHAVE_PREFERENCIAS_IMPRESSAO = 'easystok.impressao.v1'
const EVENTO_PREFERENCIAS_IMPRESSAO = 'easystok:preferencias-impressao'
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
  let guardou
  try {
    (armazenamento ?? globalThis.localStorage).setItem(CHAVE_PREFERENCIAS_IMPRESSAO, JSON.stringify(daVisita))
    guardou = true
  } catch { guardou = false }
  // #2096: a impressão automática desta aba fica sabendo na hora; as outras abas, pelo `storage`.
  try { globalThis.dispatchEvent?.(new Event(EVENTO_PREFERENCIAS_IMPRESSAO)) } catch { /* sem janela (prova em Node) */ }
  return guardou
}

// Avisa quando as preferências mudam nesta aba ou em outra do mesmo aparelho. Devolve quem desliga.
export function ouvirPreferenciasImpressao(aoMudar) {
  const naOutraAba = (evento) => { if (evento.key === null || evento.key === CHAVE_PREFERENCIAS_IMPRESSAO) aoMudar() }
  globalThis.addEventListener?.(EVENTO_PREFERENCIAS_IMPRESSAO, aoMudar)
  globalThis.addEventListener?.('storage', naOutraAba)
  return () => {
    globalThis.removeEventListener?.(EVENTO_PREFERENCIAS_IMPRESSAO, aoMudar)
    globalThis.removeEventListener?.('storage', naOutraAba)
  }
}

// #2096: quantos canhotos saíram sozinhos hoje neste aparelho. Sem storage, conta só a visita.
export const CHAVE_CONTAGEM_AUTOMATICA = 'easystok.impressao.automatica.v1'
let contagemDaVisita = null
export function lerContagemAutomatica(hoje, armazenamento) {
  try {
    const guardado = (armazenamento ?? globalThis.localStorage).getItem(CHAVE_CONTAGEM_AUTOMATICA)
    return contagemDoDia(guardado === null ? contagemDaVisita : JSON.parse(guardado), hoje)
  } catch { return contagemDoDia(contagemDaVisita, hoje) }
}
export function gravarContagemAutomatica(contagem, armazenamento) {
  contagemDaVisita = contagem
  try { (armazenamento ?? globalThis.localStorage).setItem(CHAVE_CONTAGEM_AUTOMATICA, JSON.stringify(contagem)) } catch { /* vale a visita */ }
}
