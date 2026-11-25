/* eslint-disable no-console */
// Prova da #3339 (R1C-07, `docs/dev/anexos-estresse-atendimento-2026-11-25/r1-cardapio-comanda.md`): depois de
// "Enviar link" o modal fechava e o foco ia para o `main`; quem atende tinha de clicar no campo para seguir. Agora
// o foco volta ao campo "Mensagem para o cliente" no desktop (ponteiro fino) e fica onde está no toque.
//
//   node ferramentas/prova-3339-foco-apos-cardapio.mjs

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok  ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA  ${nome}\n      ${String(erro.message).split('\n')[0]}`) }
}

const { devolverFocoAoCampoDeMensagem } = await import('../src/features/atendimento/focoDoComposer.js')

const montarDocumento = ({ desabilitado = false, rotulo = 'Mensagem para o cliente' } = {}) => {
  const campo = { disabled: desabilitado, labels: [{ textContent: rotulo }], focado: false, focus() { this.focado = true; documento.activeElement = this } }
  const documento = { activeElement: { tagName: 'BODY' }, querySelectorAll: () => [campo] }
  return { documento, campo }
}

caso('R1C-07: no desktop o foco volta ao campo de mensagem', () => {
  assert.equal(typeof devolverFocoAoCampoDeMensagem, 'function', 'devolverFocoAoCampoDeMensagem não existe')
  const { documento, campo } = montarDocumento()
  assert.equal(devolverFocoAoCampoDeMensagem({ documento, ponteiroFino: true }), true)
  assert.equal(documento.activeElement, campo)
})

caso('no toque o foco não é puxado (o teclado abriria sozinho)', () => {
  const { documento, campo } = montarDocumento()
  assert.equal(devolverFocoAoCampoDeMensagem({ documento, ponteiroFino: false }), false)
  assert.equal(campo.focado, false)
})

caso('campo desabilitado (janela fechada) não recebe foco', () => {
  const { documento, campo } = montarDocumento({ desabilitado: true })
  assert.equal(devolverFocoAoCampoDeMensagem({ documento, ponteiroFino: true }), false)
  assert.equal(campo.focado, false)
})

caso('outro campo de texto não é confundido com o da mensagem', () => {
  const { documento, campo } = montarDocumento({ rotulo: 'Nota interna' })
  assert.equal(devolverFocoAoCampoDeMensagem({ documento, ponteiroFino: true }), false)
  assert.equal(campo.focado, false)
})

caso('o Composer ainda usa o rótulo que o foco procura', () => {
  const composer = readFileSync(new URL('../src/features/atendimento/Composer.jsx', import.meta.url), 'utf8')
  assert.ok(composer.includes('rotulo="Mensagem para o cliente"'), 'o rótulo do campo mudou no Composer')
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
