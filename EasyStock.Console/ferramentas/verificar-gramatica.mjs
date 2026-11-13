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
//      tokens de movimento moram;
//   e. menu de opcoes fora do `MenuDeAcoes` (#2185): icone de reticencias ou o rotulo
//      "Mais acoes" em outro componente. Eram 7 implementacoes, 5 rotulos e 3 icones.
//   f. vocabulario do Atendimento (#2185, R07 e R08): quem responde sozinho e o "Assistente"
//      (nao "automatico", nem "agente") e o fim da conversa e "Concluir conversa", "Concluida",
//      "Concluidas" (nao "Encerrada", nem "Encerrar atendimento"). "Automaticas" no plural e o
//      modulo de regras e continua valendo. Conta texto visivel (JSX, atributos e literais) nas
//      pastas e arquivos da tela; identificador, rota e chave de dado nao contam.
//   g. icone fora da escala (#2185, R03): `<Icone tamanho={N}>` so com 16, 20, 24 ou 32 e
//      `tamanhoIcone={N}` so com 16, 20 ou 24, e `svg { width }` em CSS no mesmo conjunto. Sem
//      `tamanho`, o Icone acompanha a fonte do pai e encaixa no degrau mais proximo.
//   i. contador desenhado a mao (#2185, R04): regra de CSS com numero tabular, altura ou largura
//      minima em px e raio fora de `Pilula.module.css`. O contador e a `Pilula contagem`; a folha
//      da tela so diz o lugar dele (position, margin).
//   j. fonte fora da escala (#2185, R09): `font-size` so por token (`--t-micro` 13, `--t-apoio` 14,
//      `--t-corpo` 15, `--t-forte` 17, `--t-titulo` 20, `--t-valor` 24), nas folhas da casca, dos
//      componentes e das telas do Atendimento. `--t-minimo` era `--t-micro` com outro nome e
//      deixou de existir. As excecoes tem motivo escrito em FONTE_EXCECOES.
//   k. referencia quebrada (#2185, R16): `css.nome` ou `css['nome']` que a folha .module.css do
//      componente nao define (o estilo some, em silencio) e `var(--token)` sem fallback que nenhuma
//      folha nem script define. Classe montada por concatenacao nao e lida.
//   l. raio e ponto fora do token (#2185, R17): `border-radius` so por `--r-*` (ou 0), e ponto de
//      estado so com `--ponto`, nas folhas da casca, dos componentes e das telas do Atendimento.
//   m. elevacao e borda das camadas (#2185, R19): camada flutuante (painel do Assistente, painel de
//      Lembretes) separa por sombra (`--sombra-media`, `--sombra-alta`) e por espaco, nunca por
//      borda de 1px nem pela sombra pequena de superficie.
//   n. largura de Modal fora das tres (#2185, R14): `largura="sm"`, `"md"` ou `"lg"` (tokens
//      --modal-sm/md/lg) nas telas do Atendimento, nos componentes e na casca. Numero ou `min(...)`
//      so com motivo escrito em LARGURA_DE_MODAL_EXCECOES.
//   h. botao so de icone fora do `BotaoIcone` (#2185, R02): `<Botao>` ou `<button>` cujo unico
//      filho e um `<Icone/>`, ou `<Botao icone=... />` sem texto. O BotaoIcone da o nome acessivel,
//      a dica e o alvo de `--toque`.
//   o. icone do ator na tela do Atendimento (#2185, plano secao 3.2): o Assistente e `sparkles`,
//      concluir e `circle-check`. `raio` fica com o modulo Automaticas e `log-out` com sair da conta.
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
  botaoCru: 11,
  termoProibido: 19,
  movimentoLiteral: 38,
  menuForaDoComponente: 0,
  atorForaDoVocabulario: 0,
  iconeForaDaEscala: 0,
  botaoSoIconeForaDoComponente: 23,
  contadorProprio: 0,
  fonteForaDaEscala: 0,
  referenciaQuebrada: 0,
  raioForaDoToken: 0,
  camadaComBordaOuSombraPequena: 0,
  larguraDeModalLiteral: 0,
  iconeDoAtorForaDoPlano: 0,
}

const CONSOLE = fileURLToPath(new URL('..', import.meta.url))
const RAIZ = join(CONSOLE, 'src')
const ICONE = join(RAIZ, 'componentes', 'Icone.jsx')
const MARCA_ICONE = join(RAIZ, 'componentes', 'MarcaIcone.jsx')
const PASTAS_SEM_BOTAO_CRU = ['features/atendimento', 'features/caixa-de-entrada']
const TOKENS = 'estilos/tokens.css'
// Superficies do Atendimento (conversa, lista, ficha, encerramento, assistente) e o dominio que
// escreve o texto delas. Gestao, Automaticas, Hall e Ajuda falam de regra, nao de quem responde.
const PASTAS_DO_VOCABULARIO = [
  'features/atendimento', 'features/caixa-de-entrada', 'features/ficha-cliente', 'features/encerramento',
  'features/agente', 'features/assistente', 'features/lembretes', 'features/notas',
]
const ARQUIVOS_DO_VOCABULARIO = [
  'dominio/conversa.js', 'dominio/linhaDoBalcao.js', 'dominio/automatico.js', 'dominio/agente.js',
  'dominio/captura.js', 'dominio/mensagem.js', 'dominio/motivoDeEscalada.js', 'dominio/resumoAtendimento.js',
  'dominio/resumoDoModo.js', 'dominio/janela.js', 'dominio/simulacoes.js', 'dominio/acoesDaConversa.js',
  'aplicacao/casos/atendimentoLocal.js', 'aplicacao/api/encerramentoEMidia.js',
]
// A ocorrencia (reclamacao do cliente) tem o proprio vocabulario: ela "encerra", a conversa "conclui".
const SEM_ENCERRADA = ['features/ficha-cliente/BlocoOcorrencia']
const PASTAS_DA_ESCALA_DE_FONTE = ['app/', 'componentes/', 'estilos/', ...PASTAS_DO_VOCABULARIO.map((pasta) => pasta + '/')]
const FONTE_EXCECOES = {
  'componentes/Trilho.module.css': 'o trilho tem 12 px aprovado na prancha (plano balcao-ux, secao 15)',
  'componentes/Campo.module.css': '16 px no celular: abaixo disso o iOS amplia a tela ao focar o campo',
  'componentes/CampoMascarado.module.css': '16 px no celular: abaixo disso o iOS amplia a tela ao focar o campo',
}
const LARGURA_DE_MODAL_EXCECOES = {
  'componentes/ImprimirPedido.jsx': 'a previa do impresso tem a largura do papel',
  'componentes/ModalFrete.jsx': 'demonstracao congelada (Lalamove), fora do Atendimento',
}
const ATOR_FORA_DO_VOCABULARIO = [
  ['automático/automática', /(?<![\p{L}\d_])autom[áà]tic[oa](?![\p{L}\d_])/iu],
  ['agente', /(?<![\p{L}\d_-])agentes?(?![\p{L}\d_-])/iu],
  ['Encerrada/Encerrar atendimento', /(?<![\p{L}\d_])Encerradas?(?![\p{L}\d_])|Encerrar atendimento|[Cc]onversa encerrada|encerrado por/u],
]
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

// --- e. menu de opcoes fora do MenuDeAcoes ---------------------------------------------

function contarMenusForaDoComponente(fontes) {
  const faltas = []
  for (const arquivo of fontes) {
    if (!arquivo.endsWith('.jsx') || /componentes[\\/](MenuDeAcoes|Icone)\.jsx$/.test(arquivo)) continue
    const codigo = semComentarios(readFileSync(arquivo, 'utf8'))
    for (const m of codigo.matchAll(/(?:nome|icone)=["']ellipsis(?:-vertical)?["']|Mais ações|\srole=["']menu["']/g)) faltas.push(`${relativo(arquivo)}:${linhaDe(codigo, m.index)} ${m[0]}`)
  }
  return faltas
}

// --- f. vocabulario do Atendimento ------------------------------------------------------

function contarForaDoVocabularioDoAtendimento(fontes) {
  const faltas = []
  for (const arquivo of fontes) {
    const rel = relativo(arquivo)
    if (!PASTAS_DO_VOCABULARIO.some((pasta) => rel.startsWith(pasta + '/')) && !ARQUIVOS_DO_VOCABULARIO.includes(rel)) continue
    const codigo = semComentarios(readFileSync(arquivo, 'utf8'))
    const trechos = []
    for (const m of codigo.matchAll(/(?<![=-])>([^<>{}]+)(?=<|\{)|\}([^<>{}]+)(?=<\/?[A-Za-z])/g)) {
      const texto = m[1] ?? m[2]
      if (/\p{L}/u.test(texto) && !/[=;]/.test(texto)) trechos.push([texto, m.index + 1])
    }
    for (const m of codigo.matchAll(/(['"`])((?:\\.|(?!\1)[^\n])*)\1/g)) {
      const inicioDaLinha = codigo.lastIndexOf('\n', m.index) + 1
      if (/^\s*(import|export)\b|\bfrom\s*$/.test(codigo.slice(inicioDaLinha, m.index))) continue
      // Chave de dado (`'agente'`, `'encerrada'`): um token minusculo, sem espaco.
      if (/^[a-z][\w-]*$/.test(m[2])) continue
      trechos.push([m[2], m.index])
    }
    for (const [texto, indice] of trechos) {
      for (const [termo, padrao] of ATOR_FORA_DO_VOCABULARIO) {
        if (termo.startsWith('Encerrada') && SEM_ENCERRADA.some((p) => rel.startsWith(p))) continue
        for (const achado of texto.matchAll(new RegExp(padrao.source, padrao.flags + 'g'))) {
          const linha = linhaDe(codigo, indice) + texto.slice(0, achado.index).split('\n').length - 1
          faltas.push(`${rel}:${linha} "${termo}": ${texto.trim().slice(0, 70)}`)
        }
      }
    }
  }
  return faltas
}

// --- g. icone fora da escala -------------------------------------------------------------

const ESCALA_DO_ICONE = [16, 20, 24, 32]
const ESCALA_DO_BOTAO_ICONE = [16, 20, 24]

function contarIconesForaDaEscala(fontes, estilos) {
  const faltas = []
  for (const arquivo of fontes) {
    if (!arquivo.endsWith('.jsx')) continue
    const codigo = semComentarios(readFileSync(arquivo, 'utf8'))
    // `MarcaIcone` e `IconeDoModulo` tambem seguem a escala, nas pastas da tela (Hall e Gestao ainda nao migraram).
    const dasPastas = PASTAS_DA_ESCALA_DE_FONTE.some((pasta) => relativo(arquivo).startsWith(pasta))
    for (const m of codigo.matchAll(dasPastas ? /<(Icone|MarcaIcone|IconeDoModulo)(?=[\s/>])([^>]*?)\/?>/gs : /<(Icone)(?=[\s/>])([^>]*?)\/?>/gs)) {
      const tamanho = /\btamanho=\{(\d+)\}/.exec(m[2])
      if (tamanho && !ESCALA_DO_ICONE.includes(Number(tamanho[1]))) faltas.push(`${relativo(arquivo)}:${linhaDe(codigo, m.index)} ${m[1]} tamanho ${tamanho[1]}`)
    }
    for (const m of codigo.matchAll(/\btamanhoIcone=\{(\d+)\}/g)) {
      if (!ESCALA_DO_BOTAO_ICONE.includes(Number(m[1]))) faltas.push(`${relativo(arquivo)}:${linhaDe(codigo, m.index)} tamanhoIcone ${m[1]}`)
    }
  }
  for (const arquivo of estilos) {
    const codigo = semComentarios(readFileSync(arquivo, 'utf8'), { css: true })
    for (const m of codigo.matchAll(/[^{}]*\bsvg\b[^{}]*\{([^}]*)\}/g)) {
      const largura = /(?<![\w-])width:\s*(\d+)px/.exec(m[1])
      if (largura && !ESCALA_DO_ICONE.includes(Number(largura[1]))) faltas.push(`${relativo(arquivo)}:${linhaDe(codigo, m.index)} svg width ${largura[1]}px`)
    }
  }
  return faltas
}

// --- h. botao so de icone fora do BotaoIcone ------------------------------------------------

function contarBotoesSoIcone(fontes) {
  const faltas = []
  const atributos = '((?:[^<>{}]|\\{[^{}]*(?:\\{[^{}]*\\}[^{}]*)*\\})*)'
  const filhoUnico = new RegExp(`<(Botao|button)(?=[\\s>])${atributos}>\\s*<Icone\\b(?:[^<>{}]|\\{[^{}]*\\})*\\/>\\s*<\\/\\1>`, 'gs')
  const semTexto = new RegExp(`<Botao(?=[\\s>])${atributos}\\/>`, 'gs')
  for (const arquivo of fontes) {
    if (!arquivo.endsWith('.jsx') || /componentes[\\/]BotaoIcone\.jsx$/.test(arquivo)) continue
    const codigo = semComentarios(readFileSync(arquivo, 'utf8'))
    for (const m of codigo.matchAll(filhoUnico)) faltas.push(`${relativo(arquivo)}:${linhaDe(codigo, m.index)} <${m[1]}> so com icone`)
    for (const m of codigo.matchAll(semTexto)) if (/\bicone=/.test(m[1])) faltas.push(`${relativo(arquivo)}:${linhaDe(codigo, m.index)} <Botao icone=... /> sem texto`)
  }
  return faltas
}

// --- i. contador desenhado a mao ------------------------------------------------------------

function contarContadoresProprios(estilos) {
  const faltas = []
  for (const arquivo of estilos) {
    if (relativo(arquivo) === 'componentes/Pilula.module.css') continue
    const codigo = semComentarios(readFileSync(arquivo, 'utf8'), { css: true })
    for (const m of codigo.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
      if (/font-variant-numeric:\s*tabular-nums/.test(m[2]) && /(min-width|height):\s*\d+px/.test(m[2]) && /border-radius/.test(m[2])) {
        faltas.push(`${relativo(arquivo)}:${linhaDe(codigo, m.index + m[1].length - m[1].trimStart().length)} ${m[1].trim().replace(/\s+/g, ' ')}`)
      }
    }
  }
  return faltas
}

// --- j. fonte fora da escala ----------------------------------------------------------------

function contarFontesForaDaEscala(estilos) {
  const faltas = []
  for (const arquivo of estilos) {
    const rel = relativo(arquivo)
    const codigo = semComentarios(readFileSync(arquivo, 'utf8'), { css: true })
    for (const m of codigo.matchAll(/--t-minimo/g)) faltas.push(`${rel}:${linhaDe(codigo, m.index)} --t-minimo (use --t-micro)`)
    if (!PASTAS_DA_ESCALA_DE_FONTE.some((pasta) => rel.startsWith(pasta)) || rel in FONTE_EXCECOES || rel === TOKENS) continue
    for (const m of codigo.matchAll(/(?<![\w-])font-size:\s*([^;}]+)/g)) {
      const valor = m[1].trim()
      if (/^(var\(--t-[a-z]+\)|inherit|0|1em|initial|unset)$/.test(valor)) continue
      faltas.push(`${rel}:${linhaDe(codigo, m.index)} font-size: ${valor}`)
    }
  }
  return faltas
}

// --- k. referencia quebrada -----------------------------------------------------------------

function contarReferenciasQuebradas(fontes, estilos) {
  const faltas = []
  const definidos = new Set()
  const cssPorCaminho = new Map()
  for (const arquivo of estilos) {
    const codigo = semComentarios(readFileSync(arquivo, 'utf8'), { css: true })
    cssPorCaminho.set(arquivo, codigo)
    for (const m of codigo.matchAll(/(--[\w-]+)\s*:/g)) definidos.add(m[1])
  }
  const codigoDosFontes = new Map(fontes.map((arquivo) => [arquivo, semComentarios(readFileSync(arquivo, 'utf8'))]))
  // Token escrito por script: `style={{ '--peso': x }}` e `setProperty('--x', ...)`.
  for (const codigo of codigoDosFontes.values()) {
    for (const m of codigo.matchAll(/['"`](--[\w-]+)['"`]/g)) definidos.add(m[1])
  }
  for (const [arquivo, codigo] of cssPorCaminho) {
    for (const m of codigo.matchAll(/var\(\s*(--[\w-]+)\s*\)/g)) {
      if (!definidos.has(m[1])) faltas.push(`${relativo(arquivo)}:${linhaDe(codigo, m.index)} var(${m[1]}) sem definicao`)
    }
  }
  for (const [arquivo, codigo] of codigoDosFontes) {
    if (!arquivo.endsWith('.jsx')) continue
    for (const importado of codigo.matchAll(/import\s+(\w+)\s+from\s+['"]([^'"]+\.module\.css)['"]/g)) {
      const caminhoDoCss = join(arquivo, '..', importado[2])
      const folha = cssPorCaminho.get(caminhoDoCss)
      if (folha === undefined) continue
      const classes = new Set([...folha.matchAll(/\.([A-Za-z_][\w-]*)/g)].map((m) => m[1]))
      const usos = [
        ...codigo.matchAll(new RegExp(`(?<![\\w./])${importado[1]}\\.([A-Za-z_]\\w*)`, 'g')),
        ...codigo.matchAll(new RegExp(`(?<![\\w.])${importado[1]}\\[\\s*['"]([\\w-]+)['"]\\s*\\]`, 'g')),
      ]
      for (const uso of usos) {
        if (!classes.has(uso[1])) faltas.push(`${relativo(arquivo)}:${linhaDe(codigo, uso.index)} ${importado[1]}.${uso[1]} nao existe em ${relativo(caminhoDoCss)}`)
      }
    }
  }
  return faltas
}

// --- l. raio e ponto fora do token ------------------------------------------------------------

function contarRaiosForaDoToken(estilos) {
  const faltas = []
  for (const arquivo of estilos) {
    const rel = relativo(arquivo)
    if (!PASTAS_DA_ESCALA_DE_FONTE.some((pasta) => rel.startsWith(pasta)) || rel === TOKENS) continue
    const codigo = semComentarios(readFileSync(arquivo, 'utf8'), { css: true })
    for (const m of codigo.matchAll(/(?<![\w-])border(?:-[a-z]+)*-radius:\s*([^;}]+)/g)) {
      if (/\d(px|%|rem|em)\b/.test(m[1].replace(/var\([^)]*\)/g, ''))) faltas.push(`${rel}:${linhaDe(codigo, m.index)} border-radius: ${m[1].trim()}`)
    }
    for (const m of codigo.matchAll(/(?<![\w-])width:\s*(7|9|10)px;\s*height:\s*\1px/g)) faltas.push(`${rel}:${linhaDe(codigo, m.index)} ponto de ${m[1]}px (use --ponto)`)
  }
  return faltas
}

// --- m. elevacao e borda das camadas ---------------------------------------------------------

const CAMADAS_FLUTUANTES = ['features/assistente/', 'features/lembretes/']

function contarCamadasComBordaOuSombraPequena(estilos) {
  const faltas = []
  for (const arquivo of estilos) {
    const rel = relativo(arquivo)
    if (!CAMADAS_FLUTUANTES.some((pasta) => rel.startsWith(pasta))) continue
    const codigo = semComentarios(readFileSync(arquivo, 'utf8'), { css: true })
    for (const m of codigo.matchAll(/(?<![\w-])border(?:-(?:top|bottom|left|right))?:\s*var\(--b-1\)\s+solid[^;}]*/g)) faltas.push(`${rel}:${linhaDe(codigo, m.index)} ${m[0].trim()}`)
    for (const m of codigo.matchAll(/box-shadow:\s*var\(--sombra\)/g)) faltas.push(`${rel}:${linhaDe(codigo, m.index)} ${m[0].trim()}`)
  }
  return faltas
}

// --- n. largura de Modal fora das tres ---------------------------------------------------------

function contarLarguraDeModalLiteral(fontes) {
  const faltas = []
  for (const arquivo of fontes) {
    const rel = relativo(arquivo)
    if (!arquivo.endsWith('.jsx') || !PASTAS_DA_ESCALA_DE_FONTE.some((pasta) => rel.startsWith(pasta)) || rel in LARGURA_DE_MODAL_EXCECOES) continue
    const codigo = semComentarios(readFileSync(arquivo, 'utf8'))
    for (const m of codigo.matchAll(/<Modal\b(?:[^<>{}]|\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\})*?\slargura=(?:"([^"]*)"|\{([^}]*)\})/gs)) {
      const valor = m[1] ?? m[2]
      if (!/^(sm|md|lg)$/.test(valor) && !/^['"]?(sm|md|lg)['"]?(\s*[?:]|$)/.test(valor) && /\d/.test(valor)) faltas.push(`${rel}:${linhaDe(codigo, m.index)} largura=${valor}`)
    }
  }
  return faltas
}

// --- o. icone do ator ---------------------------------------------------------------------

function contarIconeDoAtorForaDoPlano(fontes) {
  const faltas = []
  for (const arquivo of fontes) {
    const rel = relativo(arquivo)
    if (!PASTAS_DO_VOCABULARIO.some((pasta) => rel.startsWith(pasta + '/')) && !ARQUIVOS_DO_VOCABULARIO.includes(rel)) continue
    const codigo = semComentarios(readFileSync(arquivo, 'utf8'))
    for (const m of codigo.matchAll(/\b(?:nome|icone)(?:=|:\s*)(?:\{\s*)?['"](raio|log-out)['"]/g)) faltas.push(`${rel}:${linhaDe(codigo, m.index)} icone ${m[1]}`)
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
  ['menuForaDoComponente', 'menu de opcoes fora do MenuDeAcoes', contarMenusForaDoComponente(fontes)],
  ['atorForaDoVocabulario', 'ator ou fim de conversa fora do vocabulario do Atendimento', contarForaDoVocabularioDoAtendimento(fontes)],
  ['iconeForaDaEscala', 'icone fora da escala 16, 20, 24 e 32', contarIconesForaDaEscala(fontes, estilos)],
  ['botaoSoIconeForaDoComponente', 'botao so de icone fora do BotaoIcone', contarBotoesSoIcone(fontes)],
  ['contadorProprio', 'contador desenhado a mao fora da Pilula contagem', contarContadoresProprios(estilos)],
  ['fonteForaDaEscala', 'font-size fora da escala por token (e --t-minimo)', contarFontesForaDaEscala(estilos)],
  ['referenciaQuebrada', 'classe de css module ou token que nao existe', contarReferenciasQuebradas(fontes, estilos)],
  ['raioForaDoToken', 'border-radius literal ou ponto fora de --ponto', contarRaiosForaDoToken(estilos)],
  ['camadaComBordaOuSombraPequena', 'camada flutuante com borda de 1px ou sombra pequena', contarCamadasComBordaOuSombraPequena(estilos)],
  ['larguraDeModalLiteral', 'Modal com largura fora de sm, md e lg', contarLarguraDeModalLiteral(fontes)],
  ['iconeDoAtorForaDoPlano', 'icone do Assistente ou de concluir fora do plano (secao 3.2)', contarIconeDoAtorForaDoPlano(fontes)],
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
