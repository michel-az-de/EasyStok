// Prova da #3010: Respostas automáticas sem título repetido e sem chave crua para quem lê.
//  - Um título por nível: a seção das automáticas não repete o nome da tela (h1 na rota, h2 na janela).
//  - Prévia com exemplo preenchido: {abre} e {linkCardapio} viram horário e link; nada de `{` sobra.
//  - Onde se edita, as variáveis são selos legíveis, não uma linha de texto com chaves.
//
// Roda com: node --import ./ferramentas/ambienteDoVite.mjs ferramentas/prova-3010-respostas-automaticas.mjs
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* original */ } } return next(s, c) },
})
const a = await import('../src/dominio/automacao.js')
const ler = (p) => readFileSync(new URL(`../src/${p}`, import.meta.url), 'utf8')
const jsx = ler('features/gestao/respostas/AbaRespostas.jsx')
const css = ler('features/gestao/respostas/abaRespostas.module.css')

// 1. O exemplo preenche as cinco variáveis, com o horário de abertura e o link do cardápio.
{
  assert.equal(typeof a.exemploDaPrevia, 'function', 'falta exemploDaPrevia no domínio')
  const ex = a.exemploDaPrevia({ origem: 'https://loja.exemplo' })
  for (const v of a.VARIAVEIS) assert.ok(String(ex[v.chave] ?? '').trim(), `o exemplo não traz ${v.chave}`)
  assert.match(ex.linkCardapio, /^https:\/\/loja\.exemplo[/#]/)
  const partes = a.previaDoServidor('Volta {abre}. Cardápio: {linkCardapio} Oi, {nome}.', ex, a.VARIAVEIS)
  assert.equal(partes.some((p) => p.tipo === 'erro'), false, 'sobrou chave marcada como erro')
  assert.doesNotMatch(partes.map((p) => p.texto).join(''), /[{}]/, 'sobrou chave crua na prévia')
}

// 2. Sem o terceiro argumento a prévia do servidor continua como antes (só nome, pedido e faixa).
{
  const partes = a.previaDoServidor('Volta {abre}.', { nome: 'Maria' })
  assert.equal(partes.find((p) => p.texto === '{abre}')?.tipo, 'erro')
}

// 3. Um título por nível.
{
  assert.doesNotMatch(jsx, /<h[1-6][^>]*>\s*\{NOME_RESPOSTAS_AUTOMATICAS\}/, 'a seção repete o nome da tela em um título')
}

// 4. A lista e o editor usam a prévia com exemplo, e as variáveis viram selos.
{
  assert.match(jsx, /previaDoServidor\(/, 'a lista e o editor não usam a prévia preenchida')
  assert.match(jsx, /exemploDaPrevia\(/)
  assert.doesNotMatch(jsx, /VARIAVEIS_TEXTO/, 'as variáveis ainda são uma linha de texto com chaves')
  assert.match(jsx, /className=\{css\.selo\}/, 'as variáveis não aparecem como selo')
  assert.match(css, /\.selo\s*\{/)
  assert.doesNotMatch(css, /border-left\s*:/, 'barra lateral colorida é proibida')
}
console.log('prova-3010: ok')
