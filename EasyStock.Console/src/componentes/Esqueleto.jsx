import css from './Esqueleto.module.css'

// #2762: um esqueleto só para o console. Antes cada tela escrevia o seu (Atendimento, Caixa) e o resto
// mostrava "Carregando…" em texto solto, que salta quando o dado entra.
// `forma`: `linha` (pedaço de texto, 1em de altura), `bloco` (linha de lista, cartão, imagem) ou
// `circulo` (avatar). `largura` e `altura` aceitam qualquer medida CSS; use token ou `ch`, nunca px solto.
// A peça é enfeite (aria-hidden). Quem anuncia a espera é o pai, ou o `EsqueletoDeLista` abaixo.
const medida = (v) => (typeof v === 'number' ? `${v}px` : v)

export function Esqueleto({ forma = 'bloco', largura, altura, className }) {
  const estilo = {}
  if (largura != null) estilo['--largura'] = medida(largura)
  if (altura != null) estilo['--altura'] = medida(altura)
  const classes = [css.esqueleto, css[forma], className].filter(Boolean).join(' ')
  return <span className={classes} style={estilo} aria-hidden="true" />
}

// Lista que ainda não chegou: `linhas` fileiras com a altura de uma linha de verdade. O texto
// (`children`, ex.: "Carregando as contas…") continua na página para o leitor de tela e para as provas;
// só deixa de ser a única coisa na tela.
export function EsqueletoDeLista({ linhas = 4, altura, children = 'Carregando…', className }) {
  return (
    <output className={[css.lista, className].filter(Boolean).join(' ')} aria-busy="true">
      <span className="sr">{children}</span>
      {Array.from({ length: linhas }, (_, i) => <Esqueleto key={i} forma="bloco" altura={altura} />)}
    </output>
  )
}
