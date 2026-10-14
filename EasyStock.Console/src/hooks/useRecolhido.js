import { useCallback, useSyncExternalStore } from 'react'
import { CHAVE_RECOLHIDOS, alternarRecolhido, estaRecolhido, lerRecolhidos } from './recolhidos'

// Seção recolhida da Ficha, lembrada neste navegador (#1442). Um armazém só
// para todas as seções: cada uma chamando o localStorage por conta própria se
// pisaria na hora de gravar (mesmo cuidado de useLarguras). Leitura e escrita
// em try/catch: sem acesso (modo privado, cota), a escolha vale até o reload.

let mapa = null
const ouvintes = new Set()

function atual() {
  if (mapa === null) {
    try {
      mapa = lerRecolhidos(localStorage.getItem(CHAVE_RECOLHIDOS))
    } catch {
      mapa = {}
    }
  }
  return mapa
}

function assinar(ouvinte) {
  ouvintes.add(ouvinte)
  return () => ouvintes.delete(ouvinte)
}

function gravar(novo) {
  mapa = novo
  try {
    localStorage.setItem(CHAVE_RECOLHIDOS, JSON.stringify(mapa))
  } catch {
    // Sem memória: vale só até o reload.
  }
  ouvintes.forEach((ouvinte) => ouvinte())
}

function alternar(chave, padrao) {
  gravar(alternarRecolhido(atual(), chave, padrao))
}

// Abre uma seção de fora dela: abrir o cardápio rola a Ficha até a comanda,
// e comanda recolhida não recebe o item arrastado nem aparece na rolagem.
export function abrirSecao(chave) {
  if (atual()[chave] !== false) gravar({ ...atual(), [chave]: false })
}

// `chave` vazia: seção que não recolhe, sempre aberta. `padrao` (#1697): como a seção nasce
// enquanto ninguém escolheu.
export function useRecolhido(chave, padrao = false) {
  const recolhidos = useSyncExternalStore(assinar, atual, atual)
  const trocar = useCallback(() => { if (chave) alternar(chave, padrao) }, [chave, padrao])
  return [Boolean(chave) && estaRecolhido(recolhidos, chave, padrao), trocar]
}
