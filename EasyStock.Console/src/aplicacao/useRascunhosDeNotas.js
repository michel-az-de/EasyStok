import { useMemo, useSyncExternalStore } from 'react'
import { EVENTO_SESSAO_ALTERADA, identidadeDaSessao, lerSessao } from '../infra/api/sessao'
import { rascunhosDeNotasDaVisita } from './rascunhosDeNotas'

// A visita também termina no Hall, onde o editor não está montado. Renovar a mesma
// identidade conserva texto; sair ou trocar de conta cria outra memória privada.
const aoTrocarSessao = () => rascunhosDeNotasDaVisita(identidadeDaSessao(lerSessao()))
globalThis.window?.addEventListener(EVENTO_SESSAO_ALTERADA, aoTrocarSessao)
globalThis.window?.addEventListener('storage', aoTrocarSessao)
if (import.meta.hot) import.meta.hot.dispose(() => {
  window.removeEventListener(EVENTO_SESSAO_ALTERADA, aoTrocarSessao)
  window.removeEventListener('storage', aoTrocarSessao)
})

export function useRascunhosDeNotas(sessao) {
  const identidade = identidadeDaSessao(sessao)
  const memoria = useMemo(() => rascunhosDeNotasDaVisita(identidade), [identidade])
  const estado = useSyncExternalStore(memoria.assinar, memoria.ler)
  return { ...estado, memoria }
}
