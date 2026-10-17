/* eslint-disable no-console */
// Prova da issue #1651 (I2.2): frete por distância configurado pelo console no modo API.
//   1. a configuração gravada (GET, em metros e centavos) vira formulário em km e reais, e volta;
//   2. o formulário avisa o motivo antes de chamar a API (cozinha, raio, faixas fora de ordem);
//   3. a tela fala com GET/PUT `api/minha-vitrine/entrega/raio` e POST `.../raio/testar`;
//   4. o resultado do "Testar um endereço" vira texto de balcão: frete, distância e faixa.
//
//   node ferramentas/prova-1651-frete-raio.mjs

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

const chamadas = []
globalThis.fetch = async (url, opcoes = {}) => {
  chamadas.push({ url, metodo: opcoes.method ?? 'GET', corpo: opcoes.body ? JSON.parse(opcoes.body) : undefined })
  return new Response(JSON.stringify({ data: { modo: 'raio' } }), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const dominio = await import('../src/dominio/entregasApi.js')
const infra = await import('../src/infra/api/entregasApi.js')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

const GRAVADO = {
  configurado: true, raioMaxMetros: 5000, faixaGratisMetros: 500, fatorRota: 1.4,
  faixas: [{ id: 'ate-2000m', ateMetros: 2000, valorCentavos: 1500 }, { id: 'ate-5000m', ateMetros: 5000, valorCentavos: 2550 }],
}
const FORM = {
  cep: '05500-000', numero: ' 100 ', raioKm: '5', gratisKm: '0,5', fatorRota: '1,4',
  faixas: [{ ateKm: '2', valor: '15,00' }, { ateKm: '5', valor: '25,50' }],
}

// --- 1. Configuração gravada <-> formulário -------------------------------------------------
await confere('configuração gravada vira formulário em km e reais, sem endereço', () => {
  assert.deepEqual(dominio.formularioDoFreteRaio(GRAVADO), {
    cep: '', numero: '', raioKm: '5', gratisKm: '0,5', fatorRota: '1,4',
    faixas: [{ ateKm: '2', valor: '15,00' }, { ateKm: '5', valor: '25,50' }],
  })
})
await confere('sem configuração, o formulário abre com uma faixa em branco e fator 1,4', () => {
  for (const vazio of [null, { configurado: false, faixas: [] }]) {
    const f = dominio.formularioDoFreteRaio(vazio)
    assert.equal(f.fatorRota, '1,4')
    assert.equal(f.gratisKm, '0')
    assert.deepEqual(f.faixas, [{ ateKm: '', valor: '' }])
  }
})
await confere('formulário vira o corpo da API em metros e centavos', () => {
  assert.deepEqual(dominio.corpoFreteRaio(FORM), {
    cep: '05500000', numero: '100', raioMaxMetros: 5000, faixaGratisMetros: 500, fatorRota: 1.4,
    faixas: [{ ateMetros: 2000, valorCentavos: 1500 }, { ateMetros: 5000, valorCentavos: 2550 }],
  })
})
await confere('sem endereço, o corpo manda nulo para a API manter a cozinha', () => {
  const corpo = dominio.corpoFreteRaio({ ...FORM, cep: '', numero: '' })
  assert.equal(corpo.cep, null)
  assert.equal(corpo.numero, null)
})
await confere('centavos não sofrem com ponto flutuante', () => {
  const corpo = dominio.corpoFreteRaio({ ...FORM, faixas: [{ ateKm: '4,35', valor: '19,99' }, { ateKm: '5', valor: '0,07' }] })
  assert.deepEqual(corpo.faixas, [{ ateMetros: 4350, valorCentavos: 1999 }, { ateMetros: 5000, valorCentavos: 7 }])
})

// --- 2. Validação antes da API ---------------------------------------------------------------
await confere('formulário válido passa; cozinha já gravada dispensa o endereço', () => {
  assert.equal(dominio.erroDoFormularioFreteRaio(FORM, false), null)
  assert.equal(dominio.erroDoFormularioFreteRaio({ ...FORM, cep: '', numero: '' }, true), null)
})
await confere('primeira gravação exige a cozinha; endereço pela metade nunca passa', () => {
  assert.match(dominio.erroDoFormularioFreteRaio({ ...FORM, cep: '', numero: '' }, false), /cozinha/i)
  assert.match(dominio.erroDoFormularioFreteRaio({ ...FORM, cep: '0550' }, true), /CEP/)
  assert.match(dominio.erroDoFormularioFreteRaio({ ...FORM, numero: '' }, true), /número/i)
})
await confere('raio, grátis e fator fora do aceito dizem o motivo', () => {
  assert.match(dominio.erroDoFormularioFreteRaio({ ...FORM, raioKm: '0' }, false), /raio/i)
  assert.match(dominio.erroDoFormularioFreteRaio({ ...FORM, gratisKm: '6' }, false), /grátis/i)
  assert.match(dominio.erroDoFormularioFreteRaio({ ...FORM, fatorRota: '0,8' }, false), /fator/i)
})
await confere('faixa fora de ordem, repetida, sem valor ou que não alcança o raio é recusada', () => {
  const com = (faixas) => dominio.erroDoFormularioFreteRaio({ ...FORM, faixas }, false)
  assert.match(com([{ ateKm: '5', valor: '25' }, { ateKm: '2', valor: '15' }]), /ordem/i)
  assert.match(com([{ ateKm: '5', valor: '25' }, { ateKm: '5', valor: '30' }]), /ordem/i)
  assert.match(com([{ ateKm: '0,4', valor: '5' }, { ateKm: '5', valor: '25' }]), /grátis/i)
  assert.match(com([{ ateKm: '2', valor: '' }, { ateKm: '5', valor: '25' }]), /valor/i)
  assert.match(com([{ ateKm: '2', valor: '15' }, { ateKm: '4', valor: '25' }]), /alcan/i)
})
await confere('tudo grátis até o raio dispensa faixas', () => {
  assert.equal(dominio.erroDoFormularioFreteRaio({ ...FORM, gratisKm: '5', faixas: [{ ateKm: '', valor: '' }] }, false), null)
  assert.deepEqual(dominio.corpoFreteRaio({ ...FORM, gratisKm: '5', faixas: [{ ateKm: '', valor: '' }] }).faixas, [])
})

// --- 3. Rotas da API -------------------------------------------------------------------------
await confere('a tela lê, grava e testa nas rotas do frete por raio', async () => {
  chamadas.length = 0
  await infra.obterFreteRaio()
  await infra.salvarFreteRaio({ raioMaxMetros: 5000 })
  await infra.testarFreteRaio({ cep: '05500000', numero: '100' })
  assert.deepEqual(chamadas.map((c) => `${c.metodo} ${c.url}`), [
    'GET /api/minha-vitrine/entrega/raio',
    'PUT /api/minha-vitrine/entrega/raio',
    'POST /api/minha-vitrine/entrega/raio/testar',
  ])
  assert.deepEqual(chamadas[1].corpo, { raioMaxMetros: 5000 })
  assert.deepEqual(chamadas[2].corpo, { cep: '05500000', numero: '100' })
})

// --- 4. Resultado do teste em texto -----------------------------------------------------------
const RAIO = { modo: 'raio', distanciaMetros: 2780, distanciaRotaMetros: 3892, distanciaEstimada: true, faixaId: 'ate-5000m', valorCentavos: 2550, gratis: false, foraDeCobertura: false, motivo: null }
await confere('dentro do raio: frete, distância de rota e aviso de estimativa', () => {
  const r = dominio.resumoDoTesteDeFrete(RAIO)
  assert.match(r.titulo, /25,50/)
  assert.match(r.detalhe, /3,9 km/)
  assert.match(r.detalhe, /estimad/i)
  assert.equal(r.tom, 'ok')
})
await confere('rota medida não leva o aviso de estimativa', () => {
  assert.doesNotMatch(dominio.resumoDoTesteDeFrete({ ...RAIO, distanciaEstimada: false }).detalhe, /estimad/i)
})
await confere('grátis e fora do raio dizem isso no título', () => {
  assert.match(dominio.resumoDoTesteDeFrete({ ...RAIO, gratis: true, valorCentavos: 0, faixaId: 'gratis' }).titulo, /grátis/i)
  const fora = dominio.resumoDoTesteDeFrete({ ...RAIO, foraDeCobertura: true, valorCentavos: null, faixaId: null, distanciaRotaMetros: 7200 })
  assert.match(fora.titulo, /fora/i)
  assert.match(fora.detalhe, /7,2 km/)
  assert.equal(fora.tom, 'atencao')
})
await confere('cotação da Lalamove diz de onde veio o valor (#1882)', () => {
  const r = dominio.resumoDoTesteDeFrete({ ...RAIO, faixaId: 'lalamove', valorCentavos: 1800 })
  assert.match(r.titulo, /18,00/)
  assert.match(r.detalhe, /Lalamove/)
  assert.doesNotMatch(dominio.resumoDoTesteDeFrete(RAIO).detalhe, /Lalamove/)
})
await confere('quando a tabela de zonas decide, a tela repete o motivo da API', () => {
  const r = dominio.resumoDoTesteDeFrete({ modo: 'zona', motivo: 'Não localizamos este endereço com precisão.' })
  assert.match(r.titulo, /zonas/i)
  assert.equal(r.detalhe, 'Não localizamos este endereço com precisão.')
  assert.equal(r.tom, 'atencao')
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
process.exit(falhas.length ? 1 : 0)
