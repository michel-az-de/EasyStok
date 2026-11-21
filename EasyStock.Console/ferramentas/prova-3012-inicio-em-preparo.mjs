// Prova da #3012 (crítica UX de 20/11, Início): a busca com resultado não repete o "Limpar busca" (o X do campo
// limpa) e a rota de área ou tela congelada (#1891) diz que está em preparo, em vez do "não existe" genérico.
//
// Roda com: node ferramentas/prova-3012-inicio-em-preparo.mjs
// Contra outra árvore (ex.: o master, para ver o Red): PROVA_SRC=<pasta src> node ferramentas/prova-3012-inicio-em-preparo.mjs

import { readFileSync } from 'node:fs'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

const base = process.env.PROVA_SRC ? pathToFileURL(process.env.PROVA_SRC.replace(/[\/]?$/, '/')) : new URL('../src/', import.meta.url)
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})
const { ROTA_HALL, rotaDaHash } = await import(new URL('dominio/rota.js', base).href)
const jsxHall = readFileSync(fileURLToPath(new URL('features/hall/HallDeModulos.jsx', base)), 'utf8')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }
const GENERICO = 'Esta página não existe ou não está disponível. Escolha uma área para continuar.'

confere('a busca com resultado não repete o "Limpar busca": o X do campo limpa', () => {
  assert.doesNotMatch(jsxHall, /Limpar busca/, 'o botão duplicado saiu do Início')
  assert.equal(jsxHall.split('Ver todos os módulos').length - 1, 1, 'o vazio segue com o botão de voltar à lista')
})

confere('área e tela congeladas voltam ao Início dizendo que estão em preparo', () => {
  for (const [hash, nome] of [
    ['#/m/campanhas', 'Campanhas'], ['#/m/campanhas/fidelidade', 'Fidelidade e cupons'], ['#/m/cozinha/producao', 'Produção e cardápio do dia'],
  ]) {
    for (const fonteApi of [true, false]) {
      const rota = rotaDaHash(hash, { fonteApi })
      assert.equal(rota.tipo, ROTA_HALL, `${hash} volta ao Início`)
      assert.notEqual(rota.aviso, GENERICO, `${hash} não cai no aviso genérico`)
      assert.match(rota.aviso, new RegExp(`^${nome}.* ainda está em preparo e não abre nesta versão\. Escolha uma área para continuar\.$`), hash)
    }
  }
})

confere('hash que não está no catálogo segue com o aviso genérico', () => {
  for (const hash of ['#/nada-a-ver', '#/m/invalido/tela', '#/m/cadastros/invalida', '#/m/campanhas/outra']) {
    assert.equal(rotaDaHash(hash, { fonteApi: true }).aviso, GENERICO, hash)
  }
})

console.log(`\n${passou} verificações, todas passaram.`)
