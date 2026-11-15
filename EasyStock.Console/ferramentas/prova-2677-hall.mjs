// Prova da #2677: achados do módulo Hall na homologação #1769 (laboratório local, Casa da Baba).
//
// H1: o cartão Produção ("N lotes vencidos") leva a Perdas, onde a lista "Vencidos com saldo" lê a mesma
//     fonte do hall (`/perdas/vencidos`). Estoque do dia conta outra coisa e mostrava 0.
// H2: sem o número do Atendimento no pulso, o resumo não afirma "nada precisa de você": ele não mediu as conversas.
// H3: "Fechar caixa" no cartão só para quem pode fechar (`acoes.gerenciarCaixa`, a mesma regra da tela do caixa).
//
// Roda com: node ferramentas/prova-2677-hall.mjs
// Contra outra árvore (ex.: o master, para ver o Red): PROVA_SRC=<pasta src> node ferramentas/prova-2677-hall.mjs

import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'
import { transformWithOxc } from 'vite'

const base = process.env.PROVA_SRC ? pathToFileURL(process.env.PROVA_SRC.replace(/[\\/]?$/, '/')) : new URL('../src/', import.meta.url)
const raiz = fileURLToPath(base)
const jsx = new Map(await Promise.all(readdirSync(raiz, { recursive: true }).filter((n) => n.endsWith('.jsx')).map(async (nome) => {
  const arquivo = join(raiz, nome)
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env', '({ DEV: false })')
  return [pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code]
})))
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      for (const ext of ['.js', '.jsx']) { try { return proximo(especificador + ext, contexto) } catch { /* próximo formato */ } }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (jsx.has(url)) return { format: 'module', source: jsx.get(url), shortCircuit: true }
    if (url.endsWith('.css')) return { format: 'module', source: 'export default new Proxy({}, { get: (_, p) => String(p) })', shortCircuit: true }
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', source: 'export const FONTE_API=true;export const API_BASE="";', shortCircuit: true }
    if (url.endsWith('.json')) return { format: 'module', source: 'export default ' + readFileSync(new URL(url), 'utf8'), shortCircuit: true }
    return proximo(url, contexto)
  },
})

const fonte = (caminho) => readFileSync(new URL(caminho, base), 'utf8')
const tentar = async (caminho) => { try { return await import(new URL(caminho, base).href) } catch (erro) { return { ERRO_DE_IMPORT: erro.message } } }
const corpo = (seletor, texto) => {
  const escapado = seletor.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return [...texto.matchAll(new RegExp(`(?:^|\\n|\\})\\s*${escapado}\\s*\\{([^}]*)\\}`, 'g'))].map((m) => m[1]).join(' ')
}

const { createElement: h } = await import('react')
const { renderToStaticMarkup } = await import('react-dom/server')
const estado = await tentar('dominio/estadoDoHall.js')
const pulso = await tentar('dominio/pulsoDoHall.js')
const { PulsoDoHall } = await tentar('features/hall/PulsoDoHall.jsx')
globalThis.document ??= { documentElement: { dataset: { theme: 'light' } } }
const jsxHall = fonte('features/hall/HallDeModulos.jsx')

let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok    ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA ${nome}\n      ${String(erro.message).split('\n')[0]}`) }
}
const lido = (dados) => ({ estado: 'ok', dados, em: 1 })

caso('H1: "N lotes vencidos" leva a Produção > Perdas, onde os lotes vencidos com saldo estão listados', () => {
  const lotes = pulso.pendenciaDoModulo('producao', { producao: lido({ vencidos: [{}, {}] }) })
  assert.equal(lotes.href, '#/m/producao/perdas')
  assert.match(fonte('features/gestao/producao/AbaPerdasApi.jsx'), /aria-label="Lotes vencidos"/, 'Perdas lista os lotes vencidos')
  assert.match(fonte('aplicacao/usePerdasApi.js'), /lerVencidos\(\)/, 'a lista vem de lerVencidos')
  assert.match(fonte('aplicacao/perdas.js'), /lerVencidos = async \(\) => \(\(await obterVencidos\(\)\)/, 'lerVencidos lê obterVencidos, a fonte do hall')
  assert.match(fonte('aplicacao/usePulsoDoHall.js'), /obterVencidos/)
})

caso('H2: sem o Atendimento no pulso, o resumo fala só do que mediu', () => {
  const conversa = { situacao: 'Assumida', aguardaResposta: true }
  const fontes = {
    atendimento: lido({ itens: [conversa, conversa] }),
    pedidos: lido({ itens: [{ status: 'aguardando' }, { status: 'preparando' }, { status: 'pronto' }] }),
  }
  assert.equal(estado.resumoDoDia(fontes, ['cozinha', 'entregas']).texto, 'Hoje · 3 em andamento')
  assert.equal(estado.resumoDoDia({ pedidos: lido({ itens: [] }) }, ['cozinha', 'entregas']).texto, 'Hoje · nada em andamento')
  assert.doesNotMatch(estado.resumoDoDia(fontes, ['cozinha', 'financeiro']).texto, /precisa/)
  // Com o Atendimento contado, nada muda.
  assert.equal(estado.resumoDoDia(fontes).texto, 'Hoje · 2 precisam de você · 3 em andamento')
  assert.equal(estado.resumoDoDia({ atendimento: lido({ itens: [] }), pedidos: lido({ itens: [] }) }).texto, 'Hoje · nada precisa de você')
})

caso('H3: "Fechar caixa" só aparece para quem pode fechar o caixa', () => {
  const item = { id: 'financeiro', estado: 'ok', alerta: 'Caixa de outro dia ainda aberto.' }
  assert.equal(estado.acaoDoIndicador(item, { podeFecharCaixa: true }), 'Fechar caixa')
  assert.equal(estado.acaoDoIndicador(item, { podeFecharCaixa: false }), null)
  assert.equal(estado.acaoDoIndicador(item), null, 'sem a permissão informada, não promete a ação')
  const fontes = { financeiro: lido({ aberto: true, saldoEsperado: 40, saldoInicial: 0, movimentos: [], esquecidoAberto: true }) }
  const desenhar = (podeFecharCaixa) => renderToStaticMarkup(h(PulsoDoHall, { ids: ['financeiro'], foco: 'x', fontes, podeFecharCaixa, aoMover: () => {} }))
  const operador = desenhar(false)
  assert.doesNotMatch(operador, /Fechar caixa/)
  assert.match(operador, /data-severidade="alerta"/, 'o alerta continua visível')
  assert.match(operador, /Conferir caixa/)
  assert.match(desenhar(true), /Fechar caixa/)
  assert.match(jsxHall, /podeFecharCaixa=\{acoes\?\.gerenciarCaixa === true\}/)
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)${estado.ERRO_DE_IMPORT ? ` · import: ${estado.ERRO_DE_IMPORT.split('\n')[0]}` : ''}`)
if (falhas.length) process.exitCode = 1
