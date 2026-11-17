/* eslint-disable no-console */
// Prova da #2813 (B4): Financeiro › Fechamentos do aparelho, a consulta SOMENTE LEITURA dos fechamentos de caixa que o
// aparelho antigo guardou (`GET api/mobile/legacy-import/fechamentos`). Dados sintéticos: nada do backup real.
//
// 1. Domínio: ordem (dia desc, depois a hora do fechamento), valores em REAIS, gaveta sem conferência, observação nula,
//    item ilegível contado como não lido, aviso de ignorados.
// 2. Cliente da API: só GET na rota certa, resposta crua `{ itens, ignorados }` (fora do envelope), sem escrita.
// 3. Catálogo: a tela mora no Financeiro, só no modo API e só para a gerência da loja (`controlarLoja`); rota e ícone.
// 4. Tela (componente real renderizado no Node): carregando, erro, vazio, lista e aviso de ignorados, sem controle
//    que edite, apague ou reenvie.
//
//   node ferramentas/prova-2813-fechamentos-legados.mjs
//
// Mesmo gancho de `prova-2227-lista-carregando.mjs`.

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

globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }

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

const {
  avisoDeIgnorados, diaDoFechamento, fechamentoLegadoDaApi, fechamentosLegadosDaApi, quemFechou, totaisDoFechamento,
} = await import('../src/dominio/fechamentosLegados.js')
const { dataHora } = await import('../src/dominio/formato.js')
const { MODULOS, moduloPorId, telasDoMenu } = await import('../src/dominio/modulos.js')
const { rotaDaHash } = await import('../src/dominio/rota.js')
const { iconeDaTela, partirIcone } = await import('../src/componentes/iconesDoConsole.js')
const clienteDaApi = await import('../src/infra/api/fechamentosLegadosApi.js')
const { carregarFechamentosLegados } = await import('../src/aplicacao/fechamentosLegados.js')
const { CorpoDosFechamentosLegados } = await import('../src/features/gestao/caixa/AbaFechamentosLegados.jsx')

// Fechamento como a API manda (chaves reais de `totals`), com valores inventados.
const fch = (extra = {}) => ({
  id: 'fch-1', importId: '00000000-0000-0000-0000-000000000001', dateKey: '2026-03-02', closedAt: '2026-03-02T21:30:00Z',
  closedBy: 'Operadora de teste', notes: null, origem: 'aparelho-legado',
  totals: { sold: 1234.5, in: 200, out: 50.25, balance: 1384.25, conferredBalance: null, entriesCount: 12, ordersCount: 9 },
  ...extra,
})
// O Intl põe espaço sem quebra entre "R$" e o número.
const semEspaco = (texto) => String(texto).replaceAll(' ', ' ')
const lista = (itens) => fechamentosLegadosDaApi({ itens, ignorados: 0 }).itens

// ---- 1. Domínio ----
await confere('a lista vai do dia mais novo ao mais antigo; no mesmo dia, o fechamento mais tarde primeiro; o id desempata', () => {
  const { itens } = fechamentosLegadosDaApi({
    ignorados: 0,
    itens: [
      fch({ id: 'fch-d', dateKey: '2025-12-31', closedAt: '2025-12-31T23:00:00Z' }),
      fch({ id: 'fch-b', dateKey: '2026-03-02', closedAt: '2026-03-02T15:00:00Z' }),
      fch({ id: 'fch-f', dateKey: '2026-03-04', closedAt: '2026-03-04T20:00:00Z' }),
      fch({ id: 'fch-e', dateKey: '2026-03-04', closedAt: '2026-03-04T20:00:00Z' }),
      fch({ id: 'fch-a', dateKey: '2026-03-02', closedAt: '2026-03-02T21:30:00Z' }),
    ],
  })
  assert.deepEqual(itens.map((i) => i.id), ['fch-e', 'fch-f', 'fch-a', 'fch-b', 'fch-d'])
})

await confere('os totais vêm em reais, como no aparelho: 1234,5 é R$ 1.234,50, não R$ 12,35', () => {
  const [fechamento] = lista([fch()])
  const totais = Object.fromEntries(totaisDoFechamento(fechamento).map((t) => [t.rotulo, semEspaco(t.valor)]))
  assert.equal(totais.Vendido, 'R$ 1.234,50')
  assert.equal(totais.Entradas, 'R$ 200,00')
  assert.equal(totais['Saídas'], 'R$ 50,25')
  assert.equal(totais.Saldo, 'R$ 1.384,25')
  assert.equal(totais.Pedidos, '9')
  assert.equal(totais['Lançamentos'], '12')
})

await confere('gaveta sem conferência diz "Não conferida"; gaveta conferida em zero continua zero', () => {
  const gaveta = (conferredBalance) => totaisDoFechamento(lista([fch({ totals: { ...fch().totals, conferredBalance } })])[0])
    .find((t) => t.rotulo === 'Gaveta conferida').valor
  assert.equal(gaveta(null), 'Não conferida')
  assert.equal(semEspaco(gaveta(0)), 'R$ 0,00')
  assert.equal(semEspaco(gaveta(1380)), 'R$ 1.380,00')
})

await confere('observação nula fica vazia e a observação escrita passa como está', () => {
  assert.equal(fechamentoLegadoDaApi(fch({ notes: null })).observacoes, '')
  assert.equal(fechamentoLegadoDaApi(fch({ notes: 'Faltou troco no turno da tarde' })).observacoes, 'Faltou troco no turno da tarde')
})

await confere('item ilegível não entra na lista e entra na conta de ignorados, somado ao que a API já pulou', () => {
  const resposta = { ignorados: 2, itens: [fch(), { dateKey: '2026-03-02' }, fch({ id: 'fch-2', dateKey: 'ontem' }), null, 'texto'] }
  const { itens, ignorados } = fechamentosLegadosDaApi(resposta)
  assert.deepEqual(itens.map((i) => i.id), ['fch-1'])
  assert.equal(ignorados, 2 + 4)
  assert.deepEqual(fechamentosLegadosDaApi(null), { itens: [], ignorados: 0 })
})

await confere('quem fechou: data e hora no fuso da loja e o nome do operador; o dia vem sem escorregar de fuso', () => {
  const [fechamento] = lista([fch()])
  const quando = dataHora(Date.parse('2026-03-02T21:30:00Z'))
  assert.match(quando, /02\/03\/2026.*18:30/)
  assert.equal(quemFechou(fechamento), `Fechado em ${quando} por Operadora de teste`)
  assert.equal(quemFechou({ fechadoEm: null, fechadoPor: 'Operadora de teste' }), 'Fechado por Operadora de teste')
  assert.equal(quemFechou({ fechadoEm: null, fechadoPor: '' }), '')
  assert.equal(diaDoFechamento(fechamento), '2 de março de 2026')
})

await confere('aviso de ignorados: vazio sem nenhum, singular e plural', () => {
  assert.equal(avisoDeIgnorados(0), '')
  assert.equal(avisoDeIgnorados(1), '1 fechamento do aparelho não pôde ser lido e ficou fora desta lista.')
  assert.equal(avisoDeIgnorados(3), '3 fechamentos do aparelho não puderam ser lidos e ficaram fora desta lista.')
})

// ---- 2. Cliente da API e caso de uso ----
await confere('o cliente só lê: GET na rota certa, sem corpo, resposta crua fora do envelope, nenhuma função de escrita', async () => {
  let chamada
  globalThis.fetch = async (url, opcoes) => { chamada = { url, opcoes }; return new Response(JSON.stringify({ itens: [fch()], ignorados: 1 })) }
  const resposta = await clienteDaApi.listarFechamentosLegados()
  assert.equal(chamada.url, '/api/mobile/legacy-import/fechamentos')
  assert.equal(chamada.opcoes.method, 'GET')
  assert.equal(chamada.opcoes.body, undefined)
  assert.equal(resposta.ignorados, 1)
  assert.equal(resposta.itens[0].id, 'fch-1')
  assert.deepEqual(Object.keys(clienteDaApi), ['listarFechamentosLegados'])
  assert.doesNotMatch(readFileSync(new URL('../src/infra/api/fechamentosLegadosApi.js', import.meta.url), 'utf8'), /metodo\s*:/)
})

await confere('carregarFechamentosLegados devolve a lista já ordenada e no modelo da tela', async () => {
  globalThis.fetch = async () => new Response(JSON.stringify({
    itens: [fch({ id: 'fch-a', dateKey: '2026-03-01' }), fch({ id: 'fch-b', dateKey: '2026-03-05' })], ignorados: 0,
  }))
  const resultado = await carregarFechamentosLegados()
  assert.deepEqual(resultado.itens.map((i) => i.id), ['fch-b', 'fch-a'])
  assert.equal(resultado.ignorados, 0)
})

// ---- 3. Catálogo: menu, permissão, rota, ícone ----
const idsDoFinanceiro = (opcoes) => telasDoMenu(moduloPorId('financeiro'), opcoes).map((t) => t.id)

await confere('a tela mora no Financeiro, só no modo API, e só para a gerência da loja (controlarLoja, ou verFechamentosLegados quando o servidor mandar)', () => {
  assert.ok(MODULOS.find((m) => m.id === 'financeiro').telas.some((t) => t.id === 'fechamentos-legados'))
  assert.ok(idsDoFinanceiro({ fonteApi: true, acoes: { controlarLoja: true } }).includes('fechamentos-legados'))
  assert.ok(idsDoFinanceiro({ fonteApi: true, acoes: { verFechamentosLegados: true } }).includes('fechamentos-legados'))
  // A chave do servidor, quando existe, manda: ela vence a gerência da loja.
  assert.ok(!idsDoFinanceiro({ fonteApi: true, acoes: { verFechamentosLegados: false, controlarLoja: true } }).includes('fechamentos-legados'))
  // O Operador abre o Financeiro pelo Caixa do dia e a API lhe responde 403 aqui: a tela nem aparece.
  // Ter o caixa (`gerenciarCaixa`) ou as contas sozinho não libera: a tela é da gerência da loja.
  assert.ok(!idsDoFinanceiro({ fonteApi: true, acoes: { gerenciarCaixa: true, visualizarContasPagar: true, visualizarContasReceber: true, verPedidos: true } }).includes('fechamentos-legados'))
  assert.ok(!idsDoFinanceiro({ fonteApi: true, acoes: {} }).includes('fechamentos-legados'))
  // Na demonstração não há o que ler.
  assert.ok(!idsDoFinanceiro({ fonteApi: false }).includes('fechamentos-legados'))
})

await confere('a rota #/m/financeiro/fechamentos-legados abre a aba da tela no modo API e cai no Início na demonstração', () => {
  const rota = rotaDaHash('#/m/financeiro/fechamentos-legados', { fonteApi: true })
  assert.deepEqual([rota.tipo, rota.modulo, rota.tela, rota.aba], ['modulo', 'financeiro', 'fechamentos-legados', 'fechamentos-legados'])
  assert.equal(rotaDaHash('#/m/financeiro/fechamentos-legados', { fonteApi: false }).tipo, 'hall')
})

await confere('a tela tem ícone próprio no menu, diferente do de toda outra tela do Financeiro', () => {
  const nome = partirIcone(iconeDaTela('financeiro', 'fechamentos-legados'))?.nome
  assert.ok(nome, 'sem ícone próprio')
  const dosOutros = moduloPorId('financeiro').telas.filter((t) => t.id !== 'fechamentos-legados')
    .map((t) => partirIcone(iconeDaTela('financeiro', t.id))?.nome)
  assert.ok(!dosOutros.includes(nome), `ícone repetido: ${nome}`)
})

await confere('a aba do catálogo está ligada ao PainelDeAjuste (a janela de Gestão e a rota usam o mesmo)', async () => {
  // Importar o ModalGestao.jsx arrasta as fontes binárias (.woff2), que o carregador de provas em Node não lê: confere o código.
  const { readFileSync } = await import('node:fs')
  const fonte = readFileSync(new URL('../src/features/gestao/ModalGestao.jsx', import.meta.url), 'utf8')
  const tela = moduloPorId('financeiro').telas.find((t) => t.id === 'fechamentos-legados')
  assert.match(fonte, /import \{ AbaFechamentosLegados \} from '\.\/caixa\/AbaFechamentosLegados'/)
  assert.match(fonte, new RegExp(`'${tela.aba}': <AbaFechamentosLegados />`))
})

// ---- 4. A tela ----
const propsBase = { carregando: false, erro: null, itens: [], ignorados: 0, tentarDeNovo() {} }
const tela = (extra) => renderToStaticMarkup(h(CorpoDosFechamentosLegados, { ...propsBase, ...extra }))

await confere('carregando: mostra o esqueleto e não diz "nenhum fechamento" antes de a leitura chegar', () => {
  const html = tela({ carregando: true })
  assert.match(html, /Carregando os fechamentos/)
  assert.match(html, /aria-busy="true"/)
  assert.doesNotMatch(html, /Nenhum fechamento do aparelho/)
  assert.doesNotMatch(html, /<ul/)
})

await confere('erro: diz o motivo, oferece tentar de novo e não finge lista vazia', () => {
  const html = tela({ erro: 'Sem conexão com o EasyStok.' })
  assert.match(html, /role="alert"/)
  assert.match(html, /Sem conexão com o EasyStok\./)
  assert.match(html, /Tentar de novo/)
  assert.doesNotMatch(html, /Nenhum fechamento do aparelho/)
})

await confere('vazio: a frase de vazio, sem alerta nem aviso de ignorados', () => {
  const html = tela({})
  assert.match(html, /Nenhum fechamento do aparelho/)
  assert.doesNotMatch(html, /role="alert"|fora desta lista/)
})

await confere('lista: um item por fechamento, do dia mais novo ao mais antigo, com operador, observações e totais', () => {
  const html = tela({
    itens: lista([
      fch({ notes: 'Faltou troco no turno da tarde' }),
      fch({ id: 'fch-2', dateKey: '2026-03-03', closedAt: '2026-03-03T21:00:00Z', closedBy: 'Outro operador' }),
    ]),
  })
  assert.equal(html.match(/<li/g).length, 2)
  assert.ok(html.indexOf('3 de março de 2026') < html.indexOf('2 de março de 2026'))
  assert.match(html, /por Operadora de teste/)
  assert.match(html, /por Outro operador/)
  assert.match(html, /Observações:.*Faltou troco no turno da tarde/)
  assert.match(semEspaco(html), /R\$ 1\.234,50/)
  assert.match(html, /Não conferida/)
})

await confere('só consulta: a lista não traz botão, link, campo nem formulário que edite, apague ou reenvie', () => {
  const html = tela({ itens: lista([fch({ notes: 'Faltou troco no turno da tarde' })]) })
  assert.doesNotMatch(html, /<button|<a[ >]|<input|<select|<textarea|<form/i)
})

await confere('aviso de ignorados só aparece quando há ignorados, inclusive junto do vazio', () => {
  assert.doesNotMatch(tela({ itens: lista([fch()]) }), /fora desta lista/)
  assert.match(tela({ itens: lista([fch()]), ignorados: 2 }), /2 fechamentos do aparelho não puderam ser lidos e ficaram fora desta lista\./)
  const vazioComAviso = tela({ ignorados: 1 })
  assert.match(vazioComAviso, /1 fechamento do aparelho não pôde ser lido e ficou fora desta lista\./)
  assert.match(vazioComAviso, /Nenhum fechamento do aparelho/)
})

console.log(`\n${passou} ok, ${falhas.length} falharam`)
process.exit(falhas.length > 0 ? 1 : 0)
