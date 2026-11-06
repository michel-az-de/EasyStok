/* eslint-disable no-console */
// Prova da #2235: regras puras da banca de UX da Produção (resumo do estoque, validade, origem do
// lote, perda na linha, progresso das receitas, estado do aparelho, nome e detalhe do vencido).
//
//   node ferramentas/prova-2235-banca-producao.mjs

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

const { resumoDoEstoque, quandoVence, origemDoLote, detalheDoLote, podeRegistrarPerda } = await import('../src/aplicacao/estoqueDoDia.js')
const { progressoDasReceitas, receitaEmBranco, textoDoProgresso } = await import('../src/aplicacao/receitas.js')
const { estadoDosAparelhos } = await import('../src/aplicacao/aparelhos.js')
const { comNomeDoCardapio, comPorcaoDoLote, detalheDoVencido, mensagemDoVencido } = await import('../src/aplicacao/perdas.js')

let passou = 0
const falhas = []
const confere = (nome, fn) => {
  try { fn(); passou++; console.log('ok   ', nome) } catch (e) { falhas.push(nome); console.log('FALHA', nome, '\n     ', e.message) }
}

confere('T1-1: o topo conta porções, lotes vencidos e vendas sem saldo', () => {
  const pratos = [{ saldo: 0, lotes: [{ vencido: true }, { vencido: false }] }, { saldo: 3, lotes: [{ vencido: true }] }]
  assert.deepEqual(resumoDoEstoque(pratos, [{}, {}]), { porcoes: 3, vencidos: 2, vendasSemSaldo: 2 })
  assert.deepEqual(resumoDoEstoque(), { porcoes: 0, vencidos: 0, vendasSemSaldo: 0 })
})

confere('T1-5: a validade diz quando venceu ou quando vence', () => {
  assert.equal(quandoVence({}), 'Sem validade informada')
  assert.equal(quandoVence({ validade: '2026-10-17', vencido: true }), 'Venceu em 17/10 · fora do saldo') // #2258 (auditoria P2): o lote vencido diz que saiu do saldo
  assert.equal(quandoVence({ validade: '2026-11-05', diasParaVencer: 0 }), 'Vence hoje')
  assert.equal(quandoVence({ validade: '2026-11-06', diasParaVencer: 1 }), 'Vence amanhã')
  assert.equal(quandoVence({ validade: '2026-11-09', diasParaVencer: 4 }), 'Vence em 09/11')
})

confere('T1-3: a origem do lote fala a língua da loja', () => {
  assert.equal(origemDoLote({ origem: 'mobile', operadorNome: 'Ana' }), 'Lançado no aparelho por Ana')
  assert.equal(origemDoLote({ origem: 'producao' }), 'Lançado na produção do dia')
  assert.equal(origemDoLote({ origem: 'web' }), 'Lançado pelo cadastro do estoque')
  assert.equal(origemDoLote({ origem: 'entrada', operadorNome: 'Bia' }), 'Entrada de estoque por Bia')
  assert.equal(origemDoLote({ origem: 'producao', operadorUserId: 'u1' }, 'u1'), 'Lançado por você')
  assert.ok(!/mobile|web|producao/.test(origemDoLote({ origem: 'web' })), 'sem nome técnico')
})

confere('T1-4: o lote diz o tamanho junto do código', () => {
  assert.equal(detalheDoLote({ codigo: 'L-1', porcao: '500 g', origem: 'Lançado por Ana' }), 'L-1 · 500 g · Lançado por Ana')
  assert.equal(detalheDoLote({}), 'Sem código')
})

confere('T1-5: "Registrar perda" só no lote vencido, ligado e com saldo', () => {
  const ok = { vencido: true, itemEstoqueId: 'i', produtoId: 'p', quantidade: 2 }
  assert.equal(podeRegistrarPerda(ok), true)
  assert.equal(podeRegistrarPerda({ ...ok, vencido: false }), false)
  assert.equal(podeRegistrarPerda({ ...ok, itemEstoqueId: null }), false)
  assert.equal(podeRegistrarPerda({ ...ok, quantidade: 0 }), false)
})

confere('Receitas: o progresso diz quantos pratos têm receita e o próximo', () => {
  const r = [{ sku: 'a', linhas: 2 }, { sku: 'b', linhas: 0 }, { sku: 'c' }]
  assert.deepEqual(progressoDasReceitas(r), { comReceita: 1, total: 3, proximo: 'b' })
  assert.equal(textoDoProgresso(progressoDasReceitas(r)), '1 de 3 pratos com receita.')
  assert.equal(textoDoProgresso({ comReceita: 2, total: 2 }), 'Todos os 2 pratos têm receita.')
  assert.equal(textoDoProgresso({ comReceita: 0, total: 0 }), '')
})

confere('P2-2: o botão do aparelho diz qual está ligado e quando apareceu', () => {
  const agora = Date.parse('2026-11-05T12:00:00Z')
  assert.equal(estadoDosAparelhos([], agora), 'Nenhum aparelho conectado')
  assert.equal(estadoDosAparelhos([{ revoked: true }, { pendingPair: true }], agora), 'Nenhum aparelho conectado')
  assert.equal(estadoDosAparelhos([{ label: 'Cozinha', lastSeenAt: '2026-11-05T11:55:00Z' }], agora), 'Cozinha · ativo agora')
  assert.equal(estadoDosAparelhos([{ defaultOperatorName: 'Ana', lastSeenAt: '2026-11-05T11:30:00Z' }], agora), 'Ana · visto há 30 min')
  assert.equal(estadoDosAparelhos([{ lastSeenAt: '2026-11-05T09:00:00Z' }], agora), 'Aparelho · visto há 3 h')
  assert.equal(estadoDosAparelhos([{}, {}], agora), '2 aparelhos conectados')
  assert.equal(estadoDosAparelhos([{ label: 'auto-provisioned', defaultOperatorName: 'Rosa', lastSeenAt: '2026-11-05T11:55:00Z' }], agora), 'Rosa · ativo agora', 'rótulo técnico do pareamento não é nome')
  assert.equal(estadoDosAparelhos([{ label: 'auto-provisioned', lastSeenAt: '2026-11-05T11:55:00Z' }], agora), 'Aparelho · ativo agora')
})

confere('Perdas B2: o prato aparece com o nome do Cardápio', () => {
  const itens = [{ produtoId: 'p1', nome: 'lasanha bolonhesa' }, { produtoId: 's1', nome: 'Molho' }]
  const pratos = [{ produtoId: 'p1', nome: 'Lasanha Bolonhesa' }, { produtoId: null, nome: 'Avulso' }]
  assert.deepEqual(comNomeDoCardapio(itens, pratos), [{ produtoId: 'p1', nome: 'Lasanha Bolonhesa', ehPrato: true }, { produtoId: 's1', nome: 'Molho' }])
  assert.deepEqual(comNomeDoCardapio(), [])
})

confere('Perdas B3: o vencido diz porção, data e por que não tem custo', () => {
  assert.equal(detalheDoVencido({ lote: 'L-9', quantidade: 1, validade: '2026-10-17', diasVencido: 18, valor: 0, ehPrato: true }),
    'L-9 · 1 porção · venceu em 17/10 · custo do lote não registrado')
  assert.equal(detalheDoVencido({ lote: null, quantidade: 3, diasVencido: 1, valor: 12.5, ehPrato: true }).replace(/\s/g, ' '),
    'sem lote · 3 porções · venceu há 1 dia · R$ 12,50')
  assert.equal(detalheDoVencido({ lote: 'I-1', quantidade: 0.25, validade: '2026-11-01', valor: 0 }), 'I-1 · 0,25 · venceu em 01/11 · custo do lote não registrado')
})

confere('Banca 4: o vencido diz o tamanho do lote e a mensagem diz o que foi lançado', () => {
  const pratos = [{ lotes: [{ itemEstoqueId: 'ie-1', porcao: '600 g' }, { itemEstoqueId: 'ie-2', porcao: null }] }]
  const [a, b] = comPorcaoDoLote([{ itemEstoqueId: 'ie-1', lote: 'L-1' }, { itemEstoqueId: 'ie-2', lote: 'L-1' }], pratos)
  assert.equal(a.porcao, '600 g')
  assert.equal(b.porcao, undefined)
  assert.equal(detalheDoVencido({ ...a, quantidade: 3, validade: '2026-10-07', valor: 0, ehPrato: true }), 'L-1 · 600 g · 3 porções · venceu em 07/10 · custo do lote não registrado')
  assert.equal(mensagemDoVencido({ quantidade: 3, nome: 'Lasanha', porcao: '600 g', lote: 'L-1', ehPrato: true }), 'Perda por vencimento registrada: 3 porções de Lasanha (600 g), lote L-1.')
  assert.equal(mensagemDoVencido({ quantidade: 1.5, nome: null, lote: null }), 'Perda por vencimento registrada: 1,5.')
})

confere('Banca 4: abrir a receita de prato sem produto não chama a API', () => {
  assert.deepEqual(receitaEmBranco({ nome: 'Bolo' }), { produtoId: null, nome: 'Bolo', rendimento: 1, unidadeRendimento: 'Un', unidadeMedidaBase: null, linhas: [], custoTotal: null, custoPorRendimento: null })
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
