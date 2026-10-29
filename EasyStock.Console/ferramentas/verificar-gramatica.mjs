// Guarda da gramatica do console (#2145, plano docs/plan/balcao-ux, secoes 3.2 e 3.3).
// Quatro contagens com teto. O teto comeca no numero medido no master quando a guarda
// nasceu e so desce: quem limpa uma ocorrencia baixa o teto no mesmo PR, quem cria uma
// nova reprova aqui.
//
//   a. chave de icone usada no codigo que nao esta no dicionario do `Icone`
//      (componentes/Icone.jsx): renderizaria nada, em silencio;
//   b. `<button` cru em features/atendimento e features/caixa-de-entrada, onde o botao
//      passa a ser `Botao`/`BotaoIcone`;
//   c. termo interno na tela (secao 3.1): texto JSX visivel e os atributos
//      aria-label, title e placeholder. Comentario, import, classe e identificador
//      nao contam;
//   d. duracao (ms, s) ou easing (cubic-bezier, ease*) literal em transition/animation
//      nos .css de src/. `var(--...)` nao conta e tokens.css fica fora: e la que os
//      tokens de movimento moram.
//
// Le o codigo como texto, sem dependencia. As contagens b, c e d sao heuristicas de
// proposito: o que importa e que o numero so desca.
//
// Roda com: node ferramentas/verificar-gramatica.mjs
// Sai com 1 quando alguma contagem passa do teto; quando fica abaixo, pede para baixar.

import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'

const TETOS = {
  chaveForaDoDicionario: 0,
  botaoCru: 16,
  termoProibido: 37,
  movimentoLiteral: 40,
}

const CONSOLE = fileURLToPath(new URL('..', import.meta.url))
const RAIZ = join(CONSOLE, 'src')
const ICONE = join(RAIZ, 'componentes', 'Icone.jsx')
const MARCA_ICONE = join(RAIZ, 'componentes', 'MarcaIcone.jsx')
const PASTAS_SEM_BOTAO_CRU = ['features/atendimento', 'features/caixa-de-entrada']
const TOKENS = 'estilos/tokens.css'
const TERMOS = [
  ['Canhoto', /(?<![\p{L}\d_])[Cc]anhotos?(?![\p{L}\d_])/u],
  ['Lead', /(?<![\p{L}\d_])[Ll]eads?(?![\p{L}\d_])/u],
  // "Peça" e a peca da galeria; o verbo ("peça à dona", "peça o endereço") nao conta.
  ['Peça', /(?<![\p{L}\d_])[Pp]eças?(?![\p{L}\d_])(?! (?:à|ao|aos|a|o|os|um|uma|que|para)(?![\p{L}\d_]))/u],
  ['esteira', /(?<![\p{L}\d_])[Ee]steiras?(?![\p{L}\d_])/u],
  ['Console', /(?<![\p{L}\d_])[Cc]onsole(?![\p{L}\d_])/u],
  ['Hall', /(?<![\p{L}\d_])[Hh]all(?![\p{L}\d_])/u],
  ['Trilho', /(?<![\p{L}\d_])[Tt]rilhos?(?![\p{L}\d_])/u],
  ['a dona', /(?<![\p{L}\d_])[Aa] dona(?![\p{L}\d_])/u],
]

const arquivos = (dir, extensao) => readdirSync(dir).flatMap((nome) => {
  const caminho = join(dir, nome)
  if (statSync(caminho).isDirectory()) return arquivos(caminho, extensao)
  return extensao.test(caminho) ? [caminho] : []
})

const relativo = (caminho) => relative(RAIZ, caminho).replaceAll('\\', '/')
const linhaDe = (texto, indice) => texto.slice(0, indice).split('\n').length

// Troca comentario por espaco, preservando quebras de linha (o indice continua valendo
// para a linha). Pula strings para nao confundir `https://` com comentario.
function semComentarios(codigo, { css = false } = {}) {
  let saida = ''
  for (let i = 0; i < codigo.length; i++) {
    const c = codigo[i]
    const proximo = codigo[i + 1]
    if (!css && (c === "'" || c === '"' || c === '`')) {
      let j = i + 1
      while (j < codigo.length && codigo[j] !== c && codigo[j] !== '\n') j += codigo[j] === '\\' ? 2 : 1
      if (c === '`') while (j < codigo.length && codigo[j] !== '`') j += codigo[j] === '\\' ? 2 : 1
      saida += codigo.slice(i, j + 1)
      i = j
    } else if (c === '/' && proximo === '*') {
      const fim = codigo.indexOf('*/', i + 2)
      const trecho = codigo.slice(i, fim < 0 ? codigo.length : fim + 2)
      saida += trecho.replace(/[^\n]/g, ' ')
      i += trecho.length - 1
    } else if (!css && c === '/' && proximo === '/' && codigo[i - 1] !== ':') {
      const fim = codigo.indexOf('\n', i)
      const trecho = codigo.slice(i, fim < 0 ? codigo.length : fim)
      saida += ' '.repeat(trecho.length)
      i += trecho.length - 1
    } else {
      saida += c
    }
  }
  return saida
}

// Conteudo de `{ ... }` a partir da chave de abertura em `inicio`, com chaves aninhadas.
function blocoBalanceado(texto, inicio) {
  let profundidade = 0
  for (let i = inicio; i < texto.length; i++) {
    if (texto[i] === '{') profundidade++
    else if (texto[i] === '}' && --profundidade === 0) return texto.slice(inicio + 1, i)
  }
  return texto.slice(inicio + 1)
}

// Literais de string de uma expressao, menos os que so servem de comparacao
// (`meio.id === 'pix' ? 'dollar-sign' : 'credit-card'` rende as duas chaves, nao 'pix').
const literais = (expressao) => [...expressao
  .replace(/(===|!==|==|!=)\s*(['"])[^'"]*\2/g, '')
  .replace(/(['"])[^'"]*\1\s*(===|!==|==|!=)/g, '')
  .matchAll(/(['"])([^'"\n]+)\1/g)].map((m) => m[2])

// Valor de um atributo JSX `nome=` em `codigo`, a partir do indice do `=`.
function valorDoAtributo(codigo, indiceIgual) {
  const resto = codigo.slice(indiceIgual + 1)
  const aspas = resto.match(/^\s*(['"])([^'"]*)\1/)
  if (aspas) return { literais: [aspas[2]], expressao: null }
  const abre = resto.search(/\S/)
  if (resto[abre] !== '{') return { literais: [], expressao: null }
  const expressao = blocoBalanceado(resto, abre)
  return { literais: literais(expressao), expressao }
}

const chavesDoBloco = (codigo, nomeDaConstante) => {
  const inicio = codigo.indexOf(`const ${nomeDaConstante} = {`)
  if (inicio < 0) return []
  const bloco = blocoBalanceado(codigo, codigo.indexOf('{', inicio))
  return [...bloco.matchAll(/(?:^|[\s,{])(?:'([^']+)'|([A-Za-z_]\w*))\s*:/g)].map((m) => m[1] ?? m[2])
}

// --- a. chave de icone fora do dicionario -------------------------------------------

function contarChavesForaDoDicionario(fontes) {
  const codigoDoIcone = semComentarios(readFileSync(ICONE, 'utf8'))
  const dicionario = new Set([...chavesDoBloco(codigoDoIcone, 'ICONES'), ...chavesDoBloco(codigoDoIcone, 'PROPRIOS')])
  dicionario.delete('...PROPRIOS')
  if (dicionario.size === 0) throw new Error('Dicionario do Icone nao encontrado em componentes/Icone.jsx (const ICONES = { ... }).')
  // Chaves do MarcaIcone (geometria da marca) valem so em arquivo que importa o MarcaIcone.
  const marca = new Set(chavesDoBloco(readFileSync(MARCA_ICONE, 'utf8'), 'DESENHOS'))

  const faltas = []
  for (const arquivo of fontes) {
    if (arquivo === ICONE || arquivo === MARCA_ICONE) continue
    const codigo = semComentarios(readFileSync(arquivo, 'utf8'))
    const usaMarca = /import\s*\{[^}]*\bMarcaIcone\b/.test(codigo)
    const usos = []
    // `<Icone nome=...>` (nao `<IconeDoModulo`).
    for (const tag of codigo.matchAll(/<Icone(?=[\s/>])/g)) {
      const fimDaTag = codigo.indexOf('>', tag.index)
      const nome = /\snome=/.exec(codigo.slice(tag.index, fimDaTag + 1))
      if (nome) usos.push(...valorDoAtributo(codigo, tag.index + nome.index + nome[0].length - 1).literais.map((k) => [k, tag.index]))
    }
    // `icone="..."`/`icone={...}` passado a Botao, Chip, Pilula, Vazio, Cartao e cia.
    for (const m of codigo.matchAll(/\sicone=/g)) {
      const valor = valorDoAtributo(codigo, m.index + m[0].length - 1)
      if (valor.expressao?.trimStart().startsWith('<')) continue
      usos.push(...valor.literais.map((k) => [k, m.index]))
    }
    // `icone: 'x'` em objeto e `iconeAlgo = 'x'` em variavel.
    for (const m of codigo.matchAll(/\bicone(?:[A-Z]\w*\s*=|\s*:)\s*(['"])([^'"\n]+)\1/g)) usos.push([m[2], m.index])
    // Tabelas `const ICONE_X = { chave: 'icone' }`.
    for (const m of codigo.matchAll(/const ICONE\w*\s*=\s*\{/g)) {
      const bloco = blocoBalanceado(codigo, m.index + m[0].length - 1)
      for (const v of bloco.matchAll(/:\s*(['"])([^'"\n]+)\1/g)) usos.push([v[2], m.index])
    }
    for (const [chave, indice] of usos) {
      if (dicionario.has(chave) || (usaMarca && marca.has(chave))) continue
      faltas.push(`${relativo(arquivo)}:${linhaDe(codigo, indice)} chave "${chave}"`)
    }
  }
  return faltas
}

// --- b. <button cru -------------------------------------------------------------------

function contarBotoesCrus(fontes) {
  const faltas = []
  for (const arquivo of fontes) {
    if (!PASTAS_SEM_BOTAO_CRU.some((pasta) => relativo(arquivo).startsWith(pasta + '/'))) continue
    const codigo = semComentarios(readFileSync(arquivo, 'utf8'))
    for (const m of codigo.matchAll(/<button(?=[\s>])/g)) faltas.push(`${relativo(arquivo)}:${linhaDe(codigo, m.index)}`)
  }
  return faltas
}

// --- c. termo proibido na tela --------------------------------------------------------

function contarTermosProibidos(fontes) {
  const faltas = []
  for (const arquivo of fontes) {
    if (!arquivo.endsWith('.jsx')) continue
    const codigo = semComentarios(readFileSync(arquivo, 'utf8'))
    const trechos = []
    // Texto entre tags: `>texto<`, `>texto{`, `}texto<`. O `>` nao pode ser de `=>`/`>=`.
    // Trecho com `=` ou `;` e codigo entre duas expressoes, nao texto.
    for (const m of codigo.matchAll(/(?<![=-])>([^<>{}]+)(?=<|\{)|\}([^<>{}]+)(?=<\/?[A-Za-z])/g)) {
      const texto = m[1] ?? m[2]
      if (/\p{L}/u.test(texto) && !/[=;]/.test(texto)) trechos.push([texto, m.index + 1])
    }
    for (const m of codigo.matchAll(/\s(aria-label|title|placeholder)=/g)) {
      const indice = m.index + m[0].length - 1
      const { literais: textos, expressao } = valorDoAtributo(codigo, indice)
      const crases = expressao ? [...expressao.matchAll(/`([^`]*)`/g)].map((t) => t[1].replace(/\$\{[^}]*\}/g, ' ')) : []
      for (const texto of [...textos, ...crases]) trechos.push([texto, m.index])
    }
    for (const [texto, indice] of trechos) {
      for (const [termo, padrao] of TERMOS) {
        for (const achado of texto.matchAll(new RegExp(padrao.source, 'gu'))) {
          const linha = linhaDe(codigo, indice) + texto.slice(0, achado.index).split('\n').length - 1
          faltas.push(`${relativo(arquivo)}:${linha} "${termo}"`)
        }
      }
    }
  }
  return faltas
}

// --- d. duracao ou easing literal no CSS ----------------------------------------------

function contarMovimentoLiteral(estilos) {
  const faltas = []
  for (const arquivo of estilos) {
    if (relativo(arquivo) === TOKENS) continue
    const codigo = semComentarios(readFileSync(arquivo, 'utf8'), { css: true })
    for (const m of codigo.matchAll(/(?<![\w-])(transition|animation)(-[a-z-]+)?\s*:\s*([^;}]+)/g)) {
      let valor = m[3]
      // `var(--x)` e `var(--x, 200ms)` nao contam: o literal e problema do token.
      while (/var\([^()]*\)/.test(valor)) valor = valor.replace(/var\([^()]*\)/g, ' ')
      if (/(?<![\w.-])\d*\.?\d+m?s(?![\w-])|cubic-bezier\(|(?<![\w-])ease(-in-out|-in|-out)?(?![\w-])/.test(valor)) {
        faltas.push(`${relativo(arquivo)}:${linhaDe(codigo, m.index)} ${m[1]}${m[2] ?? ''}: ${m[3].trim()}`)
      }
    }
  }
  return faltas
}

// --- tetos ----------------------------------------------------------------------------

const fontes = arquivos(RAIZ, /\.(jsx?|mjs)$/)
const estilos = arquivos(RAIZ, /\.css$/)

const GUARDAS = [
  ['chaveForaDoDicionario', 'chave de icone fora do dicionario do Icone', contarChavesForaDoDicionario(fontes)],
  ['botaoCru', '<button cru em atendimento e caixa de entrada', contarBotoesCrus(fontes)],
  ['termoProibido', 'termo interno na tela (secao 3.1)', contarTermosProibidos(fontes)],
  ['movimentoLiteral', 'duracao ou easing literal em transition/animation', contarMovimentoLiteral(estilos)],
]

let reprovou = false
for (const [chave, descricao, achados] of GUARDAS) {
  const teto = TETOS[chave]
  const n = achados.length
  if (n > teto) {
    reprovou = true
    console.error(`Gramatica: ${descricao}: ${n}, acima do teto ${teto}.`)
    console.error(achados.map((a) => '  ' + a).join('\n'))
    console.error(`  Corrija a ocorrencia nova; o teto (TETOS.${chave} em ferramentas/verificar-gramatica.mjs) so desce.`)
  } else if (n < teto) {
    console.log(`Gramatica: ${descricao}: ${n}, abaixo do teto ${teto}; baixe o teto para ${n} (TETOS.${chave}).`)
  } else {
    console.log(`Gramatica: ${descricao}: ${n}, no teto.`)
  }
}

if (reprovou) process.exit(1)
console.log('Gramatica: ok.')
