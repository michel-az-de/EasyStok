// Prova da issue #40 (rodada 13, registro 102): telas por endereço (Cozinha
// e Cardápio por link) só trocavam ao montar o app. Mudar a hash com o app já
// aberto (janela nova bloqueada, ou o navegador reaproveitando a mesma aba,
// comum em tablet) deixava a tela presa no Balcão, e o link do cardápio
// aberto na mesma aba chegava a mostrar dados da dona para o cliente.
//
// Cobre a função pura que decide a tela a partir do hash
// (`dominio/rota.js: rotaDaHash`). O hook que reage a `hashchange`
// (`hooks/useHash.js`) e o fallback de `window.open` bloqueado
// (`app/Moldura.jsx: abrirCozinha`) são validados na tela real (ver registro
// da decisão); aqui não entram por dependerem de `window`/DOM, fora do
// alcance do Node puro.
//
// Mesmo gancho de `prova-bloqueio-preparo.mjs`: o código de `src/` importa
// sem extensão (o Vite resolve, o Node puro não).
//
// Roda com: node ferramentas/prova-r13-rotas.mjs

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

const {
  ROTA_HALL, ROTA_PRINCIPAL, ROTA_ENTREGAS, ROTA_COZINHA, ROTA_CARDAPIO_LINK, HASH_ENTREGAS, HASH_COZINHA, rotaDaHash,
} = await import('../src/dominio/rota.js')
const { PREFIXO_ROTA } = await import('../src/dominio/cardapioLink.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

// #1447 (homologação de 07/10): sem hash o console abre no hall de módulos; o
// Balcão passou a ser `#/m/atendimento` (prova-1447-hall-modulos.mjs).
confere('sem hash (carga normal) é o hall de módulos', () => {
  assert.equal(rotaDaHash('').tipo, ROTA_HALL)
  assert.equal(rotaDaHash(undefined).tipo, ROTA_HALL)
  assert.equal(rotaDaHash(null).tipo, ROTA_HALL)
})

confere('hash de Entregas dá a rota de Entregas', () => {
  assert.equal(rotaDaHash(HASH_ENTREGAS).tipo, ROTA_ENTREGAS)
})

confere('hash de Cozinha dá a rota de Cozinha, aberta na mesma aba ou em janela nova', () => {
  assert.equal(rotaDaHash(HASH_COZINHA).tipo, ROTA_COZINHA)
})

confere('hash do cardápio por link dá a rota do cliente, nunca a do Balcão', () => {
  const rota = rotaDaHash(PREFIXO_ROTA + 'c1')
  assert.equal(rota.tipo, ROTA_CARDAPIO_LINK)
  assert.notEqual(rota.tipo, ROTA_PRINCIPAL)
  assert.equal(rota.conversaId, 'c1')
})

confere('hash desconhecida cai no hall, não trava nem finge outra tela', () => {
  assert.equal(rotaDaHash('#/nada-a-ver').tipo, ROTA_HALL)
})

confere('mesma hash, chamadas diferentes: a rota é sempre recalculada (nada de cache preso na primeira leitura)', () => {
  const primeira = rotaDaHash(HASH_COZINHA)
  const depoisDeEntregas = rotaDaHash(HASH_ENTREGAS)
  const voltaParaCozinha = rotaDaHash(HASH_COZINHA)
  assert.equal(primeira.tipo, ROTA_COZINHA)
  assert.equal(depoisDeEntregas.tipo, ROTA_ENTREGAS)
  assert.equal(voltaParaCozinha.tipo, ROTA_COZINHA)
})

console.log(`\n${passou} verificações passaram.`)
