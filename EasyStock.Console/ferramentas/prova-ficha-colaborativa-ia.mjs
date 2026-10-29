// Componentes reais com hooks controlados; HTTP/provedor somente na fronteira de teste. Não certifica inferência real.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { transformWithOxc } from 'vite'

const arquivo = new URL('../src/features/ficha-cliente/AreaColaborativaIa.jsx', import.meta.url)
const compilado = (await transformWithOxc(readFileSync(arquivo, 'utf8'), arquivo.pathname, { jsx: { runtime: 'automatic' } })).code
const conversa = 'a1111111-1111-4111-8111-111111111111'
const outra = 'b2222222-2222-4222-8222-222222222222'
const geracao = 'c3333333-3333-4333-8333-333333333333'
const pendente = () => { let resolver, recusar; const promise = new Promise((s, n) => { resolver = s; recusar = n }); return { promise, resolver, recusar } }
const g = globalThis.__fichaIa = { identidade: 'empresa:atendente', chamadas: [], catalogo: { cardapio: [{ sku: 'lasanha', nome: 'Lasanha', preco: 9999 }] },
  estado: { fonteApi: true, selecionada: { id: conversa, nome: 'Ana', cliente: { endereco: 'Rua de teste, 10' },
    pedido: { pedidoId: 'pedido-real', numero: 412, totalApi: 87, itens: [{ sku: 'lasanha', qtd: 2, nome: 'Lasanha', precoUnitario: 38 }] } } } }
let ativo
const mesmas = (a, b) => a && b && a.length === b.length && a.every((v, i) => Object.is(v, b[i]))
g.hooks = {
  useState(inicial) { const h = ativo, i = h.i++; if (!(i in h.valores)) h.valores[i] = typeof inicial === 'function' ? inicial() : inicial;
    return [h.valores[i], (v) => { h.valores[i] = typeof v === 'function' ? v(h.valores[i]) : v; h.sujo = true }] },
  useRef(inicial) { return ativo.valores[ativo.i++] ??= { current: inicial } },
  useEffect(fn, deps) { const h = ativo, i = h.i++, antes = h.efeitos.get(i); if (!mesmas(antes?.deps, deps)) h.pendentes.push(() => { antes?.limpar?.(); h.efeitos.set(i, { deps, limpar: fn() }) }) },
}
const codigo = (texto) => 'data:text/javascript,' + encodeURIComponent(texto)
registerHooks({
  resolve(spec, ctx, proximo) {
    if (spec === 'react') return { url: codigo('const h=globalThis.__fichaIa.hooks; export const useState=h.useState,useRef=h.useRef,useEffect=h.useEffect,useLayoutEffect=h.useEffect'), shortCircuit: true }
    if (spec === 'react/jsx-runtime') return { url: codigo('export const Fragment="fragment"; export const jsx=(type,props,key)=>({type,props,key});export const jsxs=jsx'), shortCircuit: true }
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) for (const ext of ['.js', '.jsx']) { try { return proximo(spec + ext, ctx) } catch { /* próximo formato */ } }
    return proximo(spec, ctx)
  },
  load(url, ctx, proximo) {
    const inline = (source) => ({ format: 'module', source, shortCircuit: true })
    if (url === arquivo.href) return inline(compilado)
    if (url.endsWith('/aplicacao/contextos.js')) return inline('const g=globalThis.__fichaIa;export const useAtendimento=()=>g.estado,useCatalogo=()=>g.catalogo')
    if (url.endsWith('/infra/api/cliente.js')) return inline('export const chamarApi=(rota,opcoes)=>{const g=globalThis.__fichaIa;g.chamadas.push({rota,opcoes});return g.proxima.promise}')
    if (url.endsWith('/infra/api/sessao.js')) return inline('export const lerSessao=()=>globalThis.__fichaIa.identidade,identidadeDaSessao=s=>s')
    if (url.endsWith('.css')) return inline('export default new Proxy({},{get:(_,p)=>String(p)})')
    if (url.includes('/componentes/') && url.endsWith('.jsx')) {
      const nomes = [...readFileSync(new URL(url), 'utf8').matchAll(/export (?:function|const) (\w+)/g)].map(m => m[1])
      return inline(nomes.map(n => `export const ${n}=${JSON.stringify(n)}`).join(';'))
    }
    return proximo(url, ctx)
  },
})
const { AreaColaborativaIa, BlocoColaborativoIa } = await import(arquivo.href)
const { validarMontagemFicha, comandaDaFichaIa } = await import('../src/dominio/montagemFicha.js')
const { organizarFicha } = await import('../src/aplicacao/montagemFicha.js')
const resposta = () => ({ conversaId: conversa, geracaoId: geracao, geradoEm: '2026-10-29T15:20:00Z', blocos: [
  { id: 'nota-1', tipo: 'nota', texto: 'Prefere retirar no sábado.', acao: null },
  { id: 'comanda-1', tipo: 'comanda', texto: 'Confira os itens atuais.', acao: null },
  { id: 'frete-1', tipo: 'frete', texto: 'Confira a entrega antes de confirmar.', acao: null },
  { id: 'acao-1', tipo: 'acao', acao: 'rascunho', texto: 'Qual horário você prefere?' },
] })
function host(componente, props) {
  const h = { valores: [], efeitos: new Map(), props, arvore: null,
    render(novas = h.props) { h.props = novas; let voltas = 0; do { assert.ok(++voltas < 12); h.sujo = false; h.i = 0; h.pendentes = []; ativo = h;
      h.arvore = componente(h.props); for (const efeito of h.pendentes) efeito() } while (h.sujo); return h.arvore },
    desmontar() { for (const e of h.efeitos.values()) e.limpar?.() },
  }; h.render(); return h
}
function elementos(no) { return Array.isArray(no) ? no.flatMap(elementos) : !no?.props ? [] : [no, ...elementos(no.props.children)] }
const doTipo = (h, tipo) => elementos(h.arvore).filter(e => e.type === tipo)
const texto = no => Array.isArray(no) ? no.map(texto).join('') : no?.props ? texto(no.props.children) : no == null ? '' : String(no)
const botao = (h, rotulo) => doTipo(h, 'Botao').find(b => texto(b) === rotulo)
const enviar = h => doTipo(h, 'form')[0].props.onSubmit({ preventDefault() {} })
let grupos = 0
const provar = async (nome, fn) => { await fn(); grupos++; console.log('ok ' + nome) }

await provar('montagem só acontece no clique explícito; espera, evita duplicação e preserva a ordem do modelo', async () => {
  g.chamadas = []; g.proxima = pendente()
  const acoes = []
  const h = host(AreaColaborativaIa, { conversaId: conversa, aoProporNota: t => acoes.push(t) })
  h.render(); g.estado = { ...g.estado, eventoSse: 1 }; h.render()
  assert.equal(g.chamadas.length, 0, 'render e SSE não gastam inferência')
  doTipo(h, 'CampoTexto')[0].props.onChange({ target: { value: '  Prepare a retirada.  ' } }); h.render()
  const emCurso = enviar(h)
  await enviar(h)
  assert.equal(g.chamadas.length, 1)
  assert.equal(g.chamadas[0].rota, '/api/atendimento/assistente/ficha')
  assert.equal(g.chamadas[0].opcoes.metodo, 'POST')
  assert.deepEqual(g.chamadas[0].opcoes.corpo, { conversaId: conversa, orientacao: 'Prepare a retirada.' })
  assert.equal(g.chamadas[0].opcoes.identidadeEsperada, 'empresa:atendente')
  h.render(); assert.equal(botao(h, 'Organizando…').props.disabled, true)
  g.proxima.resolver(resposta()); await emCurso; h.render()
  assert.deepEqual(doTipo(h, BlocoColaborativoIa).map(b => b.props.bloco.tipo), ['nota', 'comanda', 'frete', 'acao'])
  assert.equal(acoes.length, 0, 'gerar blocos não salva nota nem envia mensagem')
  assert.match(texto(h.arvore), /Proposta das 12h20|Proposta das 12:20/)
  h.render({ ...h.props, blocosSoltos: [`${geracao}:nota-1`] })
  assert.deepEqual(doTipo(h, BlocoColaborativoIa).map(b => b.props.bloco.tipo), ['comanda', 'frete', 'acao'])
  assert.equal(botao(h, 'Organizar com IA').props['data-organizar-ficha-ia'], conversa, 'retorno de foco permanece disponível após retirar o bloco da área')
  assert.equal(botao(h, 'Organizar com IA').props.disabled, false)
  h.desmontar()
})

await provar('comanda mostra itens e total da API, sem recalcular pelo preço atual do catálogo', () => {
  const bloco = { ...resposta().blocos[1], conversaId: conversa, geracaoId: geracao, identidadeSessao: g.identidade }
  const h = host(BlocoColaborativoIa, { bloco, aoAbrirComanda() {} })
  assert.match(texto(h.arvore), /2 × Lasanha/)
  assert.match(texto(h.arvore), /87,00/)
  assert.doesNotMatch(texto(h.arvore), /19\.998,00/)
  const semValor = { ...g.estado.selecionada, pedido: { ...g.estado.selecionada.pedido, totalApi: undefined } }
  assert.equal(comandaDaFichaIa(semValor, g.catalogo.cardapio).total, null, 'pedido confirmado sem total da API não inventa valor')
  h.desmontar()
})

await provar('nota só passa ao callback por clique; bloqueia repetição e sessão/conversa antiga', async () => {
  const nota = { ...resposta().blocos[0], conversaId: conversa, geracaoId: geracao, identidadeSessao: g.identidade }
  const final = pendente(), chamadas = []
  const h = host(BlocoColaborativoIa, { bloco: nota, aoProporNota: t => { chamadas.push(t); return final.promise } })
  assert.equal(chamadas.length, 0)
  const confirmar = botao(h, 'Revisar nota').props.onClick()
  await botao(h, 'Revisar nota').props.onClick()
  assert.deepEqual(chamadas, ['Prefere retirar no sábado.'])
  final.resolver(true); await confirmar; h.render()
  const cliqueAntigo = botao(h, 'Revisar nota').props.onClick
  g.estado = { ...g.estado, selecionada: { ...g.estado.selecionada, estado: 'Encerrado' } }; h.render()
  assert.equal(h.arvore, null)
  await cliqueAntigo(); assert.equal(chamadas.length, 1, 'concluir a mesma conversa também invalida o botão antigo')
  g.estado = { ...g.estado, selecionada: { ...g.estado.selecionada, estado: 'Em atendimento' } }; h.render()
  g.estado = { ...g.estado, selecionada: { ...g.estado.selecionada, id: outra } }; h.render()
  assert.equal(h.arvore, null)
  await cliqueAntigo(); assert.equal(chamadas.length, 1)
  g.estado = { ...g.estado, selecionada: { ...g.estado.selecionada, id: conversa } }; g.identidade = 'empresa:outra'
  await cliqueAntigo(); assert.equal(chamadas.length, 1)
  g.identidade = 'empresa:atendente'; h.desmontar()
})

await provar('resposta atrasada é abortada e não aparece na conversa seguinte; falha mantém orientação para tentar de novo', async () => {
  g.proxima = pendente()
  const h = host(AreaColaborativaIa, { conversaId: conversa })
  const emCurso = enviar(h), sinal = g.chamadas.at(-1).opcoes.sinal
  g.estado = { ...g.estado, selecionada: { ...g.estado.selecionada, id: outra } }; h.render({ conversaId: outra })
  assert.equal(sinal.aborted, true)
  g.proxima.resolver(resposta()); await emCurso; h.render()
  assert.equal(doTipo(h, BlocoColaborativoIa).length, 0)
  g.proxima = pendente(); doTipo(h, 'CampoTexto')[0].props.onChange({ target: { value: 'Conferir entrega' } }); h.render()
  const falha = enviar(h); g.proxima.recusar(new Error('IA indisponível nesta loja.')); await falha; h.render()
  assert.match(texto(h.arvore), /IA indisponível/)
  assert.equal(doTipo(h, 'CampoTexto')[0].props.value, 'Conferir entrega')
  assert.equal(botao(h, 'Organizar com IA').props.disabled, false)
  h.desmontar(); g.estado = { ...g.estado, selecionada: { ...g.estado.selecionada, id: conversa } }
})

await provar('fronteira rejeita dados financeiros/modelos/ações/código fora do contrato e troca de identidade', async () => {
  for (const alterar of [r => { r.conversaId = outra }, r => { r.blocos[0].total = 1 }, r => { r.blocos[0].tipo = 'html' },
    r => { r.blocos[0].texto = '<script>alert(1)</script>' }, r => { r.blocos[3].acao = 'enviar_mensagem' },
    r => { r.blocos[0].texto = 'n'.repeat(501) }, r => { r.blocos = [] }, r => { r.blocos.push(...r.blocos) }]) {
    const r = resposta(); alterar(r); assert.throws(() => validarMontagemFicha(r, conversa), /organização válida/)
  }
  const antes = g.chamadas.length
  await assert.rejects(organizarFicha('../fora', ''), /Conversa inválida/)
  await assert.rejects(organizarFicha(conversa, 'x'.repeat(2001)), /2000/)
  assert.equal(g.chamadas.length, antes)
  g.proxima = pendente(); const emCurso = organizarFicha(conversa, ''); g.identidade = 'empresa:outra'
  g.proxima.resolver(resposta()); await assert.rejects(emCurso, /sessão mudou/)
  g.identidade = 'empresa:atendente'
})

await provar('comanda, frete, cardápio, rascunho e soltar usam apenas callbacks explícitos da conversa atual', async () => {
  const chamadas = [], antes = g.chamadas.length
  for (const [bloco, rotulo, nome, args] of [
    [resposta().blocos[1], 'Ver comanda', 'comanda', []],
    [resposta().blocos[2], 'Consultar frete', 'frete', []],
    [{ id: 'acao-2', tipo: 'acao', acao: 'abrir_cardapio', texto: 'Confira os pratos.' }, 'Abrir cardápio', 'cardapio', []],
    [resposta().blocos[3], 'Usar no rascunho', 'rascunho', ['Qual horário você prefere?']],
  ]) {
    const payload = { ...bloco, conversaId: conversa, geracaoId: geracao, identidadeSessao: g.identidade }
    const h = host(BlocoColaborativoIa, { bloco: payload,
      aoAbrirComanda: (...a) => chamadas.push(['comanda', a]), aoCalcularFrete: (...a) => chamadas.push(['frete', a]),
      aoAbrirCardapio: (...a) => chamadas.push(['cardapio', a]), aoRascunho: (...a) => chamadas.push(['rascunho', a]),
      aoSoltar: b => chamadas.push(['soltar', b]) })
    const anteriores = chamadas.length
    if (nome === 'frete') assert.match(texto(h.arvore), /Rua de teste, 10/)
    await botao(h, rotulo).props.onClick()
    assert.equal(chamadas.length, anteriores + 1)
    assert.deepEqual(chamadas.at(-1), [nome, args])
    h.render()
    await doTipo(h, 'BotaoIcone')[0].props.onClick()
    assert.deepEqual(chamadas.at(-1), ['soltar', payload], 'soltar conserva geração, conversa e identidade, sem criar uma ação automática')
    h.desmontar()
  }
  assert.equal(g.chamadas.length, antes, 'ações de bloco não chamam a API de inferência nem uma rota de envio')
})

console.log(`${grupos} grupos da ficha colaborativa aprovados; inferência do provedor real continua fora deste teste.`)
