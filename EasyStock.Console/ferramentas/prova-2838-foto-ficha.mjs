// #2838: a foto trocada na Ficha some porque a releitura reaproveitava a leitura do dossiê que já estava em voo e
// tinha saído do servidor antes da gravação. Quem acabou de gravar relê com a leitura que começa depois
// (`relerClienteDaConversa`, coberta em prova-2734-releitura-cliente).
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

const modal = readFileSync(new URL('../src/features/ficha-cliente/ModalCliente.jsx', import.meta.url), 'utf8')
const corpo = modal.match(/async function alterarFoto[\s\S]*?\r?\n {2}\}\r?\n/)?.[0]
assert.ok(corpo, 'a Ficha tem a troca de foto')
assert.match(corpo, /relerClienteDaConversa\(conversa\.id\)/, 'depois de gravar a foto, a Ficha relê com a leitura que começa depois')
assert.doesNotMatch(corpo, /carregarClienteDaConversa\(/, 'e não reaproveita a leitura em voo')

console.log('prova-2838-foto-ficha ok: a foto trocada relê a ficha depois da gravação')
