/* eslint-disable no-console */
// Prova da issue #2227: o Balcão não diz "Conversas 0" nem "Nenhuma conversa aberta agora."
// antes de a lista chegar da API. Zero conversa sem leitura concluída é "carregando" ou "erro",
// a mesma regra do painel Agora (#1611). Componentes reais, renderizados no Node:
//
//   node ferramentas/prova-2227-lista-carregando.mjs
//
// Mesmo gancho de `prova-1785-ficha.mjs`.

import assert from 'node:assert/strict'
import { readFileSync, readdirSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { join } from 'node:path'
import { transformWithOxc } from 'vite'
import { createElement as h } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'

const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const jsx = new Map(await Promise.all(readdirSync(raiz, { recursive: true }).filter(n => n.endsWith('.jsx')).map(async nome => {
  const arquivo = join(raiz, nome)
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env', '({ DEV: false })')
  return [pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code]
})))
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      for (const ext of ['.js', '.jsx']) { try { return proximo(spec + ext, contexto) } catch { /* próximo formato */ } }
    }
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (jsx.has(url)) return { format: 'module', source: jsx.get(url), shortCircuit: true }
    if (url.endsWith('.css')) return { format: 'module', source: 'export default new Proxy({}, { get: (_, p) => String(p) })', shortCircuit: true }
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', source: 'export const FONTE_API=true;export const API_BASE="";', shortCircuit: true }
    if (url.endsWith('.json')) return { format: 'module', source: 'export default ' + readFileSync(new URL(url), 'utf8'), shortCircuit: true }
    return proximo(url, contexto)
  },
})

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

const { PainelConversas } = await import('../src/features/caixa-de-entrada/PainelConversas.jsx')
const { ContextoEstado, ContextoAcoes, ContextoCatalogo } = await import('../src/aplicacao/contextos.js')
const { estadoInicial } = await import('../src/aplicacao/reducer.js')
const { CANAIS } = await import('../src/infra/catalogo.js')

const AGORA = Date.parse('2026-11-03T21:30:00-03:00')
const { filtros } = estadoInicial({ conversas: [], catalogo: { cardapio: [], janelas: [], canais: [] }, regras: [] })
const conversa = {
  id: 'c1', nome: 'Marina Souza', canal: 'WhatsApp', estado: 'Aberto', conta: 'cliente',
  cliente: { tags: [], notas: [], pedidos: 1 }, pedido: null, passagem: null,
  mensagens: [{ id: 'm1', dir: 'in', texto: 'Oi, tem lasanha?', em: new Date(AGORA - 120000).toISOString() }],
  ultimaEm: new Date(AGORA - 120000).toISOString(),
}

function balcao({ estado, conversas = [], fonteApi = true }) {
  const valor = {
    conversas, visiveis: conversas, encerradasDoBalcao: [], bloqueadasDoBalcao: [], selecionadaId: null, filtros,
    agora: AGORA, automaticoPausado: {}, aberta: true, horarioDaLoja: null, pagamentosNaoVistos: new Set(),
    fonteApi, sincronizacao: { estado, mensagem: null, aviso: null }, sessao: { usuario: { id: 'u1', nome: 'Felipe' } },
  }
  const acoes = { mudarFiltro() {}, selecionar() {}, listarNaoEntregues: async () => [], reenviar: async () => {} }
  return renderToStaticMarkup(h(ContextoEstado.Provider, { value: valor },
    h(ContextoCatalogo.Provider, { value: { cardapio: [], canais: CANAIS, janelas: [] } },
      h(ContextoAcoes.Provider, { value: acoes }, h(PainelConversas, {})))))
}
const cabecalho = (html) => html.match(/<h2>(.*?)<\/h2>/)?.[1] ?? ''
// #2232 (L5, L11): a lista abre em "Precisa de você" e o total mora no chip "Todas", não no título.
const VAZIO_DE_HOJE = /Nenhuma conversa aberta agora\.|Ninguém esperando você\./
// #2185 (R04): o número do chip é a Pilula contagem, o contador único do app.
const totalNoChip = (html) => html.match(/Todas <span class="contagem[^"]*"[^>]*>(\d+)<\/span>/)?.[1]

// --- 1. Regra pura ---------------------------------------------------------------
await confere('1. cargaDaLista: carregando, erro ou nada', async () => {
  const { cargaDaLista } = await import('../src/dominio/cargaDaLista.js')
  assert.equal(cargaDaLista({ fonteApi: true, estado: 'carregando', total: 0 }), 'carregando')
  assert.equal(cargaDaLista({ fonteApi: true, estado: 'erro', total: 0 }), 'erro')
  assert.equal(cargaDaLista({ fonteApi: true, estado: 'ok', total: 0 }), null)
  assert.equal(cargaDaLista({ fonteApi: true, estado: 'erro', total: 3 }), null)
  assert.equal(cargaDaLista({ fonteApi: false, estado: 'desligada', total: 0 }), null)
})
await confere('1. o painel Agora segue com o mesmo texto (#1611)', async () => {
  const { avisoDeResponder } = await import('../src/dominio/agora.js')
  assert.equal(avisoDeResponder({ fonteApi: true, estado: 'carregando', total: 0 }), 'Carregando as conversas…')
  assert.match(avisoDeResponder({ fonteApi: true, estado: 'erro', total: 0 }), /Não consegui carregar/)
  assert.equal(avisoDeResponder({ fonteApi: true, estado: 'ok', total: 0 }), null)
})

// --- 2. Carregando ---------------------------------------------------------------
await confere('2. carregando: a lista diz que está carregando, não que está vazia', () => {
  const html = balcao({ estado: 'carregando' })
  assert.match(html, /Carregando as conversas…/)
  assert.doesNotMatch(html, /Nenhuma conversa|Ninguém esperando/)
})
await confere('2. carregando: o cabeçalho não diz 0', () => {
  const html = balcao({ estado: 'carregando' })
  assert.match(cabecalho(html), /Conversas/)
  assert.doesNotMatch(cabecalho(html), /\d/)
  assert.doesNotMatch(html, /(Todas|Precisa de você) <span[^>]*>0<\/span>/)
})

// --- 3. Erro ---------------------------------------------------------------------
await confere('3. erro sem lista: diz que não carregou, sem contagem', () => {
  const html = balcao({ estado: 'erro' })
  assert.match(html, /Não consegui carregar as conversas/)
  assert.doesNotMatch(html, /Nenhuma conversa/)
  assert.doesNotMatch(cabecalho(html), /\d/)
})

// --- 4. Lido -------------------------------------------------------------------------
await confere('4. lido e vazio: os vazios de hoje continuam', () => {
  const html = balcao({ estado: 'ok' })
  assert.match(html, VAZIO_DE_HOJE)
  assert.equal(totalNoChip(html), '0')
  assert.doesNotMatch(html, /Carregando/)
})
await confere('4. erro com lista na tela: mostra a lista e a contagem', () => {
  const html = balcao({ estado: 'erro', conversas: [conversa] })
  assert.match(html, /Marina Souza/)
  assert.equal(totalNoChip(html), '1')
  assert.doesNotMatch(html, /Não consegui carregar/)
})
await confere('4. demonstração: vazio de sempre, sem carregando', () => {
  const html = balcao({ estado: 'desligada', fonteApi: false })
  assert.match(html, VAZIO_DE_HOJE)
  assert.doesNotMatch(html, /Carregando/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
