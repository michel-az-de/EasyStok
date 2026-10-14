/* eslint-disable no-console */
// Prova da #1808 item 4: o banco de imagens associa a imagem a um prato.
//   - a ficha da imagem lista qualquer prato da empresa, os vinculados primeiro, sem os arquivados;
//   - a grade "Fotos do cardápio" abre a escolha de imagem para o prato em vez de só abrir a URL.
//
//   node ferramentas/prova-1808-acervo-prato.mjs

import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

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
const importar = async (caminho) => { try { return await import(caminho) } catch (erro) { console.log(`(importação de ${caminho}: ${erro.message})`); return {} } }

const pratosDoAcervo = await importar('../src/dominio/pratosDoAcervo.js')
const fonte = (caminho) => readFileSync(new URL(caminho, import.meta.url), 'utf8')

await confere('a ficha lista qualquer prato da empresa, vinculados primeiro, sem arquivados', () => {
  const itens = [
    { cardapioItemId: 'b', nome: 'Ravioli' },
    { cardapioItemId: 'a', nome: 'Lasanha' },
    { cardapioItemId: 'c', nome: 'Nhoque', arquivado: true },
    { cardapioItemId: 'd', nome: 'Água' },
  ]
  assert.deepEqual(pratosDoAcervo.pratosParaUsar(itens, ['b']), [
    { id: 'b', nome: 'Ravioli', vinculado: true },
    { id: 'd', nome: 'Água', vinculado: false },
    { id: 'a', nome: 'Lasanha', vinculado: false },
  ])
  assert.deepEqual(pratosDoAcervo.pratosParaUsar(undefined, undefined), [])
})

await confere('a ficha tem "Usar em um prato" e a grade de fotos abre a escolha da imagem', () => {
  const aba = fonte('../src/features/gestao/acervo/AbaAcervo.jsx')
  const grade = fonte('../src/features/gestao/acervo/FotosCardapio.jsx')
  assert.match(aba, /Usar em um prato/)
  assert.match(aba, /pratosParaUsar/)
  assert.match(aba, /prepararPublicacao\(prato/, 'a escolha usa o prepararPublicacao existente')
  assert.match(grade, /aoEscolherPrato/)
  assert.doesNotMatch(grade, /<a className=\{css\.cartao\}/, 'a foto não é mais só um link para a URL')
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
