import assert from 'node:assert/strict'
import { imprimirHtml } from '../src/componentes/htmlNoNavegador.js'

let impressas = 0
let abas = 0
let atual
let liberarFontes
globalThis.window = { open: () => { abas++; return {} } }
globalThis.document = {
  createElement: () => {
    const eventos = {}
    atual = { title: '', style: {}, remove: () => {}, setAttribute: () => {},
      addEventListener: (nome, fn) => { eventos[nome] = fn },
      contentDocument: { fonts: { ready: new Promise((ok) => { liberarFontes = ok }) } },
      contentWindow: { addEventListener: () => {}, focus: () => {}, print: () => { impressas++ } },
      carregar: () => eventos.load() }
    return atual
  },
  body: { appendChild: () => {} },
}

const antesDaCarga = new AbortController()
const primeira = imprimirHtml('<html>teste</html>', { sinal: antesDaCarga.signal })
antesDaCarga.abort()
await assert.rejects(primeira, { name: 'AbortError' })
await atual.carregar()
assert.equal(impressas, 0)
assert.equal(abas, 0)

const duranteFonte = new AbortController()
const segunda = imprimirHtml('<html>teste</html>', { sinal: duranteFonte.signal })
const esperando = atual.carregar()
duranteFonte.abort()
liberarFontes()
await assert.rejects(segunda, { name: 'AbortError' })
await esperando
assert.equal(impressas, 0)
assert.equal(abas, 0)

const terceira = imprimirHtml('<html>teste</html>')
const carregada = atual.carregar()
liberarFontes()
await carregada
assert.equal(await terceira, 'dialogo')
assert.equal(impressas, 1)
assert.equal(abas, 0)
await assert.rejects(imprimirHtml(''), /vazio/)
console.log('Cancelamento antes da carga e durante fontes: nenhum disparo. Fluxo válido: uma chamada ao diálogo, sem confirmação automática.')
