/* eslint-disable no-console */
// Prova da #2944 (E1-11): trocar um rótulo do glossário não altera nenhuma decisão de lógica.
//   node ferramentas/prova-2944-chaves.mjs
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const { ROTULOS, CODIGOS } = await import('../src/dominio/rotulos.js')
const { rotuloCurtoDoMotivo } = await import('../src/dominio/motivoDeEscalada.js')
const { falhaDeEnvio, TIPOS_DE_FALHA } = await import('../src/dominio/falhaDeEnvio.js')
const { nomeDaConversa, nomeExibido } = await import('../src/dominio/nomeDoContato.js')

const FRASE_API = 'Cliente pediu ajuda pela comanda. Confira a conversa e a seleção antes de continuar.'
const padrao = { ...ROTULOS }

// 1. A classificação do motivo vem da frase da API; o texto do chip vem do glossário.
ROTULOS.CHIP_AJUDA_NO_PEDIDO = 'Texto trocado'
assert.equal(rotuloCurtoDoMotivo(FRASE_API), 'Texto trocado', 'o chip segue o glossário')
assert.equal(rotuloCurtoDoMotivo('Conversa reaberta'), 'Conversa reaberta')
Object.assign(ROTULOS, padrao)

// 2. Falha fora do prazo: decide pelo código, com o prazo dito pelo glossário.
ROTULOS.PRAZO_24H = 'prazo trocado'
assert.equal(falhaDeEnvio(CODIGOS.FORA_DA_JANELA).tipo, TIPOS_DE_FALHA.FORA_DA_JANELA)
assert.equal(falhaDeEnvio('erro 131047').tipo, TIPOS_DE_FALHA.FORA_DA_JANELA)
assert.match(falhaDeEnvio(CODIGOS.FORA_DA_JANELA).texto, /prazo trocado/, 'a frase usa o glossário')
Object.assign(ROTULOS, padrao)

// 3. O nome padrão do lead decide por valor da API, não pelo rótulo.
ROTULOS.SEM_NOME = 'Outro'
assert.equal(nomeDaConversa({ nome: 'Cliente WhatsApp', contato: '5511988887777' }), '+55 (11) 98888-7777')
assert.equal(nomeExibido({ nome: 'Cliente WhatsApp', telefone: '+5511988887777' }), '+55 (11) 98888-7777')
Object.assign(ROTULOS, padrao)

console.log('prova-2944-chaves: ok')
