/* eslint-disable no-console */
// #2734: quem acabou de gravar o cadastro precisa de uma leitura do dossiê que comece depois da gravação. A leitura
// que já estava em voo pode ter saído do servidor antes: reaproveitá-la deixaria a Ficha com o cadastro antigo.
//
//   node ferramentas/prova-2734-releitura-cliente.mjs
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
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    if (url.endsWith('.json')) {
      return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    }
    return proximo(url, contexto)
  },
})

globalThis.sessionStorage = {
  getItem: () => JSON.stringify({ token: 't', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa' } }),
  setItem: () => {}, removeItem: () => {},
}
globalThis.window = globalThis.window ?? { dispatchEvent: () => {} }

// Cada leitura do dossiê fica retida até a prova soltar: é assim que a leitura "em voo" existe de verdade.
const dossies = []
globalThis.fetch = async (url, opcoes = {}) => {
  const caminho = new URL(String(url), 'http://console.teste').pathname
  const json = (corpo) => new Response(JSON.stringify(corpo), { status: 200, headers: { 'Content-Type': 'application/json' } })
  if ((opcoes.method ?? 'GET') !== 'GET' || !caminho.endsWith('/dossie')) return json({ data: null })
  return json({ data: await new Promise((soltar) => dossies.push(soltar)) })
}

const acao = await import('../src/aplicacao/acoes.js')
const { criarAcoesClienteApi } = await import('../src/aplicacao/api/cliente.js')

const despachos = []
const acoes = criarAcoesClienteApi({
  despachar: (a) => despachos.push(a),
  estadoRef: { current: { conversas: [{ id: 'c1', nome: 'Ana Souza', clienteId: 'cl1', vinculoRevisao: 0, cliente: {} }] } },
})
const dossie = (nota) => ({
  cliente: { id: 'cl1', nome: 'Ana Souza', telefone: '+5511999998888', email: null, observacoes: null, motivoBloqueio: null, fotoUrl: null },
  enderecos: [], tags: [], notas: [{ id: 'N1', texto: nota, autor: 'Operadora', pedidoId: null, mensagemId: null, criadoEm: '2026-11-12T21:00:00' }],
  ultimosPedidos: [], itemFavorito: null, ultimaCompraEm: null, totalPedidos: 0, bloqueado: false, conversasRecentes: [],
  pedidoEmAndamento: null, contatoInformado: null, totalGasto: 0,
})
const pausa = (ms) => new Promise((r) => setTimeout(r, ms))
const ate = async (condicao, descricao) => {
  for (let i = 0; i < 200; i += 1) { if (condicao()) return; await pausa(5) }
  assert.fail(`não aconteceu: ${descricao}`)
}

// 1. Duas leituras comuns ao mesmo tempo viram uma chamada só (#1474): isso continua valendo.
const primeira = acoes.carregarClienteDaConversa('c1')
const repetida = acoes.carregarClienteDaConversa('c1')
await ate(() => dossies.length === 1, 'a primeira leitura sair')
await pausa(30)
assert.equal(dossies.length, 1, 'leitura repetida em voo não chama o servidor de novo')

// 2. Com a leitura ainda em voo, o cadastro é gravado e a tela pede a releitura: ela espera a que está em voo
//    terminar e lê de novo, porque a que estava em voo pode ter saído do servidor antes da gravação.
assert.equal(typeof acoes.relerClienteDaConversa, 'function', 'existe a leitura que começa depois da gravação')
const relida = acoes.relerClienteDaConversa('c1')
await pausa(30)
assert.equal(dossies.length, 1, 'a releitura espera a leitura em voo terminar')
dossies[0](dossie('Nota de antes da gravação'))
assert.deepEqual(await Promise.all([primeira, repetida]), [true, true])
await ate(() => dossies.length === 2, 'a segunda leitura sair depois da primeira')
dossies[1](dossie('Nota depois da gravação'))
assert.equal(await relida, true)
const lidos = despachos.filter((d) => d.tipo === acao.CLIENTE_DA_API)
assert.equal(lidos.length, 2, 'as duas leituras chegam à Ficha, a nova por último')
assert.match(JSON.stringify(lidos.at(-1)), /Nota depois da gravação/, 'a Ficha fica com o que o servidor tinha depois da gravação')

// 3. Sem leitura em voo, a releitura é uma leitura só.
const sozinha = acoes.relerClienteDaConversa('c1')
await ate(() => dossies.length === 3, 'a leitura sair')
dossies[2](dossie('Nota depois da gravação'))
assert.equal(await sozinha, true)
assert.equal(dossies.length, 3)

// 4. Duas gravações seguidas com uma leitura em voo: uma releitura só depois dela, não uma rajada.
const emVoo = acoes.carregarClienteDaConversa('c1')
await ate(() => dossies.length === 4, 'a leitura sair')
const duas = [acoes.relerClienteDaConversa('c1'), acoes.relerClienteDaConversa('c1')]
dossies[3](dossie('Nota depois da gravação'))
await emVoo
await ate(() => dossies.length === 5, 'a releitura sair')
await pausa(30)
assert.equal(dossies.length, 5, 'duas releituras pedidas juntas viram uma chamada')
dossies[4](dossie('Nota depois da gravação'))
assert.deepEqual(await Promise.all(duas), [true, true])

console.log('prova-2734-releitura-cliente ok: a releitura começa depois da gravação, espera a leitura em voo e não vira rajada')
