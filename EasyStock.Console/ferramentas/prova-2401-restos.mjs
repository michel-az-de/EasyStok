/* eslint-disable no-console */
// Prova da #2401: restos da revisão geral de 12/11 no console.
//
// C3.5. "Nova viagem" sai com Idempotency-Key: repetir depois de uma resposta perdida não cria outra viagem.
// C3.8. A espera de reconexão do SSE só volta à base depois de uma conexão estável. Um servidor que manda o
//       `ready` e cai em seguida (deploy, 503) não faz todas as abas baterem a cada 10 s.
//
//   node ferramentas/prova-2401-restos.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* resolução padrão */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    return proximo(url, contexto)
  },
})

globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok    ' + descricao) } catch (erro) {
    falhas.push(descricao)
    const motivo = String(erro.message).split('\n').filter(Boolean).slice(0, 4).join(' | ')
    console.log('FALHA ' + descricao + '\n      ' + motivo)
  }
}
const fonte = (caminho) => readFileSync(new URL(caminho, import.meta.url), 'utf8')

const { criarViagem } = await import('../src/infra/api/entregasApi.js')
const { criarEsperaDeReconexao } = await import('../src/aplicacao/recargaAoVivo.js')

await confere('C3.5 criarViagem manda a chave no Idempotency-Key', async () => {
  let cabecalhos
  globalThis.fetch = async (_url, o) => { cabecalhos = o.headers; return new Response(JSON.stringify({ data: {} })) }
  await criarViagem('entregador-1', 'chave-1')
  assert.equal(cabecalhos['Idempotency-Key'], 'chave-1')
})

await confere('C3.5 o botão "Nova viagem" das Entregas passa uma chave por corpo', () => {
  const texto = fonte('../src/aplicacao/useEntregasApi.js')
  assert.match(texto, /criarViagem: \(entregadorId\) => \{[^}]*envioDaViagem\.para\(/s)
  assert.match(texto, /api\.criarViagem\(entregadorId, chave\)/)
})

await confere('C3.8 ready seguido de queda logo depois mantém a espera crescente', () => {
  let agora = 0
  const espera = criarEsperaDeReconexao({ base: 10000, aleatorio: () => 0, agora: () => agora })
  assert.equal(espera.proxima(), 10000)
  espera.zerar() // ready
  agora += 1000 // caiu 1 s depois
  assert.equal(espera.proxima(), 20000, 'conexão de 1 s não é conexão boa: a espera não volta à base')
  espera.zerar()
  agora += 1000
  assert.equal(espera.proxima(), 40000)
})

await confere('C3.8 conexão estável por 30 s volta a espera à base', () => {
  let agora = 0
  const espera = criarEsperaDeReconexao({ base: 10000, aleatorio: () => 0, agora: () => agora })
  espera.proxima(); espera.proxima(); espera.proxima()
  espera.zerar()
  agora += 30000
  assert.equal(espera.proxima(), 10000)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
