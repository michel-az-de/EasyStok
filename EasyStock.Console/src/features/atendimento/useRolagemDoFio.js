import { useEffect, useRef } from 'react'
import { deveRolarParaOFim, pertoDoFim, retratoDoFio } from '../../dominio/rolagemDoFio'

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
export function useRolagemDoFio(fimRef, {
  mensagens, chaveRolagem, digitando, totalDeNotas,
}) {
  const noFim = useRef(true)
  const anterior = useRef(null)

  useEffect(() => {
    const area = areaRolavel(fimRef.current)
    if (!area) return undefined
    const medir = () => {
      noFim.current = pertoDoFim({ alturaTotal: area.scrollHeight, topo: area.scrollTop, alturaVisivel: area.clientHeight })
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
    }
    anterior.current = depois
  }, [fimRef, chaveRolagem, total, ultimaId, ultimaDaCasa, digitando, totalDeNotas])
}
