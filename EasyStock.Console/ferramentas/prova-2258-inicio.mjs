// Prova da #2258: auditoria de UX/UI (Casa da Baba, 05/11), tela Início (Home) do Console.
//
// Cobre as regras puras (resumo = soma dos cartões, plural de "precisa(m) de você", ordem padrão dos
// módulos, "Tudo em dia" só com leitura, link do cartão com pendência) e as regras de composição
// (topo fixo, cartões da mesma linha com a mesma altura, uma só mensagem de busca vazia, dica de arrasto
// só no modo de ajuste, favoritos só quando existem). O desenho em si é conferido no navegador.
//
// Roda com: node ferramentas/prova-2258-inicio.mjs
// Contra outra árvore (ex.: o master, para ver o Red): PROVA_SRC=<pasta src> node ferramentas/prova-2258-inicio.mjs

import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'
import { transformWithOxc } from 'vite'

const base = process.env.PROVA_SRC ? pathToFileURL(process.env.PROVA_SRC.replace(/[\\/]?$/, '/')) : new URL('../src/', import.meta.url)
const raiz = fileURLToPath(base)
const jsx = new Map(await Promise.all(readdirSync(raiz, { recursive: true }).filter((n) => n.endsWith('.jsx')).map(async (nome) => {
  const arquivo = join(raiz, nome)
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env', '({ DEV: false })')
  return [pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code]
})))
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      for (const ext of ['.js', '.jsx']) { try { return proximo(especificador + ext, contexto) } catch { /* próximo formato */ } }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (jsx.has(url)) return { format: 'module', source: jsx.get(url), shortCircuit: true }
    if (url.endsWith('.css')) return { format: 'module', source: 'export default new Proxy({}, { get: (_, p) => String(p) })', shortCircuit: true }
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', source: 'export const FONTE_API=true;export const API_BASE="";', shortCircuit: true }
    if (url.endsWith('.json')) return { format: 'module', source: 'export default ' + readFileSync(new URL(url), 'utf8'), shortCircuit: true }
    return proximo(url, contexto)
  },
})

const fonte = (caminho) => readFileSync(new URL(caminho, base), 'utf8')
const tentar = async (caminho) => { try { return await import(new URL(caminho, base).href) } catch (erro) { return { ERRO_DE_IMPORT: erro.message } } }
const corpo = (seletor, texto) => {
  const escapado = seletor.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return [...texto.matchAll(new RegExp(`(?:^|\\n|\\})\\s*${escapado}\\s*\\{([^}]*)\\}`, 'g'))].map((m) => m[1]).join(' ')
}

const { createElement: h } = await import('react')
const { renderToStaticMarkup } = await import('react-dom/server')
const modulos = await tentar('dominio/modulos.js')
const estado = await tentar('dominio/estadoDoHall.js')
const pulso = await tentar('dominio/pulsoDoHall.js')
const { normalizarPreferenciasHall: normalizar } = await tentar('dominio/preferenciasHall.js')
const { ContextoAcessoModulos } = await tentar('aplicacao/acessoModulos.jsx')
const { HallDeModulos } = await tentar('features/hall/HallDeModulos.jsx')
const { PulsoDoHall } = await tentar('features/hall/PulsoDoHall.jsx')
const { PersonalizarHall } = await tentar('features/hall/PersonalizarHall.jsx')
const { IconeDoModulo } = await tentar('features/hall/IconeDoModulo.jsx')
globalThis.document ??= { documentElement: { dataset: { theme: 'light' } } }

const jsxHall = fonte('features/hall/HallDeModulos.jsx')
const jsxPulso = fonte('features/hall/PulsoDoHall.jsx')
const jsxPersonalizar = fonte('features/hall/PersonalizarHall.jsx')
const css = fonte('features/hall/hall.module.css')

let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok    ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA ${nome}\n      ${String(erro.message).split('\n')[0]}`) }
}

// Fixtures: 3 conversas esperando, 2 pedidos na fila, 1 pronto para sair.
const conversa = { situacao: 'Assumida', aguardaResposta: true }
const lido = (dados) => ({ estado: 'ok', dados, em: 1 })
const fontes = {
  atendimento: lido({ itens: [conversa, conversa, conversa, { situacao: 'Encerrada' }] }),
  pedidos: lido({ itens: [{ status: 'aguardando' }, { status: 'preparando' }, { status: 'pronto' }, { status: 'entregue' }] }),
}
const umaConversa = { atendimento: lido({ itens: [conversa] }) }

// #2258 (banca H1): só conversa pede ação; fila e prontos entram como "em andamento".
caso('H1: o resumo do dia é a soma dos cartões que pedem a dona, com o nome "precisam de você"', () => {
  assert.equal(estado.resumoDoDia(fontes).texto, 'Hoje · 3 precisam de você · 3 em andamento')
  const soma = pulso.indicadoresDoHall(fontes, ['atendimento', 'cozinha', 'entregas']).reduce((total, item) => total + item.valor, 0)
  assert.equal(estado.resumoDoDia(fontes).total, soma)
  assert.equal(estado.resumoDoDia(umaConversa, ['atendimento']).texto, 'Hoje · 1 precisa de você')
})

caso('H1: cartão que não está na tela não entra na soma; sem cartão ou sem leitura não há resumo', () => {
  assert.equal(estado.resumoDoDia(fontes, ['atendimento', 'cozinha']).texto, 'Hoje · 3 precisam de você · 2 em andamento')
  assert.equal(estado.resumoDoDia(fontes, ['financeiro']).texto, null)
  assert.equal(estado.resumoDoDia({}).texto, null, 'sem leitura não há resumo')
  assert.equal(estado.resumoDoDia({ ...fontes, pedidos: { estado: 'erro', dados: fontes.pedidos.dados } }).texto, null, 'leitura que falhou tira o resumo')
  assert.equal(estado.resumoDoDia({ atendimento: lido({ itens: [] }), pedidos: lido({ itens: [] }) }).texto, 'Hoje · nada precisa de você')
})

caso('H1: a tela passa ao resumo os ids dos cartões visíveis', () => {
  assert.match(jsxHall, /<ResumoDoDia fontes=\{fontes\} ids=\{idsDoPulso\} \/>/)
  assert.match(fonte('features/hall/ResumoDoDia.jsx'), /resumoDoDia\(fontes, ids\)/)
})

caso('H2/H3: o plural vale no cartão e na linha de baixo ("1 precisa", "3 precisam")', () => {
  assert.equal(pulso.pendenciaDoModulo('atendimento', fontes).texto, '3 conversas precisam de você')
  assert.equal(pulso.pendenciaDoModulo('atendimento', umaConversa).texto, '1 conversa precisa de você')
  const um = pulso.indicadoresDoHall({ pedidos: lido({ itens: [{ status: 'pronto' }] }) }, ['entregas'])[0]
  const varios = pulso.indicadoresDoHall(fontes, ['entregas'])[0]
  assert.equal(um.rotulo, 'Pronto para sair')
  assert.equal(pulso.indicadoresDoHall({ pedidos: lido({ itens: [{ status: 'pronto' }, { status: 'pronto' }] }) }, ['entregas'])[0].rotulo, 'Prontos para sair')
  assert.equal(varios.rotulo, 'Pronto para sair', '1 pronto no exemplo')
  assert.equal(pulso.indicadoresDoHall(fontes, ['atendimento'])[0].rotulo, 'Precisa de você')
})

caso('H2/H3: os cartões de número usam o rótulo do domínio e o ícone do mapa único dos módulos', () => {
  const html = renderToStaticMarkup(h(PulsoDoHall, { ids: ['atendimento', 'cozinha', 'entregas', 'financeiro'], foco: 'Cliente primeiro', fontes, aoMover: () => {} }))
  assert.match(html, /Precisa de você/)
  assert.doesNotMatch(html, /Atendimento pendente/)
  assert.match(html, /Pronto para sair/)
  for (const id of ['atendimento', 'cozinha', 'entregas', 'financeiro']) {
    const desenho = renderToStaticMarkup(h(IconeDoModulo, { modulo: { id }, tamanho: 20 }))
    assert.ok(html.includes(desenho), `o cartão de ${id} usa o mesmo ícone do módulo`)
  }
  assert.doesNotMatch(jsxPulso, /icone: '/)
})

caso('H4: com "Fechar caixa" à vista, o texto lido é o mesmo (sem "Conferir caixa" oculto)', () => {
  const caixa = { financeiro: lido({ aberto: true, saldoEsperado: 40, saldoInicial: 0, movimentos: [], esquecidoAberto: true }) }
  const html = renderToStaticMarkup(h(PulsoDoHall, { ids: ['financeiro'], foco: 'x', fontes: caixa, aoMover: () => {} }))
  assert.match(html, /Fechar caixa/)
  assert.doesNotMatch(html, /Conferir caixa/)
  const normal = { financeiro: lido({ aberto: true, saldoEsperado: 40, saldoInicial: 0, movimentos: [] }) }
  assert.match(renderToStaticMarkup(h(PulsoDoHall, { ids: ['financeiro'], foco: 'x', fontes: normal, aoMover: () => {} })), /Conferir caixa/)
})

caso('H5: cartões da mesma linha têm a mesma altura, sem esticar até o maior da grade', () => {
  assert.match(corpo('.indicadores', css), /align-items:\s*stretch/)
  assert.doesNotMatch(corpo('.indicadores', css), /align-items:\s*start/)
  assert.match(corpo('.indicadores > li', css), /display:\s*flex/)
  assert.match(corpo('.indicador', css), /flex:\s*1/)
  assert.doesNotMatch(corpo('.indicador', css), /height:\s*100%/)
})

caso('H7/A4: o atalho do rodapé usa o nome único "Respostas automáticas"', () => {
  assert.equal(modulos.NOME_RESPOSTAS_AUTOMATICAS, 'Respostas automáticas')
  const html = renderToStaticMarkup(h(ContextoAcessoModulos.Provider, { value: { permite: () => true, acoes: {}, aoSair: () => {} } },
    h(HallDeModulos, { fonteApi: true, sessao: null, agora: Date.parse('2026-11-04T18:00:00Z') })))
  assert.match(html, /Respostas automáticas/)
  assert.doesNotMatch(html, />Automáticas</)
})

caso('H8: a dica de arrastar só aparece no modo "Organizar áreas", nunca no rodapé do Início', () => {
  const rodape = jsxHall.slice(jsxHall.indexOf('className={css.rodapeHall}'), jsxHall.indexOf('</main>'))
  assert.ok(rodape.length > 50, 'rodapé encontrado')
  assert.doesNotMatch(rodape, /Arraste|dicaArrasto/)
  const organizacao = jsxHall.slice(jsxHall.indexOf('className={css.organizacao}'), jsxHall.indexOf('className={css.opcoesOrdem}'))
  assert.match(organizacao, /Arraste pelo botão Mover/)
  assert.match(organizacao, /também arrastam/)
})

caso('H10: o topo fica fixo ao rolar, com fundo opaco acima do conteúdo', () => {
  const topo = corpo('.topo', css)
  assert.match(topo, /position:\s*sticky/)
  assert.match(topo, /top:\s*0/)
  assert.match(topo, /z-index:\s*\d+/)
  assert.match(topo, /background:\s*var\(--papel\)/)
})

caso('A10: busca sem resultado tem um aviso e um botão só', () => {
  assert.equal(jsxHall.split('Nenhum módulo ou tela encontrado').length - 1, 1, 'o aviso aparece uma vez')
  assert.match(jsxHall, /destinos\.length === 0 \? ''/, 'o contador "0 telas encontradas" não é escrito')
  assert.match(jsxHall, /buscando && destinos\.length > 0 && <Botao[^>]*>[^<]*Limpar busca/, '"Limpar busca" só com resultado')
  assert.equal(jsxHall.split('Ver todos os módulos').length - 1, 1)
})

caso('C1/P1: o cartão com pendência leva ao ponto exato; sem ela, ao módulo', () => {
  const cardapio = { cardapio: lido({ itens: [{ hoje: true, pendencias: ['foto'] }, { hoje: true, pendencias: [] }] }) }
  const pendencia = pulso.pendenciaDoModulo('cardapio', cardapio)
  assert.equal(pendencia.texto, '1 prato com pendência')
  assert.equal(pendencia.href, '#/m/cardapio/itens/pendencia')
  assert.equal(pulso.pendenciaDoModulo('cardapio', { cardapio: lido({ itens: [{ hoje: true, pendencias: [] }] }) }).href, undefined)
  assert.match(jsxHall, /href=\{pendencia\?\.href \?\? modulo\.href\}/)
})

caso('P1: Produção é medida pelos lotes vencidos e nunca diz "Tudo em dia" sem ler', () => {
  const lotes = pulso.pendenciaDoModulo('producao', { producao: lido({ vencidos: [{}, {}] }) })
  assert.deepEqual([lotes.texto, lotes.atencao, lotes.href], ['2 lotes vencidos', true, '#/m/producao/estoque'])
  assert.equal(pulso.pendenciaDoModulo('producao', { producao: lido({ vencidos: [] }) }).texto, 'Nenhum lote vencido')
  for (const id of ['atendimento', 'cozinha', 'entregas', 'financeiro', 'cardapio', 'producao', 'configuracoes', 'campanhas']) {
    assert.equal(estado.estadoDoCartao(id, {}), null, `${id} sem leitura não afirma nada`)
  }
  assert.equal(estado.estadoDoCartao('producao', { producao: { estado: 'erro' } }), null)
  assert.doesNotMatch((fonte('dominio/estadoDoHall.js') + fonte('dominio/pulsoDoHall.js')).replace(/\/\/[^\n]*/g, ''), /Tudo em dia/)
  assert.match(fonte('aplicacao/usePulsoDoHall.js'), /producao && \['producao'/)
  assert.match(jsxHall, /producao: permitido\('producao'\)/)
})

caso('A2: a ordem padrão da home segue ORDEM_DOS_MODULOS; a ordem salva pela usuária continua valendo', () => {
  assert.deepEqual(modulos.ORDEM_DOS_MODULOS.slice(0, 8), ['atendimento', 'cozinha', 'entregas', 'cardapio', 'producao', 'financeiro', 'cadastros', 'configuracoes'])
  assert.deepEqual(estado.comOrdemPorUso(null).ordem, modulos.ORDEM_DOS_MODULOS)
  assert.deepEqual(estado.restaurarOrdemPorUso(null).ordem, modulos.ORDEM_DOS_MODULOS)
  const ids = modulos.modulosDoHall({ fonteApi: true }).map((modulo) => modulo.id)
  assert.deepEqual(ids, modulos.ORDEM_DOS_MODULOS.filter((id) => ids.includes(id)))
  const salva = { ordem: ['financeiro', ...modulos.ORDEM_DOS_MODULOS.filter((id) => id !== 'financeiro')] }
  assert.equal(estado.comOrdemPorUso(salva).ordem[0], 'financeiro')
  assert.deepEqual(estado.comOrdemPorUso({ foco: 'gestao' }).ordem.slice(0, 2), ['financeiro', 'atendimento'])
})

caso('J1: a lista de indicadores e a prévia "No painel" seguem a mesma ordem', () => {
  const todos = ['atendimento', 'cozinha', 'entregas', 'financeiro', 'cardapio'].map((id) => ({ id, nome: id.toUpperCase(), icone: 'caixa' }))
  const html = renderToStaticMarkup(h(PersonalizarHall, { preferencia: normalizar({ foco: 'gestao' }), modulos: todos, aoSalvar() {}, aoFechar() {}, aoOrganizar: null }))
  const previa = html.match(/No painel: ([^<]*)\./)[1].split(' → ')
  const lista = [...html.matchAll(/<span>(ATENDIMENTO|COZINHA|ENTREGAS|FINANCEIRO)<\/span>/g)].map((m) => m[1])
  assert.deepEqual(previa, lista)
  assert.equal(previa[0], 'FINANCEIRO', 'o modelo "Gestão da casa" começa pelo caixa')
})

caso('J2: "Seus favoritos são mantidos" só aparece com favoritos', () => {
  const todos = ['atendimento', 'cozinha'].map((id) => ({ id, nome: id, icone: 'caixa' }))
  const texto = (preferencia) => renderToStaticMarkup(h(PersonalizarHall, { preferencia: normalizar(preferencia), modulos: todos, aoSalvar() {}, aoFechar() {}, aoOrganizar: null }))
  assert.doesNotMatch(texto({ favoritos: [] }), /Seus favoritos são mantidos/)
  assert.match(texto({ favoritos: ['cozinha'] }), /Seus favoritos são mantidos/)
  assert.match(jsxPersonalizar, /favoritos\.length > 0 && ' Seus favoritos/)
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)${estado.ERRO_DE_IMPORT ? ` · import: ${estado.ERRO_DE_IMPORT.split('\n')[0]}` : ''}`)
if (falhas.length) process.exitCode = 1
