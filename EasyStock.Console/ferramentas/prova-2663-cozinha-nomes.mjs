/* eslint-disable no-console */
// Prova da #2663 (homologação #1769, Cozinha K1 e K2):
//   - K1: os botões de cada cartão do KDS nomeiam o pedido, como a alça já faz
//     (com dois pedidos na fila, "Começar preparo" repetido não diz de quem é);
//   - K2: no celular a faixa de chips de linha ocupa a linha inteira e a gaveta de canhotos desce
//     (antes os chips encolhiam para 70 px e só "Toda" aparecia).
// Componente real renderizado no Node (mesmo gancho de `prova-2658-entregas-nomes.mjs`):
//
//   node ferramentas/prova-2663-cozinha-nomes.mjs

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

const { CartaoKds } = await import('../src/features/cozinha/CartaoKds.jsx')
const agora = Date.parse('2026-11-15T11:00:00Z')
const pedido = (extra) => ({
  id: 'p1', numeroCurto: '243B63C4', numeroComanda: 4, numeroDoDia: 3, clienteNome: 'Ana Souza', status: 'aguardando',
  statusRotulo: 'Aguardando', itens: [], atrasado: false, janela: null, ...extra,
})
const cartao = (p, extra = {}) => renderToStaticMarkup(h('ul', null, h(CartaoKds, {
  pedido: p, agora, linhas: {}, linhaFiltro: null, aoAvancar() {}, aoAbrir() {}, ...extra,
})))
const botao = (html, texto) => html.match(new RegExp(`<button[^>]*>(?:(?!</button>).)*${texto}(?:(?!</button>).)*</button>`, 's'))?.[0] ?? ''
const nome = (html, texto) => botao(html, texto).match(/aria-label="([^"]+)"/)?.[1] ?? '(sem aria-label)'

for (const [status, toque] of [['aguardando', 'Começar preparo'], ['preparando', 'Marcar pronto'], ['pronto', 'Saiu para entrega']]) {
  await confere(`K1: ${toque} nomeia o pedido`, () => {
    assert.equal(nome(cartao(pedido({ status })), toque), `${toque}: Comanda 4 de Ana Souza`)
  })
}

await confere('K1: Enviando… também nomeia o pedido', () => {
  assert.equal(nome(cartao(pedido(), { movendo: true }), 'Enviando'), 'Enviando…: Comanda 4 de Ana Souza')
})

await confere('K1: Ver comanda nomeia o pedido', () => {
  assert.equal(nome(cartao(pedido()), 'Ver comanda'), 'Ver comanda: Comanda 4 de Ana Souza')
})

await confere('K1: Imprimir pedido nomeia o pedido', () => {
  assert.equal(nome(cartao(pedido()), 'Imprimir pedido'), 'Imprimir pedido: Comanda 4 de Ana Souza')
})

await confere('K2: no celular a faixa de chips ocupa a linha inteira da barra de filtros', () => {
  const folha = readFileSync(fileURLToPath(new URL('../src/features/cozinha/cozinha.module.css', import.meta.url)), 'utf8')
  const celular = folha.slice(folha.indexOf('@media (max-width: 640px)'))
  const regra = celular.match(/\.filtros > \.chipsFiltro \{([^}]*)\}/)?.[1] ?? ''
  assert.match(regra, /flex:\s*1 1 100%/)
})

// D2 (decisão do Felipe): o checkout grava o endereço nas observações com rótulos de máquina; a
// Cozinha mostra só o endereço formatado e o que o cliente escreveu. O dado gravado não muda.
const RETRATO = '[Entrega] Rua Teodoro Sampaio, 104, Pinheiros, São Paulo/SP, CEP 05411000'
const GUEST = '[Guest] CEP 05411-000, numero 104'
const ENDERECO = 'Rua Teodoro Sampaio, 104, Pinheiros, São Paulo/SP, CEP 05411-000'
const kds = await import('../src/dominio/kds.js')

await confere('D2: o retrato do checkout vira endereço formatado, sem rótulos', () => {
  assert.deepEqual(kds.leituraDasObservacoes(`${RETRATO} | ${GUEST}`), { endereco: ENDERECO, observacoes: null })
})

await confere('D2: o que o cliente escreveu continua como observação', () => {
  assert.deepEqual(kds.leituraDasObservacoes(`Ligar antes | ${RETRATO} | ${GUEST}`), { endereco: ENDERECO, observacoes: 'Ligar antes' })
})

await confere('D2: só o rótulo de convidado vira "CEP ..., nº ..."', () => {
  assert.deepEqual(kds.leituraDasObservacoes(GUEST), { endereco: 'CEP 05411-000, nº 104', observacoes: null })
})

await confere('D2: observação sem rótulo fica como está', () => {
  assert.deepEqual(kds.leituraDasObservacoes('Sem cebola'), { endereco: null, observacoes: 'Sem cebola' })
})

await confere('D2: o cartão mostra o endereço formatado, sem [Entrega] nem [Guest]', () => {
  const html = cartao(pedido({ observacoes: `Ligar antes | ${RETRATO} | ${GUEST}` }))
  assert.ok(!/\[Entrega\]|\[Guest\]|numero 104/.test(html), 'rótulo cru no cartão')
  assert.ok(html.includes(ENDERECO), 'endereço formatado ausente')
  assert.ok(html.includes('Ligar antes'), 'observação do cliente ausente')
})

await confere('D2: o canhoto impresso também sai sem rótulos', () => {
  const dados = kds.canhotoDoKds(pedido({ observacoes: `Ligar antes | ${RETRATO} | ${GUEST}` }), {})
  assert.equal(dados.endereco, ENDERECO)
  assert.equal(dados.observacoes, 'Ligar antes')
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
