// #3384 (r4 P1-3): a restrição alimentar só tingia a pílula; a frase "Sem lactose: confira a ficha do prato" saía
// cinza como qualquer outra. O resumo diz qual motivo é (`motivo`) e a barra dá à frase o tom de aviso, sem laranja.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      try { return proximo(spec + '.js', contexto) } catch { /* resolução normal */ }
    }
    return proximo(spec, contexto)
  },
})
const { resumoDoModo } = await import('../src/dominio/resumoDoModo.js')
const conversa = { id: 1, mensagens: [{ dir: 'in', texto: 'ela não pode lactose' }] }
const restricao = { chave: 'restricao', rotulo: 'Restrição alimentar', tom: 'aviso', texto: 'Sem lactose: confira a ficha do prato' }

const r = resumoDoModo(conversa, false, false, { motivo: restricao })
assert.equal(r.motivo, 'restricao', 'o resumo diz que o motivo é a restrição')
assert.equal(r.linha, 'Sem lactose: confira a ficha do prato')
assert.equal(resumoDoModo(conversa, false, false, { motivo: null }).motivo, null, 'sem motivo, nada a destacar')

const barra = readFileSync(new URL('../src/features/atendimento/BarraModo.jsx', import.meta.url), 'utf8')
assert.match(barra, /motivo === 'restricao'/, 'a barra destaca a frase da restrição')
const estilo = readFileSync(new URL('../src/features/atendimento/atendimento.module.css', import.meta.url), 'utf8')
const regra = estilo.match(/\.modoTextoAviso\s*\{([^}]*)\}/)
assert.ok(regra, 'existe o tom de aviso da frase')
assert.match(regra[1], /var\(--alerta\)/, 'cor de aviso, não laranja de ação')
assert.doesNotMatch(regra[1], /border-left|--marca/, 'sem borda só à esquerda e sem a cor de ação')
console.log('prova-3384-restricao-destaque: ok')
