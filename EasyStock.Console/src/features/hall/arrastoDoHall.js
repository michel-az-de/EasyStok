import { useRef } from 'react'
import { KeyboardSensor, MouseSensor, TouchSensor, useSensor, useSensors } from '@dnd-kit/core'
import { sortableKeyboardCoordinates } from '@dnd-kit/sortable'

// #1839 D: contadores e cartões arrastam direto, sem modo Organizar. O próprio link é a alça:
// mouse arrasta depois de 6 px (clique curto abre), toque depois de segurar, e no teclado Espaço
// pega e solta, setas movem, Esc cancela. Enter continua abrindo o link.
const TECLAS = { start: ['Space'], cancel: ['Escape'], end: ['Space'] }

export function useSensoresDoHall() {
  return useSensors(
    useSensor(MouseSensor, { activationConstraint: { distance: 6 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 200, tolerance: 6 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates, keyboardCodes: TECLAS }),
  )
}

// O sortable marca a alça como botão; aqui a alça é um link e deve continuar sendo anunciada como
// link. Fica só a instrução de teclado (aria-describedby).
export function alcaDoLink(atributos, listeners) {
  return { 'aria-describedby': atributos['aria-describedby'], ...listeners, draggable: false }
}

// Soltar o cartão sobre ele mesmo dispara o clique do link; o clique logo após um arrasto é engolido.
export function useCliqueAposArrasto() {
  const arrastou = useRef(false)
  return {
    aoComecar: () => { arrastou.current = true },
    aoTerminar: () => { window.setTimeout(() => { arrastou.current = false }, 0) },
    onClickCapture: (evento) => { if (arrastou.current) { evento.preventDefault(); evento.stopPropagation() } },
  }
}
