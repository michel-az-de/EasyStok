import { useSyncExternalStore } from 'react'

const obterHash = () => (typeof window === 'undefined' ? '' : window.location.hash)
const obterHashNoServidor = () => ''

function inscrever(notificar) {
  if (typeof window === 'undefined') return () => {}
  window.addEventListener('hashchange', notificar)
  return () => window.removeEventListener('hashchange', notificar)
}

// Hash da URL, ao vivo (issue #40, rodada 13): reage a `hashchange`, disparado
// tanto por navegação normal (voltar do navegador) quanto por
// `window.location.hash = ...` e por um `window.open` que o navegador decidiu
// reaproveitar a aba atual em vez de abrir uma nova. `useSyncExternalStore`
// porque o hash é estado de fora do React, não `useState` com leitura só no
// primeiro render.
export function useHash() {
  return useSyncExternalStore(inscrever, obterHash, obterHashNoServidor)
}
