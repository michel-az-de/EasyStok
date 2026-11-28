/* eslint-disable no-console */
// #2734: regra do dono (12/11/2026). Proibida barra ou borda colorida só de um lado de cartão, item, aviso ou chip.
// Destaque vem de contorno uniforme de 1px, fundo tingido, selo ou peso do texto. Divisor neutro de 1px é grade.
// Varre todo .css de src/ e lista arquivo:linha de: (a) border-left/right/inline-start/inline-end grossa (2px ou mais,
// ou token --trilho/--b-2/--b-3), de qualquer cor; (b) a mesma borda fina com cor que não seja a da grade
// (--linha, --linha-forte ou transparent; sem cor declarada vale a cor do texto, que também reprova);
// (c) border-*-color lateral avulso; (d) box-shadow inset só horizontal; (e) pseudo-elemento ::before/::after
// posicionado colado num lado, de cima a baixo, fino e pintado (#3393: a barra do menu ativo escapava por aqui).
import assert from 'node:assert/strict'
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

const LADO = '(?:left|right|inline-start|inline-end)'
const lado = new RegExp(String.raw`border-${LADO}\s*:\s*([^;}]*)`, 'g')
const ladoCor = new RegExp(String.raw`border-${LADO}-color\s*:`, 'g')
const sombra = /box-shadow\s*:\s*([^;}]*)/g
const neutro = /^(?:0|none|transparent)\b/
const grosso = (v) => /--(?:trilho|b-2|b-3)\b/.test(v) || /(?:^|[\s(])(?:[2-9]|\d{2,})(?:\.\d+)?px/.test(v)
// O que sobra do valor depois de tirar a espessura e o estilo é a cor.
const corDe = (v) => v.replace(/!important/g, ' ')
  .replace(/var\(--(?:b-[123]|trilho)\)|(?:\d*\.)?\d+(?:px|rem|em)\b|\b(?:thin|medium|thick)\b/g, ' ')
  .replace(/\b(?:solid|dashed|dotted|double|groove|ridge|inset|outset)\b/g, ' ').trim()
const corDaGrade = /^(?:var\(--linha(?:-forte)?\)|transparent)$/
// inset com deslocamento só no eixo x: os comprimentos iniciais (até 4) têm o primeiro diferente de 0 e o resto 0
// ou ausente (ex.: `inset 3px 0 0 <cor>` e a forma curta `inset 3px 0 <cor>`).
// #3393: espessura em token (`inset var(--trilho) 0 0 <cor>`) conta como comprimento diferente de 0.
const ESPESSURA = String.raw`var\(--(?:trilho|b-[123])\)`
const horizontal = (v) => v.split(/,(?![^(]*\))/).some((s) => {
  const m = s.trim().match(new RegExp(String.raw`^inset\s+((?:(?:-?[\d.]+(?:px|rem)?|${ESPESSURA})\s+){1,4})`))
  if (!m) return false
  const n = m[1].trim().split(/\s+/).map((x) => (x.startsWith('var(') ? 1 : parseFloat(x)))
  return n[0] !== 0 && n.slice(1, 3).every((x) => x === 0) && n.length < 4 + 1
})

function achadosDe(texto) {
  const linhaDe = (i) => texto.slice(0, i).split('\n').length
  const achados = []
  for (const m of texto.matchAll(lado)) {
    const v = m[1].trim()
    if (neutro.test(v)) continue
    if (grosso(v)) achados.push(`${linhaDe(m.index)}  borda lateral grossa: ${m[0].trim()}`)
    else if (!corDaGrade.test(corDe(v))) achados.push(`${linhaDe(m.index)}  borda lateral colorida: ${m[0].trim()}`)
  }
  // Cor lateral avulsa na cor da grade ou transparente não pinta barra: apaga um lado (ex.: o anel do giro de espera,
  // que é um círculo com um quarto vazio). Qualquer outra cor reprova, como em (b).
  for (const m of texto.matchAll(ladoCor)) {
    const cor = texto.slice(m.index + m[0].length).match(/^\s*([^;}]*)/)[1].replace(/!important/g, '').trim()
    if (!corDaGrade.test(cor)) achados.push(`${linhaDe(m.index)}  cor de borda lateral avulsa: ${m[0].trim()} ${cor}`)
  }
  for (const m of texto.matchAll(sombra)) {
    if (horizontal(m[1])) achados.push(`${linhaDe(m.index)}  sombra inset horizontal: ${m[0].trim()}`)
  }
  // (e) barra desenhada por pseudo-elemento: absoluto, colado num lado, com top e bottom, largura fina e fundo pintado.
  for (const m of texto.matchAll(/([^{}]*::?(?:before|after)[^{}]*)\{([^{}]*)\}/g)) {
    const corpo = m[2]
    const colado = new RegExp(String.raw`(?:^|[;\s])(?:inset-)?(?:${LADO})\s*:\s*0(?![\d.])`).test(corpo)
    const alto = /(?:^|[;\s])top\s*:/.test(corpo) && /(?:^|[;\s])bottom\s*:/.test(corpo)
    const fino = new RegExp(String.raw`(?:^|[;\s])width\s*:\s*(?:${ESPESSURA}|[1-6](?:\.\d+)?px)`).test(corpo)
    const pintado = /background(?:-color)?\s*:\s*(?!none|transparent)/.test(corpo)
    if (/position\s*:\s*absolute/.test(corpo) && colado && alto && fino && pintado) achados.push(`${linhaDe(m.index + m[1].length)}  barra lateral por pseudo-elemento: ${m[1].trim()}`)
  }
  return achados
}

// O detector prova a si mesmo antes de varrer: o que reprova e o que passa, caso a caso.
const AMOSTRAS = [
  ['.a { border-left: 3px solid var(--marca); }', 1],
  ['.a { border-left: var(--b-1) solid var(--alerta); }', 1],
  ['.a { border-right: 1px solid color-mix(in srgb, var(--erro) 30%, transparent); }', 1],
  ['.a { border-inline-start: var(--b-2) solid var(--linha); }', 1],
  ['.a { border-inline-end: var(--trilho) solid var(--marca); }', 1],
  ['.a { border-left: 1px solid; }', 1],
  ['.a { border-left: 1px solid currentColor; }', 1],
  ['.a { border-left-color: var(--marca); }', 1],
  ['.a { border-right-color: currentColor; }', 1],
  ['.a { border-inline-start-color: var(--alerta) !important; }', 1],
  ['.a { border: 2px solid currentColor; border-right-color: transparent; border-radius: 999px; }', 0],
  ['.a { border-left-color: var(--linha); }', 0],
  ['.a { box-shadow: inset 3px 0 0 var(--marca); }', 1],
  ['.a { box-shadow: inset 3px 0 var(--marca); }', 1],
  ['.a { border-left: var(--b-1) solid var(--linha); }', 0],
  ['.a { border-right: 1px solid var(--linha-forte); }', 0],
  ['.a { border-left: 1px solid transparent; }', 0],
  ['.a { border-left: 0; }', 0],
  ['.a { border-right: none; }', 0],
  ['.a { border: var(--b-1) solid var(--marca); }', 0],
  ['.a { border-left-width: 0; border-left-style: solid; }', 0],
  ['.a { box-shadow: 0 1px 2px rgb(0 0 0 / 0.1); }', 0],
  ['.a { box-shadow: inset 0 0 0 1px var(--marca); }', 0],
  ['.a { box-shadow: inset var(--trilho) 0 0 var(--marca), var(--sombra-media); }', 1],
  ['.a { box-shadow: inset 0 0 0 var(--b-1) var(--marca); }', 0],
  ['.a::before { content: ""; position: absolute; left: 0; top: 8px; bottom: 8px; width: var(--b-3); background: var(--acao); }', 1],
  ['.a::after { content: ""; position: absolute; inset-inline-start: 0; top: 0; bottom: 0; width: 4px; background-color: var(--marca); }', 1],
  ['.a::before { content: ""; position: absolute; inset: 0; background: var(--marca); }', 0],
  ['.a::before { content: ""; position: absolute; left: 0; top: 0; bottom: 0; width: 50%; background: var(--linha); }', 0],
]
for (const [amostra, esperado] of AMOSTRAS) assert.equal(achadosDe(amostra).length, esperado, amostra)

const raiz = fileURLToPath(new URL('../src', import.meta.url))
const arquivos = []
const varrer = (dir) => {
  for (const nome of readdirSync(dir)) {
    const caminho = join(dir, nome)
    if (statSync(caminho).isDirectory()) varrer(caminho)
    else if (nome.endsWith('.css')) arquivos.push(caminho)
  }
}
varrer(raiz)

const achados = []
for (const arquivo of arquivos) {
  const nome = relative(raiz, arquivo).replaceAll(sep, '/')
  for (const a of achadosDe(readFileSync(arquivo, 'utf8'))) achados.push(`${nome}:${a}`)
}

if (achados.length) {
  console.error(`prova-2734: ${achados.length} ocorrência(s) de barra lateral colorida:`)
  for (const a of achados) console.error('  ' + a)
  process.exit(1)
}
console.log(`prova-2734 ok: ${arquivos.length} arquivos .css sem barra lateral colorida (${AMOSTRAS.length} amostras do detector)`)
