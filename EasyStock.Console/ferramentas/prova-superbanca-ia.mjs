// Hooks e adaptadores reais, com respostas HTTP controladas. SSR prova os estados de
// interface; foco, teclado e dimensões devem ser conferidos no navegador.
import assert from 'node:assert/strict'
import { readFileSync, readdirSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { join } from 'node:path'
import { transformWithOxc } from 'vite'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'

let ativo
const depsIguais = (a, b) => a && b && a.length === b.length && a.every((v, i) => Object.is(v, b[i]))
const hooks = {
  useState(inicial) {
    const host = ativo, i = host.indice++
    if (!(i in host.valores)) host.valores[i] = typeof inicial === 'function' ? inicial() : inicial
    return [host.valores[i], valor => {
      const novo = typeof valor === 'function' ? valor(host.valores[i]) : valor
      if (!Object.is(novo, host.valores[i])) { host.valores[i] = novo; host.sujo = true }
    }]
  },
  useRef(inicial) {
    const host = ativo, i = host.indice++
    return host.valores[i] ??= { current: inicial }
  },
  useEffect(fn, deps) {
    const host = ativo, i = host.indice++, anterior = host.efeitos.get(i)
    if (!anterior || !depsIguais(anterior.deps, deps)) {
      host.pendentes.push(() => { anterior?.limpar?.(); host.efeitos.set(i, { deps, limpar: fn() }) })
    }
  },
}
class Hospedeiro {
  constructor(hook, props) {
    this.hook = hook; this.props = props; this.valores = []; this.efeitos = new Map()
    this.renderizar()
  }
  renderizar(props = this.props) {
    this.props = props
    let voltas = 0
    do {
      assert.ok(voltas++ < 20, 'renderização deve estabilizar')
      this.sujo = false; this.indice = 0; this.pendentes = []; ativo = this
      this.resultado = this.hook(this.props); ativo = null
      this.pendentes.forEach(fn => fn())
    } while (this.sujo)
    return this.resultado
  }
  desmontar() { for (const e of this.efeitos.values()) e.limpar?.() }
}
const jsx = new Map()
const raiz = fileURLToPath(new URL('../src/', import.meta.url))
function arquivos(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap(e => e.isDirectory() ? arquivos(join(dir, e.name)) : [join(dir, e.name)])
}
for (const arquivo of [
  ...arquivos(join(raiz, 'componentes')).filter(a => a.endsWith('.jsx')),
  join(raiz, 'features/agente/PainelAgente.jsx'),
  join(raiz, 'features/assistente/AcoesPropostas.jsx'),
  join(raiz, 'features/assistente/Cartoes.jsx'),
]) {
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env.DEV', 'false')
  jsx.set(pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code)
}
globalThis.__superbancaIA = hooks
const modulo = source => 'data:text/javascript,' + encodeURIComponent(source)
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec === 'react' && contexto.parentURL?.endsWith('/features/assistente/useConversaAssistente.js')) {
      return { url: modulo('const h=globalThis.__superbancaIA;export const useState=h.useState,useRef=h.useRef,useLayoutEffect=h.useEffect;'), shortCircuit: true }
    }
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      for (const ext of ['.js', '.jsx']) { try { return proximo(spec + ext, contexto) } catch { /* próximo formato */ } }
    }
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (jsx.has(url)) return { format: 'module', source: jsx.get(url), shortCircuit: true }
    if (url.endsWith('.css')) return { format: 'module', source: "export default new Proxy({}, {get: (_, p) => String(p)})", shortCircuit: true }
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', source: "export const FONTE_API=true;export const API_BASE='';", shortCircuit: true }
    if (url.endsWith('.json')) return { format: 'module', source: 'export default ' + readFileSync(new URL(url), 'utf8'), shortCircuit: true }
    return proximo(url, contexto)
  },
})
const { useConversaAssistente } = await import('../src/features/assistente/useConversaAssistente.js')
const { criarAcoesAgenteApi } = await import('../src/aplicacao/api/agente.js')
const { PainelAgente } = await import('../src/features/agente/PainelAgente.jsx')
const { AcoesPropostas } = await import('../src/features/assistente/AcoesPropostas.jsx')
const { focarResultadoDaAcao } = await import('../src/features/assistente/focoDaAcao.js')
const { CartaoFotoSuspeita, CartaoPadraoDeCompra } = await import('../src/features/assistente/Cartoes.jsx')
const { respostaDoAssistente, executarAcaoDoAssistente } = await import('../src/dominio/acoesDoAssistente.js')
const provas = []
async function provar(nome, executar) { await executar(); provas.push(nome); console.log('ok ' + nome) }
const pendente = () => {
  let resolver, recusar
  const promise = new Promise((res, rej) => { resolver = res; recusar = rej })
  return { promise, resolver, recusar }
}
const conversa = { id: 'a', nome: 'Ana fixture', clienteId: 'cliente-a', mensagens: [] }
const props = (extra = {}) => ({ conversa, historico: [], perguntar: async () => 'Resposta fixture', executarAcao: async () => true, ...extra })
const perguntarNo = (h, texto) => { h.resultado.setPergunta(texto); h.renderizar(); return h.resultado.aoPerguntar() }

await provar('Perguntar duas vezes enquanto pendente faz uma única consulta', async () => {
  const espera = pendente(); let total = 0
  const h = new Hospedeiro(useConversaAssistente, props({ perguntar: () => { total++; return espera.promise } }))
  const primeira = perguntarNo(h, 'Como responder?')
  const segunda = h.resultado.aoPerguntar()
  assert.equal(await segunda, false); assert.equal(total, 1)
  assert.equal(h.renderizar().perguntando, true)
  espera.resolver('Uma resposta'); assert.equal(await primeira, true)
  assert.equal(h.renderizar().perguntando, false)
  h.desmontar()
})
await provar('Falha conserva pergunta e retry reutiliza a troca sem apagar novo rascunho', async () => {
  const perguntas = []; let falhar = true
  const h = new Hospedeiro(useConversaAssistente, props({ perguntar: async texto => {
    perguntas.push(texto)
    if (falhar) throw Object.assign(new Error('Sem conexão com o EasyStok.'), { codigo: 'SEM_CONEXAO' })
    return 'Recuperado'
  } }))
  assert.equal(await perguntarNo(h, 'Pergunta guardada'), false)
  const troca = h.renderizar().trocas[0]
  assert.equal(troca.pergunta, 'Pergunta guardada'); assert.match(troca.erro, /continua acima/)
  h.resultado.setPergunta('Outra dúvida ainda não enviada'); h.renderizar(); falhar = false
  assert.equal(await h.resultado.aoRepetir(troca), true)
  h.renderizar(); assert.equal(h.resultado.pergunta, 'Outra dúvida ainda não enviada')
  assert.equal(h.resultado.trocas.length, 1); assert.equal(h.resultado.trocas[0].resposta, 'Recuperado')
  assert.deepEqual(perguntas, ['Pergunta guardada', 'Pergunta guardada']); h.desmontar()
})
await provar('Trocar cliente libera o campo e resposta antiga não encerra a consulta nova', async () => {
  const antiga = pendente(), nova = pendente()
  const base = props({ perguntar: (_, c) => c.id === 'a' ? antiga.promise : nova.promise })
  const h = new Hospedeiro(useConversaAssistente, base)
  const anterior = perguntarNo(h, 'Pergunta da Ana')
  h.renderizar({ ...base, conversa: { ...conversa, id: 'b', nome: 'Bruno fixture' } })
  assert.equal(h.resultado.perguntando, false); assert.equal(h.resultado.trocas.length, 0)
  const atual = perguntarNo(h, 'Pergunta do Bruno')
  antiga.resolver('Resposta antiga'); assert.equal(await anterior, false)
  assert.equal(h.renderizar().perguntando, true); assert.equal(h.resultado.trocas[0].pergunta, 'Pergunta do Bruno')
  nova.resolver('Resposta atual'); assert.equal(await atual, true)
  assert.equal(h.renderizar().trocas[0].resposta, 'Resposta atual'); h.desmontar()
})
await provar('Desmontar antes da resposta invalida a consulta', async () => {
  const espera = pendente(); const h = new Hospedeiro(useConversaAssistente, props({ perguntar: () => espera.promise }))
  const consulta = perguntarNo(h, 'Dúvida pendente'); h.desmontar(); espera.resolver('Atrasada')
  assert.equal(await consulta, false); assert.equal(h.resultado.trocas[0]?.resposta ?? null, null)
})
await provar('Resposta vazia permite retry e indisponibilidade não expõe configuração de servidor', async () => {
  const h = new Hospedeiro(useConversaAssistente, props({ perguntar: async () => ({ texto: '', acoes: [] }) }))
  assert.equal(await perguntarNo(h, 'Uma pergunta'), false)
  assert.match(h.renderizar().trocas[0].erro, /não trouxe uma resposta/)
  h.renderizar(props({ perguntar: async () => { throw Object.assign(new Error('configure Anthropic:ApiKey'), { codigo: 'ASSISTENTE_INDISPONIVEL' }) } }))
  assert.equal(await h.resultado.aoRepetir(h.resultado.trocas[0]), false)
  assert.doesNotMatch(h.renderizar().trocas[0].erro, /Anthropic/); assert.match(h.resultado.trocas[0].erro, /responsável/)
  h.desmontar()
})
const proposta = respostaDoAssistente({ acoes: [{ tipo: 'nota_interna', texto: 'Observação fixture' }] }).acoes[0]
async function comProposta(executar) {
  const h = new Hospedeiro(useConversaAssistente, props({ perguntar: async () => ({ acoes: [proposta] }), executarAcao: executar }))
  await perguntarNo(h, 'Anote a preferência'); h.renderizar()
  return h
}
await provar('Proposta não executa sozinha; clique duplo espera a confirmação e executa uma vez', async () => {
  const espera = pendente(); let total = 0
  const h = await comProposta(() => { total++; return espera.promise })
  assert.equal(total, 0)
  const troca = h.resultado.trocas[0], acao = troca.acoes[0]
  const primeira = h.resultado.aoExecutarAcao(troca.id, acao)
  assert.equal(await h.resultado.aoExecutarAcao(troca.id, acao), false)
  assert.equal(h.renderizar().trocas[0].acoes[0].estado, 'executando')
  espera.resolver(true); assert.equal(await primeira, true)
  assert.equal(h.renderizar().trocas[0].acoes[0].estado, 'feita')
  assert.equal(await h.resultado.aoExecutarAcao(troca.id, acao), false); assert.equal(total, 1); h.desmontar()
})
await provar('Retorno false mantém ação como falha e libera retry confirmado', async () => {
  let confirmar = false
  const h = await comProposta(async () => confirmar)
  const troca = h.resultado.trocas[0], acao = troca.acoes[0]
  assert.equal(await h.resultado.aoExecutarAcao(troca.id, acao), false)
  assert.equal(h.renderizar().trocas[0].acoes[0].estado, 'falhou')
  confirmar = true; assert.equal(await h.resultado.aoExecutarAcao(troca.id, acao), true)
  assert.equal(h.renderizar().trocas[0].acoes[0].estado, 'feita'); h.desmontar()
})
await provar('Troca de cliente durante ação não marca sucesso no cliente seguinte', async () => {
  const espera = pendente(); let contextoAtual
  const h = await comProposta((_, c, validar) => { assert.equal(c.id, 'a'); contextoAtual = validar; return espera.promise })
  const troca = h.resultado.trocas[0]
  const operacao = h.resultado.aoExecutarAcao(troca.id, troca.acoes[0])
  h.renderizar({ ...h.props, conversa: { ...conversa, id: 'b' } })
  assert.equal(contextoAtual(), false); espera.resolver(true); assert.equal(await operacao, false)
  assert.equal(h.renderizar().trocas.length, 0); h.desmontar()
})
await provar('Executor real recusa falso sucesso de nota e envio', async () => {
  await assert.rejects(() => executarAcaoDoAssistente(proposta, conversa, { salvarNota: async () => false }), /confirmação/)
  const envio = respostaDoAssistente({ acoes: [{ tipo: 'enviar_cardapio' }] }).acoes[0]
  await assert.rejects(() => executarAcaoDoAssistente(envio, conversa, { obterLinkCardapio: async () => ({ url: 'https://fixture.test/menu' }), enviar: async () => false }), /confira|confirmado/i)
})

globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'fixture', expiraEm: Date.now() + 60000, empresa: { id: 'empresa-fixture' } }) }
await provar('Adaptador de sugestão faz uma chamada no clique duplo e ignora resposta anterior', async () => {
  const a = pendente(), b = pendente(), chamadas = [], despachos = []
  globalThis.fetch = url => { chamadas.push(url); return url.includes('/a/') ? a.promise : b.promise }
  const api = criarAcoesAgenteApi({ despachar: acao => despachos.push(acao) })
  const primeira = api.consultarAgente('a'), repetida = api.consultarAgente('a')
  const segunda = api.consultarAgente('b')
  b.resolver(new Response(JSON.stringify({ data: { texto: 'Atual B' } }))); await segunda
  a.resolver(new Response(JSON.stringify({ data: { texto: 'Antiga A' } }))); await Promise.all([primeira, repetida])
  assert.equal(chamadas.length, 2)
  assert.deepEqual(despachos.filter(d => d.tipo === 'agente-respondeu').map(d => [d.id, d.sugestao.texto]), [['b', 'Atual B']])
})
await provar('Voltar ao primeiro cliente reutiliza consulta pendente e mostra a resposta do cliente atual', async () => {
  const a = pendente(), b = pendente(), chamadas = [], despachos = []
  globalThis.fetch = url => { chamadas.push(url); return url.includes('/a/') ? a.promise : b.promise }
  const api = criarAcoesAgenteApi({ despachar: acao => despachos.push(acao) })
  const primeira = api.consultarAgente('a'), segunda = api.consultarAgente('b'), retomada = api.consultarAgente('a')
  a.resolver(new Response(JSON.stringify({ data: { texto: 'Atual A' } }))); await retomada; await primeira
  b.resolver(new Response(JSON.stringify({ data: { texto: 'Antiga B' } }))); await segunda
  assert.equal(chamadas.length, 2)
  assert.deepEqual(despachos.filter(d => d.tipo === 'agente-respondeu').map(d => [d.id, d.sugestao.texto]), [['a', 'Atual A']])
})
const painel = extra => renderToStaticMarkup(createElement(PainelAgente, { modo: 'api', estado: 'ocioso', aoConsultar() {}, aoDescartar() {}, ...extra }))
await provar('Erro do agente aparece aberto com retry e texto de suporte legível', async () => {
  const html = painel({ estado: 'erro', erro: 'Agente indisponível: configure Anthropic:Enabled=true e Anthropic:ApiKey no servidor.' })
  assert.match(html, /role="alert"/); assert.match(html, /Tentar novamente/)
  assert.match(html, /responsável pelo sistema/); assert.doesNotMatch(html, /Anthropic:/)
})
await provar('Consulta aberta anuncia espera sem inventar leitura de ficha ou cardápio', async () => {
  const html = painel({ estado: 'pensando' })
  assert.match(html, /<output/); assert.match(html, /Consultando a IA/)
})
await provar('Sugestão pronta diz que não foi enviada e oferece apenas rascunho', async () => {
  const html = painel({ estado: 'pronto', sugestao: { texto: 'Texto fixture', acao: 'propor', intencao: { rotulo: 'Sugestão' } } })
  assert.match(html, /Ainda não foi enviado/); assert.match(html, /Usar no rascunho/)
  assert.doesNotMatch(html, />Enviar ao cliente</)
})
await provar('Proposta sem cadastro fica impedida, ligada à explicação e sem falso sucesso', async () => {
  const html = renderToStaticMarkup(createElement(AcoesPropostas, { acoes: [proposta], semCadastro: true, aoExecutar() {} }))
  assert.match(html, /Salve o cadastro/); assert.match(html, /disabled=""/); assert.match(html, /aria-describedby=/)
  assert.doesNotMatch(html, /Nota salva/)
})
await provar('Confirmação prepara foco no status da ação sem mover scroll', async () => {
  const botao = {}, body = {}, focos = []
  const status = { focus: opcoes => focos.push(opcoes) }
  const origem = { botao, item: { isConnected: true, querySelector: () => status } }
  assert.equal(focarResultadoDaAcao(origem, { body, activeElement: botao }), true)
  assert.equal(focarResultadoDaAcao(origem, { body, activeElement: body }), true)
  assert.deepEqual(focos, [{ preventScroll: true }, { preventScroll: true }])
})
await provar('Confirmação não rouba foco de outro campo, outra conversa ou item desmontado', async () => {
  const botao = {}, body = {}; let focos = 0
  const origem = { botao, item: { isConnected: true, querySelector: () => ({ focus: () => focos++ }) } }
  assert.equal(focarResultadoDaAcao(origem, { body, activeElement: {} }), false)
  origem.item.isConnected = false
  assert.equal(focarResultadoDaAcao(origem, { body, activeElement: body }), false)
  assert.equal(focarResultadoDaAcao(null, { body, activeElement: body }), false)
  assert.equal(focos, 0)
})
await provar('Falha devolve foco ao botão de retry sem mover scroll', async () => {
  const botao = {}, body = {}, focos = [], seletores = []
  const retry = { focus: opcoes => focos.push(opcoes) }
  const origem = { botao, item: { isConnected: true, querySelector: seletor => { seletores.push(seletor); return retry } } }
  assert.equal(focarResultadoDaAcao(origem, { body, activeElement: body }, 'falhou'), true)
  assert.deepEqual(seletores, ['button:not(:disabled)'])
  assert.deepEqual(focos, [{ preventScroll: true }])
})
await provar('Falha não rouba foco de rascunho ou de outra conversa', async () => {
  const botao = {}, body = {}; let focos = 0
  const origem = { botao, item: { isConnected: true, querySelector: () => ({ focus: () => focos++ }) } }
  assert.equal(focarResultadoDaAcao(origem, { body, activeElement: {} }, 'falhou'), false)
  origem.item.isConnected = false
  assert.equal(focarResultadoDaAcao(origem, { body, activeElement: body }, 'falhou'), false)
  assert.equal(focos, 0)
})
await provar('Foto fictícia é explicitamente demonstração; padrão sem oferta não promete promoção', async () => {
  const foto = renderToStaticMarkup(createElement(CartaoFotoSuspeita, { dado: { confianca: 0.7, motivos: ['Motivo fictício'] }, aoDispensar() {} }))
  assert.match(foto, /Esta foto não foi analisada/); assert.doesNotMatch(foto, /Confiança do indício/)
  const padrao = renderToStaticMarkup(createElement(CartaoPadraoDeCompra, { dado: { pedidos: 3, dia: 'domingo', prato: 'Lasanha', sugestaoTexto: 'Separei uma condição especial' }, aoDispensar() {} }))
  assert.doesNotMatch(padrao, /Usar sugestão|condição especial/)
})
console.log(JSON.stringify({ total: provas.length, aprovadas: provas.length, limites: ['HTTP controlado; nenhuma chamada externa à IA ou envio real ao cliente.', 'Foco, teclado, pixels e leitor de tela exigem verificação no navegador.'] }))
