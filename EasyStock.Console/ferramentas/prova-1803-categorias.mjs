/* eslint-disable no-console */
// Prova da #1803: categorias antigas do cardápio.
//   - sem seção, a tela lê a prévia da conversão e diz quais categorias antigas os pratos usam;
//   - o botão "Converter" explica o efeito; o item de gestão traz o secaoId que a API já devolve.
//
//   node ferramentas/prova-1803-categorias.mjs

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
    return proximo(url, contexto)
  },
})

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

const gestaoSecoes = await import('../src/aplicacao/gestaoSecoes.js')
const secoesApi = await import('../src/infra/api/secoesApi.js')
const cardapioApi = await import('../src/infra/api/cardapioApi.js')
const fonte = (caminho) => readFileSync(new URL(caminho, import.meta.url), 'utf8')

const PREVIA = { categorias: [
  { nome: 'Massas recheadas', pratos: 5, existe: false },
  { nome: 'Nhoque', pratos: 2, existe: false },
  { nome: 'Molhos artesanais', pratos: 1, existe: true },
], pratos: 8 }

await confere('o item de gestão guarda o secaoId que a API devolve (null sem seção)', () => {
  const base = { cardapioItemId: 'i1', nome: 'Lasanha', linha: 'ParaServir', preco: 30, categoria: 'massas recheadas', visivel: true, disponivel: true, ordem: 1 }
  assert.equal(cardapioApi.itemGestaoDaApi({ ...base, secaoId: 's1' }).secaoId, 's1')
  assert.equal(cardapioApi.itemGestaoDaApi(base).secaoId, null)
})

await confere('a prévia da conversão é lida por GET, sem gravar', async () => {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET' } = {}) => {
    chamadas.push({ method, url })
    return new Response(JSON.stringify({ data: PREVIA }), { status: 200 })
  }
  const previa = await gestaoSecoes.lerPreviaMigracao()
  assert.deepEqual(chamadas.map((c) => c.method), ['GET'])
  assert.ok(chamadas[0].url.endsWith('/api/atendimento/comanda/cardapio/secoes/migrar-categorias/previa'), chamadas[0].url)
  assert.deepEqual(previa, secoesApi.previaDaApi(PREVIA))
  assert.equal(previa.pratos, 8)
  assert.deepEqual(previa.categorias[2], { nome: 'Molhos artesanais', pratos: 1, existe: true })
})

await confere('o resumo diz quantas categorias antigas, quais e quantos pratos', () => {
  const r = gestaoSecoes.resumoCategoriasAntigas(secoesApi.previaDaApi(PREVIA))
  assert.equal(r.texto, 'Os pratos já usam 3 categorias antigas: Massas recheadas, Nhoque, Molhos artesanais (8 pratos).')
  assert.equal(r.novas, 2, 'Molhos artesanais já existe: só liga os pratos')
  assert.match(r.efeito, /cria 2 categorias/)
  assert.match(r.efeito, /liga os 8 pratos/)
  assert.match(r.efeito, /sem duplicar/)
})

await confere('singular e prévia vazia', () => {
  const um = gestaoSecoes.resumoCategoriasAntigas({ categorias: [{ nome: 'Nhoque', pratos: 1, existe: false }], pratos: 1 })
  assert.equal(um.texto, 'Os pratos já usam 1 categoria antiga: Nhoque (1 prato).')
  assert.match(um.efeito, /cria 1 categoria e liga o prato/)
  assert.equal(gestaoSecoes.resumoCategoriasAntigas({ categorias: [], pratos: 0 }), null)
  assert.equal(gestaoSecoes.resumoCategoriasAntigas(null), null)
})

await confere('com prévia, a tela não diz "nenhuma categoria" e explica o botão', () => {
  const tela = fonte('../src/features/gestao/cardapio/AbaCategoriasApi.jsx')
  assert.match(tela, /resumoCategoriasAntigas/)
  assert.match(tela, /antigas\.efeito/)
  assert.match(tela, /aria-describedby="efeito-conversao"/, 'o botão aponta para a explicação do efeito')
  assert.match(tela, /!antigas\s*&&/, 'o "Nenhuma categoria ainda" só aparece sem categoria antiga')
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
