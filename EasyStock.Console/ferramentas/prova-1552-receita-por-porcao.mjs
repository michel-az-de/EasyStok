/* eslint-disable no-console */
// Prova da #1552 (M1.4e): receita e embalagem por tamanho de porção no console (modo API).
//   1. a embalagem é da porção: o detalhe traz, o editor manda só quando o EasyStok conhece o campo
//      ou quando ela escolheu, e tirar a escolha manda vazio;
//   2. a lista de embalagens da porção é o insumo marcado como embalagem e contado por unidade; quem
//      não lê Insumos fica sem a escolha, sem quebrar a ficha;
//   3. a receita diz em que rende (porções, kg ou g) e o que salva é a unidade escolhida;
//   4. o editor da receita orienta: prato com mais de um tamanho pede rendimento em peso, e a
//      embalagem da receita não desce para porção com embalagem própria;
//   5. na produção, escolher a porção traz o peso dela sem apagar o que foi digitado.
//
//   node ferramentas/prova-1552-receita-por-porcao.mjs

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

const {
  corpoDasPorcoes, gramasDaPorcao, linhaComPorcao, linhaComPrato, pesoAoEscolherPorcao, pesoDeReferenciaDoPrato, porcaoNova, porcoesMudaram, rascunhoDasPorcoes,
} = await import('../src/dominio/porcoes.js')
const { motivosDosPendentes, planejar } = await import('../src/aplicacao/planejamento.js')
const { detalheDaApi } = await import('../src/infra/api/cardapioApi.js')
const { insumoDaApi, receitaDaApi } = await import('../src/infra/api/producaoApi.js')
const { lerEmbalagensDaPorcao } = await import('../src/aplicacao/edicaoCardapio.js')
const { ondeOInsumoEUsado, rotuloDaEmbalagem } = await import('../src/aplicacao/insumos.js')
const { criarGestaoReceitas, orientacoesDaReceita, rotuloDoRendimento, UNIDADES_RENDIMENTO } = await import('../src/aplicacao/receitas.js')

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

function montarFetch(responder = () => ({ data: {} })) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET', body } = {}) => {
    const chamada = { metodo: method, url, corpo: body ? JSON.parse(body) : null }
    chamadas.push(chamada)
    const { status = 200, data = null, error } = responder(chamada) ?? {}
    return new Response(JSON.stringify(error ? { error } : { data }), { status })
  }
  return chamadas
}

const porcaoDaApi = (extra = {}) => ({ id: 'v-300', rotulo: '300 g', preco: 28, peso: '300 g', disponivel: true, padrao: true, ...extra })

// --- 1. A embalagem é da porção -------------------------------------------------------------
await confere('o detalhe traz a embalagem da porção; API anterior não manda o campo e a chave não nasce', () => {
  const novo = detalheDaApi({ porcoes: [porcaoDaApi({ embalagemId: 'b-300' }), porcaoDaApi({ id: 'v-800', rotulo: '800 g', embalagemId: null })] })
  assert.deepEqual(novo.porcoes.map((p) => p.embalagemId), ['b-300', null])
  const antigo = detalheDaApi({ porcoes: [porcaoDaApi()] })
  assert.equal('embalagemId' in antigo.porcoes[0], false)
  assert.equal('embalagemId' in rascunhoDasPorcoes(antigo.porcoes)[0], false)
})

await confere('o corpo só leva a embalagem quando a linha a conhece, e leva junto o "mexer"', () => {
  const [daApi] = corpoDasPorcoes(rascunhoDasPorcoes(detalheDaApi({ porcoes: [porcaoDaApi({ embalagemId: 'b-300' })] }).porcoes))
  assert.deepEqual([daApi.embalagemId, daApi.mexerEmbalagem], ['b-300', true])

  const [semChave] = corpoDasPorcoes(rascunhoDasPorcoes([porcaoDaApi()]))
  assert.equal('embalagemId' in semChave, false, 'sem a chave, o EasyStok não mexe na embalagem')
  assert.equal('mexerEmbalagem' in semChave, false)

  const [nova] = corpoDasPorcoes([{ ...porcaoNova(), rotulo: '1,2 kg', preco: '90,00', embalagemId: 'b-1200' }])
  assert.deepEqual([nova.id, nova.embalagemId, nova.mexerEmbalagem], [null, 'b-1200', true], 'porção nova já nasce com a embalagem escolhida')
})

await confere('tirar a escolha manda vazio, e trocar a embalagem conta como mudança', () => {
  const iniciais = detalheDaApi({ porcoes: [porcaoDaApi({ embalagemId: 'b-300' })] }).porcoes
  const linhas = rascunhoDasPorcoes(iniciais)
  assert.equal(porcoesMudaram(iniciais, linhas), false)

  const tirada = linhas.map((l) => ({ ...l, embalagemId: null }))
  assert.equal(porcoesMudaram(iniciais, tirada), true)
  assert.deepEqual([corpoDasPorcoes(tirada)[0].embalagemId, corpoDasPorcoes(tirada)[0].mexerEmbalagem], [null, true])
  assert.equal(porcoesMudaram(iniciais, linhas.map((l) => ({ ...l, embalagemId: 'b-outra' }))), true)
})

// --- 2. As embalagens que a porção pode usar ---------------------------------------------------
await confere('a lista é o insumo marcado como embalagem e contado por unidade', async () => {
  montarFetch(() => ({ data: [
    { produtoId: 'b-300', nome: 'Bandeja 300 g', unidade: 'Un', saldo: 40, receitas: 0, embalagem: true },
    { produtoId: 'filme', nome: 'Filme', unidade: 'Kg', saldo: 2, receitas: 0, embalagem: true },
    { produtoId: 'molho', nome: 'Molho', unidade: 'G', saldo: 5000, receitas: 1, embalagem: false },
  ] }))
  assert.deepEqual(await lerEmbalagensDaPorcao(), [{ id: 'b-300', nome: 'Bandeja 300 g' }])
})

await confere('quem não lê Insumos fica sem a escolha, e a ficha abre do mesmo jeito', async () => {
  montarFetch(() => ({ status: 403, error: { message: 'Sem permissão.' } }))
  assert.deepEqual(await lerEmbalagensDaPorcao(), [])
})

await confere('a aba Insumos diz onde a embalagem da porção é usada, em vez de "não entra em receita"', () => {
  const daApi = (extra) => insumoDaApi({ produtoId: 'b', nome: 'Bandeja', unidade: 'Un', saldo: 40, receitas: 0, embalagem: true, ...extra })
  assert.equal(daApi({}).porcoes, 0, 'API anterior não manda o campo')
  assert.equal(ondeOInsumoEUsado(daApi({ porcoes: 2 })), 'embalagem de 2 porções')
  assert.equal(ondeOInsumoEUsado(daApi({ porcoes: 1, receitas: 3 })), 'usado em 3 receitas · embalagem de 1 porção')
  assert.equal(ondeOInsumoEUsado(daApi({ receitas: 1 })), 'usado em 1 receita')
  assert.equal(ondeOInsumoEUsado(daApi({})), 'não entra em receita')
  assert.match(rotuloDaEmbalagem(daApi({ porcoes: 2 })), /uma por porção produzida/)
  assert.equal(rotuloDaEmbalagem(daApi({ porcoes: 1, receitas: 1 })), 'Embalagem usada em toda produção')
  assert.equal(rotuloDaEmbalagem(daApi({})), 'Embalagem usada em toda produção', 'sem uso em porção, o rótulo de antes')
})

// --- 3. Em que a receita rende ------------------------------------------------------------------
await confere('a receita rende em porções, kg ou g, e a lista diz o prato com seus tamanhos', () => {
  assert.deepEqual(UNIDADES_RENDIMENTO.map((u) => u.valor), ['Un', 'Kg', 'G'])
  assert.deepEqual(['Un', 'Kg', 'L'].map(rotuloDoRendimento), ['porções', 'kg', 'L'], 'unidade fora da lista aparece como está')
  const r = receitaDaApi({ cardapioItemId: 'i-1', produtoId: 'p-1', nome: 'Lasanha', rendimentoBase: 3, rendimentoUnidade: 'Kg', linhas: 2, porcoes: 2, porcoesComEmbalagem: 1 })
  assert.deepEqual([r.porcoes, r.porcoesComEmbalagem], [2, 1])
  const antiga = receitaDaApi({ cardapioItemId: 'i-1', produtoId: 'p-1', nome: 'Lasanha', rendimentoBase: 6, rendimentoUnidade: 'Un', linhas: 2 })
  assert.deepEqual([antiga.porcoes, antiga.porcoesComEmbalagem], [0, 0], 'API anterior: sem orientação, sem quebra')
})

await confere('salvar manda a unidade do rendimento que ela escolheu', async () => {
  const chamadas = montarFetch()
  const erros = []
  const gestao = criarGestaoReceitas({ recarregar: async () => {}, aoErro: (m) => erros.push(m) })
  const ok = await gestao.salvar('p-1', { rendimento: '3', unidadeRendimento: 'Kg', unidadeMedidaBase: 'Un', linhas: [{ insumoId: 'molho', quantidade: '1,2', unidade: 'Kg' }] })
  assert.equal(ok, true, erros.join(' '))
  const corpo = chamadas.find((c) => c.metodo === 'PUT').corpo
  assert.deepEqual([corpo.rendimentoBase, corpo.rendimentoUnidade], [3, 'Kg'])
  assert.deepEqual(corpo.linhas.map((l) => [l.insumoId, l.quantidade, l.unidade]), [['molho', 1.2, 'Kg']])
})

// --- 4. O que o editor da receita orienta ---------------------------------------------------------
const insumos = [{ id: 'molho', nome: 'Molho', embalagem: false }, { id: 'bandeja', nome: 'Bandeja', embalagem: true }]
const comBandeja = [{ insumoId: 'molho' }, { insumoId: 'bandeja' }]
const orientar = (prato, unidadeRendimento, linhas = [{ insumoId: 'molho' }]) => orientacoesDaReceita({ prato, unidadeRendimento, linhas, insumos })

await confere('prato com mais de um tamanho e rendimento em porções: orienta o rendimento em peso', () => {
  const [texto, ...resto] = orientar({ porcoes: 2, porcoesComEmbalagem: 0 }, 'Un')
  assert.match(texto, /tamanhos diferentes/)
  assert.match(texto, /rende em peso/)
  assert.equal(resto.length, 0)
  assert.deepEqual(orientar({ porcoes: 2, porcoesComEmbalagem: 0 }, 'Kg'), [], 'em peso, a comida já acompanha o tamanho')
  assert.deepEqual(orientar({ porcoes: 1, porcoesComEmbalagem: 0 }, 'Un'), [], 'um tamanho só: rendimento em porções está certo')
  assert.deepEqual(orientar({ porcoes: 0, porcoesComEmbalagem: 0 }, 'Un'), [], 'prato sem porções')
})

await confere('embalagem na receita: diz quando ela não desce e onde escolher a de cada porção', () => {
  assert.match(orientar({ porcoes: 2, porcoesComEmbalagem: 2 }, 'Kg', comBandeja)[0], /não desce na produção delas/)
  assert.match(orientar({ porcoes: 2, porcoesComEmbalagem: 1 }, 'Kg', comBandeja)[0], /1 de 2 porções/)
  assert.match(orientar({ porcoes: 2, porcoesComEmbalagem: 0 }, 'Kg', comBandeja)[0], /desce pelo peso/)
  assert.deepEqual(orientar({ porcoes: 2, porcoesComEmbalagem: 0 }, 'Un', comBandeja).length, 1, 'em porções, uma bandeja por porção está certa: só a orientação do peso')
  assert.deepEqual(orientar({ porcoes: 0, porcoesComEmbalagem: 0 }, 'Un', comBandeja), [], 'prato de um tamanho com embalagem na receita segue como sempre')
})

// --- 5. O peso da porção na produção ---------------------------------------------------------------
await confere('o peso da porção sai do peso para mostrar ou do nome', () => {
  const casos = [
    [{ peso: '300 g', rotulo: 'Individual' }, 300], [{ peso: '', rotulo: '800g' }, 800], [{ rotulo: '1,2 kg' }, 1200],
    [{ rotulo: '1,1 kg' }, 1100], [{ rotulo: '1.200 g' }, 1200], [{ peso: '0,5 kg' }, 500], [{ peso: '800 g · serve 2 pessoas' }, 800],
    [{ rotulo: '500 gramas' }, 500], [{ rotulo: 'Individual (300 g)' }, 300], [{ rotulo: 'Família' }, null], [{ rotulo: 'Grande' }, null], [null, null],
  ]
  for (const [porcao, esperado] of casos) assert.equal(gramasDaPorcao(porcao), esperado, JSON.stringify(porcao))
})

await confere('texto ambíguo não vira peso: campo vazio é melhor que peso errado na etiqueta e na baixa', () => {
  // Antes: "1/2 kg" virava 2 kg, "2x300 g" virava 300, "350 a 400 g" virava 400 e "1 500 g" virava 500.
  for (const rotulo of ['1/2 kg', '2x300 g', '350 a 400 g', '1 500 g', 'serve 2 · 800 g', '1,200 g', '300,5 g', '2 g', '50 kg', '500 ml', '12 unidades']) {
    assert.equal(gramasDaPorcao({ rotulo }), null, rotulo)
  }
  assert.equal(gramasDaPorcao({ peso: '2x300 g', rotulo: '600 g' }), 600, 'o peso para mostrar não deu: vale o nome')
})

await confere('escolher a porção traz o peso dela, troca o que veio da porção anterior e respeita o digitado', () => {
  const p300 = { id: 'v-300', rotulo: '300 g' }
  const p800 = { id: 'v-800', rotulo: '800 g' }
  const semPeso = { id: 'v-f', rotulo: 'Família' }
  assert.equal(pesoAoEscolherPorcao('', null, p300), '300')
  assert.equal(pesoAoEscolherPorcao('300', p300, p800), '800', 'o peso tinha vindo da porção anterior')
  assert.equal(pesoAoEscolherPorcao('320', p300, p800), '320', 'ela pesou 320 g: o número é dela')
  assert.equal(pesoAoEscolherPorcao('', null, semPeso), '')
  assert.equal(pesoAoEscolherPorcao('300', p300, semPeso), '', 'porção sem peso no nome não herda o da anterior')
})

await confere('trocar o prato leva junto o peso que veio da porção: os 300 g da lasanha não vão para os 800 g do nhoque', () => {
  const pratos = [
    { sku: 'lasanha', porcoes: [{ id: 'l-300', rotulo: '300 g' }, { id: 'l-800', rotulo: '800 g' }] },
    { sku: 'nhoque', porcoes: [{ id: 'n-800', rotulo: '800 g' }] },
    { sku: 'bolo', porcoes: [] },
  ]
  const vazia = { id: 'x', sku: '', variacaoId: '', porcoes: '4', pesoPorPorcaoG: '', pesoRealG: '', validadeDias: '5' }
  const lasanha300 = linhaComPorcao(linhaComPrato(vazia, pratos, 'lasanha'), pratos, 'l-300')
  assert.deepEqual([lasanha300.sku, lasanha300.variacaoId, lasanha300.pesoPorPorcaoG, lasanha300.porcoes], ['lasanha', 'l-300', '300', '4'])

  const nhoque = linhaComPrato(lasanha300, pratos, 'nhoque')
  assert.deepEqual([nhoque.variacaoId, nhoque.pesoPorPorcaoG], ['', ''], 'porção zerada e o peso dela também')
  assert.equal(linhaComPorcao(nhoque, pratos, 'n-800').pesoPorPorcaoG, '800')

  const pesada = linhaComPrato({ ...lasanha300, pesoPorPorcaoG: '320' }, pratos, 'nhoque')
  assert.equal(pesada.pesoPorPorcaoG, '320', 'o peso que ela digitou não some ao trocar o prato')
  assert.equal(linhaComPrato({ ...vazia, sku: 'bolo', pesoPorPorcaoG: '500' }, pratos, 'lasanha').pesoPorPorcaoG, '500', 'prato sem porções: o peso é dela')
})

// --- 6. Planejamento com receita em peso -------------------------------------------------------------
await confere('o planejamento manda o peso da porção padrão; sem peso no cardápio, manda só as porções', async () => {
  const cardapio = [
    { sku: 'i-lasanha', porcao: '300 g', porcoes: [{ id: 'l-800', rotulo: '800 g', padrao: false }, { id: 'l-300', rotulo: '300 g', padrao: true }] },
    { sku: 'i-nhoque', porcao: '500 g · serve 1', porcoes: [] },
    { sku: 'i-bolo', porcao: 'Fatia', porcoes: [] },
  ]
  assert.deepEqual(cardapio.map(pesoDeReferenciaDoPrato), [300, 500, null])

  const chamadas = montarFetch(() => ({ data: { consolidado: [], itens: [] } }))
  await planejar([
    { sku: 'i-lasanha', produtoId: 'p-lasanha', porcoes: '10' }, { sku: 'i-nhoque', produtoId: 'p-nhoque', porcoes: '4' },
    { sku: 'i-bolo', produtoId: 'p-bolo', porcoes: '2' }, { sku: 'i-fora', produtoId: 'p-fora', porcoes: '0' },
  ], cardapio)
  assert.deepEqual(chamadas[0].corpo.pratos, [
    { produtoId: 'p-lasanha', porcoes: 10, pesoPorPorcaoG: 300 },
    { produtoId: 'p-nhoque', porcoes: 4, pesoPorPorcaoG: 500 },
    { produtoId: 'p-bolo', porcoes: 2 },
  ])
})

await confere('prato que fica de fora do planejamento aparece com o nome e o que fazer', () => {
  const linhas = [{ produtoId: 'p-lasanha', nome: 'Lasanha' }, { produtoId: 'p-bolo', nome: 'Bolo' }]
  const [peso, receita] = motivosDosPendentes([
    { produtoId: 'p-lasanha', status: 'Erro', erro: 'Unidade desejada (Un) incompativel com rendimento do produto (Kg)' },
    { produtoId: 'p-bolo', status: 'SemReceita', erro: null },
  ], linhas)
  assert.match(peso, /^Lasanha ficou de fora: a receita rende em peso/)
  assert.match(receita, /^Bolo ficou de fora: está sem receita/)
  assert.deepEqual(motivosDosPendentes([], linhas), [])
})

console.log(`\n${passou} verificações passaram, ${falhas.length} falharam.`)
if (falhas.length) process.exit(1)
