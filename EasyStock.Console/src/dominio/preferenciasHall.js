import { MODULOS } from './modulos'

const idsDoCatalogo = MODULOS.map((modulo) => modulo.id)
const idsValidos = (valor) => Array.isArray(valor)
  ? [...new Set(valor.filter((id) => idsDoCatalogo.includes(id)))] : []

export function normalizarPreferenciasHall(valor) {
  const ordem = idsValidos(valor?.ordem)
  return {
    versao: 1,
    ordem: [...ordem, ...idsDoCatalogo.filter((id) => !ordem.includes(id))],
    favoritos: idsValidos(valor?.favoritos),
  }
}

export function ordenarModulosDoHall(modulos, preferencia) {
  const { ordem } = normalizarPreferenciasHall(preferencia)
  // A entrada já contém só os módulos permitidos. Preferência nunca dá acesso.
  return [...modulos].sort((a, b) => ordem.indexOf(a.id) - ordem.indexOf(b.id))
}

export function favoritosDoHall(modulos, preferencia) {
  const { favoritos } = normalizarPreferenciasHall(preferencia)
  return ordenarModulosDoHall(modulos, preferencia)
    .filter((modulo) => modulo.disponivel && favoritos.includes(modulo.id))
}

export function alternarFavoritoDoHall(preferencia, id) {
  const atual = normalizarPreferenciasHall(preferencia)
  if (!idsDoCatalogo.includes(id)) return atual
  return { ...atual, favoritos: atual.favoritos.includes(id)
    ? atual.favoritos.filter((favorito) => favorito !== id) : [...atual.favoritos, id] }
}

export function moverModuloDoHall(preferencia, id, destino) {
  const atual = normalizarPreferenciasHall(preferencia)
  const origem = atual.ordem.indexOf(id)
  const chegada = atual.ordem.indexOf(destino)
  if (origem < 0 || chegada < 0 || origem === chegada) return atual
  const ordem = [...atual.ordem]
  ordem.splice(origem, 1)
  ordem.splice(chegada, 0, id)
  return { ...atual, ordem }
}

export function favoritosPrimeiroNoHall(preferencia) {
  const atual = normalizarPreferenciasHall(preferencia)
  return { ...atual, ordem: [
    ...idsDoCatalogo.filter((id) => atual.favoritos.includes(id)),
    ...idsDoCatalogo.filter((id) => !atual.favoritos.includes(id)),
  ] }
}

export function restaurarOrdemDoHall(preferencia) {
  return { ...normalizarPreferenciasHall(preferencia), ordem: [...idsDoCatalogo] }
}
