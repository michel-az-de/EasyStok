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
// conteúdo de verdade é replantado em `document.body` nesse retângulo. O
// CSS de .popover continua o mesmo (bottom/top: 100% relativo ao wrapper
// plantado), então quem não passa `portal` renderiza exatamente como antes.
export function Popover({
  rotulo, grupos, aoEscolher, aoFechar, posicao = 'acima', children, semAutoFoco = false, portal = false,
}) {
  const caixaRef = useRef(null)
  const marcadorRef = useRef(null)
  const [retangulo, setRetangulo] = useState(null)
  useEscape(true, aoFechar)

  useLayoutEffect(() => {
    if (!portal) return
    setRetangulo(marcadorRef.current?.parentElement?.getBoundingClientRect() ?? null)
  }, [portal])

  // Sugestão presa ao campo de texto (atalho "/" do composer) não pode
  // roubar o foco: ela está digitando, e o Popover de clique de sempre foca
  // o primeiro item. `semAutoFoco` é opt-in: quem já chama Popover sem essa
  // prop continua exatamente igual.
  useEffect(() => {
    if (semAutoFoco) return
    caixaRef.current?.querySelector('button')?.focus()
  }, [semAutoFoco])

  useEffect(() => {
    const aoClicarFora = (evento) => {
      if (!caixaRef.current?.contains(evento.target)) aoFechar()
    }
    // captura na fase de captura para o clique no gatilho também fechar
    document.addEventListener('pointerdown', aoClicarFora)
    return () => document.removeEventListener('pointerdown', aoClicarFora)
  }, [aoFechar])

  const corpo = (
    <div
      className={`${css.popover} ${posicao === 'abaixo' ? css.abaixo : ''}`}
      role="menu"
      aria-label={rotulo}
      ref={caixaRef}
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
          position: 'fixed',
          left: retangulo.left,
          width: retangulo.width,
          top: retangulo.top,
          height: retangulo.height,
          zIndex: 30,
        }}
        >
          {corpo}
        </div>,
        document.body,
      )}
    </>
  )
}
