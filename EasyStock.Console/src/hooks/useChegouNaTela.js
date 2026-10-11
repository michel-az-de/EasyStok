import { useEffect, useState } from 'react'

// O ancestral que rola o elemento: a margem do observador só vale contra ele, não contra a janela.
function quemRola(elemento) {
  for (let pai = elemento.parentElement; pai; pai = pai.parentElement) {
    const rolagem = getComputedStyle(pai).overflowY
    if (rolagem === 'auto' || rolagem === 'scroll') return pai
  }
  return null
}

// Diz quando o elemento chegou perto da área visível, e não volta atrás (#1577): adia trabalho caro
// (baixar a mídia de um balão) até ele estar para aparecer. `margem`: quanto antes avisar, em
// alturas de quem rola. Navegador sem IntersectionObserver: vale como visível, o de sempre.
export function useChegouNaTela(ref, { margem = '100%' } = {}) {
  const [chegou, setChegou] = useState(() => typeof IntersectionObserver === 'undefined')

  useEffect(() => {
    const elemento = ref.current
    if (chegou || !elemento) return undefined
    const observador = new IntersectionObserver((entradas) => {
      if (entradas.some((entrada) => entrada.isIntersecting)) setChegou(true)
    }, { root: quemRola(elemento), rootMargin: `${margem} 0px` })
    observador.observe(elemento)
    return () => observador.disconnect()
  }, [ref, chegou, margem])

  return chegou
}
