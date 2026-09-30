import css from './ListaDados.module.css'

// Pares rótulo e valor. Recebe dados, não monta nenhum por conta própria.
export function ListaDados({ itens }) {
  return (
    <dl className={css.lista}>
      {itens.filter((i) => i.valor != null && i.valor !== '').map((item) => (
        <div className={css.fileira} key={item.rotulo}>
          <dt>{item.rotulo}</dt>
          <dd>{item.valor}</dd>
        </div>
      ))}
    </dl>
  )
}
