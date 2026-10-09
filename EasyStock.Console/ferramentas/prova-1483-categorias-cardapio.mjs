/* eslint-disable no-console */
// Prova da #1483 (M1.3): categorias do cardápio (seções) no console.
//   - criar, renomear, esconder, mover, excluir e converter gravam pelas rotas do Gerente;
//   - cada gravação relê; erro avisa; a conversão diz quanto fez;
//   - o item vai para a categoria pelo secaoId ("" tira); M1 ganha a tela Categorias.
//
//   node ferramentas/prova-1483-categorias-cardapio.mjs

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

const { criarGestaoSecoes, lerSecoes } = await import('../src/aplicacao/gestaoSecoes.js')
const { corpoDoItem, detalheDaApi } = await import('../src/infra/api/cardapioApi.js')
const { moduloPorId } = await import('../src/dominio/modulos.js')

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

const BASE = '/api/atendimento/comanda/cardapio/secoes'
const LISTA = [(m, u) => m === 'GET' && u.endsWith(BASE), { data: [
  { secaoId: 's1', nome: 'Massas', ordem: 1, visivel: true, itens: 3 },
  { secaoId: 's2', nome: 'Bebidas', ordem: 2, visivel: false, itens: 0 },
] }]

async function montar(rotas) {
  const chamadas = montarFetch([...rotas, LISTA])
  const secoes = await lerSecoes()
  const erros = []
  const avisos = []
  let relidas = 0
  const gestao = criarGestaoSecoes({
    obterSecoes: () => secoes, recarregar: async () => { relidas += 1 }, aoErro: (m) => erros.push(m), aoAviso: (m) => avisos.push(m),
  })
  return { gestao, chamadas, erros, avisos, relidas: () => relidas, secoes }
}

await confere('lê as categorias com o número de pratos', async () => {
  const { secoes } = await montar([])
  assert.deepEqual(secoes.map((s) => [s.id, s.nome, s.itens, s.visivel]), [['s1', 'Massas', 3, true], ['s2', 'Bebidas', 0, false]])
})

await confere('criar, renomear, esconder, mover e excluir chamam as rotas e relêem', async () => {
  const m = await montar([[(mt) => mt !== 'GET', { data: {} }]])
  await m.gestao.criar(' Sobremesas ')
  await m.gestao.renomear('s1', 'Massas frescas')
  await m.gestao.alternarVisivel('s2')
  await m.gestao.mover('s2', 'subir')
  await m.gestao.excluir('s2')
  const escritas = m.chamadas.filter((c) => c.metodo !== 'GET').map((c) => [c.metodo, c.url.replace(BASE, ''), c.corpo])
  assert.deepEqual(escritas, [
    ['POST', '', { nome: 'Sobremesas' }],
    ['PUT', '/s1', { nome: 'Massas frescas' }],
    ['POST', '/s2/visivel', { visivel: true }],
    ['POST', '/s2/mover', { direcao: 'Subir' }],
    ['DELETE', '/s2', null],
  ])
  assert.equal(m.relidas(), 5)
})

await confere('nome vazio não chama a API', async () => {
  const m = await montar([])
  assert.equal(await m.gestao.criar('   '), false)
  assert.ok(!m.chamadas.some((c) => c.metodo === 'POST'))
})

await confere('excluir categoria com prato: o erro do EasyStok vira aviso', async () => {
  const m = await montar([[(mt) => mt === 'DELETE', { status: 400, error: { code: 'VALIDATION', message: 'A categoria "Massas" tem 3 prato(s).' } }]])
  assert.equal(await m.gestao.excluir('s1'), false)
  assert.ok(m.erros.some((e) => /3 prato/.test(e)))
})

await confere('converter as categorias antigas diz quanto fez', async () => {
  const m = await montar([[(mt, u) => mt === 'POST' && u.endsWith('/migrar-categorias'), { data: { secoesCriadas: 2, itensLigados: 5 } }]])
  await m.gestao.migrar()
  assert.ok(m.avisos.some((a) => /2 categoria.*5 prato/.test(a)))
})

await confere('o item vai para a categoria pelo secaoId; "" tira; ausente não mexe', () => {
  const base = { nome: 'Lasanha', linha: 'servir', porcao: '1 kg', preco: 45 }
  assert.equal(corpoDoItem({ ...base, secaoId: 's1' }).secaoId, 's1')
  assert.equal(corpoDoItem({ ...base, secaoId: null }).secaoId, '')
  assert.equal('secaoId' in corpoDoItem(base), false)
  assert.equal(detalheDaApi({ secaoId: 's1' }).secaoId, 's1')
})

await confere('M1 ganha a tela Categorias só no modo API', () => {
  const tela = moduloPorId('cardapio').telas.find((t) => t.id === 'categorias')
  assert.equal(tela?.aba, 'categorias')
  assert.equal(tela?.soApi, true)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
