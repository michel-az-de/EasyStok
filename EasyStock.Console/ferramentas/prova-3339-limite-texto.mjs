// #3339 (R1C-08): mensagem acima do limite da API mostra o contador e o motivo, e Enviar volta ao cortar.
// a 4097 caracteres (limite 4096, o da API; o brief falava em 6000, que não é o limite) o aviso diz "1 a mais" e o envio fica desligado;
// b cortando ao limite (4096) o aviso some e o envio volta a valer;
// c o aviso de quem passa mais de um caractere diz o número que falta cortar.
// Antes: o texto dizia "passa 1 caractere do limite", sem a forma "1 a mais" que a tela pede.
import assert from 'node:assert/strict'
import { LIMITE_TEXTO_MENSAGEM, avisoDeLimiteDoTexto } from '../src/dominio/limiteDoTexto.js'

const falhas = []
let passou = 0
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log('ok ' + descricao) } catch (erro) {
    falhas.push(descricao); console.log(`FALHA ${descricao}\n ${String(erro.message).split('\n').slice(0, 4).join(' ')}`)
  }
}

confere('a. 4097 caracteres: o aviso diz "1 a mais" e o envio fica desligado', () => {
  const aviso = avisoDeLimiteDoTexto('a'.repeat(LIMITE_TEXTO_MENSAGEM + 1))
  assert.equal(aviso.acima, true, 'acima do limite tem de desligar o Enviar')
  assert.match(aviso.texto, /1 a mais/, `o aviso não diz "1 a mais": "${aviso.texto}"`)
})

confere('b. cortando ao limite o aviso some e o envio volta', () => {
  assert.equal(avisoDeLimiteDoTexto('a'.repeat(LIMITE_TEXTO_MENSAGEM)).acima, false)
})

confere('c. quem passa mais de um caractere diz o número que falta cortar', () => {
  const aviso = avisoDeLimiteDoTexto('a'.repeat(LIMITE_TEXTO_MENSAGEM + 25))
  assert.match(aviso.texto, /25 a mais/, `o aviso não diz "25 a mais": "${aviso.texto}"`)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) { console.log(falhas.map((f) => ' - ' + f).join('\n')); process.exit(1) }
