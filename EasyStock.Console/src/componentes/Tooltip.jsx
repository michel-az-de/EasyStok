import { Children, cloneElement, isValidElement, useCallback, useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import css from './Tooltip.module.css'

// #2146 (plano #2130, seção 3.3): a dica que substitui o `title` nativo. Aparece no hover do mouse e
// no foco por teclado depois de 500 ms; some no Escape, no blur, ao sair e ao clicar. Nunca em toque:
// no celular o `title` não existia de fato e a dica cobriria o dedo.
//
// Não embrulha o gatilho em elemento nenhum: o filho recebe os eventos por `cloneElement`, para as
// folhas que miram `.acoes > button` continuarem valendo, e a ref dele fica intocada. A dica mora num
// portal no `body` como `popover="manual"`, que sobe para a camada de cima do navegador (acima de um
// <dialog> aberto) sem medir ancestral; fechada, fica escondida no DOM, para o `aria-describedby` valer.
//
// `texto` é "Verbo + objeto" (seção 3.5); `atalho` aparece ao lado, em <kbd> ("Ctrl+K").
// O `aria-describedby` só entra quando o texto difere do nome acessível do gatilho (o aria-label ou,
// sem ele, o texto dos filhos): repetir o nome no leitor de tela é ruído.
export const ATRASO_DA_DICA_MS = 500

const emToque = () => typeof window !== 'undefined' && window.matchMedia?.('(pointer: coarse)').matches
const normalizar = (texto) => (texto ?? '').replace(/\s+/g, ' ').trim().toLowerCase()
const textoDe = (no) => {
  if (typeof no === 'string' || typeof no === 'number') return String(no)
  if (Array.isArray(no)) return no.map(textoDe).join(' ')
  return isValidElement(no) ? textoDe(no.props.children) : ''
}

export function Tooltip({ texto, atalho, children }) {
  const filho = Children.only(children)
  const {
    'aria-label': nomeDeclarado, 'aria-describedby': descritoDele, children: conteudo,
    onPointerEnter, onPointerLeave, onPointerDown, onFocus, onBlur,
  } = filho.props
  const id = useId()
  const dicaRef = useRef(null)
  const gatilhoRef = useRef(null)
  const relogio = useRef(null)
  const [aberta, setAberta] = useState(false)
  const destino = typeof document === 'undefined' ? null : document.body

  const fechar = useCallback(() => {
    clearTimeout(relogio.current)
    setAberta(false)
  }, [])

  const agendar = (gatilho) => {
    if (!texto || emToque()) return
    clearTimeout(relogio.current)
    gatilhoRef.current = gatilho
    relogio.current = setTimeout(() => { if (gatilho.isConnected) setAberta(true) }, ATRASO_DA_DICA_MS)
  }

  useEffect(() => () => clearTimeout(relogio.current), [])

  // Escape fecha só a dica e não consome a tecla: o diálogo ou o menu de baixo segue o que já fazia.
  useEffect(() => {
    if (!aberta) return undefined
    const aoTeclar = (evento) => { if (evento.key === 'Escape') fechar() }
    document.addEventListener('keydown', aoTeclar, true)
    window.addEventListener('scroll', fechar, true)
    return () => {
      document.removeEventListener('keydown', aoTeclar, true)
      window.removeEventListener('scroll', fechar, true)
    }
  }, [aberta, fechar])

  // Acima do gatilho, centralizada; cai para baixo quando não cabe em cima. Nunca sai da janela.
  useLayoutEffect(() => {
    const dica = dicaRef.current
    if (!dica) return
    const mostrar = aberta && gatilhoRef.current?.isConnected
    if (dica.showPopover) {
      if (mostrar && !dica.matches(':popover-open')) dica.showPopover()
      if (!mostrar && dica.matches(':popover-open')) dica.hidePopover()
    } else {
      dica.hidden = !mostrar
    }
    if (!mostrar) return
    const estilo = getComputedStyle(dica)
    const espaco = parseFloat(estilo.getPropertyValue('--e-2')) || 0
    const margem = parseFloat(estilo.getPropertyValue('--e-16')) || 0
    const alvo = gatilhoRef.current.getBoundingClientRect()
    const acima = alvo.top - dica.offsetHeight - espaco
    const cabeAcima = acima >= margem
    const esquerda = Math.max(margem, Math.min(alvo.left + alvo.width / 2 - dica.offsetWidth / 2, innerWidth - dica.offsetWidth - margem))
    dica.style.top = `${Math.round(cabeAcima ? acima : alvo.bottom + espaco)}px`
    dica.style.left = `${Math.round(esquerda)}px`
    dica.dataset.lado = cabeAcima ? 'acima' : 'abaixo'
  }, [aberta])

  const descreve = Boolean(texto && destino) && normalizar(nomeDeclarado ?? textoDe(conteudo)) !== normalizar(texto)

  const gatilho = cloneElement(filho, {
    'aria-describedby': [descritoDele, descreve ? id : null].filter(Boolean).join(' ') || undefined,
    onPointerEnter: (evento) => {
      onPointerEnter?.(evento)
      if (evento.pointerType !== 'touch') agendar(evento.currentTarget)
    },
    onPointerLeave: (evento) => { onPointerLeave?.(evento); fechar() },
    onPointerDown: (evento) => { onPointerDown?.(evento); fechar() },
    onFocus: (evento) => {
      onFocus?.(evento)
      if (evento.target.matches?.(':focus-visible')) agendar(evento.currentTarget)
    },
    onBlur: (evento) => { onBlur?.(evento); fechar() },
  })

  return (
    <>
      {gatilho}
      {texto && destino && createPortal(
        <div ref={dicaRef} id={id} role="tooltip" popover="manual" className={css.dica} data-dica>
          {texto}
          {atalho && <kbd className={css.atalho}>{atalho}</kbd>}
        </div>,
        destino,
      )}
    </>
  )
}
