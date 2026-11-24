import { useId } from 'react'
import css from './Campo.module.css'
import { erroDoLimite } from '../componentes/limiteDoCampo'

// #2810: o que aparece embaixo do campo. A frase do limite não some com `erro` (o botão segue travado) e não tira
// a dica do lugar; `erro` segue no lugar da dica e o `aviso` de quem chama segue cedendo ao limite.
const textosDoCampo = ({ erro, passou, aviso, dica }) => {
  const avisoNaTela = !erro && !passou && aviso
  return { erro, passou, aviso: avisoNaTela, dica: !erro && !avisoNaTela && dica }
}
const descricao = (id, textos, adicional) => [
  textos.erro && `${id}-erro`, textos.passou && `${id}-limite`, textos.aviso && `${id}-aviso`, textos.dica && `${id}-dica`, adicional,
].filter(Boolean).join(' ') || undefined

// #2258 (auditoria T12, padrão "erro embaixo do próprio campo"): `erro` liga borda, aria-invalid e
// aria-describedby no próprio campo, e o texto do erro fica colado nele, no lugar da dica.
// #2457 (U4): `obrigatorio` mostra o * fora do nome acessível e liga aria-required no campo.
// #2734 (A27): `aviso` é o aviso de limite (ex.: caracteres a mais): `role="status"`, para não reanunciar a cada tecla;
// só `erro` de validação é `alert`. O aviso não marca o campo como inválido.
// #2792: `limite` substitui o `maxLength` (que cortava o texto colado em silêncio): o texto fica inteiro, a frase
// "Use até N caracteres. Agora tem M." sai num parágrafo próprio (`-limite`, sem role, ligado pelo aria-describedby)
// e o campo fica `aria-invalid`. Quem envia trava o botão com `algumPassaDoLimite` (componentes/limiteDoCampo.js).
// A contagem muda a cada tecla, então ela não é região viva: o leitor de tela ouve uma vez "Passou do limite de N
// caracteres." (região própria, texto estável) e lê a contagem pelo aria-describedby do campo.
function Envelope({ id, rotulo, rotuloOculto, textos, limite, obrigatorio, children }) {
  return (
    <div className={css.campo}>
      <label className={rotuloOculto ? 'sr' : css.rotulo} htmlFor={id}>{rotulo}{obrigatorio && <span aria-hidden="true"> *</span>}</label>
      {children}
      {textos.erro && <p id={`${id}-erro`} className={css.erro} role="alert">{textos.erro}</p>}
      {textos.passou && <p id={`${id}-limite`} className={css.erro}>{textos.passou}</p>}
      {textos.aviso && <p id={`${id}-aviso`} className={css.erro} role="status">{textos.aviso}</p>}
      {textos.dica && <p id={`${id}-dica`} className={css.dica}>{textos.dica}</p>}
      {limite != null && <AnuncioDoLimite limite={limite} passou={Boolean(textos.passou)} />}
    </div>
  )
}

export function AnuncioDoLimite({ limite, passou }) {
  return <span className="sr" role="status">{passou ? `Passou do limite de ${limite} caracteres.` : ''}</span>
}

export function CampoTexto({ rotulo, rotuloOculto, dica, erro, aviso, limite, obrigatorio, tipo = 'text', id: idInformado, className, ...resto }) {
  const idGerado = useId()
  const id = idInformado ?? idGerado
  const passou = erroDoLimite(resto.value, limite)
  const textos = textosDoCampo({ erro, passou, aviso, dica })
  return (
    <Envelope id={id} rotulo={rotulo} rotuloOculto={rotuloOculto} textos={textos} limite={limite} obrigatorio={obrigatorio}>
      <input
        id={id} type={tipo} className={[css.controle, className].filter(Boolean).join(' ')}
        aria-invalid={erro || passou ? true : undefined} aria-required={obrigatorio || undefined} {...resto}
        aria-describedby={descricao(id, textos, resto['aria-describedby'])}
      />
    </Envelope>
  )
}

export function CampoArea({
  rotulo, rotuloOculto, dica, erro, aviso, limite, obrigatorio, className, id: idInformado, ...resto
}) {
  const idGerado = useId()
  const id = idInformado ?? idGerado
  const passou = erroDoLimite(resto.value, limite)
  const textos = textosDoCampo({ erro, passou, aviso, dica })
  return (
    <Envelope id={id} rotulo={rotulo} rotuloOculto={rotuloOculto} textos={textos} limite={limite} obrigatorio={obrigatorio}>
      <textarea
        id={id}
        className={[css.controle, css.area, className].filter(Boolean).join(' ')}
        aria-invalid={erro || passou ? true : undefined} aria-required={obrigatorio || undefined} {...resto}
        aria-describedby={descricao(id, textos, resto['aria-describedby'])}
      />
    </Envelope>
  )
}

// Opções chegam como dados. O componente não conhece canal, estado nem janela.
export function CampoSelecao({ rotulo, rotuloOculto, dica, erro, obrigatorio, opcoes, id: idInformado, className, ...resto }) {
  const idGerado = useId()
  const id = idInformado ?? idGerado
  const textos = textosDoCampo({ erro, dica })
  return (
    <Envelope id={id} rotulo={rotulo} rotuloOculto={rotuloOculto} textos={textos} obrigatorio={obrigatorio}>
      <select id={id} className={[css.controle, className].filter(Boolean).join(' ')} aria-invalid={erro ? true : undefined} aria-required={obrigatorio || undefined} {...resto} aria-describedby={descricao(id, textos, resto['aria-describedby'])}>
        {opcoes.map((o) => (
          <option key={o.valor} value={o.valor} disabled={o.desabilitada}>{o.rotulo}</option>
        ))}
      </select>
    </Envelope>
  )
}
