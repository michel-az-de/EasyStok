/* eslint-disable no-console */
// Prova da #2600: Q02 da auditoria QA de 13/11. A aba Integrações dizia "Ligado" para a credencial da Meta
// mesmo quando quem envia é a conexão por QR (status.provider = wppconnect), e o número de teste da Meta
// ("Test Number") aparecia como teste ok. A situação do cartão WhatsApp tem de seguir o status do canal.
//
//   node ferramentas/prova-2600-canal-que-envia.mjs

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

const dominio = await import('../src/dominio/chavesIntegracao.js')

let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok  ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA  ${nome}\n      ${String(erro.message).split('\n')[0]}`) }
}

const whatsapp = {
  provider: 'whatsapp', nome: 'WhatsApp', ativo: true, temCredencial: true, origem: 'global',
  ultimoTesteEm: '2026-11-13T15:00:00Z', ultimoTesteOk: true,
  ultimoTesteMensagem: 'Número +1 555-189-8702 (Test Number) conferido, qualidade alta.',
}
const real = { ...whatsapp, ultimoTesteMensagem: 'Número +55 11 99999-0000 (Casa da Baba) conferido, qualidade alta.' }

caso('existe situacaoDoCartao', () => assert.equal(typeof dominio.situacaoDoCartao, 'function'))

caso('QR enviando: a chave da Meta não aparece como Ligado e aponta para Canais', () => {
  const s = dominio.situacaoDoCartao(real, { provider: 'wppconnect', sessaoWeb: { conectado: true } })
  assert.notEqual(s.pilula.texto, 'Ligado')
  assert.equal(s.pilula.tom, 'neutro')
  assert.ok(s.avisos.some((a) => /QR/.test(a.texto) && /Canais/.test(a.texto)), JSON.stringify(s.avisos))
})

caso('número de teste da Meta vira alerta', () => {
  const s = dominio.situacaoDoCartao(whatsapp, { provider: 'meta', phoneNumberId: '1' })
  assert.ok(s.avisos.some((a) => a.tom === 'perigo' && /número de teste da Meta/i.test(a.texto)), JSON.stringify(s.avisos))
  assert.notEqual(s.pilula.tom, 'ok')
})

caso('Meta enviando com número real: nada muda', () => {
  const s = dominio.situacaoDoCartao(real, { provider: 'meta', phoneNumberId: '1' })
  assert.deepEqual(s, { pilula: { tom: 'ok', texto: 'Ligado' }, avisos: [] })
})

caso('status ainda não chegou: o cartão fica como a API diz', () => {
  assert.deepEqual(dominio.situacaoDoCartao(real, undefined), { pilula: { tom: 'ok', texto: 'Ligado' }, avisos: [] })
})

caso('outra integração não depende do status do WhatsApp', () => {
  const mp = { provider: 'mercadopago', ativo: false }
  assert.deepEqual(dominio.situacaoDoCartao(mp, { provider: 'wppconnect' }), { pilula: { tom: 'neutro', texto: 'Desligado' }, avisos: [] })
})

console.log(`\n${ok}/${ok + falhas.length} casos ok`)
if (falhas.length) process.exit(1)
