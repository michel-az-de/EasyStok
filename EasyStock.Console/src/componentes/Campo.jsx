import { useId } from 'react'
import css from './Campo.module.css'

const descricao = (id, dica, adicional) => [dica && `${id}-dica`, adicional].filter(Boolean).join(' ') || undefined

function Envelope({ id, rotulo, rotuloOculto, dica, children }) {
  return (
    <div>
      <label className={rotuloOculto ? 'sr' : css.rotulo} htmlFor={id}>{rotulo}</label>
      {children}
      {dica && <p id={`${id}-dica`} className={css.dica}>{dica}</p>}
    </div>
  )
}

export function CampoTexto({ rotulo, rotuloOculto, dica, tipo = 'text', ...resto }) {
  const id = useId()
  return (
    <Envelope id={id} rotulo={rotulo} rotuloOculto={rotuloOculto} dica={dica}>
      <input id={id} type={tipo} className={css.controle} {...resto} aria-describedby={descricao(id, dica, resto['aria-describedby'])} />
    </Envelope>
  )
}

export function CampoArea({
  rotulo, rotuloOculto, dica, className, ...resto
}) {
  const id = useId()
  return (
    <Envelope id={id} rotulo={rotulo} rotuloOculto={rotuloOculto} dica={dica}>
      <textarea
        id={id}
        className={[css.controle, css.area, className].filter(Boolean).join(' ')}
        {...resto}
        aria-describedby={descricao(id, dica, resto['aria-describedby'])}
      />
    </Envelope>
  )
}

// Opções chegam como dados. O componente não conhece canal, estado nem janela.
export function CampoSelecao({ rotulo, rotuloOculto, dica, opcoes, ...resto }) {
  const id = useId()
  return (
    <Envelope id={id} rotulo={rotulo} rotuloOculto={rotuloOculto} dica={dica}>
      <select id={id} className={css.controle} {...resto} aria-describedby={descricao(id, dica, resto['aria-describedby'])}>
        {opcoes.map((o) => (
          <option key={o.valor} value={o.valor} disabled={o.desabilitada}>{o.rotulo}</option>
        ))}
      </select>
    </Envelope>
  )
}
