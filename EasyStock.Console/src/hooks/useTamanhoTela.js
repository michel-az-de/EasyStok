import { useEffect, useState } from 'react'

export const CELULAR = 'celular'
export const TABLET = 'tablet'
export const DESKTOP = 'desktop'

const classificar = (largura) => {
  if (largura < 760) return CELULAR
  if (largura < 1180) return TABLET
  return DESKTOP
}

// Um único ponto decide o porte da tela. Nenhum componente mede largura sozinho.
export function useTamanhoTela() {
  const [tamanho, setTamanho] = useState(() =>
    classificar(typeof window === 'undefined' ? 1440 : window.innerWidth))

  useEffect(() => {
    const aoRedimensionar = () => setTamanho(classificar(window.innerWidth))
    window.addEventListener('resize', aoRedimensionar)
    return () => window.removeEventListener('resize', aoRedimensionar)
  }, [])

  return tamanho
}

// O topo tem um limiar próprio, mais alto que o do layout: abaixo de 1024 px os
// cinco controles não cabem numa linha e os secundários vão para o menu Mais.
// A medida continua morando aqui, nunca dentro do componente.
export const LARGURA_TOPO_INTEIRO = 1024

export function useTopoCompacto() {
  const [compacto, setCompacto] = useState(() =>
    (typeof window === 'undefined' ? false : window.innerWidth < LARGURA_TOPO_INTEIRO))

  useEffect(() => {
    const consulta = window.matchMedia(`(max-width: ${LARGURA_TOPO_INTEIRO - 1}px)`)
    const aoMudar = () => setCompacto(consulta.matches)
    aoMudar()
    consulta.addEventListener('change', aoMudar)
    return () => consulta.removeEventListener('change', aoMudar)
  }, [])

  return compacto
}
