/* eslint-disable no-console */
// Prova da #1529 (M1.4a): porções do prato no console (modo API).
//   - o detalhe traz as porções; o editor valida nome, preço e nome repetido;
//   - só uma padrão; a edição manda o id da porção e só vai quando mudou;
//   - com porções, o preço e a porção do prato acompanham a padrão.
//
//   node ferramentas/prova-1529-porcoes.mjs

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

const { rascunhoDasPorcoes, porcaoNova, erroDasPorcoes, comPadrao, corpoDasPorcoes, porcoesMudaram, resumoDoItem, precoDoTexto } = await import('../src/dominio/porcoes.js')
const { corpoDoItem, detalheDaApi } = await import('../src/infra/api/cardapioApi.js')

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

const DO_EASYSTOK = [
  { id: 'v-1', rotulo: '300 g', preco: 28, peso: '300 g', disponivel: true, padrao: true, sku: null },
  { id: 'v-2', rotulo: '800 g', preco: 62, peso: '', disponivel: false, padrao: false, sku: 'RAV-800' },
]

await confere('o detalhe do EasyStok traz as porções e o rascunho mostra o preço como ela digita', () => {
  const d = detalheDaApi({ porcoes: [{ id: 'v-1', rotulo: '300 g', preco: 28, peso: '300 g', disponivel: true, padrao: true }] })
  assert.deepEqual(d.porcoes[0], { id: 'v-1', rotulo: '300 g', preco: 28, peso: '300 g', disponivel: true, padrao: true, sku: null })
  assert.equal(rascunhoDasPorcoes(d.porcoes)[0].preco, '28,00')
  assert.deepEqual(detalheDaApi({}).porcoes, [], 'prato sem porções')
})

await confere('preço digitado com vírgula e milhar', () => {
  assert.equal(precoDoTexto('28,50'), 28.5)
  assert.equal(precoDoTexto('1.250,00'), 1250)
  assert.ok(Number.isNaN(precoDoTexto('')))
})

await confere('porção sem nome, sem preço ou com nome repetido não passa', () => {
  const linhas = rascunhoDasPorcoes(DO_EASYSTOK)
  assert.equal(erroDasPorcoes(linhas), null)
  assert.match(erroDasPorcoes([...linhas, porcaoNova()]), /nome/)
  assert.match(erroDasPorcoes([{ ...linhas[0], preco: '0' }]), /preço/)
  assert.match(erroDasPorcoes([linhas[0], { ...linhas[1], rotulo: '300 G' }]), /mesmo nome/)
})

await confere('só uma padrão; sem nenhuma marcada, a primeira vale', () => {
  const linhas = rascunhoDasPorcoes(DO_EASYSTOK)
  assert.deepEqual(comPadrao(linhas, 'v-2').map((l) => l.padrao), [false, true])
  const semPadrao = linhas.map((l) => ({ ...l, padrao: false }))
  assert.deepEqual(corpoDasPorcoes(semPadrao).map((p) => p.padrao), [true, false])
})

await confere('o corpo vai com o id da porção (edição não troca a porção) e o SKU que veio', () => {
  const corpo = corpoDasPorcoes(rascunhoDasPorcoes(DO_EASYSTOK))
  assert.deepEqual(corpo[1], { id: 'v-2', rotulo: '800 g', preco: 62, peso: null, disponivel: false, padrao: false, sku: 'RAV-800' })
  assert.equal(corpoDasPorcoes([{ ...porcaoNova(), rotulo: ' Fatia ', preco: '12,00' }])[0].id, null, 'porção nova vai sem id')
})

await confere('sem mudança as porções não vão; mudou o preço, vai a lista inteira', () => {
  const linhas = rascunhoDasPorcoes(DO_EASYSTOK)
  assert.equal(porcoesMudaram(DO_EASYSTOK, linhas), false)
  assert.equal('porcoes' in corpoDoItem({ nome: 'Ravióli', preco: 28 }), false, 'ausente = não mexe')
  const mudadas = linhas.map((l, i) => (i === 0 ? { ...l, preco: '30,00' } : l))
  assert.equal(porcoesMudaram(DO_EASYSTOK, mudadas), true)
  assert.equal(corpoDoItem({ nome: 'Ravióli', preco: 30, porcoes: corpoDasPorcoes(mudadas) }).porcoes.length, 2)
  assert.equal(porcoesMudaram(DO_EASYSTOK, []), true, 'tirar todas também é mudança')
})

await confere('com porções, o preço e a porção do prato acompanham a padrão', () => {
  assert.deepEqual(resumoDoItem(rascunhoDasPorcoes(DO_EASYSTOK)), { preco: 28, porcao: '300 g' })
  assert.deepEqual(resumoDoItem(comPadrao(rascunhoDasPorcoes(DO_EASYSTOK), 'v-2')), { preco: 62, porcao: '800 g' })
  assert.equal(resumoDoItem([]), null)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
