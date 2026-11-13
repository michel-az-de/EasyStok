import { useId } from 'react'
import css from './Campo.module.css'

const descricao = (id, dica, adicional, erro) => [erro ? `${id}-erro` : dica && `${id}-dica`, adicional].filter(Boolean).join(' ') || undefined

// #2258 (auditoria T12, padrão "erro embaixo do próprio campo"): `erro` liga borda, aria-invalid e
// aria-describedby no próprio campo, e o texto do erro fica colado nele, no lugar da dica.
// #2457 (U4): `obrigatorio` mostra o * fora do nome acessível e liga aria-required no campo.
function Envelope({ id, rotulo, rotuloOculto, dica, erro, obrigatorio, children }) {
  return (
    <div className={css.campo}>
      <label className={rotuloOculto ? 'sr' : css.rotulo} htmlFor={id}>{rotulo}{obrigatorio && <span aria-hidden="true"> *</span>}</label>
      {children}
      {erro
        ? <p id={`${id}-erro`} className={css.erro} role="alert">{erro}</p>
        : dica && <p id={`${id}-dica`} className={css.dica}>{dica}</p>}
    </div>
  )
}

export function CampoTexto({ rotulo, rotuloOculto, dica, erro, obrigatorio, tipo = 'text', id: idInformado, className, ...resto }) {
  const idGerado = useId()
  const id = idInformado ?? idGerado
  return (
    <Envelope id={id} rotulo={rotulo} rotuloOculto={rotuloOculto} dica={dica} erro={erro} obrigatorio={obrigatorio}>
      <input
        id={id} type={tipo} className={[css.controle, className].filter(Boolean).join(' ')}
        aria-invalid={erro ? true : undefined} aria-required={obrigatorio || undefined} {...resto}
        aria-describedby={descricao(id, dica, resto['aria-describedby'], erro)}
      />
    </Envelope>
  )
}

export function CampoArea({
  rotulo, rotuloOculto, dica, erro, obrigatorio, className, id: idInformado, ...resto
}) {
  const idGerado = useId()
  const id = idInformado ?? idGerado
  return (
    <Envelope id={id} rotulo={rotulo} rotuloOculto={rotuloOculto} dica={dica} erro={erro} obrigatorio={obrigatorio}>
      <textarea
        id={id}
        className={[css.controle, css.area, className].filter(Boolean).join(' ')}
        aria-invalid={erro ? true : undefined} aria-required={obrigatorio || undefined} {...resto}
        aria-describedby={descricao(id, dica, resto['aria-describedby'], erro)}
      />
    </Envelope>
  )
}

// Opções chegam como dados. O componente não conhece canal, estado nem janela.
export function CampoSelecao({ rotulo, rotuloOculto, dica, opcoes, id: idInformado, className, ...resto }) {
  const idGerado = useId()
  const id = idInformado ?? idGerado
  return (
    <Envelope id={id} rotulo={rotulo} rotuloOculto={rotuloOculto} dica={dica}>
      <select id={id} className={[css.controle, className].filter(Boolean).join(' ')} {...resto} aria-describedby={descricao(id, dica, resto['aria-describedby'])}>
        {opcoes.map((o) => (
          <option key={o.valor} value={o.valor} disabled={o.desabilitada}>{o.rotulo}</option>
        ))}
      </select>
    </Envelope>
  )
}
