import css from './Vazio.module.css'
import { Icone } from './Icone'

// Estado vazio que ensina, sempre com os três: título do que está faltando,
// frase dizendo o que fazer e o botão que faz. Parágrafo cinza solto não diz a
// ninguém o que fazer em seguida, então ele não existe mais aqui.
export function Vazio({ titulo, acao, children, icone, role }) {
  return (
    <div className={css.vazio} role={role}>
      {icone && <span className={css.icone}><Icone nome={icone} tamanho={24} /></span>}
      <strong className={css.titulo}>{titulo}</strong>
      {children && <p className={css.frase}>{children}</p>}
      {acao && <div className={css.acao}>{acao}</div>}
    </div>
  )
}
