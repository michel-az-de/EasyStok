/* eslint-disable no-console */
// Prova da #1511 (M2.6): controle de perdas no console (modo API).
//   - motivo da lista (D-M2-02), "Outro" com texto; acima de R$ 50 o 403 vira aviso;
//   - vencido sugerido só sai quando ela confirma; desfazer é o estorno de saída;
//   - resumo do período com o que foi desfeito.
//
//   node ferramentas/prova-1511-perdas.mjs

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

const { erroDaPerda, criarGestaoPerdas, lerResumoDePerdas, lerVencidos, MOTIVOS_PERDA } = await import('../src/aplicacao/perdas.js')
const { estoqueDoDiaDaApi } = await import('../src/infra/api/producaoApi.js')
const { moduloPorId } = await import('../src/dominio/modulos.js')

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

function montarFetch(rotas) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET', body } = {}) => {
    chamadas.push({ metodo: method, url, corpo: body ? JSON.parse(body) : null })
    for (const [teste, resposta] of rotas) {
      if (teste(method, url)) {
        const { status = 200, data = null, error } = resposta
        return new Response(status === 204 ? null : JSON.stringify(error ? { error } : { data }), { status })
      }
    }
    return new Response(JSON.stringify({ data: null }), { status: 200 })
  }
  return chamadas
}

function montar(rotas) {
  const chamadas = montarFetch(rotas)
  const erros = []
  const feitos = []
  let relidas = 0
  const gestao = criarGestaoPerdas({ recarregar: async () => { relidas += 1 }, aoErro: (m) => erros.push(m), aoFeito: (m) => feitos.push(m) })
  return { gestao, chamadas, erros, feitos, relidas: () => relidas }
}

const PERDAS = '/api/atendimento/producao/perdas'

await confere('motivos da lista (D-M2-02) e "Outro" pede texto', () => {
  assert.deepEqual(MOTIVOS_PERDA.map((m) => m.id), ['Vencido', 'PerdaNoPreparo', 'Doacao', 'Degustacao', 'Outro'])
  const base = { produtoId: 'p-1', quantidade: '2', motivo: 'PerdaNoPreparo' }
  assert.equal(erroDaPerda(base), null)
  assert.match(erroDaPerda({ ...base, produtoId: '' }), /prato ou o insumo/)
  assert.match(erroDaPerda({ ...base, quantidade: '0' }), /maior que zero/)
  assert.match(erroDaPerda({ ...base, motivo: 'Outro', texto: ' ' }), /escreva o motivo/)
  assert.equal(erroDaPerda({ ...base, motivo: 'Outro', texto: 'caiu no chão' }), null)
})

await confere('lançar manda produto, quantidade (vírgula vira ponto) e motivo, e relê', async () => {
  const m = montar([[(mt, u) => mt === 'POST' && u.endsWith(PERDAS), { status: 201, data: { quantidade: 1.5, valor: 6 } }]])
  assert.equal(await m.gestao.lancar({ produtoId: 'p-1', quantidade: '1,5', motivo: 'Outro', texto: ' caiu ', nome: 'Lasanha' }), true)
  assert.deepEqual(m.chamadas.find((c) => c.metodo === 'POST').corpo,
    { produtoId: 'p-1', itemEstoqueId: null, quantidade: 1.5, motivo: 'Outro', texto: 'caiu' })
  assert.equal(m.relidas(), 1)
  assert.match(m.feitos[0], /Lasanha/)
})

await confere('acima de R$ 50 sem Gerente: o 403 do EasyStok vira o aviso dele', async () => {
  const m = montar([[(mt) => mt === 'POST', { status: 403, error: { code: 'PERDA_EXIGE_GERENTE', message: 'Perda de R$ 60,00 passa de R$ 50,00: só o Gerente lança.' } }]])
  assert.equal(await m.gestao.lancar({ produtoId: 'p-1', quantidade: '5', motivo: 'PerdaNoPreparo' }), false)
  assert.match(m.erros[0], /só o Gerente lança/)
})

await confere('rascunho inválido não chama a API', async () => {
  const m = montar([])
  assert.equal(await m.gestao.lancar({ produtoId: 'p-1', quantidade: '', motivo: 'Vencido' }), false)
  assert.equal(m.chamadas.length, 0)
})

await confere('vencido sugerido só sai quando ela confirma, do lote dele, como Vencido', async () => {
  const m = montar([[(mt, u) => mt === 'POST' && u.endsWith(PERDAS), { status: 201, data: {} }]])
  await m.gestao.lancarVencido({ produtoId: 'p-1', itemEstoqueId: 'l-9', quantidade: 3, lote: 'LOT-V' })
  assert.deepEqual(m.chamadas[0].corpo, { produtoId: 'p-1', itemEstoqueId: 'l-9', quantidade: 3, motivo: 'Vencido', texto: null })
})

await confere('desfazer é o estorno de saída que já existe, com motivo', async () => {
  const m = montar([[(mt, u) => mt === 'POST' && u.endsWith('/api/estoque/estorno/m-1'), { data: {} }]])
  assert.equal(await m.gestao.desfazer({ id: 'm-1', nome: 'Lasanha' }), true)
  assert.deepEqual(m.chamadas[0].corpo, { motivo: 'Perda desfeita pelo console' })
})

await confere('resumo do período com os lançamentos e o que foi desfeito', async () => {
  const chamadas = montarFetch([[(mt, u) => mt === 'GET' && u.includes(PERDAS), { data: {
    de: '2026-10-03', ate: '2026-10-09', valor: 13,
    porMotivo: [{ motivo: 'PerdaNoPreparo', rotulo: 'Perda no preparo', quantidade: 2, valor: 10 }],
    porProduto: [{ produtoId: 'p-1', produto: 'Lasanha', quantidade: 3, valor: 13 }],
    lancamentos: [{ movimentacaoId: 'm-1', data: '2026-10-08T12:00:00Z', produtoId: 'p-1', produto: 'Lasanha', lote: 'LOT-A',
      quantidade: 2, valor: 10, motivo: 'PerdaNoPreparo', descricao: 'Perda · Perda no preparo', desfeita: true }],
  } }]])
  const r = await lerResumoDePerdas('2026-10-03', '2026-10-09')
  assert.ok(chamadas[0].url.endsWith('/perdas?de=2026-10-03&ate=2026-10-09'))
  assert.equal(r.valor, 13)
  assert.equal(r.porProduto[0].nome, 'Lasanha')
  assert.equal(r.lancamentos[0].desfeita, true)
})

await confere('vencidos e o produto do prato no estoque do dia', async () => {
  montarFetch([[(mt, u) => u.endsWith('/perdas/vencidos'), { data: [
    { itemEstoqueId: 'l-9', produtoId: 'p-1', produto: 'Lasanha', lote: 'LOT-V', quantidade: 3, validadeEm: '2026-10-07T00:00:00', diasVencido: 2, valor: 12 },
  ] }]])
  const [v] = await lerVencidos()
  assert.deepEqual([v.itemEstoqueId, v.validade, v.diasVencido], ['l-9', '2026-10-07', 2])
  assert.equal(estoqueDoDiaDaApi({ pratos: [{ cardapioItemId: 'i-1', produtoId: 'p-1', nome: 'x', saldo: 1, descoberto: 0 }] }).pratos[0].produtoId, 'p-1')
})

await confere('M2 ganha a tela Perdas só no modo API', () => {
  const tela = moduloPorId('producao').telas.find((t) => t.id === 'perdas')
  assert.equal(tela?.aba, 'perdas')
  assert.equal(tela?.soApi, true)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
