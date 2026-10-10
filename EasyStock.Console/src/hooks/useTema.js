import { useEffect, useState } from 'react'

const CHAVE_TEMA = 'casa-da-baba:tema'
const EVENTO_TEMA = 'cdb:tema'

function lerTema() {
  return document.documentElement.dataset.theme
    || (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
}

export function trocarTema(valor) {
  const aplicar = () => {
    document.documentElement.dataset.theme = valor
    window.dispatchEvent(new Event(EVENTO_TEMA))
  }
  if (document.startViewTransition && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    document.startViewTransition(aplicar)
  } else aplicar()
  try {
    window.localStorage.setItem(CHAVE_TEMA, valor)
  } catch {
    // Sem persistência, a escolha vale enquanto a página estiver aberta.
  }
}

export function useTema() {
  const [tema, setTema] = useState(lerTema)
  useEffect(() => {
    const atualizar = () => setTema(lerTema())
    const sistema = window.matchMedia('(prefers-color-scheme: dark)')
    window.addEventListener(EVENTO_TEMA, atualizar)
    sistema.addEventListener('change', atualizar)
    return () => {
      window.removeEventListener(EVENTO_TEMA, atualizar)
      sistema.removeEventListener('change', atualizar)
    }
  }, [])
  return [tema, trocarTema]
}
