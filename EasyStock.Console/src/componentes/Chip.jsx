import css from './Chip.module.css'
import { Icone } from './Icone'

// Chip com três papéis (seção 1, 2 e 4 da direção visual, passo zero):
//   filtro   liga e desliga (aria-pressed), zero ou mais ligados ao mesmo tempo.
//   escolha  um dentro de um `role="radiogroup"` do pai (aria-checked).
//   tag      do cliente, com × para tirar e texto editável em toque.
// `emBreve` desabilita o clique e escreve o motivo no próprio rótulo (a
// palavra "em breve" já vem em `children`, o componente só estiliza).
export function Chip({
  papel = 'filtro', ativo = false, emBreve = false, tracejado = false, icone, className,
  onClick, onRemover, rotuloRemover, children,
}) {
  const classes = [
    css.chip, ativo ? css.ativo : '', emBreve ? css.emBreve : '', tracejado ? css.tracejado : '',
    className ?? '',
  ].filter(Boolean).join(' ')

  if (papel === 'tag') {
    return (
      <span className={classes}>
        {icone && <Icone nome={icone} tamanho={20} />}
        <span className={css.textoTag}>{children}</span>
        {onRemover && (
          <button type="button" className={css.remover} aria-label={rotuloRemover} onClick={onRemover}>
            <Icone nome="x" tamanho={20} />
          </button>
        )}
      </span>
    )
  }

  const papelAria = papel === 'escolha'
    ? { role: 'radio', 'aria-checked': ativo }
    : { 'aria-pressed': ativo }

  return (
    <button
      type="button"
      className={classes}
      aria-disabled={emBreve || undefined}
      onClick={emBreve ? undefined : onClick}
      {...papelAria}
    >
      {ativo && <Icone nome="check" tamanho={20} />}
      {icone && !ativo && <Icone nome={icone} tamanho={20} />}
      {children}
    </button>
  )
}
