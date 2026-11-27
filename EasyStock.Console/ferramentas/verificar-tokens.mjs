// Guard de tokens do Atendimento (#3374, C1): nos `.module.css` de atendimento, caixa-de-entrada e ficha-cliente,
// espaçamento e cor vêm de `var(--…)` (escala `--e-1..--e-6` e tokens de cor de `estilos/tokens.css`), nunca de valor
// literal. Acusa `gap`, `padding*`, `margin*` com px/rem/em, e `color`, `background*`, `border*` com hex, rgb/hsl/oklch,
// color-mix, light-dark ou nome de cor.
// Permitidos: 0, auto, inherit, initial, unset, transparent, currentColor, none, e `1px` em borda (largura do traço).
//
//   node ferramentas/verificar-tokens.mjs            erro: sai 1 se houver ocorrência (a C7 ligou no `qualidade`)
//   TOKENS_ESTRITO=0 node ferramentas/verificar-tokens.mjs   aviso: só imprime a contagem e sai 0
//   TOKENS_DETALHE=1 ...                             lista cada ocorrência com arquivo:linha
import { readdirSync, readFileSync } from 'node:fs'
import { join, relative, resolve } from 'node:path'

const raiz = resolve(import.meta.dirname, '..')
const PASTAS = ['src/features/atendimento', 'src/features/caixa-de-entrada', 'src/features/ficha-cliente']
const NOMES_DE_COR = 'aliceblue antiquewhite aqua aquamarine azure beige bisque black blanchedalmond blue blueviolet brown burlywood cadetblue chartreuse chocolate coral cornflowerblue cornsilk crimson cyan darkblue darkcyan darkgoldenrod darkgray darkgreen darkgrey darkkhaki darkmagenta darkolivegreen darkorange darkorchid darkred darksalmon darkseagreen darkslateblue darkslategray darkslategrey darkturquoise darkviolet deeppink deepskyblue dimgray dimgrey dodgerblue firebrick floralwhite forestgreen fuchsia gainsboro ghostwhite gold goldenrod gray green greenyellow grey honeydew hotpink indianred indigo ivory khaki lavender lavenderblush lawngreen lemonchiffon lightblue lightcoral lightcyan lightgoldenrodyellow lightgray lightgreen lightgrey lightpink lightsalmon lightseagreen lightskyblue lightslategray lightslategrey lightsteelblue lightyellow lime limegreen linen magenta maroon mediumaquamarine mediumblue mediumorchid mediumpurple mediumseagreen mediumslateblue mediumspringgreen mediumturquoise mediumvioletred midnightblue mintcream mistyrose moccasin navajowhite navy oldlace olive olivedrab orange orangered orchid palegoldenrod palegreen paleturquoise palevioletred papayawhip peachpuff peru pink plum powderblue purple rebeccapurple red rosybrown royalblue saddlebrown salmon sandybrown seagreen seashell sienna silver skyblue slateblue slategray slategrey snow springgreen steelblue tan teal thistle tomato turquoise violet wheat white whitesmoke yellow yellowgreen'.split(' ')
const REGEX_NOME = new RegExp(`(?<![\\w-])(?:${NOMES_DE_COR.join('|')})(?![\\w-])`, 'i')

const ESPACO = /^(?:gap|row-gap|column-gap|padding(?:-[\w-]+)?|margin(?:-[\w-]+)?)$/
const COR = /^(?:color|background(?:-color)?|border-color|border-(?:top|right|bottom|left|block|inline)(?:-[\w-]+)?-color|outline-color)$/
const BORDA = /^(?:border|border-(?:top|right|bottom|left|block|inline)(?:-[\w-]+)?|outline)$/ // atalho: só a parte de cor conta

// Tira var(...) por inteiro (com o valor de reserva dentro), com parênteses balanceados.
function semVar(valor) {
  let saida = ''
  for (let i = 0; i < valor.length;) {
    if (valor.startsWith('var(', i)) {
      let nivel = 0
      do { if (valor[i] === '(') nivel++; else if (valor[i] === ')') nivel--; i++ } while (nivel > 0 && i < valor.length)
      saida += ' '
    } else saida += valor[i++]
  }
  return saida
}
const literalDeEspaco = (v) => /(?<![\w.-])-?(?:\d*\.)?\d*[1-9]\d*(?:px|rem|em)\b/.test(v)
const literalDeCor = (v) => /#[\da-f]{3,8}\b/i.test(v) || /\b(?:rgba?|hsla?|hwb|lab|lch|oklab|oklch|color|color-mix|light-dark)\(/i.test(v) || REGEX_NOME.test(v.replace(/url\([^)]*\)/gi, ''))

function arquivos(pasta) {
  return readdirSync(resolve(raiz, pasta), { recursive: true }).filter((f) => f.endsWith('.module.css')).map((f) => join(pasta, f))
}

const porArquivo = new Map()
const detalhes = []
for (const arquivo of PASTAS.flatMap(arquivos).sort()) {
  const fonte = readFileSync(resolve(raiz, arquivo), 'utf8')
  const limpa = fonte.replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, ' ')) // mantém as quebras para achar a linha
  let n = 0
  for (const m of limpa.matchAll(/(?<![\w-])([a-z-]+)\s*:\s*([^;{}]+)(?=;|})/g)) {
    const [, prop, bruto] = m
    const valor = semVar(bruto)
    let achou = false
    if (ESPACO.test(prop)) achou = literalDeEspaco(valor)
    else if (COR.test(prop)) achou = literalDeCor(valor)
    else if (BORDA.test(prop)) achou = literalDeCor(valor) // a largura 1px do traço é permitida
    if (!achou) continue
    n++
    detalhes.push(`${arquivo}:${limpa.slice(0, m.index).split('\n').length}  ${prop}: ${bruto.trim()}`)
  }
  porArquivo.set(arquivo, n)
}

const total = [...porArquivo.values()].reduce((s, n) => s + n, 0)
for (const [arquivo, n] of porArquivo) if (n > 0) console.log(`${String(n).padStart(4)}  ${relative('.', resolve(raiz, arquivo)).replaceAll('\\', '/')}`)
if (process.env.TOKENS_DETALHE === '1') console.log(detalhes.join('\n'))
console.log(`Tokens: ${total} valor(es) literal(is) de espaçamento ou cor em ${[...porArquivo.values()].filter((n) => n > 0).length} de ${porArquivo.size} arquivo(s).`)
if (total > 0 && process.env.TOKENS_ESTRITO !== '0') {
  console.error('Tokens: troque o valor literal por var(--e-1..--e-6) ou por um token de cor.')
  process.exit(1)
}
