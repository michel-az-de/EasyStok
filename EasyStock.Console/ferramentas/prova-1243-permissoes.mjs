/* eslint-disable no-console */
// Prova da F13, item 5 (issue #1243): expediente, configuração e avisos do cliente são só de Admin
// na API. Para o operador comum, carregar não vira alarme em toda abertura; agir diz que é coisa
// do administrador, em vez do genérico "sem permissão".
//
//   node ferramentas/prova-1243-permissoes.mjs

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
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    return proximo(url, contexto)
  },
})

const acao = await import('../src/aplicacao/acoes.js')
const { criarAcoesExpedienteApi } = await import('../src/aplicacao/api/expediente.js')
const { criarAcoesConsentimentosApi } = await import('../src/aplicacao/api/consentimentos.js')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${erro.message.split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}
const esperar = () => new Promise((r) => setTimeout(r, 20))
globalThis.fetch = async () => new Response('', { status: 403 })

const expediente = () => {
  const despachados = []
  const estadoRef = { current: { funcionamento: {}, lojaAberta: null } }
  return { acoes: criarAcoesExpedienteApi({ despachar: (a) => despachados.push(a), estadoRef }), despachados }
}

await confere('operador: carregar o expediente com 403 não acende aviso', async () => {
  const { acoes, despachados } = expediente()
  acoes.recarregarExpediente()
  await esperar()
  assert.ok(!despachados.some((a) => a.tipo === acao.AVISO_API), `avisou: ${despachados.map((a) => a.mensagem).join(' | ')}`)
})

await confere('operador: abrir ou fechar a loja com 403 diz que é do administrador', async () => {
  const { acoes, despachados } = expediente()
  acoes.alternarLoja(Date.parse('2026-10-01T15:00:00Z'))
  await esperar()
  const aviso = despachados.find((a) => a.tipo === acao.AVISO_API)
  assert.ok(aviso, 'não avisou')
  assert.match(aviso.mensagem, /administrador/)
  // A tela trocou a loja antes da resposta; sem permissão, volta (a releitura também dá 403).
  assert.equal(despachados.filter((a) => a.tipo === acao.ALTERNAR_LOJA).length, 2, 'a troca local não foi desfeita')
})

await confere('operador: avisos do cliente com 403 explicam que é do administrador', async () => {
  const { carregarAvisos } = criarAcoesConsentimentosApi()
  await assert.rejects(() => carregarAvisos('cliente-1'), /administrador/)
})

console.log(`\n${passou} verificações passaram, ${falhas.length} falharam.`)
if (falhas.length > 0) process.exit(1)
