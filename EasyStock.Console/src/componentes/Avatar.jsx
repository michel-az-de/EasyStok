import css from './Avatar.module.css'

// Foto do contato. Quando a integração existir, `foto` vem da mensageria e a
// inicial some. Sem foto, o fallback é determinístico: a mesma pessoa recebe
// sempre a mesma cor, então o reconhecimento visual funciona mesmo assim.

const MATIZES = [8, 32, 96, 150, 190, 262, 300, 338]

const iniciais = (nome) => nome
  .split(' ')
  .filter((p) => p.length > 2)
  .slice(0, 2)
  .map((p) => p[0].toUpperCase())
  .join('')

function matizDe(nome) {
  const soma = [...nome].reduce((acc, ch) => acc + ch.charCodeAt(0), 0)
  return MATIZES[soma % MATIZES.length]
}

export function Avatar({ nome, foto, tamanho = 'medio', selo }) {
  const estilo = { '--matiz': matizDe(nome) }
  return (
    <span className={`${css.envelope} ${css[tamanho]}`}>
      {foto
        ? <img className={css.foto} src={foto} alt={'Foto de ' + nome} />
        : (
          <span className={css.inicial} style={estilo} aria-hidden="true">
            {iniciais(nome) || '?'}
          </span>
        )}
      {selo && <span className={css.selo} title={selo.titulo}>{selo.icone}</span>}
    </span>
  )
}
