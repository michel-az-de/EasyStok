import { useId } from 'react'
import css from './Campo.module.css'
import { erroDoLimite } from '../componentes/limiteDoCampo'

const descricao = (id, dica, adicional, erro, aviso) => [erro ? `${id}-erro` : aviso ? `${id}-aviso` : dica && `${id}-dica`, adicional].filter(Boolean).join(' ') || undefined

// #2258 (auditoria T12, padrão "erro embaixo do próprio campo"): `erro` liga borda, aria-invalid e
// aria-describedby no próprio campo, e o texto do erro fica colado nele, no lugar da dica.
// #2457 (U4): `obrigatorio` mostra o * fora do nome acessível e liga aria-required no campo.
// #2734 (A27): `aviso` é o aviso de limite (ex.: caracteres a mais): `role="status"`, para não reanunciar a cada tecla;
// só `erro` de validação é `alert`. O aviso não marca o campo como inválido.
// #2792: `limite` substitui o `maxLength` (que cortava o texto colado em silêncio): o texto fica inteiro, a frase
// "Use até N caracteres. Agora tem M." sai como aviso (`role="status"`, sem reanunciar a cada tecla) e o campo
// fica `aria-invalid`. Quem envia trava o botão com `algumPassaDoLimite` (componentes/limiteDoCampo.js).
function Envelope({ id, rotulo, rotuloOculto, dica, erro, aviso, obrigatorio, children }) {
  return (
    <div className={css.campo}>
      <label className={rotuloOculto ? 'sr' : css.rotulo} htmlFor={id}>{rotulo}{obrigatorio && <span aria-hidden="true"> *</span>}</label>
      {children}
      {erro
        ? <p id={`${id}-erro`} className={css.erro} role="alert">{erro}</p>
        : aviso
          ? <p id={`${id}-aviso`} className={css.erro} role="status">{aviso}</p>
          : dica && <p id={`${id}-dica`} className={css.dica}>{dica}</p>}
    </div>
  )
}

export function CampoTexto({ rotulo, rotuloOculto, dica, erro, aviso, limite, obrigatorio, tipo = 'text', id: idInformado, className, ...resto }) {
  const idGerado = useId()
  const id = idInformado ?? idGerado
  const passou = erro ? null : erroDoLimite(resto.value, limite)
  const avisoFinal = passou ?? aviso
  return (
    <Envelope id={id} rotulo={rotulo} rotuloOculto={rotuloOculto} dica={dica} erro={erro} aviso={avisoFinal} obrigatorio={obrigatorio}>
      <input
        id={id} type={tipo} className={[css.controle, className].filter(Boolean).join(' ')}
        aria-invalid={erro || passou ? true : undefined} aria-required={obrigatorio || undefined} {...resto}
        aria-describedby={descricao(id, dica, resto['aria-describedby'], erro, avisoFinal)}
      />
    </Envelope>
  )
}

export function CampoArea({
  rotulo, rotuloOculto, dica, erro, aviso, limite, obrigatorio, className, id: idInformado, ...resto
}) {
  const idGerado = useId()
  const id = idInformado ?? idGerado
  const passou = erro ? null : erroDoLimite(resto.value, limite)
  const avisoFinal = passou ?? aviso
  return (
    <Envelope id={id} rotulo={rotulo} rotuloOculto={rotuloOculto} dica={dica} erro={erro} aviso={avisoFinal} obrigatorio={obrigatorio}>
      <textarea
        id={id}
        className={[css.controle, css.area, className].filter(Boolean).join(' ')}
        aria-invalid={erro || passou ? true : undefined} aria-required={obrigatorio || undefined} {...resto}
        aria-describedby={descricao(id, dica, resto['aria-describedby'], erro, avisoFinal)}
      />
    </Envelope>
  )
}

// Opções chegam como dados. O componente não conhece canal, estado nem janela.
export function CampoSelecao({ rotulo, rotuloOculto, dica, erro, obrigatorio, opcoes, id: idInformado, className, ...resto }) {
  const idGerado = useId()
  const id = idInformado ?? idGerado
  return (
    <Envelope id={id} rotulo={rotulo} rotuloOculto={rotuloOculto} dica={dica} erro={erro} obrigatorio={obrigatorio}>
      <select id={id} className={[css.controle, className].filter(Boolean).join(' ')} aria-invalid={erro ? true : undefined} aria-required={obrigatorio || undefined} {...resto} aria-describedby={descricao(id, dica, resto['aria-describedby'], erro)}>
        {opcoes.map((o) => (
          <option key={o.valor} value={o.valor} disabled={o.desabilitada}>{o.rotulo}</option>
        ))}
      </select>
    </Envelope>
  )
}
