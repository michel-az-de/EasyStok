/* eslint-disable no-console */
// Prova da #2792 (frente F1): o editor de modelo de "E-mail e SMS" mostra as variáveis do evento.
// - A API lê GET /api/notificacoes/variaveis-catalogo?tipoEvento= (rota do controller, não a do openapi antigo).
// - A dica da Mensagem usa a grafia Scriban {{ x }}, nunca as {nome} do chat.
// - Catálogo vazio, inválido ou com erro não quebra o editor (sem dica).
//
// Roda com: node ferramentas/prova-2792-modelos-variaveis.mjs
// Contra o master (Red): PROVA_SRC=<pasta src> node ferramentas/prova-2792-modelos-variaveis.mjs
import { existsSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { pathToFileURL, fileURLToPath } from 'node:url'
import assert from 'node:assert/strict'

const raiz = process.env.PROVA_SRC ? process.env.PROVA_SRC.replace(/[\/]$/, '') : fileURLToPath(new URL('../src', import.meta.url))
const ler = (rel) => readFileSync(join(raiz, rel), 'utf8')
let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok    ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA ${nome}\n      ${erro.message.split('\n')[0]}`) }
}

const arquivoDominio = join(raiz, 'dominio/variaveisDoModelo.js')
const dominio = existsSync(arquivoDominio) ? await import(pathToFileURL(arquivoDominio).href) : null

caso('domínio variaveisDoModelo existe', () => assert.ok(dominio, 'src/dominio/variaveisDoModelo.js não existe'))
caso('dica lista as variáveis com {{ x }} e a descrição', () => {
  const dica = dominio.dicaDasVariaveis([{ nomeVariavel: 'produto.nome', descricao: 'Nome do produto', exemplo: 'Leite' }, { nomeVariavel: 'diasRestantes', descricao: '', exemplo: '3' }])
  assert.equal(dica, 'Você pode usar: {{ produto.nome }} Nome do produto · {{ diasRestantes }}')
})
caso('dica não usa a grafia {x} do chat', () => assert.doesNotMatch(dominio.dicaDasVariaveis([{ nomeVariavel: 'a', descricao: 'b' }]), /(^|[^{])\{[^{]/))
caso('catálogo vazio, nulo ou torto vira sem dica', () => {
  for (const c of [[], null, undefined, {}, 'x', [{}, { nomeVariavel: '  ' }, null]]) assert.equal(dominio.dicaDasVariaveis(c), undefined)
})

const api = ler('infra/api/cadastrosApi.js')
caso('cadastrosApi lê /api/notificacoes/variaveis-catalogo?tipoEvento=', () => {
  assert.match(api, /\/api\/notificacoes\/variaveis-catalogo\?/)
  assert.match(api, /variaveisDoModeloCadastro\s*=/)
})
caso('a prévia continua em preview-draft', () => assert.match(api, /templates\/preview-draft/))
caso('aplicacao/cadastros exporta variaveisDoModeloCadastro', () => assert.match(ler('aplicacao/cadastros.js'), /variaveisDoModeloCadastro/))

const editor = ler('features/gestao/cadastros/ModelosCadastro.jsx')
caso('o editor carrega o catálogo pelo tipo de evento e cancela ao trocar', () => {
  assert.match(editor, /variaveisDoModeloCadastro\(dados\.tipoEvento/)
  assert.match(editor, /AbortController/)
})
// #2810: a rejeição só zera o catálogo quando a requisição não foi cancelada (troca de evento no meio do voo).
caso('erro ou 403 não quebra o editor (rejeição vira catálogo vazio)', () => assert.match(editor, /variaveisDoModeloCadastro\([^\n]*\.then\(catalogar, \(\) => \{ if \(!controle\.signal\.aborted\) catalogar\(\[\]\) \}\)/))
caso('a dica vai no campo Mensagem', () => assert.match(editor, /<CampoArea rotulo="Mensagem" dica=\{[^}]*dicaDasVariaveis\(catalogo\)/))

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
