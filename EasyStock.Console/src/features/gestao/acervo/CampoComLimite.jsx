import { useId, useState } from 'react'
import { erroDoLimite } from '../../../componentes/limiteDoCampo'
import css from './acervo.module.css'

// #2792: o limite (que espelha o servidor) no lugar do `maxLength`, que cortava em silêncio o texto colado.
// O campo continua não controlado (os formulários do acervo leem `FormData` e alguns preenchem o valor por
// código): a frase "Use até N caracteres. Agora tem M." aparece logo abaixo, ligada por aria-describedby.
export function CampoComLimite({ rotulo, limite, como: Como = 'textarea', ...resto }) {
  const id = useId()
  const [texto, setTexto] = useState(String(resto.defaultValue ?? ''))
  const erro = erroDoLimite(texto, limite)
  return (
    <div className={css.campo}>
      <label>{rotulo}<Como {...resto} onInput={(e) => setTexto(e.currentTarget.value)} aria-invalid={erro ? true : undefined} aria-describedby={erro ? `${id}-erro` : undefined} /></label>
      {erro && <p id={`${id}-erro`} className={css.erro} role="status">{erro}</p>}
    </div>
  )
}
