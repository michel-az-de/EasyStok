import { useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { Icone } from '../../componentes/Icone'
import { guardarPosicaoPostit, lerPosicaoPostit, limitarPostit, recolherPostit } from '../../aplicacao/posicoesDosPostits'
import css from './NotasDoCliente.module.css'

const janelaAtual = () => ({ largura: window.innerWidth, altura: window.innerHeight })
const movimentoReduzido = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true

function PostitSolto({ nota, inicial, saindo, focar, aoGuardar, aoRecolher }) {
  const idAjuda = useId()
  const papel = useRef(null)
  const alca = useRef(null)
  const arraste = useRef(null)
  const atual = useRef(inicial)
  const inicioTeclado = useRef(inicial)
  const [posicao, setPosicao] = useState(inicial)
  const [arrastando, setArrastando] = useState(false)
  const tamanho = () => {
    const r = papel.current?.getBoundingClientRect()
    return { largura: r?.width ?? 280, altura: r?.height ?? 180 }
  }
  const mover = (p) => {
    const limitada = limitarPostit(p, tamanho(), janelaAtual())
    atual.current = limitada
    setPosicao(limitada)
    return limitada
  }
  useEffect(() => { if (focar) alca.current?.focus({ preventScroll: true }) }, [focar])

  useLayoutEffect(() => {
    const ajustar = () => {
      const r = papel.current.getBoundingClientRect()
      const p = limitarPostit(atual.current, { largura: r.width, altura: r.height }, janelaAtual())
      if (p.x !== atual.current.x || p.y !== atual.current.y) {
        atual.current = p; setPosicao(p); aoGuardar(p)
      }
    }
    ajustar()
    window.addEventListener('resize', ajustar)
    const observador = typeof ResizeObserver === 'function' ? new ResizeObserver(ajustar) : null
    observador?.observe(papel.current)
    return () => { window.removeEventListener('resize', ajustar); observador?.disconnect() }
  }, [aoGuardar])

  function iniciar(evento) {
    if (saindo || (evento.pointerType === 'mouse' && evento.button !== 0)) return
    evento.preventDefault()
    evento.currentTarget.focus()
    inicioTeclado.current = atual.current
    arraste.current = { id: evento.pointerId, x: evento.clientX, y: evento.clientY, antes: atual.current }
    evento.currentTarget.setPointerCapture(evento.pointerId)
    setArrastando(true)
  }
  function terminar(evento, cancelar = false) {
    const inicio = arraste.current
    if (!inicio || inicio.id !== evento.pointerId) return
    arraste.current = null
    const p = cancelar ? mover(inicio.antes) : atual.current
    aoGuardar(p)
    setArrastando(false)
    if (evento.currentTarget.hasPointerCapture(inicio.id)) evento.currentTarget.releasePointerCapture(inicio.id)
  }
  function aoTeclar(evento) {
    if (saindo) return
    const setas = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }
    if (evento.key === 'Escape') {
      evento.preventDefault(); evento.stopPropagation()
      const antes = arraste.current?.antes ?? inicioTeclado.current
      arraste.current = null; setArrastando(false); aoGuardar(mover(antes))
    } else if (setas[evento.key] && !evento.altKey && !evento.ctrlKey && !evento.metaKey) {
      evento.preventDefault(); evento.stopPropagation()
      const [x, y] = setas[evento.key], passo = evento.shiftKey ? 24 : 8
      aoGuardar(mover({ x: atual.current.x + x * passo, y: atual.current.y + y * passo }))
    }
  }
  return <aside ref={papel} className={`${css.solto} ${saindo ? css.saindo : ''}`} style={{ left: posicao.x, top: posicao.y }}
    data-postit-solto={nota.id} aria-label="Nota interna solta na tela">
    <header className={css.cabecalho}>
      <button ref={alca} type="button" className={css.mover} aria-label="Mover nota" aria-describedby={idAjuda}
        data-arrastando={arrastando || undefined} disabled={saindo}
        onFocus={() => { inicioTeclado.current = atual.current }} onKeyDown={aoTeclar}
        onPointerDown={iniciar} onPointerMove={(e) => { if (arraste.current?.id === e.pointerId) mover({ x: arraste.current.antes.x + e.clientX - arraste.current.x, y: arraste.current.antes.y + e.clientY - arraste.current.y }) }}
        onPointerUp={(e) => terminar(e)} onPointerCancel={(e) => terminar(e, true)} onLostPointerCapture={(e) => terminar(e, true)}>
        <Icone nome="grip-vertical" tamanho={18} /><span>Nota{nota.autor ? ` · ${nota.autor}` : ''}</span>
      </button>
      <button type="button" className={css.controle} aria-label="Restaurar posição da nota" title="Restaurar posição" disabled={saindo}
        onClick={() => aoGuardar(mover({ x: janelaAtual().largura - tamanho().largura - 24, y: 112 }))}><Icone nome="undo-2" tamanho={18} /></button>
      <button type="button" className={css.controle} aria-label="Recolher nota à ficha" title="Recolher à ficha" disabled={saindo} onClick={aoRecolher}>
        <Icone nome="arrow-left" tamanho={18} />
      </button>
    </header>
    <span id={idAjuda} className="sr">Arraste pelo cabeçalho ou use as setas. Shift move mais rápido. Escape restaura a posição anterior.</span>
    <div className={css.corpo}><p className={css.texto}>{nota.texto}</p>{nota.em && <small>{nota.em}</small>}</div>
  </aside>
}

export function NotasDoCliente({ conversaId, notas = [] }) {
  const [soltas, setSoltas] = useState({})
  const [saindo, setSaindo] = useState({})
  const [ultimaSolta, setUltimaSolta] = useState(null)
  const [erro, setErro] = useState(null)
  const temporizadores = useRef(new Map())
  const coluna = useRef(null)
  const controles = useRef(new Map())
  useEffect(() => {
    setSoltas((antes) => {
      let proximo = antes
      for (const nota of notas) {
        if (!nota.id || Object.hasOwn(antes, nota.id)) continue
        const posicao = lerPosicaoPostit(conversaId, nota.id)
        if (posicao) { if (proximo === antes) proximo = { ...antes }; proximo[nota.id] = posicao }
      }
      return proximo
    })
  }, [conversaId, notas])
  useEffect(() => {
    const pendentes = temporizadores.current
    return () => { for (const timer of pendentes.values()) clearTimeout(timer) }
  }, [])

  function guardar(id, p) {
    if (!guardarPosicaoPostit(conversaId, id, p)) setErro('A posição não pôde ser guardada neste navegador.')
  }
  function soltar(nota, evento) {
    const r = evento.currentTarget.closest('article').getBoundingClientRect()
    const p = limitarPostit({ x: r.left, y: r.top }, { largura: 280, altura: 180 }, janelaAtual())
    guardar(nota.id, p)
    setUltimaSolta(nota.id)
    setSoltas((antes) => ({ ...antes, [nota.id]: p }))
  }
  function recolher(id) {
    if (temporizadores.current.has(id)) return
    setSaindo((antes) => ({ ...antes, [id]: true }))
    const timer = setTimeout(() => {
      temporizadores.current.delete(id)
      if (!recolherPostit(conversaId, id)) setErro('A posição não pôde ser atualizada neste navegador.')
      setSoltas((antes) => ({ ...antes, [id]: null }))
      setSaindo((antes) => ({ ...antes, [id]: false }))
      requestAnimationFrame(() => (controles.current.get(id) ?? coluna.current)?.focus())
    }, movimentoReduzido() ? 0 : 150)
    temporizadores.current.set(id, timer)
  }
  return <section ref={coluna} className={css.coluna} data-destino-nota={conversaId} aria-label="Notas internas do cliente" tabIndex={-1}>
    {notas.slice(0, 2).map((nota, indice) => <article key={nota.id ?? indice} className={css.nota}>
      <small>Nota{nota.autor ? ` · ${nota.autor}` : ''}</small>
      {soltas[nota.id] ? <p className={css.local}>Nota solta na tela.</p> : <p className={css.texto}>{nota.texto}</p>}
      {nota.id && <button type="button" ref={(no) => { if (no) controles.current.set(nota.id, no); else controles.current.delete(nota.id) }}
        className={css.soltar} onClick={(e) => soltas[nota.id] ? recolher(nota.id) : soltar(nota, e)}>
        {soltas[nota.id] ? 'Recolher à ficha' : 'Soltar na tela'}
      </button>}
    </article>)}
    {erro && <p role="status" className={css.erro}>{erro}</p>}
    {typeof document !== 'undefined' && notas.filter((n) => n.id && soltas[n.id]).map((nota) => createPortal(
      <PostitSolto key={nota.id} nota={nota} inicial={soltas[nota.id]} saindo={saindo[nota.id]} focar={ultimaSolta === nota.id}
        aoGuardar={(p) => guardar(nota.id, p)} aoRecolher={() => recolher(nota.id)} />, document.body, `${conversaId}:${nota.id}`))}
  </section>
}
