import css from './Vazio.module.css'

// Estado vazio que ensina, sempre com os três: título do que está faltando,
// frase dizendo o que fazer e o botão que faz. Parágrafo cinza solto não diz a
// ninguém o que fazer em seguida, então ele não existe mais aqui.
export function Vazio({ titulo, acao, children }) {
  return (
    <div className={css.vazio}>
      <strong className={css.titulo}>{titulo}</strong>
      <p className={css.frase}>{children}</p>
      {acao && <div className={css.acao}>{acao}</div>}
    </div>
  )
}
