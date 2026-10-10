import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { useEscape } from '../hooks/useEscape'
import css from './Popover.module.css'

// `posicao` decide de que lado a camada abre. O padrão continua sendo acima,
// que é onde o composer precisa; 'abaixo' serve para gatilho no topo da tela.
// `children` substitui `grupos`/`aoEscolher` quando o conteúdo não é lista de
// texto (UX-05: grade de foto ou figurinha, por exemplo). Ancoragem, Esc e
// clique fora continuam os mesmos para os dois casos.
//
// `portal` (achado do Composer, generalizado aqui): um ancestral com
// overflow-y ou overflow-x diferente de `visible` recorta este popover
// mesmo quando ele sai por um eixo que não é o da rolagem, porque a spec de
// CSS não permite um eixo `visible` com o outro `auto`/`hidden`/`scroll` (só
// dá pra declarar os dois juntos). O Composer já tinha essa correção só para
// o atalho "/"; `portal` faz o mesmo sem exigir que quem chama meça nada: um
// marcador `display:none` nasce no lugar de sempre só para achar, via
// `parentElement`, o retângulo do ancestral `position:relative` de cada
// chamador (todos já têm um, é a âncora do próprio popover hoje), e o
// conteúdo é replantado no dialog aberto mais próximo ou no body. Assim,
// um menu da Ficha permanece na camada modal e não fica no fundo inerte. O
// CSS de .popover continua o mesmo (bottom/top: 100% relativo ao wrapper
// plantado), então quem não passa `portal` renderiza exatamente como antes.
export function Popover({
  rotulo, grupos, aoEscolher, aoFechar, posicao = 'acima', children, semAutoFoco = false, portal = false,
}) {
  const caixaRef = useRef(null)
  const marcadorRef = useRef(null)
  const origemRef = useRef(null)
  const [retangulo, setRetangulo] = useState(null)
  useEscape(true, (evento) => {
    // Uma janela aberta sobre este menu recebe seu próprio Escape nativo.
    const janelaDoMenu = caixaRef.current?.closest('dialog[open]') ?? null
    const janelaDoFoco = document.activeElement?.closest('dialog[open]') ?? null
    if (janelaDoMenu !== janelaDoFoco) return
    evento.preventDefault()
    evento.stopImmediatePropagation()
    aoFechar()
    if (origemRef.current?.isConnected) origemRef.current.focus()
  }, { capturar: true })

  useLayoutEffect(() => {
    if (!origemRef.current) origemRef.current = document.activeElement
    if (!portal) return
    const ancora = marcadorRef.current?.parentElement
    if (!ancora) return
    const medida = ancora.getBoundingClientRect()
    const dialogo = ancora.closest('dialog[open]')
    const referencia = dialogo?.getBoundingClientRect()
    const escalaX = dialogo ? referencia.width / dialogo.offsetWidth : 1
    const escalaY = dialogo ? referencia.height / dialogo.offsetHeight : 1
    setRetangulo({
      left: dialogo ? (medida.left - referencia.left) / escalaX + dialogo.scrollLeft - dialogo.clientLeft : medida.left,
      top: dialogo ? (medida.top - referencia.top) / escalaY + dialogo.scrollTop - dialogo.clientTop : medida.top,
      width: medida.width / escalaX, height: medida.height / escalaY,
      destino: dialogo ?? document.body, emDialogo: !!dialogo,
    })
  }, [portal])

  useLayoutEffect(() => {
    if (!retangulo?.emDialogo || posicao !== 'abaixo') return
    const caixa = caixaRef.current
    const dialogo = retangulo.destino
    const estilo = getComputedStyle(caixa)
    const margem = parseFloat(estilo.getPropertyValue('--e-4'))
    const espaco = parseFloat(estilo.getPropertyValue('--e-2'))
    const largura = Math.min(360, dialogo.clientWidth - 2 * margem)
    const esquerda = Math.max(margem, Math.min(retangulo.left + retangulo.width - largura, dialogo.clientWidth - largura - margem))
    // O dialog animado é um containing block. Mantém o menu dentro dele,
    // sem o foco provocar rolagem horizontal ou esconder o último item.
    Object.assign(caixa.style, {
      position: 'absolute', width: `${largura}px`, left: `${esquerda - retangulo.left}px`, right: 'auto',
      maxHeight: `${dialogo.clientHeight - 2 * margem}px`,
    })
    const abaixo = dialogo.clientHeight - retangulo.top - retangulo.height - espaco - margem
    const acima = retangulo.top - espaco - margem
    const inverter = caixa.offsetHeight > abaixo && acima > abaixo
    Object.assign(caixa.style, {
      top: inverter ? 'auto' : `${retangulo.height + espaco}px`,
      bottom: inverter ? `${retangulo.height + espaco}px` : 'auto',
      maxHeight: `${Math.max(1, inverter ? acima : abaixo)}px`,
      transformOrigin: inverter ? 'bottom right' : 'top right',
    })
  }, [retangulo, posicao])

  // Sugestão presa ao campo de texto (atalho "/" do composer) não pode
  // roubar o foco: ela está digitando, e o Popover de clique de sempre foca
  // o primeiro item. `semAutoFoco` é opt-in: quem já chama Popover sem essa
  // prop continua exatamente igual.
  useEffect(() => {
    if (semAutoFoco) return
    caixaRef.current?.querySelector('button')?.focus()
  }, [semAutoFoco, retangulo])

  useEffect(() => {
    const aoClicarFora = (evento) => {
      // O próprio gatilho alterna aberto/fechado no click, depois deste pointerdown.
      if (origemRef.current?.matches('button') && origemRef.current.contains(evento.target)) return
      if (!caixaRef.current?.contains(evento.target)) aoFechar()
    }
    document.addEventListener('pointerdown', aoClicarFora)
    return () => document.removeEventListener('pointerdown', aoClicarFora)
  }, [aoFechar])

  const corpo = (
    <div
      className={`${css.popover} ${posicao === 'abaixo' ? css.abaixo : ''}`}
      role="menu"
      tabIndex={-1}
      aria-label={rotulo}
      ref={caixaRef}
      onClickCapture={(evento) => {
        // A ação pode desmontar o menu e abrir um Modal no mesmo clique.
        // Devolve o foco antes disso, para a nova janela guardar o acionador.
        if (evento.target.closest('[role="menuitem"]') && origemRef.current?.isConnected) origemRef.current.focus()
      }}
      onKeyDown={(evento) => {
        if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(evento.key)
          || !evento.target.matches('[role="menuitem"]')) return
        const itens = [...caixaRef.current.querySelectorAll('[role="menuitem"]:not(:disabled)')]
        const atual = itens.indexOf(document.activeElement)
        const proximo = evento.key === 'Home' ? 0 : evento.key === 'End' ? itens.length - 1
          : (atual + (evento.key === 'ArrowDown' ? 1 : -1) + itens.length) % itens.length
        evento.preventDefault()
        itens[proximo]?.focus()
      }}
    >
      {children ?? grupos.map((grupo) => (
        <div className={css.grupo} key={grupo.titulo}>
          <p className={css.titulo}>{grupo.titulo}</p>
          {grupo.itens.map((item) => (
            <button
              type="button"
              role="menuitem"
              className={css.item}
              key={item.chave}
              onClick={() => aoEscolher(item)}
            >
              <strong>{item.titulo}</strong>
              <span>{item.detalhe}</span>
            </button>
          ))}
        </div>
      ))}
    </div>
  )

  if (!portal) return corpo

  return (
    <>
      <span ref={marcadorRef} style={{ display: 'none' }} />
      {retangulo && createPortal(
        <div style={{
          position: retangulo.emDialogo ? 'absolute' : 'fixed',
          left: retangulo.left,
          width: retangulo.width,
          top: retangulo.top,
          height: retangulo.height,
          zIndex: 70,
          pointerEvents: 'none',
        }}
        >
          {corpo}
        </div>,
        retangulo.destino,
      )}
    </>
  )
}
