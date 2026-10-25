/* eslint-disable no-console */
// #2021: salvar endereço/cadastro invalida a prévia de frete da mesma comanda.
// Executa o hook real e a API real com HTTP controlado, sem criar pedidos.
// node --test ferramentas/prova-2021-frete.mjs
import { registerHooks } from 'node:module'
import { setImmediate } from 'node:timers/promises'
import { test, mock } from 'node:test'
import assert from 'node:assert/strict'

let atual = null
const iguais = (a, b) => a && b && a.length === b.length && a.every((valor, i) => Object.is(valor, b[i]))
globalThis.__hooksSimulacao2021 = {
  useState(inicial) {
    const host = atual, i = host.indice++
    if (!(i in host.valores)) host.valores[i] = inicial
    return [host.valores[i], (valor) => { host.valores[i] = typeof valor === 'function' ? valor(host.valores[i]) : valor }]
  },
  useMemo(calcular, deps) {
    const host = atual, i = host.indice++
    if (!host.valores[i] || !iguais(host.valores[i].deps, deps)) host.valores[i] = { deps, valor: calcular() }
    return host.valores[i].valor
  },
  useEffect(efeito, deps) {
    const host = atual, i = host.indice++
    if (!host.efeitos[i] || !iguais(host.efeitos[i].deps, deps)) host.pendentes.push(() => {
      host.efeitos[i]?.limpar?.()
      host.efeitos[i] = { deps, limpar: efeito() }
    })
  },
}
registerHooks({
  resolve(nome, contexto, proximo) {
    if (nome === 'react') return { url: 'prova:react', shortCircuit: true }
    if (nome.startsWith('.') && !/\.[a-z]+$/i.test(nome)) {
      try { return proximo(nome + '.js', contexto) } catch { /* resolução original */ }
    }
    return proximo(nome, contexto)
  },
  load(url, contexto, proximo) {
    if (url === 'prova:react') return { format: 'module', shortCircuit: true, source: 'export const { useState, useMemo, useEffect } = globalThis.__hooksSimulacao2021' }
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    return proximo(url, contexto)
  },
})
const { useSimulacaoDoPedido } = await import('../src/aplicacao/useSimulacaoDoPedido.js')
const pedido = { itens: [{ sku: 'massa', qtd: 1, obs: '' }], janela: 'j1|2026-10-24' }
const dados = { conversaId: 'c1', clienteId: 'cliente-1', endereco: 'Rua A, 10, Centro, 01000-000', pedido, ativo: true }
const resposta = (frete) => new Response(JSON.stringify({ data: { frete, total: 38 + frete } }))

function montar(t, entrada = dados) {
  mock.timers.enable({ apis: ['setTimeout'] })
  globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }
  const host = { indice: 0, valores: [], efeitos: [], pendentes: [], entrada }
  host.renderizar = (proximo = host.entrada) => {
    host.entrada = proximo
    host.indice = 0
    atual = host
    const valor = useSimulacaoDoPedido(host.entrada)
    atual = null
    while (host.pendentes.length) host.pendentes.shift()()
    return valor
  }
  host.esperar = async () => {
    mock.timers.tick(350)
    await setImmediate()
    return host.renderizar()
  }
  t.after(() => { for (const efeito of host.efeitos) efeito?.limpar?.(); mock.timers.reset() })
  return host
}

test('endereço salvo retira o frete antigo imediatamente e consulta o novo sem mudar a comanda', async (t) => {
  const host = montar(t), chamadas = []
  let frete = 12
  globalThis.fetch = async (url, opcoes) => {
    chamadas.push({ url: String(url), corpo: JSON.parse(opcoes.body) })
    return resposta(frete)
  }
  assert.equal(host.renderizar(), null)
  assert.deepEqual(await host.esperar(), { frete: 12, total: 50 })
  frete = 20
  assert.equal(host.renderizar({ ...dados, endereco: 'Rua B, 20, Bairro B, 02000-000' }), null, 'o total do endereço anterior deve sair antes da nova resposta')
  assert.deepEqual(await host.esperar(), { frete: 20, total: 58 })
  assert.equal(chamadas.length, 2)
  assert.deepEqual(chamadas[1].corpo, chamadas[0].corpo, 'endereço é lido no cadastro pelo servidor, sem mudar o contrato HTTP')
  await host.esperar()
  assert.equal(chamadas.length, 2, 'render sem alteração não repete a simulação')
})

test('cadastro vinculado permite repetir a simulação que falhou para o lead', async (t) => {
  const host = montar(t, { ...dados, clienteId: null }), chamadas = []
  globalThis.fetch = async () => {
    chamadas.push(host.entrada.clienteId)
    return host.entrada.clienteId ? resposta(12) : new Response(JSON.stringify({ message: 'Cadastre o cliente' }), { status: 400 })
  }
  host.renderizar()
  assert.equal(await host.esperar(), null)
  host.renderizar(dados)
  assert.deepEqual(await host.esperar(), { frete: 12, total: 50 })
  assert.deepEqual(chamadas, [null, 'cliente-1'])
})

test('resposta atrasada do endereço antigo não substitui o cálculo novo', async (t) => {
  const host = montar(t)
  let resolverAntiga, sinalAntigo, chamadas = 0
  globalThis.fetch = async (url, opcoes) => {
    chamadas += 1
    if (chamadas === 1) {
      sinalAntigo = opcoes.signal
      return new Promise((resolve) => { resolverAntiga = resolve })
    }
    return resposta(20)
  }
  host.renderizar()
  await host.esperar()
  host.renderizar({ ...dados, endereco: 'Rua B, 20, Bairro B, 02000-000' })
  assert.equal(sinalAntigo.aborted, true, 'a consulta antiga deve ser cancelada')
  assert.deepEqual(await host.esperar(), { frete: 20, total: 58 })
  resolverAntiga(resposta(12))
  await setImmediate()
  assert.deepEqual(host.renderizar(), { frete: 20, total: 58 })
})

test('falha do novo endereço mantém a prévia vazia e pedido criado encerra a consulta', async (t) => {
  const host = montar(t)
  let chamadas = 0
  globalThis.fetch = async () => ++chamadas === 1 ? resposta(12) : new Response('{}', { status: 400 })
  host.renderizar()
  await host.esperar()
  assert.equal(host.renderizar({ ...dados, endereco: 'Endereço fora da área' }), null)
  assert.equal(await host.esperar(), null)
  host.renderizar({ ...host.entrada, pedido: { ...pedido, pedidoId: 'pedido-criado' } })
  await host.esperar()
  assert.equal(chamadas, 2)
})