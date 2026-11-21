import { forwardRef, useId } from 'react'
import css from './Botao.module.css'
import { Icone } from './Icone'

// Um botão para toda a aplicação. A variante muda a aparência, nunca o contrato.
// A classe de fora SOMA com a do componente, nunca substitui.
//
// Três variantes (secao 4 da direção visual): primario, secundario, texto.
// #1683: `perigo` é o primário de quem confirma ação destrutiva (cancelar, estornar, desfazer).
// `padrao` e `discreto` são os nomes antigos, mantidos como apelido no CSS
// para quem ainda chama assim não quebrar antes do quarto passe.
//
// #2258 (auditoria P6, T14): desabilitado tem cara própria (sem preenchimento, contorno tracejado) e,
// com `motivo`, diz por que está parado: o texto fica visível antes do botão, vira o `title` e é lido
// pelo leitor de tela (aria-describedby). "Cliquei e nada aconteceu" deixa de existir.
export const Botao = forwardRef(function Botao(
  { variante = 'secundario', largo = false, tipo = 'button', icone, motivo, className, children, ...resto }, ref,
) {
  const idMotivo = useId()
  const classes = [css.botao, css[variante], largo ? css.largo : '', className]
    .filter(Boolean).join(' ')
  const explicar = Boolean(resto.disabled && motivo)
  const botao = (
    <button
      type={tipo} ref={ref} className={classes} {...resto}
      title={explicar ? motivo : resto.title}
      aria-describedby={[resto['aria-describedby'], explicar ? idMotivo : null].filter(Boolean).join(' ') || undefined}
    >
      {icone ? <Icone nome={icone} tamanho={20} /> : null}
      {children}
    </button>
  )
  // #2258 (banca a11y): com `motivo`, o invólucro existe sempre; ligar e desligar o botão não troca a
  // raiz da árvore, então o <button> não remonta e não perde foco nem ref.
  if (!motivo) return botao
  return (
    <span className={css.comMotivo}>
      {explicar && <span id={idMotivo} className={css.motivo}>{motivo}</span>}
      {botao}
    </span>
  )
})
