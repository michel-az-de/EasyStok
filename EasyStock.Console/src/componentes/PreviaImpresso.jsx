import { useEffect, useRef, useState } from 'react'
import { Botao } from './Botao'
import css from './ImprimirPedido.module.css'

const LARGURA_MM = { a4: 210, a5: 148, 'etiqueta-10x15': 100, 'cupom-80': 80, 'cupom-58': 58 }

export function PreviaImpresso({ html, modelo }) {
  const moldura = useRef(null)
  const [escala, setEscala] = useState(1)
  const [ajustar, setAjustar] = useState(true)
  const largura = (LARGURA_MM[modelo] ?? 210) * 96 / 25.4
  useEffect(() => {
    const observer = new ResizeObserver(([entrada]) => setEscala(Math.min(1, entrada.contentRect.width / largura)))
    observer.observe(moldura.current)
    return () => observer.disconnect()
  }, [largura])
  const zoom = ajustar ? escala : 1
  return <>
    <div className={css.zoom}>
      <Botao variante="texto" aria-pressed={ajustar} onClick={() => setAjustar(true)}>Ajustar à largura</Botao>
      <Botao variante="texto" aria-pressed={!ajustar} onClick={() => setAjustar(false)}>Tamanho real</Botao>
      <span>{Math.round(zoom * 100)}%</span>
    </div>
    <div ref={moldura} className={css.moldura}>
      <iframe title="Documento que será impresso" sandbox="allow-same-origin" srcDoc={html}
        className={css.folha} style={{ width: largura, height: 460 / zoom, zoom }} />
    </div>
  </>
}
