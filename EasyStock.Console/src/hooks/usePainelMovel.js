import { useCallback, useEffect, useRef, useState } from 'react'
import { chavePainelMovel, guardarPainelMovel, lerPainelMovel } from './estadoPainelMovel'

export function usePainelMovel(escopoId, painelId, { posicaoInicial } = {}) {
  const chave = chavePainelMovel(escopoId, painelId)
  const [estado, setEstado] = useState(() => {
    const salvo = lerPainelMovel(escopoId, painelId)
    const legado = salvo ? null : posicaoInicial?.()
    return { chave, ...(salvo ?? (legado ? { solto: true, ...legado } : { solto: false })) }
  })
  const [erro, setErro] = useState(null)
  const [foco, setFoco] = useState(0)
  const atual = useRef(estado)
  const abridor = useRef(null)
  useEffect(() => {
    if (atual.current.chave !== chave) {
      const proximo = { chave, ...(lerPainelMovel(escopoId, painelId) ?? { solto: false }) }
      atual.current = proximo
      abridor.current = null
      setEstado(proximo)
      setErro(null)
      setFoco(0)
    }
  }, [chave, escopoId, painelId])
  const guardar = useCallback((mudanca) => {
    const antes = atual.current.chave === chave ? atual.current : { chave, solto: false }
    const proximo = { ...antes, ...mudanca, chave }
    atual.current = proximo
    setEstado(proximo)
    setErro(guardarPainelMovel(escopoId, painelId, proximo) ? null : 'A posição não pôde ser guardada neste navegador.')
  }, [chave, escopoId, painelId])
  const soltar = useCallback((origem = {}) => {
    const alvo = origem?.currentTarget ?? globalThis.document?.activeElement
    if (alvo?.focus && alvo !== globalThis.document?.body && alvo !== globalThis.document?.documentElement) abridor.current = alvo
    const r = origem?.currentTarget?.getBoundingClientRect?.()
    const posicao = r ? { x: r.left, y: r.top } : Object.fromEntries(['x', 'y', 'largura', 'altura']
      .filter((campo) => Number.isFinite(origem?.[campo])).map((campo) => [campo, origem[campo]]))
    guardar({ ...posicao, solto: true })
    setFoco(n => n + 1)
  }, [guardar])
  const recolher = useCallback(() => guardar({ solto: false }), [guardar])
  const devolverFoco = useCallback(() => {
    const podeFocar = (no) => no?.isConnected && typeof no.focus === 'function' && !no.disabled
      && (!no.getClientRects || no.getClientRects().length > 0)
    const reserva = [...(globalThis.document?.querySelectorAll?.('[data-foco-reserva]') ?? [])]
      .find(no => no.getAttribute('data-foco-reserva') === chave && podeFocar(no))
    const destino = podeFocar(abridor.current) ? abridor.current : reserva
    if (!destino) return false
    destino.focus({ preventScroll: true })
    return true
  }, [chave])
  return {
    ...(estado.chave === chave ? estado : { chave, solto: false }),
    escopoId, painelId, erro, foco: estado.chave === chave ? foco : 0, guardar, soltar, recolher, devolverFoco,
  }
}
