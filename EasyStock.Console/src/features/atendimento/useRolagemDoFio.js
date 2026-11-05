import { useCallback, useEffect, useRef, useState } from 'react'
import {
  deveRolarAoFicarVisivel, deveRolarParaOFim, novasForaDaVista, pertoDoFim, retratoDoFio,
} from '../../dominio/rolagemDoFio'

// Quem rola é a área em volta do fio (PainelAtendimento), não a thread: sobe até achar.
function areaRolavel(no) {
  for (let el = no?.parentElement; el; el = el.parentElement) {
    if (/auto|scroll/.test(getComputedStyle(el).overflowY)) return el
  }
  return null
}

// #1587: o fio só vai sozinho para o fim ao trocar de conversa, quando a mensagem nova é da casa
// ou quando quem lê já estava no fim (`dominio/rolagemDoFio.js`). "Estava no fim" é medido a cada
// rolagem, antes de a mensagem nova aumentar o fio; depois dela a conta já sairia errada.
//
// #2232 (A5): no celular o painel fica `hidden` até a aba "Conversa" abrir, e painel escondido não rola:
// a conversa aparecia no topo. Ao ficar visível, o fio vai para a última mensagem. E fala nova que chega
// com quem lê mais acima devolve `novas`, que a Thread mostra como botão "Novas mensagens".
export function useRolagemDoFio(fimRef, {
  mensagens, chaveRolagem, digitando, totalDeNotas, visivel = true,
}) {
  const noFim = useRef(true)
  const anterior = useRef(null)
  const eraVisivel = useRef(visivel)
  const [novas, setNovas] = useState(0)

  useEffect(() => {
    const area = areaRolavel(fimRef.current)
    if (!area) return undefined
    const medir = () => {
      noFim.current = pertoDoFim({ alturaTotal: area.scrollHeight, topo: area.scrollTop, alturaVisivel: area.clientHeight })
      if (noFim.current) setNovas((n) => (n === 0 ? n : 0))
    }
    area.addEventListener('scroll', medir, { passive: true })
    return () => area.removeEventListener('scroll', medir)
  }, [fimRef])

  const { total, ultimaId, ultimaDaCasa } = retratoDoFio(chaveRolagem, mensagens)
  useEffect(() => {
    const depois = { conversaId: chaveRolagem, total, ultimaId, ultimaDaCasa }
    if (deveRolarParaOFim(anterior.current, depois, noFim.current)) {
      fimRef.current?.scrollIntoView({ block: 'end' })
      noFim.current = true
      setNovas((n) => (n === 0 ? n : 0))
    } else if (visivel) {
      const fora = novasForaDaVista(anterior.current, depois, false)
      if (fora > 0) setNovas((n) => n + fora)
    }
    anterior.current = depois
  }, [fimRef, chaveRolagem, total, ultimaId, ultimaDaCasa, digitando, totalDeNotas, visivel])

  useEffect(() => {
    if (deveRolarAoFicarVisivel(eraVisivel.current, visivel)) {
      fimRef.current?.scrollIntoView({ block: 'end' })
      noFim.current = true
      setNovas((n) => (n === 0 ? n : 0))
    }
    eraVisivel.current = visivel
  }, [fimRef, visivel])

  const irParaNovas = useCallback(() => {
    fimRef.current?.scrollIntoView({ block: 'end' })
    noFim.current = true
    setNovas(0)
  }, [fimRef])

  return { novas, irParaNovas }
}
