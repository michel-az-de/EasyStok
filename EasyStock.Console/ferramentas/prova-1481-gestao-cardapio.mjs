/* eslint-disable no-console */
// Prova da #1481 (M1.1): gestão do cardápio no console (modo API).
//   - a lista traz todos os itens (ocultos e desligados) e liga o dia e o site;
//   - subir e descer mandam só a direção; o EasyStok renumera (#1486);
//   - erro avisa e relê; o módulo M1 ganha a tela só no modo API.
//
//   node ferramentas/prova-1481-gestao-cardapio.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    if (url.endsWith('.json')) {
      return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    }
    return proximo(url, contexto)
  },
})

const { podeMover } = await import('../src/dominio/cardapio.js')
const { criarGestaoCardapio, lerGestao } = await import('../src/aplicacao/gestaoCardapio.js')
const { modulosDoHall, moduloPorId } = await import('../src/dominio/modulos.js')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

function montarFetch(rotas) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET', body } = {}) => {
    chamadas.push({ metodo: method, url, corpo: body ? JSON.parse(body) : null })
    for (const [teste, resposta] of rotas) {
      if (teste(method, url)) {
        const { status = 200, data = null, error } = resposta
        return new Response(status === 204 ? null : JSON.stringify(error ? { error } : { data }), { status })
      }
    }
    return new Response(JSON.stringify({ data: null }), { status: 200 })
  }
  return chamadas
}

const GESTAO = [(m, u) => m === 'GET' && u.endsWith('/api/atendimento/comanda/cardapio/gestao'), { data: [
  { cardapioItemId: 'a', nome: 'Lasanha', linha: 'ParaServir', porcao: '1 kg', preco: 45, visivel: true, disponivel: false, ordem: 1, controlaSaldo: true },
  { cardapioItemId: 'b', nome: 'Nhoque', linha: 'PrepararEmCasa', porcao: '500 g', preco: 38, visivel: false, disponivel: true, ordem: 2, controlaSaldo: false },
  { cardapioItemId: 'c', nome: 'Torta', linha: 'ParaServir', porcao: null, preco: 60, visivel: true, disponivel: true, ordem: 4, controlaSaldo: false },
] }]

await confere('podeMover: o primeiro não sobe e o último não desce', () => {
  const lista = [{ ordem: 0 }, { ordem: 0 }, { ordem: 0 }]
  assert.equal(podeMover(lista, 1, 'subir'), true)
  assert.equal(podeMover(lista, 1, 'descer'), true)
  assert.equal(podeMover(lista, 0, 'subir'), false)
  assert.equal(podeMover(lista, 2, 'descer'), false)
})

await confere('lerGestao traz todos, inclusive oculto do site e desligado de hoje (RN-16)', async () => {
  montarFetch([GESTAO])
  const itens = await lerGestao()
  assert.deepEqual(itens.map((i) => [i.sku, i.linha, i.hoje, i.noSite]),
    [['a', 'servir', false, true], ['b', 'casa', true, false], ['c', 'servir', true, true]])
})

async function montar(rotas) {
  const chamadas = montarFetch([...rotas, GESTAO])
  const itens = await lerGestao()
  const erros = []
  let relidas = 0
  const gestao = criarGestaoCardapio({
    obterItens: () => itens,
    recarregar: async () => { relidas += 1 },
    aoErro: (m) => erros.push(m),
  })
  return { gestao, chamadas, erros, relidas: () => relidas }
}

await confere('hoje e no site gravam o valor oposto e relêem', async () => {
  const m = await montar([[(mt, u) => mt === 'POST', { data: {} }]])
  await m.gestao.alternarHoje('a')
  await m.gestao.alternarNoSite('b')
  const posts = m.chamadas.filter((c) => c.metodo === 'POST')
  assert.deepEqual(posts.map((c) => [c.url.split('/').slice(-2).join('/'), c.corpo]),
    [['a/disponivel', { disponivel: true }], ['b/visivel', { visivel: true }]])
  assert.equal(m.relidas(), 2)
})

await confere('subir manda só a direção para o EasyStok renumerar (#1486)', async () => {
  const m = await montar([[(mt, u) => mt === 'POST' && u.endsWith('/c/mover'), { data: { ordem: 2 } }]])
  const ok = await m.gestao.mover('c', 'subir')
  assert.equal(ok, true)
  const posts = m.chamadas.filter((c) => c.metodo === 'POST')
  assert.equal(posts.length, 1)
  assert.deepEqual(posts[0].corpo, { direcao: 'Subir' })
  assert.equal(await m.gestao.mover('a', 'subir'), false, 'o primeiro não sobe nem chama a API')
})

await confere('erro da API avisa e relê a lista', async () => {
  const m = await montar([[(mt) => mt === 'POST', { status: 403, error: { code: 'FORBIDDEN', message: 'Só o gerente' } }]])
  const ok = await m.gestao.alternarNoSite('a')
  assert.equal(ok, false)
  assert.ok(m.erros.some((e) => /gerente/i.test(e)))
  assert.equal(m.relidas(), 1)
})

await confere('M1 ganha a tela Itens do cardápio só no modo API', () => {
  const tela = moduloPorId('cardapio').telas.find((t) => t.id === 'itens')
  assert.equal(tela?.aba, 'cardapio')
  assert.equal(modulosDoHall({ fonteApi: true }).find((m) => m.id === 'cardapio').disponivel, true)
  assert.equal(modulosDoHall({ fonteApi: false }).find((m) => m.id === 'cardapio').disponivel, false)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
