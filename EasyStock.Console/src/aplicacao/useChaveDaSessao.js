import { useSyncExternalStore } from 'react'
import { EVENTO_SESSAO_ALTERADA, lerSessao } from '../infra/api/sessao'

// #2285: a chave de armazenamento de quem está na sessão agora (empresa e usuário), relida quando a sessão
// troca na mesma aba. `montarChave(sessao)` devolve uma string estável ou null; a tela não toca a sessão.
const assinar = (ouvinte) => {
  window.addEventListener(EVENTO_SESSAO_ALTERADA, ouvinte)
  return () => window.removeEventListener(EVENTO_SESSAO_ALTERADA, ouvinte)
}

export function useChaveDaSessao(montarChave) {
  return useSyncExternalStore(assinar, () => montarChave(lerSessao()), () => null)
}
