import css from './Bloco.module.css'

// Seção titulada de painel. Existe para o título ficar consistente em todo lugar.
export function Bloco({ titulo, destaque = false, children }) {
  return (
    <section className={`${css.bloco} ${destaque ? css.destaque : ''}`}>
      <h3>{titulo}</h3>
      {children}
    </section>
  )
}
