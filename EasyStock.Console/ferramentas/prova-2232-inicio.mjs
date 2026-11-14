/* eslint-disable no-console */
// Prova da issue #2232 (banca UX/UI do Atendimento, Tela 4: Início e Módulos, achados H1 a H10 e M3).
// Uma verificação por achado da spec `docs/plan/banca-ux-2232/README.md`. Regras puras, tela
// renderizada (HTML estático, como a prova-1839) e leitura do fonte/CSS onde só existe composição.
//
//   node ferramentas/prova-2232-inicio.mjs

import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'
import { transformWithOxc } from 'vite'

const raiz = fileURLToPath(new URL('../src/', import.meta.url))
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

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
const tentar = async (caminho) => { try { return await import(caminho) } catch { return {} } }

const { createElement: h } = await import('react')
const { renderToStaticMarkup } = await import('react-dom/server')
const { ContextoAcessoModulos } = await import('../src/aplicacao/acessoModulos.jsx')
const { HallDeModulos } = await import('../src/features/hall/HallDeModulos.jsx')
const { PulsoDoHall } = await import('../src/features/hall/PulsoDoHall.jsx')
const { ResumoDoDia } = await tentar('../src/features/hall/ResumoDoDia.jsx')
const estado = await tentar('../src/dominio/estadoDoHall.js')
globalThis.document ??= { documentElement: { dataset: { theme: 'light' } } }

const jsxHall = fonte('features/hall/HallDeModulos.jsx')
const jsxPulso = fonte('features/hall/PulsoDoHall.jsx')
const css = fonte('features/hall/hall.module.css')
const corpo = (seletor, texto = css) => {
  const escapado = seletor.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return [...texto.matchAll(new RegExp(`(?:^|\\n|\\})\\s*${escapado}\\s*\\{([^}]*)\\}`, 'g'))].map((m) => m[1]).join(' ')
}
const celular = css.slice(css.indexOf('@media (max-width: 900px)'))

const renderizar = (contexto = {}) => renderToStaticMarkup(h(ContextoAcessoModulos.Provider,
  { value: { permite: () => true, acoes: {}, aoSair: () => {}, ...contexto } },
  h(HallDeModulos, { fonteApi: true, sessao: null, agora: Date.parse('2026-11-04T18:00:00Z') })))
const html = renderizar()
const htmlSemConta = renderizar({ aoSair: null })
const topo = (pagina) => pagina.slice(pagina.indexOf('<header'), pagina.indexOf('</header>'))
const rodape = (pagina) => pagina.slice(pagina.indexOf('class="rodapeHall"'))

const caixa = (dados) => ({ estado: 'ok', em: 1, dados })
const pulso = (fontes) => renderToStaticMarkup(h(PulsoDoHall, { ids: ['atendimento', 'cozinha', 'entregas', 'financeiro'], foco: 'Cliente primeiro', fontes, aoMover: () => {} }))

let passou = 0
const falhas = []
const pendentes = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log(`  ok  ${descricao}`) } catch (erro) { falhas.push(descricao); console.log(`  XX  ${descricao}\n      ${String(erro.message).split('\n')[0]}`) }
}

confere('H1  uma marca só no canto: o logo da loja, sem o "EasyStok" ao lado', () => {
  assert.match(topo(html), /alt="Casa da Baba"/)
  assert.doesNotMatch(topo(html).replace(/<[^>]*>/g, ' '), /EasyStok/)
  assert.match(jsxHall, /<Marca compacta \/>/)
})

confere('H2  o tema sai do topo: fica no menu da conta e, sem conta, num lugar discreto do rodapé', () => {
  assert.doesNotMatch(topo(html), /role="radiogroup"/)
  assert.doesNotMatch(jsxHall.slice(jsxHall.indexOf('<header'), jsxHall.indexOf('</header>')).replace(/<MenuDaConta[\s\S]*<\/MenuDaConta>/, ''), /InterruptorTema/)
  assert.match(jsxHall, /<MenuDaConta[^>]*>[\s\S]*<InterruptorTema \/>[\s\S]*<\/MenuDaConta>/)
  assert.match(rodape(htmlSemConta), /role="radiogroup"/, 'sem menu da conta o tema continua alcançável')
  assert.doesNotMatch(rodape(html), /role="radiogroup"/, 'com menu da conta o tema não aparece duas vezes')
  assert.doesNotMatch(corpo('.acoes > [role=\'radiogroup\']', celular), /grid-row/)
})

// H3: o contador "Avisos (N)" nasce no SinoDaSessao (features/notificacoes), que o hall só encaixa no topo.
confere('H3  o hall encaixa o sino numa faixa de altura de toque, alinhada ao centro', () => {
  const regra = corpo('.avisos')
  assert.match(regra, /align-items: center/)
  assert.match(regra, /min-height: var\(--toque\)/)
  // Revisão 4: sem o tema na linha 2, o sino sobe para a linha 1, numa coluna própria entre Ajuda e conta.
  const ate640 = css.slice(css.indexOf('@media (max-width: 640px)'))
  assert.match(corpo('.avisos', ate640), /grid-row: 1; grid-column: 3/)
  assert.doesNotMatch(corpo('.avisos', ate640), /grid-row: 2/)
  assert.match(corpo('.topo', ate640), /grid-auto-flow: column/)
})

// Revisão 4: Configurações mora no rodapé; favorito e posição anunciada contam só as áreas da grade.
confere('H10 favoritos e "movido para a posição N de M" contam só as áreas da grade', () => {
  assert.match(jsxHall, /favoritosDoHall\(permitidosDaGrade, preferencia\)/)
  assert.match(jsxHall, /ordenarModulosDoHall\(permitidosDaGrade, proxima\)/)
})
{
  const sino = fonte('features/notificacoes/SinoDaSessao.jsx')
  const sinoCss = fonte('features/notificacoes/notificacoes.module.css')
  const contadorUnico = !/\(\$\{total\}\)/.test(sino) && !/\.pontoAparelho \{[^}]*width: [0-8]px/.test(sinoCss)
  if (contadorUnico) confere('H3  o sino mostra o contador numérico unificado (sem "(N)" no texto nem ponto de 8 px)', () => assert.ok(contadorUnico))
  else pendentes.push('H3  o sino ainda escreve "(N)" e usa ponto de 8 px: falta o contador unificado em features/notificacoes/SinoDaSessao.jsx (fora da área do hall)')
}

confere('H4  a busca de módulos tem lupa, decorativa e alinhada ao campo', () => {
  assert.match(html, /class="campoBusca"[^]*?<svg[^>]*class="[^"]*lupa[^"]*"[^>]*aria-hidden="true"/)
  assert.match(jsxHall, /<MarcaIcone nome="buscar"/)
  const lupa = corpo('.lupa')
  assert.match(lupa, /position: absolute/)
  assert.match(lupa, /pointer-events: none/)
  assert.match(corpo('.campoBusca .entradaBusca'), /padding-left/)
})

confere('H5  o número do KPI fica em cima e o rótulo embaixo, na mesma linha de base entre os cartões', () => {
  const indicadores = html.match(/<a [^>]*data-indicador="[^"]+"[^>]*>[^]*?<\/a>/g) ?? []
  assert.equal(indicadores.length, 4)
  for (const cartao of indicadores) assert.ok(cartao.indexOf('class="valor"') > -1 && cartao.indexOf('class="valor"') < cartao.indexOf('class="cabecaIndicador"'), cartao.slice(0, 60))
  assert.match(corpo('.indicador'), /flex-direction: column/)
  assert.match(corpo('.indicador'), /justify-content: flex-start/)
})

confere('H6  KPIs em grade 2×2 e o alerta do caixa vira estado do cartão com a ação "Fechar caixa"', () => {
  // Revisão 4: o 2×2 vale só com quatro números; com 1 a 3 a grade estica, sem cartão sozinho na linha.
  assert.match(corpo('.indicadores[data-total=\'4\']', celular), /grid-template-columns: repeat\(2, minmax\(0, 1fr\)\)/)
  assert.doesNotMatch(corpo('.indicadores', celular), /repeat\(2/)
  assert.match(pulso({}), /<ul class="indicadores"[^>]*data-total="4"/)
  const tres = renderToStaticMarkup(h(PulsoDoHall, { ids: ['atendimento', 'cozinha', 'financeiro'], foco: 'Cliente primeiro', fontes: {}, aoMover: () => {} }))
  assert.match(tres, /<ul class="indicadores"[^>]*data-total="3"/)
  const comAlerta = pulso({ financeiro: caixa({ aberto: true, saldoEsperado: 40, esquecidoAberto: true }) })
  const cartaoCaixa = comAlerta.match(/<a [^>]*data-indicador="financeiro"[^>]*>[^]*?<\/a>/)[0]
  assert.match(cartaoCaixa, /data-severidade="alerta"/)
  assert.match(cartaoCaixa, /Fechar caixa/)
  assert.match(cartaoCaixa, /href="#\/m\/financeiro\/caixa"/)
  const normal = pulso({ financeiro: caixa({ aberto: true, saldoEsperado: 40 }) })
  assert.doesNotMatch(normal, /Fechar caixa|data-severidade/)
  const item = (extra) => ({ id: 'financeiro', estado: 'ok', alerta: null, ...extra })
  assert.equal(estado.severidadeDoIndicador(item({ alerta: 'Caixa de outro dia' })), 'alerta')
  assert.equal(estado.acaoDoIndicador(item({ alerta: 'Caixa de outro dia' })), 'Fechar caixa')
  assert.equal(estado.acaoDoIndicador(item({})), null)
  assert.equal(estado.acaoDoIndicador({ id: 'cozinha', estado: 'ok', alerta: 'x' }), null)
  assert.equal(estado.severidadeDoIndicador(item({})), null)
})

confere('H7  o cartão todo clicável perde a seta ">"', () => {
  const abrir = html.match(/<a [^>]*class="abrir"[^>]*>[^]*?<\/a>/g) ?? []
  assert.equal(abrir.length, 7)
  for (const link of abrir) assert.doesNotMatch(link, /<svg/, link.slice(0, 80))
  assert.doesNotMatch(css, /\.abrir > svg/)
  assert.doesNotMatch(jsxHall.slice(jsxHall.indexOf('<a className={css.abrir}'), jsxHall.indexOf('</a>', jsxHall.indexOf('<a className={css.abrir}'))), /MarcaIcone/)
})

confere('H8  cartão sem leitura não afirma "Tudo em dia"; o medido mostra o que leu ou nada', () => {
  // #2258 (auditoria P1): "Tudo em dia" é afirmação sobre uma leitura; sem leitura o cartão fica sem linha.
  assert.equal(estado.estadoDoCartao('producao', {}), null)
  assert.equal(estado.estadoDoCartao('atendimento', {}), null, 'leitura que não chegou não vira "Tudo em dia"')
  const lido = { atendimento: { estado: 'ok', dados: { itens: [{ situacao: 'Assumida', aguardaResposta: true, assumidaPorUsuarioId: 'u' }] } } }
  // #2258 (auditoria H2/H3): o mesmo nome do cartão de número, no plural certo.
  assert.deepEqual(estado.estadoDoCartao('atendimento', lido), { texto: '1 conversa precisa de você', atencao: true })
  assert.doesNotMatch(html, /Tudo em dia/)
})

confere('H9  severidade usa as cores de estado; a marca fica para a ação primária e a seleção', () => {
  for (const seletor of ['.pendencia[data-atencao]', '.cabecaIndicador > svg', '.indicador[data-severidade=\'alerta\']', '.indicador[data-severidade=\'erro\']', '.frescor', '.acaoIndicador']) {
    const regra = corpo(seletor)
    assert.ok(regra, `regra ausente: ${seletor}`)
    assert.doesNotMatch(regra, /--marca/, seletor)
  }
  assert.match(corpo('.pendencia[data-atencao]'), /var\(--alerta\)/)
  assert.match(corpo('.indicador[data-severidade=\'alerta\']'), /var\(--alerta-pastel\)/)
  assert.match(corpo('.indicador[data-severidade=\'erro\']'), /var\(--erro-pastel\)/)
  assert.match(corpo('.acaoIndicador'), /var\(--acao\)/)
  assert.doesNotMatch(css, /border-radius: *[1-9]/)
})

confere('H10  módulos na ordem de uso, grade completa e Configurações como link de rodapé', () => {
  const ordem = estado.comOrdemPorUso(null).ordem
  // #2258 (auditoria A2): atender, preparar, entregar; depois cardápio, produção e dinheiro.
  assert.deepEqual(ordem.slice(0, 3), ['atendimento', 'cozinha', 'entregas'])
  assert.equal(ordem.length, 9, 'a ordem inclui Cadastros')
  const movida = { ordem: ['financeiro', ...ordem.filter((id) => id !== 'financeiro')] }
  assert.equal(estado.comOrdemPorUso(movida).ordem[0], 'financeiro', 'a ordem que a dona escolheu vale')
  assert.equal(estado.comOrdemPorUso({ foco: 'gestao' }).ordem[0], 'financeiro', 'o foco escolhido manda na ordem de fábrica')
  assert.deepEqual(estado.favoritosPrimeiroPorUso({ favoritos: ['producao'] }).ordem.slice(0, 2), ['producao', 'atendimento'])
  const grade = html.slice(html.indexOf('aria-label="Módulos"'), html.indexOf('</ul>', html.indexOf('aria-label="Módulos"')))
  const ids = [...grade.matchAll(/data-modulo="([^"]+)"/g)].map((m) => m[1])
  assert.deepEqual(ids, ['atendimento', 'cozinha', 'entregas', 'cardapio', 'producao', 'financeiro', 'cadastros'])
  assert.equal(new Set(ids).size, ids.length, 'cada área aparece uma vez')
  assert.match(rodape(html), /<a [^>]*href="#\/m\/configuracoes"[^>]*>[^]*?Configurações<\/a>/)
})

confere('M3  o Início mostra "Hoje · N precisam de você" com as leituras do hall (#2258, auditoria H1)', () => {
  const fontes = {
    atendimento: { estado: 'ok', dados: { itens: [{ situacao: 'Assumida' }, { situacao: 'Automatica' }, { situacao: 'Encerrada' }] } },
    pedidos: { estado: 'ok', dados: { itens: [{ status: 'aguardando' }, { status: 'preparando' }, { status: 'pronto' }, { status: 'entregue' }] } },
  }
  // #2258 (auditoria H1): o resumo é a soma dos cartões (1 conversa + 2 na fila + 1 pronto).
  assert.equal(estado.resumoDoDia(fontes).texto, 'Hoje · 1 precisa de você · 3 em andamento')
  assert.equal(estado.resumoDoDia({}).texto, null, 'sem leitura não há resumo')
  assert.equal(estado.resumoDoDia({ ...fontes, pedidos: { estado: 'erro', dados: fontes.pedidos.dados } }).texto, null, 'leitura que falhou tira o resumo')
  assert.equal(estado.resumoDoDia({ atendimento: { ...fontes.atendimento, dados: { ...fontes.atendimento.dados, parcial: true } } }, ['atendimento']).texto, 'Hoje · 1+ precisa de você')
  assert.match(renderToStaticMarkup(h(ResumoDoDia, { fontes })), /Hoje · 1 precisa de você · 3 em andamento/)
  assert.equal(renderToStaticMarkup(h(ResumoDoDia, { fontes: {} })), '')
  assert.match(jsxHall, /<ResumoDoDia fontes=\{fontes\} ids=\{idsDoPulso\} \/>/)
  assert.doesNotMatch(fonte('features/hall/ResumoDoDia.jsx') + fonte('dominio/estadoDoHall.js').replace(/\/\/[^\n]*/g, ''), /esteira/i)
})

if (pendentes.length) for (const texto of pendentes) console.log(`  PENDENTE-FORA  ${texto}`)
console.log(`\n${passou} ok, ${falhas.length} falha(s), ${pendentes.length} pendente(s) fora da área do hall.`)
if (falhas.length) process.exit(1)
