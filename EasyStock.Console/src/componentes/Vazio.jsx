import css from './Vazio.module.css'
import { Icone } from './Icone'

// Estado vazio que ensina, sempre com os três: título do que está faltando,
// frase dizendo o que fazer e o botão que faz. Parágrafo cinza solto não diz a
// ninguém o que fazer em seguida, então ele não existe mais aqui.
// `como`: a tag do bloco (#1899: `output` quando o vazio é uma situação anunciada, no lugar de role="status").
//
// #2185 (R13): `variante="linha"` é o vazio de lista curta: uma frase à esquerda e, se houver, a ação ao
// lado, sem ícone nem título grande. É o único vazio em linha do app; não se escreve `<p>Nenhum…</p>` à mão.
export function Vazio({ titulo, acao, children, icone, role, variante, como: Tag = 'div' }) {
  if (variante === 'linha') {
    return (
      <Tag className={css.linha} role={role}>
        <span>{titulo}</span>
        {acao}
      </Tag>
    )
  }
  return (
    <Tag className={css.vazio} role={role}>
      {icone && <span className={css.icone}><Icone nome={icone} tamanho={24} /></span>}
      <strong className={css.titulo}>{titulo}</strong>
      {children && <p className={css.frase}>{children}</p>}
      {acao && <div className={css.acao}>{acao}</div>}
    </Tag>
  )
}
