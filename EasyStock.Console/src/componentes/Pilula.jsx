import css from './Pilula.module.css'
import { Icone } from './Icone'

// Rótulo curto de estado. A cor nunca informa sozinha: o texto sempre vai junto.
//
// `icone` é opt-in (seção 1 da direção, rodada 5): o cartão do Balcão pede uma
// marca com fundo pastel e ícone de 20 px, mas as pílulas mais antigas (barra
// de modo, composer, ficha) continuam do jeito que estão — passar o ícone é o
// que liga o fundo novo, sem ele nada muda para quem já chama este componente.
export function Pilula({ tom = 'neutro', fina = false, icone, titulo, children }) {
  const classes = [
    css.pilula, css[tom], fina ? css.fina : '', icone ? css.comIcone : '',
  ].filter(Boolean).join(' ')
  return (
    <span className={classes} title={titulo}>
      {icone && <Icone nome={icone} tamanho={20} />}
      {children}
    </span>
  )
}
