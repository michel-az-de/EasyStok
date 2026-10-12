import { normalizarPreferenciasHall } from '../dominio/preferenciasHall'

const preferenciasDaVisita = new Map()

export function chaveDasPreferenciasHall(sessao) {
  const empresa = sessao?.empresa?.id
  const usuario = sessao?.usuario?.id
  if (typeof empresa !== 'string' || !empresa || typeof usuario !== 'string' || !usuario) return null
  return `easystok.hall.v1:${encodeURIComponent(empresa)}:${encodeURIComponent(usuario)}`
}

export function lerPreferenciasHall(chave, armazenamento) {
  if (!chave) return normalizarPreferenciasHall(null)
  try {
    const guardado = (armazenamento ?? globalThis.localStorage).getItem(chave)
    if (guardado === null) return preferenciasDaVisita.get(chave) ?? normalizarPreferenciasHall(null)
    const preferencia = normalizarPreferenciasHall(JSON.parse(guardado))
    preferenciasDaVisita.set(chave, preferencia)
    return preferencia
  } catch { return preferenciasDaVisita.get(chave) ?? normalizarPreferenciasHall(null) }
}

export function gravarPreferenciasHall(chave, preferencia, armazenamento) {
  if (!chave) return false
  const proxima = normalizarPreferenciasHall(preferencia)
  preferenciasDaVisita.set(chave, proxima)
  try {
    (armazenamento ?? globalThis.localStorage).setItem(chave, JSON.stringify(proxima))
    return true
  } catch { return false }
}
