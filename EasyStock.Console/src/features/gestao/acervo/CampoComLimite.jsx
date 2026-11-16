import { useEffect, useId, useRef, useState } from 'react'
import { erroDoLimite } from '../../../componentes/limiteDoCampo'
import { AnuncioDoLimite } from '../../../componentes/Campo'
import css from './acervo.module.css'

// #2792: o limite (que espelha o servidor) no lugar do `maxLength`, que cortava em silêncio o texto colado.
// O campo continua não controlado (os formulários do acervo leem `FormData` e alguns preenchem o valor por
// código e disparam `input` no formulário): por isso o texto é relido a cada `input` do formulário, não só do
// próprio campo. A frase "Use até N caracteres. Agora tem M." aparece logo abaixo, ligada por aria-describedby,
// e o anúncio estável fica no AnuncioDoLimite (a contagem não reanuncia a cada tecla).
export function CampoComLimite({ rotulo, limite, como: Como = 'textarea', ...resto }) {
  const id = useId()
  const campo = useRef(null)
  const [texto, setTexto] = useState(String(resto.defaultValue ?? ''))
  useEffect(() => {
    const el = campo.current
    const alvo = el?.form ?? el
    if (!alvo) return undefined
    const reler = () => setTexto(el.value)
    alvo.addEventListener('input', reler)
    return () => alvo.removeEventListener('input', reler)
  }, [])
  const erro = erroDoLimite(texto, limite)
  return (
    <div className={css.campo}>
      <label>{rotulo}<Como {...resto} ref={campo} aria-invalid={erro ? true : undefined} aria-describedby={erro ? `${id}-erro` : undefined} /></label>
      {erro && <p id={`${id}-erro`} className={css.erro}>{erro}</p>}
      <AnuncioDoLimite limite={limite} passou={Boolean(erro)} />
    </div>
  )
}
