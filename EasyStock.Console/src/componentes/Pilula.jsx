import css from './Pilula.module.css'
import { Icone } from './Icone'

// Rótulo curto de estado. A cor nunca informa sozinha: o texto sempre vai junto.
//
// `icone` é opt-in (seção 1 da direção, rodada 5): o cartão do Balcão pede uma
// marca com fundo pastel e ícone de 20 px, mas as pílulas mais antigas (barra
// de modo, composer, ficha) continuam do jeito que estão — passar o ícone é o
// que liga o fundo novo, sem ele nada muda para quem já chama este componente.
//
// #2185 (R04): `contagem` é o contador do app, um desenho só (18 px, número em negrito tabular): o
// número da lista, do trilho, das abas, do sino e dos chips. `tom` vale `neutro` (quantidade), `marca`
// (o que espera resposta) ou `perigo` (vencido ou estourado). O lugar dele (canto do ícone, por exemplo)
// é do `className` de quem chama; o desenho, não.
const TONS_DA_CONTAGEM = Object.freeze({ neutro: 'neutro', marca: 'marca', perigo: 'perigo' })

//
// #2185 (R05): `tamanho="inline"` é o estado em linha miúda (cartão da lista, ficha): 13 px, ícone de 16,
// fundo do tom, e o texto longo corta com reticência. `fina` é o rótulo de apoio, sem peso: vale quando o
// estado não pede ação. O estado de um item é sempre este componente, nunca uma classe de tela.
export function Pilula({ tom = 'neutro', fina = false, tamanho, icone, titulo, contagem, className, children, ...resto }) {
  if (contagem !== undefined && contagem !== null) {
    const tomDaContagem = TONS_DA_CONTAGEM[tom] ?? 'neutro'
    return (
      <span className={[css.contagem, css['contagem_' + tomDaContagem], className].filter(Boolean).join(' ')} title={titulo} {...resto}>
        {contagem}
      </span>
    )
  }
  const emLinha = tamanho === 'inline'
  const classes = [
    css.pilula, css[tom], fina ? css.fina : '', icone && !emLinha ? css.comIcone : '', emLinha ? css.inline : '', className,
  ].filter(Boolean).join(' ')
  return (
    <span className={classes} title={titulo} {...resto}>
      {icone && <Icone nome={icone} tamanho={emLinha ? 16 : 20} />}
      {emLinha ? <span className={css.texto}>{children}</span> : children}
    </span>
  )
}
