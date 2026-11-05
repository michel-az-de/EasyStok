/* eslint-disable no-console */
// Prova da issue #2237: o anel do cartão de entrega mostra as horas que faltam acima de 99 min,
// arredondadas para baixo (nunca promete mais tempo do que há). Antes, todo prazo acima de 99 min
// dizia "1 h". Componente real, renderizado no Node:
//
//   node ferramentas/prova-2237-anel-horas.mjs
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

// Janela 12h30: conferir às 12:00 (trecho de 20 min e conferência 10 min antes de sair).
const JANELAS = [{ id: 'j2', faixa: '12h30 às 13h30' }]
const CONSTANTES = { minutosTrecho: 20, minutosChegadaEntregador: 15, minutosConferir: 10 }
const CONFERIR = Date.parse('2026-11-04T12:00:00-03:00')
const conversa = {
  id: '184', nome: 'José', cliente: { endereco: 'Rua Fradique Coutinho, 900, Vila Madalena, 05416-001' },
  pedido: { numero: '2026-0184', estado: 'pago', janela: 'j2', entregador: null, itens: [{ sku: 'LAS-CLA', qtd: 1 }], pagamentos: [] },
}
const numeroDoAnel = (minutosAntes) => {
  const html = renderToStaticMarkup(h(CartaoEntrega, {
    conversa, grupoDaViagem: null, janelas: JANELAS, agora: CONFERIR - minutosAntes * 60000, constantes: CONSTANTES,
    cardapio: [], viagensDisponiveis: [], acoes: {}, aoAbrirAgendamento: () => {}, opcoesDespacho: {},
  }))
  return /role="timer" aria-label="[^"]*">([^<]*)</.exec(html)?.[1]
}

for (const [minutos, esperado] of [[405, '6 h'], [100, '1 h'], [125, '2 h'], [99, '99'], [45, '45'], [-10, '+10']]) {
  await confere(`${minutos} min até o marco: o anel diz "${esperado}"`, () => {
    assert.equal(numeroDoAnel(minutos), esperado)
  })
}

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
