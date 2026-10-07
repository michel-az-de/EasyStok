import { Icone } from './Icone'
import css from './AssuntoEmail.module.css'

// #1432: assunto do e-mail, no cartão da conversa e, com `noFio`, acima do
// balão do e-mail recebido. Sem assunto (os outros canais), não desenha nada.
// O texto cai em reticências quando não cabe; o `title` mostra inteiro.
export function AssuntoEmail({ assunto, noFio = false }) {
  if (!assunto) return null
  return (
    <span className={noFio ? `${css.assunto} ${css.fio}` : css.assunto} title={assunto}>
      <Icone nome="email" tamanho={14} />
      <span className="sr">Assunto: </span>
      <span className={css.texto}>{assunto}</span>
    </span>
  )
}
