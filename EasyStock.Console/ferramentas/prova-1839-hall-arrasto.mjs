// Prova da #1839 pacote D (homologação gravada de 15/10): no hall, os quatro números e os oito
// cartões arrastam direto, sem modo Organizar, e a ordem dos números fica nas preferências. Cada
// cartão traz a ilustração da marca e, quando o módulo tem fonte lida, a linha viva.
//
// Roda com: node ferramentas/prova-1839-hall-arrasto.mjs

import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { transformWithOxc } from 'vite'

const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const jsx = new Map(await Promise.all(readdirSync(raiz, { recursive: true }).filter((n) => n.endsWith('.jsx')).map(async (nome) => {
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

const { MODULOS } = await import('../src/dominio/modulos.js')
const { normalizarPreferenciasHall, ordenarPaineisDoHall, moverPainelDoHall, aplicarFocoDoHall } = await import('../src/dominio/preferenciasHall.js')
const { gravarPreferenciasHall, lerPreferenciasHall } = await import('../src/aplicacao/preferenciasHall.js')
const { pendenciaDoModulo, indicadoresDoHall } = await import('../src/dominio/pulsoDoHall.js')

let passou = 0
const provar = (nome, fn) => { fn(); passou += 1; console.log('ok  ' + nome) }
const PAINEIS = ['atendimento', 'cozinha', 'entregas', 'financeiro']
const todos = MODULOS.map((m) => m.id)

// ---------------------------------------------------------------- ordem dos números

provar('sem ordem própria, os números seguem o foco (como antes)', () => {
  assert.equal(normalizarPreferenciasHall(null).ordemPaineis, null)
  assert.deepEqual(ordenarPaineisDoHall({ foco: 'gestao' }, todos), ['financeiro', 'atendimento', 'entregas', 'cozinha'])
})

provar('arrastar um número grava a ordem livre e a tela a respeita', () => {
  const movida = moverPainelDoHall({ foco: 'atendimento' }, 'financeiro', 'atendimento')
  assert.deepEqual(movida.ordemPaineis, ['financeiro', 'atendimento', 'entregas', 'cozinha'])
  assert.deepEqual(ordenarPaineisDoHall(movida, todos), ['financeiro', 'atendimento', 'entregas', 'cozinha'])
  const devolta = moverPainelDoHall(movida, 'financeiro', 'cozinha')
  assert.deepEqual(devolta.ordemPaineis, ['atendimento', 'entregas', 'cozinha', 'financeiro'])
})

provar('a ordem livre sobrevive a gravar e ler, e lixo vira ordem válida', () => {
  const mapa = new Map()
  const armazenamento = { getItem: (k) => mapa.get(k) ?? null, setItem: (k, v) => mapa.set(k, v) }
  const movida = moverPainelDoHall({}, 'entregas', 'atendimento')
  assert.equal(gravarPreferenciasHall('easystok.hall.v1:e:u', movida, armazenamento), true)
  assert.deepEqual(lerPreferenciasHall('easystok.hall.v1:e:u', armazenamento).ordemPaineis, movida.ordemPaineis)
  assert.deepEqual(normalizarPreferenciasHall({ ordemPaineis: ['cozinha', 'privado', 'cozinha'] }).ordemPaineis, ['cozinha', 'atendimento', 'entregas', 'financeiro'])
  assert.equal(normalizarPreferenciasHall({ ordemPaineis: 'financeiro' }).ordemPaineis, null)
  assert.equal(normalizarPreferenciasHall({ ordemPaineis: ['privado'] }).ordemPaineis, null)
})

provar('número oculto ou sem permissão não aparece, mas guarda o lugar', () => {
  const movida = moverPainelDoHall({ paineis: ['cozinha', 'financeiro'] }, 'financeiro', 'atendimento')
  assert.deepEqual(ordenarPaineisDoHall(movida, todos), ['financeiro', 'cozinha'])
  assert.deepEqual(ordenarPaineisDoHall(movida, ['cozinha']), ['cozinha'])
})

provar('escolher um foco no Ajustar volta os números à ordem do modelo', () => {
  const movida = moverPainelDoHall({}, 'financeiro', 'atendimento')
  assert.equal(aplicarFocoDoHall(movida, 'producao').ordemPaineis, null)
})

provar('mover para o mesmo lugar ou para id estranho não muda nada', () => {
  assert.equal(moverPainelDoHall({}, 'cozinha', 'cozinha').ordemPaineis, null)
  assert.equal(moverPainelDoHall({}, 'cardapio', 'cozinha').ordemPaineis, null)
})

// ---------------------------------------------------------------- linha viva do cartão

const conversas = { estado: 'ok', dados: { itens: [
  { situacao: 'Assumida', aguardaResposta: true, assumidaPorUsuarioId: 'u' },
  { situacao: 'Assumida', aguardaResposta: false, assumidaPorUsuarioId: null },
  { situacao: 'Automatica', aguardaResposta: true },
] } }
const pedidos = { estado: 'ok', dados: { itens: [{ status: 'aguardando' }, { status: 'preparando' }, { status: 'pronto' }, { status: 'entregue' }] } }
const cardapio = { estado: 'ok', dados: { itens: [
  { hoje: true, arquivado: false, pendencias: ['SemFoto'] }, { hoje: true, arquivado: false, pendencias: [] },
  { hoje: false, arquivado: true, pendencias: ['SemFoto'] },
] } }
const fontes = { atendimento: conversas, pedidos, financeiro: { estado: 'ok', dados: { saldoEsperado: 10, aberto: true } }, cardapio }

provar('cada módulo com fonte diz o que pede atenção, com o mesmo número de cima', () => {
  assert.deepEqual(pendenciaDoModulo('atendimento', fontes), { texto: '2 conversas sem resposta', atencao: true })
  assert.deepEqual(pendenciaDoModulo('cozinha', fontes), { texto: '2 pedidos no preparo', atencao: true })
  assert.deepEqual(pendenciaDoModulo('entregas', fontes), { texto: '1 pedido pronto para sair', atencao: true })
  assert.deepEqual(pendenciaDoModulo('financeiro', fontes), { texto: 'Caixa aberto', atencao: false })
  assert.deepEqual(pendenciaDoModulo('cardapio', fontes), { texto: '1 prato com pendência', atencao: true })
  assert.deepEqual(indicadoresDoHall(fontes, PAINEIS).map((i) => i.valor), [2, 2, 1, 10])
})

provar('sem pendência a linha acalma; caixa de outro dia aberto pede atenção', () => {
  const vazio = { estado: 'ok', dados: { itens: [] } }
  assert.deepEqual(pendenciaDoModulo('atendimento', { atendimento: vazio }), { texto: 'Nenhuma conversa esperando', atencao: false })
  assert.deepEqual(pendenciaDoModulo('cozinha', { pedidos: vazio }), { texto: 'Fila de preparo vazia', atencao: false })
  assert.deepEqual(pendenciaDoModulo('cardapio', { cardapio: { estado: 'ok', dados: { itens: [{ hoje: true, pendencias: [] }] } } }), { texto: '1 prato no cardápio de hoje', atencao: false })
  assert.deepEqual(pendenciaDoModulo('financeiro', { financeiro: { estado: 'ok', dados: { aberturaPendenteCrossDay: true } } }), { texto: 'Caixa de outro dia aberto', atencao: true })
})

provar('leitura falha, ausente ou módulo sem fonte não inventa linha', () => {
  assert.equal(pendenciaDoModulo('atendimento', { atendimento: { estado: 'erro', dados: conversas.dados } }), null)
  assert.equal(pendenciaDoModulo('cozinha', {}), null)
  for (const id of ['producao', 'campanhas', 'configuracoes']) assert.equal(pendenciaDoModulo(id, fontes), null)
  assert.equal(pendenciaDoModulo('cozinha', { pedidos: { estado: 'ok', dados: { itens: [{ status: 'aguardando' }], parcial: true } } }).texto, '1+ pedido no preparo')
})

// ---------------------------------------------------------------- tela renderizada

const { createElement: h } = await import('react')
const { renderToStaticMarkup } = await import('react-dom/server')
const { HallDeModulos } = await import('../src/features/hall/HallDeModulos.jsx')
const { IlustracaoDoModulo } = await import('../src/features/hall/IlustracaoDoModulo.jsx')
const { ContextoAcessoModulos } = await import('../src/aplicacao/acessoModulos.jsx')
globalThis.document ??= { documentElement: { dataset: { theme: 'light' } } }
const html = renderToStaticMarkup(h(ContextoAcessoModulos.Provider, { value: { permite: () => true, acoes: {}, aoSair: () => {} } },
  h(HallDeModulos, { fonteApi: true, sessao: null, agora: Date.parse('2026-10-15T18:00:00Z') })))

provar('os oito módulos têm ilustração própria, decorativa e no tema', () => {
  for (const { id } of MODULOS) {
    const svg = renderToStaticMarkup(h(IlustracaoDoModulo, { id }))
    assert.match(svg, /aria-hidden="true"/, `${id} decorativa`)
    assert.match(svg, /var\(--il-tinta\)/, `${id} usa as cores do tema`)
    assert.doesNotMatch(svg, /#[0-9a-f]{6}/i, `${id} sem cor fixa`)
    assert.match(html, new RegExp(`data-ilustracao="${id}"`), `${id} no cartão`)
  }
  assert.equal(renderToStaticMarkup(h(IlustracaoDoModulo, { id: 'privado' })), '')
})

provar('arrasto direto: sem modo Organizar, sem botão Mover e o link segue sendo link', () => {
  assert.doesNotMatch(html, />Mover<|Concluir organização|role="button"[^>]*href|aria-roledescription/)
  const links = html.match(/<a [^>]*href="#\/[^"]*"[^>]*>/g) ?? []
  assert.ok(links.length >= 8, `${links.length} links`)
  for (const link of links.filter((l) => /data-modulo|class="abrir"/.test(l))) assert.match(link, /draggable="false"/)
  assert.match(html, /Arraste os números e os cartões para mudar a ordem\./)
})

provar('#1810 continua valendo: até 20 controles na primeira vista', () => {
  const controles = (html.match(/<(a|button|input|summary|select)[\s>]/g) ?? []).length
  assert.ok(controles <= 20, `${controles} controles`)
})

provar('teclado: Espaço pega, Enter abre (Enter fora das teclas de arrasto)', () => {
  const fonte = readFileSync(new URL('../src/features/hall/arrastoDoHall.js', import.meta.url), 'utf8')
  assert.match(fonte, /start: \['Space'\]/)
  assert.doesNotMatch(fonte, /'Enter'/)
})

console.log(JSON.stringify({ prova: '1839-hall-arrasto', aprovados: passou, falhas: 0 }))
