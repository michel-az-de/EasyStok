/* eslint-disable no-console */
// Prova da F16 (issue #1246) no console: no modo API a aba Integrações fala com o EasyStok.
// Salvar manda os campos do catálogo sem nunca guardar o segredo; Testar e Desativar chamam a
// API; integração parada acende a faixa vermelha do topo.
//
//   node ferramentas/prova-1246-integracoes.mjs

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

const dominio = await import('../src/dominio/chavesIntegracao.js').catch(() => ({}))
const api = await import('../src/infra/api/integracoesApi.js').catch(() => ({}))

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

const item = (extra) => ({
  provider: 'mercadopago', nome: 'Mercado Pago', ativo: true, temCredencial: true, origem: 'loja', mascara: '9876',
  ultimoTesteEm: '2026-10-01T13:05:00Z', ultimoTesteOk: true, ultimoTesteMensagem: 'Conta conferida.', alerta: null,
  lojaGrava: true, campos: ['accessToken'], escolheAmbiente: false, ...extra,
})

function rede(resposta = { data: null }) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET', body } = {}) => {
    chamadas.push({ metodo: method, url, corpo: body ? JSON.parse(body) : null })
    return new Response(JSON.stringify(resposta), { status: 200 })
  }
  return chamadas
}

await confere('faixa: nenhuma parada, sem texto', () => {
  assert.equal(dominio.textoDaFaixa([item(), item({ provider: 'googlemaps', nome: 'Google Maps' })]), null)
})

await confere('faixa: uma parada diz qual', () => {
  assert.equal(dominio.textoDaFaixa([item({ alerta: 'parada' })]), 'Integração parada: Mercado Pago')
})

await confere('faixa: duas paradas no plural; "vencendo" não acende a faixa', () => {
  const lista = [item({ alerta: 'parada' }), item({ provider: 'lalamove', nome: 'Lalamove', alerta: 'parada' }),
    item({ provider: 'googlemaps', nome: 'Google Maps', alerta: 'vencendo' })]
  assert.equal(dominio.textoDaFaixa(lista), 'Integrações paradas: Mercado Pago e Lalamove')
})

await confere('rótulo de campo em português; desconhecido aparece como veio', () => {
  assert.equal(dominio.rotuloDoCampo('accessToken'), 'Access token')
  assert.equal(dominio.rotuloDoCampo('apiSecret'), 'Segredo da API')
  assert.equal(dominio.rotuloDoCampo('outro'), 'outro')
})

await confere('último teste: falha mostra a mensagem do provedor', () => {
  assert.match(dominio.textoDoUltimoTeste(item({ ultimoTesteOk: false, ultimoTesteMensagem: 'Token recusado.' })), /Falhou.*Token recusado\./)
  assert.match(dominio.textoDoUltimoTeste(item()), /^Ok/)
  assert.equal(dominio.textoDoUltimoTeste(item({ ultimoTesteEm: null })), 'Ainda não testado')
})

await confere('salvar manda os campos e o ambiente no PUT, sem nada a mais', async () => {
  const chamadas = rede()
  await api.salvarChave('lalamove', { apiKey: 'pk_teste', apiSecret: 'sk_teste' }, 'sandbox')
  assert.equal(chamadas.length, 1)
  assert.equal(chamadas[0].metodo, 'PUT')
  assert.match(chamadas[0].url, /\/api\/integracoes\/lalamove$/)
  assert.deepEqual(chamadas[0].corpo, { campos: { apiKey: 'pk_teste', apiSecret: 'sk_teste' }, ambiente: 'sandbox' })
})

await confere('testar e desativar chamam a API do provider', async () => {
  const chamadas = rede({ data: { provider: 'mercadopago', ok: true, mensagem: 'Conta conferida.' } })
  const r = await api.testar('mercadopago')
  await api.desativar('mercadopago')
  assert.equal(r.ok, true)
  assert.deepEqual(chamadas.map((c) => `${c.metodo} ${c.url.replace(/^.*\/api/, '/api')}`),
    ['POST /api/integracoes/mercadopago/testar', 'POST /api/integracoes/mercadopago/desativar'])
})

await confere('listar devolve a lista da API', async () => {
  rede({ data: [item()] })
  const lista = await api.listar()
  assert.equal(lista[0].mascara, '9876')
})

console.log(`\n${passou} verificações passaram, ${falhas.length} falharam.`)
if (falhas.length > 0) process.exit(1)
