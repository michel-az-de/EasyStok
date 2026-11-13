import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { limitarPainelMovel, proximaFrentePainel } from '../hooks/estadoPainelMovel'
import { usePainelMovel as useControlePainelMovel } from '../hooks/usePainelMovel'
import { janelaDosPaineis as janelaAtual, observarAreaDosPaineis } from '../hooks/areaDosPaineis'
import { BotaoIcone } from './BotaoIcone'
import { Icone } from './Icone'
import css from './PainelMovel.module.css'
export const usePainelMovel = useControlePainelMovel

const movimentoReduzido = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true

function SuperficieMovel({ controle, titulo, children, larguraInicial, alturaInicial, className, classeCorpo, focar, aoRecolher, atributos, alinharDireita, visivel }) {
  const ajuda = useId()
  const papel = useRef(null)
  const alca = useRef(null)
  const focoAtendido = useRef(null)
  const gesto = useRef(null)
  const origemTeclado = useRef(null)
  const guardar = controle.guardar
  const [frente, setFrente] = useState(proximaFrentePainel)
  const [saindo, setSaindo] = useState(false)
  const [arrastando, setArrastando] = useState(false)
  const [disponivel, setDisponivel] = useState(true)
  const inicio = () => ({ x: alinharDireita ? window.innerWidth - larguraInicial - 8 : 24,
    y: alinharDireita ? (janelaAtual().area?.y ?? 64) + 8 : 112, largura: larguraInicial,
    ...(alturaInicial ? { altura: alturaInicial } : {}) })
  const [geometria, setGeometria] = useState(() => ({ x: controle.x ?? inicio().x, y: controle.y ?? inicio().y,
    largura: controle.largura ?? larguraInicial, ...(controle.altura || alturaInicial ? { altura: controle.altura ?? alturaInicial } : {}) }))
  const atual = useRef(geometria)
  const timer = useRef(null)
  const limparFoco = useRef(null)
  const tamanho = useCallback(() => {
    const r = papel.current?.getBoundingClientRect()
    return { largura: r?.width ?? larguraInicial, altura: r?.height ?? alturaInicial ?? 180 }
  }, [larguraInicial, alturaInicial])
  function mover(nova, salvar = false) {
    const dimensoes = { ...tamanho(), ...(nova.largura ? { largura: nova.largura } : {}), ...(nova.altura ? { altura: nova.altura } : {}) }
    const limitada = limitarPainelMovel(nova, dimensoes, janelaAtual())
    const proxima = { x: limitada.x, y: limitada.y, largura: limitada.largura,
      altura: nova.altura || limitada.altura < dimensoes.altura ? limitada.altura : undefined }
    atual.current = proxima; setGeometria(proxima)
    if (salvar) controle.guardar(proxima)
    return proxima
  }
  useEffect(() => {
    if (!focar) { focoAtendido.current = focar; return }
    if (!visivel || focoAtendido.current === focar || !alca.current) return
    alca.current.focus({ preventScroll: true })
    focoAtendido.current = focar
  }, [focar, visivel])
  useEffect(() => () => { clearTimeout(timer.current); limparFoco.current?.() }, [])
  useLayoutEffect(() => {
    if (!visivel) return
    const ajustar = () => {
      const dimensoes = tamanho(), limitada = limitarPainelMovel(atual.current, dimensoes, janelaAtual())
      const minimo = (papel.current?.querySelector?.('header')?.getBoundingClientRect().height || 40)
        + (papel.current?.querySelector?.('[data-redimensionar-painel]')?.getBoundingClientRect().height || 40)
      setDisponivel(limitada.altura >= minimo)
      if (limitada.altura < minimo) {
        if (papel.current?.contains(document.activeElement)) document.querySelector?.('[data-area-envio] textarea:not(:disabled)')?.focus({ preventScroll: true })
        return
      }
      const antes = atual.current
      if (limitada.x !== antes.x || limitada.y !== antes.y || limitada.largura !== antes.largura || (antes.altura && limitada.altura !== antes.altura) || limitada.altura < dimensoes.altura) {
        const proxima = { x: limitada.x, y: limitada.y, largura: limitada.largura,
          ...(antes.altura || limitada.altura < dimensoes.altura ? { altura: limitada.altura } : {}) }
        atual.current = proxima; setGeometria(proxima); guardar(proxima)
      }
    }
    ajustar()
    return observarAreaDosPaineis(ajustar, papel.current)
  }, [guardar, tamanho, visivel])

  function iniciar(e, modo) {
    if (saindo || (e.pointerType === 'mouse' && e.button !== 0)) return
    e.preventDefault(); e.currentTarget.focus(); setFrente(proximaFrentePainel())
    const antes = { ...atual.current }
    origemTeclado.current = antes
    gesto.current = { id: e.pointerId, x: e.clientX, y: e.clientY, antes, tamanho: tamanho(), modo }
    e.currentTarget.setPointerCapture(e.pointerId); setArrastando(true)
  }
  function deslocar(e) {
    const g = gesto.current
    if (!g || g.id !== e.pointerId) return
    const dx = e.clientX - g.x, dy = e.clientY - g.y
    mover(g.modo === 'tamanho' ? { ...g.antes, largura: Math.max(240, g.tamanho.largura + dx), altura: Math.max(144, g.tamanho.altura + dy) }
      : { ...g.antes, x: g.antes.x + dx, y: g.antes.y + dy })
  }
  function terminar(e, cancelar = false) {
    const g = gesto.current
    if (!g || g.id !== e.pointerId) return
    gesto.current = null; setArrastando(false)
    mover(cancelar ? g.antes : atual.current, true)
    if (e.currentTarget.hasPointerCapture(g.id)) e.currentTarget.releasePointerCapture(g.id)
  }
  function teclar(e, modo = 'posicao') {
    const setas = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }
    if (e.key === 'Escape') {
      e.preventDefault(); e.stopPropagation()
      const antes = gesto.current?.antes ?? origemTeclado.current
      gesto.current = null; setArrastando(false)
      if (antes) mover(antes, true)
    } else if (setas[e.key] && !e.altKey && !e.ctrlKey && !e.metaKey) {
      e.preventDefault(); e.stopPropagation()
      const [dx, dy] = setas[e.key], passo = e.shiftKey ? 24 : 8, antes = atual.current, r = tamanho()
      mover(modo === 'tamanho' ? { ...antes, largura: Math.max(240, r.largura + dx * passo), altura: Math.max(144, r.altura + dy * passo) }
        : { ...antes, x: antes.x + dx * passo, y: antes.y + dy * passo }, true)
    }
  }
  function recolher() {
    if (saindo) return
    const janela = papel.current
    const dentro = (no) => janela?.contains?.(no) === true
    const eraInterno = dentro(document.activeElement)
    let mudouParaFora = false
    const observar = (evento) => { if (!dentro(evento.target) && evento.target !== document.body) mudouParaFora = true }
    document.addEventListener?.('focusin', observar, true)
    document.addEventListener?.('pointerdown', observar, true)
    limparFoco.current = () => {
      document.removeEventListener?.('focusin', observar, true)
      document.removeEventListener?.('pointerdown', observar, true)
    }
    setSaindo(true)
    timer.current = setTimeout(() => {
      const segueInterno = () => dentro(document.activeElement) || document.activeElement === document.body || !document.activeElement
      const devolver = eraInterno && !mudouParaFora && segueInterno()
      controle.recolher(); aoRecolher?.()
      // O callback pode ter escolhido outro alvo; só devolve se o foco continua sem destino.
      if (devolver && !mudouParaFora && segueInterno()) controle.devolverFoco?.()
      limparFoco.current?.(); limparFoco.current = null
    }, movimentoReduzido() ? 0 : 150)
  }
  const eventos = (modo) => ({ onPointerDown: (e) => iniciar(e, modo), onPointerMove: deslocar, onPointerUp: (e) => terminar(e),
    onPointerCancel: (e) => terminar(e, true), onLostPointerCapture: (e) => terminar(e, true),
    onKeyDown: (e) => teclar(e, modo), onFocus: () => { origemTeclado.current = { ...atual.current } } })
  return <aside ref={papel} className={`${css.painel} ${className ?? ''} ${saindo ? css.saindo : ''}`}
    style={{ left: geometria.x, top: geometria.y, width: geometria.largura, height: geometria.altura, zIndex: frente, visibility: disponivel ? undefined : 'hidden' }}
    data-painel-movel={controle.painelId} aria-label={titulo} {...atributos} onPointerDownCapture={() => setFrente(proximaFrentePainel())}
    onFocusCapture={() => setFrente(proximaFrentePainel())}>
    <header className={css.cabecalho}>
      <button ref={alca} type="button" className={css.mover} aria-label={`Mover ${titulo}`} aria-describedby={ajuda}
        data-arrastando={arrastando || undefined} disabled={saindo} {...eventos('posicao')}>
        <Icone nome="grip-vertical" tamanho={20} /><span>{titulo}</span>
      </button>
      <BotaoIcone rotulo={`Restaurar posição de ${titulo}`} dica="Restaurar posição e tamanho" icone="undo-2" disabled={saindo}
        onClick={() => mover(inicio(), true)} />
      <BotaoIcone rotulo={`Recolher ${titulo}`} dica="Recolher" icone="arrow-left" disabled={saindo} onClick={recolher} />
    </header>
    <span id={ajuda} className="sr">Arraste pelo cabeçalho ou use as setas. Shift move mais rápido. Escape desfaz o movimento. Use o canto inferior para redimensionar.</span>
    <div className={`${css.corpo} ${classeCorpo ?? ''}`}>{children}</div>
    {controle.erro && <output className={css.erro}>{controle.erro}</output>}
    <button type="button" className={css.redimensionar} data-redimensionar-painel aria-label={`Redimensionar ${titulo}`} aria-describedby={ajuda} disabled={saindo}
      {...eventos('tamanho')}><span aria-hidden="true">↘</span></button>
  </aside>
}

export function PainelMovel({ controle, titulo, children, larguraInicial = 360, alturaInicial, className, classeCorpo, focar = controle.foco, aoRecolher, atributos, alinharDireita = false, visivel = true }) {
  if (!controle.solto || typeof document === 'undefined') return null
  return createPortal(<div hidden={!visivel} inert={!visivel} style={{ display: visivel ? 'contents' : 'none' }}>
    <SuperficieMovel key={controle.chave} controle={controle} titulo={titulo} larguraInicial={larguraInicial}
      alturaInicial={alturaInicial} className={className} classeCorpo={classeCorpo} focar={focar} aoRecolher={aoRecolher} atributos={atributos} alinharDireita={alinharDireita} visivel={visivel}>{children}</SuperficieMovel>
  </div>, document.body, controle.chave)
}
