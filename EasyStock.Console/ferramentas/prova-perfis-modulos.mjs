import assert from 'node:assert/strict'
import { entradaDoPerfil, permiteModulo, permiteRota } from '../src/dominio/acessoModulos.js'

const cozinha = { portaDeEntrada: 'cozinha', modulos: [
  { id: 'cozinha', liberado: true }, { id: 'producao', liberado: true }, { id: 'financeiro', liberado: false },
] }
assert.equal(entradaDoPerfil(cozinha), '#/m/cozinha')
assert.equal(permiteRota(cozinha, { tipo: 'cozinha' }), true)
assert.equal(permiteRota(cozinha, { tipo: 'entregas' }), false)
assert.equal(permiteRota(cozinha, { tipo: 'principal', modulo: 'atendimento' }), false)
assert.equal(permiteRota(cozinha, { tipo: 'modulo', modulo: 'financeiro' }), false)
assert.equal(permiteRota(cozinha, { tipo: 'modulo', modulo: 'producao' }), true)
assert.equal(permiteRota(cozinha, { tipo: 'hall' }), true)
assert.equal(permiteModulo(null, 'cozinha'), false)
assert.equal(entradaDoPerfil({ ...cozinha, portaDeEntrada: 'financeiro' }), '#/')
assert.equal(permiteModulo({ modulos: [{ id: 'cozinha', liberado: 'true' }] }, 'cozinha'), false)
console.log('Perfis: entrada, links diretos, apelidos, hall e falha fechada aprovados.')
