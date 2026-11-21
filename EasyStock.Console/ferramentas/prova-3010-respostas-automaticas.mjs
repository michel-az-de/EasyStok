// Prova da #3010: Respostas automáticas sem título repetido e sem chave crua para quem lê.
// Telas renderizadas de verdade (react-dom/server) com os contextos; cada seção falha pelo próprio motivo.
//  1. Domínio: o exemplo preenche as cinco variáveis, e `abre` nunca vira "Volta ainda não temos...".
//  2. Compatibilidade: sem o terceiro argumento a prévia do servidor segue como antes.
//  3. Um título por nível: nenhum h1 a h3 repete o nome da tela nem outro título.
//  4. Prévia na lista sem `{`, rotulada como exemplo; chaves só como selo onde se edita.
//  5. Leitor de tela: o erro da prévia tem texto de apoio e a bolha é ligada ao rótulo.
//
// Roda com: node --import ./ferramentas/ambienteDoVite.mjs ferramentas/prova-3010-respostas-automaticas.mjs
// Contra outra árvore (ex.: o master, para ver o Red): PROVA_SRC=<pasta src> e o mesmo comando.
import assert from 'node:assert/strict'
import { readFileSync, readdirSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { join } from 'node:path'
import { createElement as h } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { transformWithOxc } from 'vite'

const raiz = process.env.PROVA_SRC ?? fileURLToPath(new URL('../src/', import.meta.url))
const jsx = new Map(await Promise.all(readdirSync(raiz, { recursive: true }).filter((n) => n.endsWith('.jsx')).map(async (nome) => {
  const arquivo = join(raiz, nome)
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env', '({ DEV: false })')
  return [pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code]
})))
registerHooks({
  resolve(s, c, next) {
    if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { for (const e of ['.js', '.jsx']) { try { return next(s + e, c) } catch { /* próximo */ } } }
    return next(s, c)
  },
  load(url, c, next) {
    if (jsx.has(url)) return { format: 'module', source: jsx.get(url), shortCircuit: true }
    if (url.endsWith('.css')) return { format: 'module', source: 'export default new Proxy({}, { get: (_, p) => String(p) })', shortCircuit: true }
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', source: 'export const FONTE_API=false;export const API_BASE="";', shortCircuit: true }
    if (url.endsWith('.json')) return { format: 'module', source: 'export default ' + readFileSync(new URL(url), 'utf8'), shortCircuit: true }
    return next(url, c)
  },
})
globalThis.window = { location: { origin: 'https://loja.exemplo' } }
const imp = (p) => import(pathToFileURL(join(raiz, p)).href)
const a = await imp('dominio/automacao.js')
const f = await imp('dominio/funcionamento.js')
const { NOME_RESPOSTAS_AUTOMATICAS } = await imp('dominio/modulos.js')
const tela = await imp('features/gestao/respostas/AbaRespostas.jsx')
const { ContextoEstado, ContextoAcoes, ContextoCatalogo } = await imp('aplicacao/contextos.js')

const AGORA = Date.parse('2026-11-04T12:00:00Z')
const TEXTO = 'Olá, {nome}! Voltamos {abre}. Cardápio: {linkCardapio}'
const regras = [
  { id: 'a1', nome: 'Primeira resposta', descricao: 'Primeira mensagem.', ativa: true, texto: TEXTO, gatilho: 'primeiro-contato' },
  { id: 'a2', nome: 'Fora do horário', descricao: 'Loja fechada.', ativa: false, texto: TEXTO, gatilho: 'fora-do-horario' },
]
const funcionamento = f.FUNCIONAMENTO_PADRAO
function montar(el) {
  const estado = { regras, fonteApi: false, cargaDasRegras: { estado: 'pronto' }, agora: AGORA, funcionamento }
  const acoes = { alternarRegra() {}, editarRegra() {}, incluirRespostaPronta() {}, editarRespostaPronta() {}, alternarArquivamentoRespostaPronta() {} }
  return renderToStaticMarkup(h(ContextoEstado.Provider, { value: estado }, h(ContextoAcoes.Provider, { value: acoes }, h(ContextoCatalogo.Provider, { value: { respostasProntas: [] } }, el))))
}
const texto = (html) => html.replace(/<[^>]+>/g, '').replace(/\s+/g, ' ').trim()
const falhas = []
const secao = (nome, fn) => { try { fn() } catch (e) { falhas.push(`[${nome}] ${e.message.split('\n')[0]}`) } }

secao('1 domínio', () => {
  assert.equal(typeof a.exemploDaPrevia, 'function', 'falta exemploDaPrevia no domínio')
  const ex = a.exemploDaPrevia({ origem: 'https://loja.exemplo' })
  for (const v of a.VARIAVEIS) assert.ok(String(ex[v.chave] ?? '').trim(), `o exemplo não traz ${v.chave}`)
  assert.match(ex.linkCardapio, /^https:\/\/loja\.exemplo[/#]/)
  const partes = a.previaDoServidor('Volta {abre}. Cardápio: {linkCardapio} Oi, {nome}.', ex, a.VARIAVEIS)
  assert.equal(partes.some((p) => p.tipo === 'erro'), false, 'sobrou chave marcada como erro')
  assert.doesNotMatch(partes.map((p) => p.texto).join(''), /[{}]/, 'sobrou chave crua na prévia')
})

secao('1b abre sem horário', () => {
  const fechada = Object.fromEntries(Object.keys(funcionamento).map((d) => [d, { abre: '08:00', fecha: '22:00', fechado: true }]))
  const ex = a.exemploDaPrevia({ agora: AGORA, funcionamento: fechada })
  assert.ok(ex.abre.trim(), 'abre vazio')
  assert.doesNotMatch(ex.abre, /ainda não temos/, `abre vira frase de erro dentro do texto: "${ex.abre}"`)
})

secao('2 compatibilidade', () => {
  const partes = a.previaDoServidor('Volta {abre}.', { nome: 'Maria' })
  assert.equal(partes.find((p) => p.texto === '{abre}')?.tipo, 'erro')
})

secao('3 título único', () => {
  const html = montar(h(tela.AbaRespostas, {}))
  const titulos = [...html.matchAll(/<h([1-3])[^>]*>([\s\S]*?)<\/h\1>/g)].map((m) => texto(m[2]))
  assert.ok(titulos.length > 0, 'a tela não tem título')
  assert.equal(titulos.includes(NOME_RESPOSTAS_AUTOMATICAS), false, `a seção repete o nome da tela: ${titulos.join(' | ')}`)
  assert.equal(new Set(titulos).size, titulos.length, `título repetido: ${titulos.join(' | ')}`)
})

secao('4a prévia da lista', () => {
  const html = montar(h(tela.AbaRespostas, {}))
  const lista = html.slice(html.indexOf('<ul class="lista"'))
  const corpo = texto(lista.slice(0, lista.indexOf('</ul>')))
  assert.doesNotMatch(corpo, /[{}]/, 'a lista mostra chave crua')
  assert.match(corpo, /Olá, Maria!/, 'a lista não mostra o texto preenchido')
  assert.match(corpo, /Exemplo/i, 'a lista não diz que o texto é um exemplo')
})

secao('4b selos', () => {
  assert.equal(typeof tela.SelosDeVariaveis, 'function', 'falta SelosDeVariaveis exportado')
  const html = renderToStaticMarkup(h(tela.SelosDeVariaveis, { variaveis: a.VARIAVEIS }))
  const selos = [...html.matchAll(/<span class="selo">([^<]*)<\/span>/g)].map((m) => m[1])
  assert.deepEqual(selos, a.VARIAVEIS.map((v) => `{${v.chave}}`), 'as variáveis não aparecem como selo')
})

secao('5 leitor de tela', () => {
  assert.equal(typeof tela.TextoPreenchido, 'function', 'falta TextoPreenchido exportado')
  const erro = renderToStaticMarkup(h(tela.TextoPreenchido, { texto: 'Oi {xyz}', exemplo: a.exemploDaPrevia(), fonteApi: false, className: 'bolha' }))
  assert.match(erro, /<mark[^>]*>[^<]*\{xyz\}[^<]*<span[^>]*>[^<]*desconhecida/, 'o erro da prévia não tem texto de apoio para leitor de tela')
  const ligada = renderToStaticMarkup(h(tela.TextoPreenchido, { texto: 'Oi', exemplo: a.exemploDaPrevia(), fonteApi: false, className: 'bolha', rotuloId: 'r1' }))
  assert.match(ligada, /aria-labelledby="r1"/, 'a bolha não está ligada ao rótulo')
})

assert.equal(falhas.length, 0, '\n' + falhas.join('\n'))
console.log('prova-3010: ok')
