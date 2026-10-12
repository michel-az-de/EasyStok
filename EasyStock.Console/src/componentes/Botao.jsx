import { forwardRef } from 'react'
import css from './Botao.module.css'
import { Icone } from './Icone'

// Um botão para toda a aplicação. A variante muda a aparência, nunca o contrato.
// A classe de fora SOMA com a do componente, nunca substitui.
//
// Três variantes (secao 4 da direção visual): primario, secundario, texto.
// `padrao` e `discreto` são os nomes antigos, mantidos como apelido no CSS
// para quem ainda chama assim não quebrar antes do quarto passe.
export const Botao = forwardRef(function Botao(
  { variante = 'secundario', largo = false, tipo = 'button', icone, className, children, ...resto }, ref,
) {
  const classes = [css.botao, css[variante], largo ? css.largo : '', className]
    .filter(Boolean).join(' ')
  return (
    <button type={tipo} ref={ref} className={classes} {...resto}>
      {icone ? <Icone nome={icone} tamanho={20} /> : null}
      {children}
    </button>
  )
})
