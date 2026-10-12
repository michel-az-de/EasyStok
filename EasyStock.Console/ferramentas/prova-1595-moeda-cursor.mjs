// Prova da #1595: o campo de moeda não multiplica o valor quando o cursor não está no fim.
//
// O campo guarda centavos e mostra o texto mascarado ("R$ 0,00"); a cada tecla ele relê os
// dígitos do texto. Com o cursor no início, a tecla entrava antes dos zeros que já estavam lá.
// A prova reproduz a conta do campo (texto mascarado + tecla numa posição = dígitos = centavos)
// com a regra antiga e com a nova, em todas as posições do cursor.
//
// Roda com: node ferramentas/prova-1595-moeda-cursor.mjs

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
})

const { cursorDeMoeda, digitosDeMoeda } = await import('../src/componentes/moedaNoCampo.js')
const { mascaraMoeda } = await import('../src/dominio/formato.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const semEspacoDuro = (texto) => texto.replace(/ /g, ' ')
const antiga = (_anterior, novo) => novo.replace(/\D/g, '')

// O que o campo faz a cada tecla: o navegador insere no cursor, a regra devolve os dígitos e a
// tela remascara. Depois da primeira tecla o navegador deixa o cursor no fim do valor novo.
function digitar(teclas, cursorInicial, regra, inicial = 0) {
  let texto = mascaraMoeda(inicial)
  let cursor = cursorInicial
  for (const tecla of teclas) {
    const novo = texto.slice(0, cursor) + tecla + texto.slice(cursor)
    texto = mascaraMoeda(Number(regra(texto, novo)))
    cursor = texto.length
  }
  return semEspacoDuro(texto)
}

confere('o defeito: pela regra antiga, cursor no início faz 5000 virar R$ 50.000,00', () => {
  assert.equal(digitar('5000', 0, antiga), 'R$ 50.000,00')
  assert.equal(digitar('5000', 3, antiga), 'R$ 50.000,00')
  assert.equal(digitar('10000', 0, antiga), 'R$ 100.000,00')
})

confere('com a regra nova, 5000 é R$ 50,00 em qualquer ponto do clique', () => {
  const tamanho = mascaraMoeda(0).length
  for (let posicao = 0; posicao <= tamanho; posicao += 1) {
    assert.equal(digitar('5000', posicao, digitosDeMoeda), 'R$ 50,00', `cursor na posição ${posicao}`)
    assert.equal(digitar('10000', posicao, digitosDeMoeda), 'R$ 100,00', `cursor na posição ${posicao}`)
  }
})

confere('acrescentar a um valor que já existe também conta no fim', () => {
  const tamanho = mascaraMoeda(1234).length
  for (let posicao = 0; posicao <= tamanho; posicao += 1) {
    assert.equal(digitar('5', posicao, digitosDeMoeda, 1234), 'R$ 123,45', `cursor na posição ${posicao}`)
  }
})

confere('digitar no fim continua igual ao que era', () => {
  for (const teclas of ['1', '68', '6850', '999999', '0', '00']) {
    const fim = mascaraMoeda(0).length
    assert.equal(digitar(teclas, fim, digitosDeMoeda), digitar(teclas, fim, antiga), teclas)
  }
})

confere('apagar e digitar por cima da seleção seguem o navegador', () => {
  const cheio = mascaraMoeda(5000)
  assert.equal(digitosDeMoeda(cheio, cheio.slice(0, -1)), '500') // Backspace no fim
  assert.equal(digitosDeMoeda(cheio, '7'), '7') // tudo selecionado, digitou 7
  assert.equal(digitosDeMoeda(cheio, ''), '') // tudo selecionado, apagou
  assert.equal(digitosDeMoeda('', '5'), '5') // campo que mostra vazio no zero
})

confere('cursor solto fora do fim vai para o fim; no fim e em trecho selecionado fica', () => {
  assert.equal(cursorDeMoeda({ inicio: 0, fim: 0, tamanho: 7 }), 7)
  assert.equal(cursorDeMoeda({ inicio: 3, fim: 3, tamanho: 7 }), 7)
  assert.equal(cursorDeMoeda({ inicio: 7, fim: 7, tamanho: 7 }), null)
  assert.equal(cursorDeMoeda({ inicio: 0, fim: 7, tamanho: 7 }), null)
  assert.equal(cursorDeMoeda({ inicio: 0, fim: 0, tamanho: 0 }), null)
})

confere('a regra vale só para moeda: telefone e cep seguem com edição no meio', () => {
  const campo = readFileSync(new URL('../src/componentes/CampoMascarado.jsx', import.meta.url), 'utf8')
  assert.match(campo, /tipo === 'moeda' \? digitosDeMoeda\(String\(valor \?\? ''\), texto\) : texto\.replace\(\/\\D\/g, ''\)/)
  assert.match(campo, /onSelect=\{tipo === 'moeda' \? manterCursorNoFim : undefined\}/)
})

console.log(`\n${passou} verificações passaram.`)
