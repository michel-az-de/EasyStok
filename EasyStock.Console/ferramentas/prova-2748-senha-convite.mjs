/* eslint-disable no-console */
// Prova da #2748: a regra de senha do convite é a da API (PasswordPolicy): letra e dígito do Unicode, símbolo é
// tudo que não é letra nem dígito (o sublinhado conta) e no máximo 128 caracteres.
//
//   node ferramentas/prova-2748-senha-convite.mjs

import assert from 'node:assert/strict'
import { regrasDaSenha, senhaValida } from '../src/dominio/usuarios.js'

let passaram = 0
const conferir = (nome, teste) => { teste(); passaram += 1; console.log('ok  ' + nome) }

conferir('sublinhado conta como símbolo, como na API', () => {
  assert.equal(senhaValida('Senha_Forte123'), true)
})

conferir('maiúscula acentuada conta como maiúscula', () => {
  assert.equal(senhaValida('Ésenha1234!'), true)
  assert.equal(senhaValida('Ésenha1234_'), true)
})

conferir('teto de 128 caracteres', () => {
  assert.equal(senhaValida('Aa1!' + 'x'.repeat(124)), true)
  assert.equal(senhaValida('Aa1!' + 'x'.repeat(125)), false)
  assert.equal(senhaValida('Aa1!' + 'x'.repeat(126)), false)
  const teto = regrasDaSenha('Aa1!' + 'x'.repeat(125)).find((r) => r.id === 'maximo')
  assert.equal(teto.rotulo, 'no máximo 128 caracteres')
  assert.equal(teto.ok, false)
})

conferir('só letra e número continua sem símbolo, e letra sem número sem número', () => {
  assert.equal(senhaValida('Senha12345ç'), false)
  assert.equal(senhaValida('Senha!!!!!!'), false)
})

console.log(`\n${passaram} verificações passaram.`)
