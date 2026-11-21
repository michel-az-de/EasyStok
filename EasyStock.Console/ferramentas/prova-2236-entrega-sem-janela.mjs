/* eslint-disable no-console */
// Prova da issue #2236: em Entregas, o pedido ainda sem janela (`pedido.janela` nasce null, #1474)
// não aparece como o mais atrasado do dia. Sem marco, o cartão não faz conta com 0 (1970) e a
// ordem "Mais próxima" o deixa por último, como `dominio/agora.js` já faz. Componente real,
// renderizado no Node:
//
//   node ferramentas/prova-2236-entrega-sem-janela.mjs
//
// Mesmo gancho de `prova-2227-lista-carregando.mjs`.

import assert from 'node:assert/strict'
import { readFileSync, readdirSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { join } from 'node:path'
import { transformWithOxc } from 'vite'
import { createElement as h } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'

const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const jsx = new Map(await Promise.all(readdirSync(raiz, { recursive: true }).filter(n => n.endsWith('.jsx')).map(async nome => {
  const arquivo = join(raiz, nome)
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env', '({ DEV: false })')
  return [pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code]
})))
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      for (const ext of ['.js', '.jsx']) { try { return proximo(spec + ext, contexto) } catch { /* próximo formato */ } }
    }
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (jsx.has(url)) return { format: 'module', source: jsx.get(url), shortCircuit: true }
    if (url.endsWith('.css')) return { format: 'module', source: 'export default new Proxy({}, { get: (_, p) => String(p) })', shortCircuit: true }
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', source: 'export const FONTE_API=true;export const API_BASE="";', shortCircuit: true }
    if (url.endsWith('.json')) return { format: 'module', source: 'export default ' + readFileSync(new URL(url), 'utf8'), shortCircuit: true }
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
    console.log(`FALHA ${descricao}\n      ${erro.message.split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

const { CartaoEntrega } = await import('../src/features/entregas/CartaoEntrega.jsx')
const { ordenarEntregas } = await import('../src/dominio/viagem.js')

const AGORA = Date.parse('2026-11-04T11:05:00-03:00')
const JANELAS = [{ id: 'j2', faixa: '12h30 às 13h30' }]
const CONSTANTES = { minutosTrecho: 20, minutosChegadaEntregador: 15, minutosConferir: 10 }
const conversa = (id, janela) => ({
  id, nome: 'Cliente ' + id, cliente: { endereco: 'Rua Cerro Corá, 1.500, Alto de Pinheiros, 05061-100' },
  pedido: { numero: '2026-0' + id, estado: 'aguardando', janela, entregador: null, itens: [{ sku: 'LAS-CLA', qtd: 1 }], pagamentos: [] },
})
const semJanela = conversa('200', null)
const comJanela = conversa('184', 'j2')
const cartao = (c) => renderToStaticMarkup(h(CartaoEntrega, {
  conversa: c, grupoDaViagem: null, janelas: JANELAS, agora: AGORA, constantes: CONSTANTES, cardapio: [],
  viagensDisponiveis: [], acoes: {}, aoAbrirAgendamento: () => {}, opcoesDespacho: {},
}))
// O anel: "rótulo lido = número na tela"; a linha 3: o texto do marco.
const anel = (html) => {
  const casado = /role="timer" aria-label="([^"]*)">([^<]*)</.exec(html)
  return casado ? casado[1] + ' = ' + casado[2] : ''
}
const linha3 = (html) => /class="linha3"><span>([^<]*)</.exec(html)?.[1] ?? ''
const leitura = (html) => `anel "${anel(html)}", linha "${linha3(html)}"`

// --- 1. Cartão do pedido sem janela -------------------------------------------------
await confere('1. sem janela: o anel não diz atraso', () => {
  const html = cartao(semJanela)
  assert.doesNotMatch(anel(html), /Atrasado|\+99/, leitura(html))
})
await confere('1. sem janela: nenhuma hora inventada (21:00 é o 0 de 1970 em São Paulo)', () => {
  const html = cartao(semJanela)
  assert.doesNotMatch(html, /21:00/, leitura(html))
  assert.match(linha3(html), /Sem janela escolhida/, leitura(html))
})
await confere('1. com janela: segue "Conferir às" com a hora e o anel em minutos', () => {
  const html = cartao(comJanela)
  assert.equal(linha3(html), 'Conferir às 12:00', leitura(html))
  assert.equal(anel(html), 'Próximo marco em 55 minutos = 55', leitura(html))
})

// --- 2. Ordem -----------------------------------------------------------------------
const ordem = (modo) => ordenarEntregas([semJanela, comJanela], modo, {
  janelas: JANELAS, agora: AGORA, constantes: CONSTANTES, viagensPorId: new Map(),
}).map((c) => c.id)
await confere('2. "Mais próxima": sem janela fica por último', () => {
  assert.deepEqual(ordem('proxima'), ['184', '200'])
})
await confere('2. "Mais longe": sem janela fica antes, como quem não tem prazo', () => {
  assert.deepEqual(ordem('longe'), ['200', '184'])
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
