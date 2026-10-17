/* eslint-disable no-console */
// Prova da issue #1884 ("Quanto fica?"): antes de gerar a cobrança a comanda mostra o frete em número, o
// total com o frete, o sinal da encomenda e o restante, vindos da simulação no servidor.
//
//   1  o corpo da simulação é o da comanda (itens, janela, data), sem forma nem envio;
//   2  a chamada vai para `POST .../conversas/{id}/pedido/simulacao` e devolve o que o servidor calculou;
//   3  as linhas da comanda: frete e total com frete, frete a combinar fora da área, sinal só na encomenda;
//   4  o resumo da barra usa os números do servidor, e sem simulação cai no texto de antes (#1875);
//   5  a simulação só roda com itens e janela, sem pedido criado, e a tela cai no texto antigo se falhar.
//
//   node ferramentas/prova-1884-simulacao.mjs

import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    return proximo(url, contexto)
  },
})

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8').replaceAll(String.fromCharCode(13), '')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok  ' + descricao) }
  catch (erro) { falhas.push(descricao); console.error('FALHOU  ' + descricao + '\n        ' + String(erro.message).split('\n')[0]) }
}

const { moeda } = await import('../src/dominio/formato.js')
const { corpoDaSimulacao, simularPedido } = await import('../src/infra/api/comandaApi.js')
const simulacaoDoPedido = await import('../src/dominio/simulacaoPedido.js')
const { resumoDaCobrancaEscolhida } = await import('../src/dominio/resumoPedido.js')

const AGORA = Date.parse('2026-10-17T15:00:00-03:00')
const encomenda = { pedidoId: null, estado: 'aguardando', itens: [{ sku: 'massa', qtd: 2, obs: 'sem sal' }], janela: 'j1|2026-10-20', meio: null }
const doServidor = { itens: 20, frete: 5, freteACombinar: false, total: 25, encomenda: true, sinal: 12.5, restante: 12.5 }

await confere('1  corpoDaSimulacao é o corpo do pedido sem forma, envio nem meio', () => {
  const corpo = corpoDaSimulacao({ ...encomenda, meio: 'pix' })
  assert.deepEqual(corpo, { itens: [{ cardapioItemId: 'massa', qtd: 2, observacao: 'sem sal' }], janelaId: 'j1', dataEntrega: '2026-10-20' })
})

await confere('2  simularPedido faz POST em pedido/simulacao e devolve o cálculo do servidor', async () => {
  globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }
  const chamadas = []
  globalThis.fetch = async (url, opcoes = {}) => {
    chamadas.push({ url: String(url), metodo: opcoes.method ?? 'GET', corpo: opcoes.body ? JSON.parse(opcoes.body) : null })
    return new Response(JSON.stringify({ data: doServidor }))
  }
  const resultado = await simularPedido('conversa-1', corpoDaSimulacao(encomenda))
  assert.equal(chamadas.length, 1)
  assert.equal(chamadas[0].metodo, 'POST')
  assert.match(chamadas[0].url, /\/api\/atendimento\/conversas\/conversa-1\/pedido\/simulacao$/)
  assert.equal(chamadas[0].corpo.janelaId, 'j1')
  assert.deepEqual(resultado, doServidor)
})

await confere('3  linhas da comanda: frete, total com frete e o sinal da encomenda em reais', () => {
  const linhas = simulacaoDoPedido.linhasDaSimulacao(doServidor)
  const texto = linhas.map((l) => l.texto).join(' | ')
  assert.ok(texto.includes(`Frete ${moeda(5)}`), texto)
  assert.ok(texto.includes(`Total com frete ${moeda(25)}`), texto)
  assert.ok(texto.includes(`sinal de ${moeda(12.5)}`), texto)
  assert.ok(texto.includes(`${moeda(12.5)} na entrega`), texto)
})

await confere('3  fora da área o frete é a combinar e não inventa total nem sinal; fora da encomenda não tem sinal', () => {
  const fora = simulacaoDoPedido.linhasDaSimulacao({ itens: 20, frete: null, freteACombinar: true, total: null, encomenda: true, sinal: null, restante: null })
  const texto = fora.map((l) => l.texto).join(' | ')
  assert.match(texto, /Frete a combinar com a equipe/)
  assert.doesNotMatch(texto, /Total com frete|sinal de/)
  const hoje = simulacaoDoPedido.linhasDaSimulacao({ itens: 20, frete: 5, freteACombinar: false, total: 25, encomenda: false, sinal: null, restante: null })
  assert.doesNotMatch(hoje.map((l) => l.texto).join(' | '), /sinal/i)
  assert.equal(simulacaoDoPedido.linhasDaSimulacao(null), null)
})

await confere('4  resumo da barra com a simulação: total com frete e sinal do servidor; sem ela, o texto antigo', () => {
  const comSimulacao = resumoDaCobrancaEscolhida(encomenda, { rotuloMeio: 'Pix', total: 20, fonteApi: true, agora: AGORA, simulacao: doServidor })
  assert.ok(comSimulacao.includes(moeda(25)), comSimulacao)
  assert.ok(comSimulacao.includes(`sinal de ${moeda(12.5)}`), comSimulacao)
  assert.doesNotMatch(comSimulacao, /o frete entra ao gerar/)
  const semSimulacao = resumoDaCobrancaEscolhida(encomenda, { rotuloMeio: 'Pix', total: 20, fonteApi: true, agora: AGORA })
  assert.match(semSimulacao, /o frete entra ao gerar/)
})

await confere('5  a simulação só vale com itens, janela e pedido ainda não criado', () => {
  const { simulacaoVale } = simulacaoDoPedido
  assert.equal(simulacaoVale(encomenda), true)
  assert.equal(simulacaoVale({ ...encomenda, pedidoId: 'p1' }), false)
  assert.equal(simulacaoVale({ ...encomenda, itens: [] }), false)
  assert.equal(simulacaoVale({ ...encomenda, janela: null }), false)
})

await confere('5  a tela pede a simulação em um só lugar e passa o resultado à comanda e à barra', () => {
  const painel = fonte('features/ficha-cliente/PainelFicha.jsx')
  assert.match(painel, /useSimulacaoDoPedido\(/)
  assert.match(painel, /<BlocoPedido[\s\S]*simulacao=\{simulacao\}/)
  assert.match(painel, /<BarraProximoPasso[\s\S]*simulacao=\{simulacao\}/)
  const bloco = fonte('features/ficha-cliente/BlocoPedido.jsx')
  assert.match(bloco, /linhasDaSimulacao\(simulacao\)/)
  assert.match(bloco, /O frete entra ao gerar a cobrança/, 'sem simulação a nota de antes continua')
  const gancho = fonte('aplicacao/useSimulacaoDoPedido.js')
  assert.match(gancho, /AbortController/)
  assert.match(gancho, /simulacaoVale\(/)
  assert.match(gancho, /catch/, 'falha da simulação não derruba a tela')
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
