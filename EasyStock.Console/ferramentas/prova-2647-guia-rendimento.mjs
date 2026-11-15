/* eslint-disable no-console */
// Prova da #2647 (achados A1 e A2 da homologação #1769):
//   - A1: gravar insumo ou receita avisa o checklist "Configuração inicial", que relê na hora;
//     falha de gravação não avisa;
//   - A2: o campo Rendimento seleciona o valor no foco, então digitar 10 sobre "1" dá 10, não 110.
//
//   node ferramentas/prova-2647-guia-rendimento.mjs

import { registerHooks } from 'node:module'
import { readFileSync, existsSync } from 'node:fs'
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

const { criarGestaoInsumos } = await import('../src/aplicacao/insumos.js')
const { criarGestaoReceitas } = await import('../src/aplicacao/receitas.js')
const caminhoAviso = new URL('../src/aplicacao/mudancasDaProducao.js', import.meta.url)
const aviso = existsSync(caminhoAviso) ? await import(caminhoAviso.href) : null

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
  globalThis.fetch = async (url, { method = 'GET' } = {}) => {
    for (const [teste, resposta] of rotas) {
      if (teste(method, url)) {
        const { status = 200, data = null, error } = resposta
        return new Response(status === 204 ? null : JSON.stringify(error ? { error } : { data }), { status })
      }
    }
    return new Response(JSON.stringify({ data: null }), { status: 200 })
  }
}

// Conta os avisos que o checklist receberia. Sem o canal, não há como o checklist saber da gravação.
function escutar() {
  assert.ok(aviso?.assinarMudancaNaProducao, 'não existe canal que avise o checklist de uma gravação na Produção')
  let avisos = 0
  const sair = aviso.assinarMudancaNaProducao(() => { avisos += 1 })
  return { avisos: () => avisos, sair }
}

const gestaoInsumos = () => criarGestaoInsumos({ recarregar: async () => {}, aoErro: () => {} })
const gestaoReceitas = () => criarGestaoReceitas({ recarregar: async () => {}, aoErro: () => {} })

await confere('A1: cadastrar o primeiro insumo avisa o checklist', async () => {
  const e = escutar()
  montarFetch([[(m) => m === 'POST', { data: { produtoId: 'p-9' } }]])
  assert.equal(await gestaoInsumos().criar({ nome: 'Molho', unidade: 'G', custo: '0,05' }), true)
  e.sair()
  assert.equal(e.avisos(), 1)
})

await confere('A1: editar insumo avisa o checklist', async () => {
  const e = escutar()
  montarFetch([[(m) => m === 'PUT', { data: null }]])
  assert.equal(await gestaoInsumos().atualizar('p-9', { minimo: '10', custo: '0,05' }), true)
  e.sair()
  assert.equal(e.avisos(), 1)
})

await confere('A1: salvar receita avisa o checklist', async () => {
  const e = escutar()
  montarFetch([[(m) => m === 'PUT', { data: null }]])
  const ok = await gestaoReceitas().salvar('p-1', {
    rendimento: '10', unidadeRendimento: 'Un', unidadeMedidaBase: 'Un',
    linhas: [{ insumoId: 's-1', quantidade: '100', unidade: 'G' }],
  })
  e.sair()
  assert.equal(ok, true)
  assert.equal(e.avisos(), 1)
})

await confere('A1: gravação recusada (403) não avisa o checklist', async () => {
  const e = escutar()
  montarFetch([[(m) => m !== 'GET', { status: 403, error: { code: 'FORBIDDEN', message: 'sem permissão' } }]])
  assert.equal(await gestaoInsumos().criar({ nome: 'Molho', unidade: 'G' }), false)
  assert.equal(await gestaoReceitas().salvar('p-1', { rendimento: '6', linhas: [{ insumoId: 's-1', quantidade: '1', unidade: 'G' }] }), false)
  e.sair()
  assert.equal(e.avisos(), 0)
})

await confere('A1: depois de sair, o checklist desmontado não é mais avisado', async () => {
  const e = escutar()
  e.sair()
  aviso.avisarMudancaNaProducao()
  assert.equal(e.avisos(), 0)
})

await confere('A1: o checklist relê a cada aviso, não só ao montar', () => {
  const fonte = readFileSync(new URL('../src/features/gestao/producao/ChecklistConfiguracao.jsx', import.meta.url), 'utf8')
  assert.match(fonte, /assinarMudancaNaProducao\(/, 'ChecklistConfiguracao não assina o aviso de gravação')
})

await confere('A2: o campo Rendimento seleciona o valor no foco', () => {
  const fonte = readFileSync(new URL('../src/features/gestao/producao/AbaReceitasApi.jsx', import.meta.url), 'utf8')
  const campo = fonte.match(/<CampoTexto rotulo="Rendimento"[\s\S]*?\/>/)
  assert.ok(campo, 'campo Rendimento não encontrado')
  assert.match(campo[0], /onFocus=\{\(e\) => e\.target\.select\(\)\}/, 'o foco deixa o "1" e põe o cursor no fim')
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
