// Handlers reais dos hooks em um ciclo determinístico de efeitos/timers.
// Não substitui a prova do Composer no navegador nem chama um provedor de IA.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import vm from 'node:vm'
import * as complemento from '../src/dominio/complementoDeTexto.js'

registerHooks({
  resolve(nome, contexto, proximo) {
    if (nome.startsWith('.') && !/\.[a-z]+$/i.test(nome)) {
      try { return proximo(nome + '.js', contexto) } catch { /* resolução padrão */ }
    }
    return proximo(nome, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    return proximo(url, contexto)
  },
})

function relogio() {
  let agora = 0, id = 0
  const tarefas = new Map()
  return {
    agora: () => agora,
    agendar: (f, ms) => { tarefas.set(++id, { f, em: agora + ms }); return id },
    cancelar: (chave) => tarefas.delete(chave),
    async avancar(ms) {
      const fim = agora + ms
      for (;;) {
        const item = [...tarefas].filter(([, v]) => v.em <= fim).sort((a, b) => a[1].em - b[1].em)[0]
        if (!item) break
        agora = item[1].em; tarefas.delete(item[0]); item[1].f()
        for (let i = 0; i < 8; i++) await Promise.resolve()
      }
      agora = fim
      for (let i = 0; i < 8; i++) await Promise.resolve()
    },
  }
}

function hookReal(nome, injetados = {}) {
  const tempo = relogio(), slots = [], efeitos = [], chamadas = []
  let indice = 0, sujo = false, props, resultado
  const sessao = { empresa: { id: 'empresa-a' }, usuario: { id: 'ana' } }
  const ouvintes = new Map()
  const janela = {
    addEventListener: (tipo, f) => { if (!ouvintes.has(tipo)) ouvintes.set(tipo, new Set()); ouvintes.get(tipo).add(f) },
    removeEventListener: (tipo, f) => ouvintes.get(tipo)?.delete(f),
  }
  const contexto = vm.createContext({
    ...complemento, ...injetados,
    useState(inicial) {
      const chave = indice++
      if (!(chave in slots)) slots[chave] = typeof inicial === 'function' ? inicial() : inicial
      return [slots[chave], (valor) => { const novo = typeof valor === 'function' ? valor(slots[chave]) : valor; if (!Object.is(novo, slots[chave])) { slots[chave] = novo; sujo = true } }]
    },
    useRef(valor) { const chave = indice++; return slots[chave] ??= { current: valor } },
    useEffect(f, deps) {
      const chave = indice++, anterior = slots[chave]
      if (!anterior || deps.some((v, i) => !Object.is(v, anterior.deps[i]))) {
        efeitos.push(() => { anterior?.limpar?.(); slots[chave] = { deps, limpar: f() } })
      }
    },
    useComplementoLigado: () => [true],
    lerSessao: () => sessao,
    identidadeDaSessao: s => `${s?.empresa?.id ?? '-'}:${s?.usuario?.id ?? '-'}`,
    EVENTO_SESSAO_ALTERADA: 'sessao',
    perguntarAssistente: (...args) => new Promise(resolve => chamadas.push({ args, resolve })),
    combinarSinais: sinais => AbortSignal.any(sinais), AbortController, AbortSignal,
    setTimeout: tempo.agendar, clearTimeout: tempo.cancelar,
    Date: { now: tempo.agora }, requestAnimationFrame: f => tempo.agendar(f, 16),
    window: janela, document: { ...janela, visibilityState: 'visible' },
  })
  const fonte = readFileSync(new URL(`../src/aplicacao/${nome}.js`, import.meta.url), 'utf8').replace(/^import .*\r?\n/gm, '').replace('export function', 'function')
  contexto.useLayoutEffect = contexto.useEffect
  vm.runInContext(`${fonte}\nglobalThis.executar = ${nome}`, contexto)
  function render(novas = props) {
    props = novas
    do { sujo = false; indice = 0; resultado = contexto.executar(props); while (efeitos.length) efeitos.shift()() } while (sujo)
    return resultado
  }
  return { tempo, chamadas, sessao, contexto, render, get resultado() { return resultado },
    emitir: tipo => { for (const f of ouvintes.get(tipo) ?? []) f() },
  }
}

const texto = 'Bom dia! Podemos '
const aceitas = []
const props = { texto, contexto: 'cliente', conversaId: 'c1', revisaoContexto: 'm1', ativo: true, aoCompletar: s => aceitas.push(s) }
const hook = hookReal('useComplementoDeTexto')
hook.render(props).aoFocar({ currentTarget: { isConnected: true, focus() {}, setSelectionRange() {} } })
hook.render()
await hook.tempo.avancar(1200)
assert.equal(hook.chamadas.length, 1)
hook.chamadas[0].resolve({ texto: texto + 'ajudar?' })
await hook.tempo.avancar(0)
assert.equal(hook.render().complemento, 'ajudar?')
const aceitarAntigo = hook.resultado.aceitar
assert.equal(hook.render({ ...props, revisaoContexto: 'm2' }).complemento, '', 'mensagem nova invalida complemento sem alterar o texto digitado')
assert.equal(aceitarAntigo(), false, 'callback antigo não aplica uma resposta anterior à mensagem nova')
assert.equal(aceitas.length, 0, 'invalidação não escreve nem envia')
await hook.tempo.avancar(8000)
assert.equal(hook.chamadas.length, 2)
hook.render({ ...props, revisaoContexto: 'pedido-pago' })
assert.equal(hook.chamadas[1].args[2].sinal.aborted, true, 'revisão do pedido cancela a consulta anterior')
hook.chamadas[1].resolve({ texto: texto + 'usar o Pix pendente.' })
await hook.tempo.avancar(0)
assert.equal(hook.render().complemento, '', 'resposta atrasada não reaparece')

const oculto = hookReal('useComplementoDeTexto')
oculto.render(props).aoFocar({ currentTarget: { isConnected: true, focus() {}, setSelectionRange() {} } })
oculto.render()
oculto.contexto.document.visibilityState = 'hidden'; oculto.emitir('visibilitychange'); oculto.render()
await oculto.tempo.avancar(2000)
assert.equal(oculto.chamadas.length, 0, 'autocomplete não chama IA com a página oculta mesmo mantendo foco')
oculto.contexto.document.visibilityState = 'visible'; oculto.emitir('visibilitychange'); oculto.render()
await oculto.tempo.avancar(1200)
assert.equal(oculto.chamadas.length, 1)
oculto.contexto.document.visibilityState = 'hidden'; oculto.emitir('visibilitychange'); oculto.render()
assert.equal(oculto.chamadas[0].args[2].sinal.aborted, true, 'ocultar aborta autocomplete em voo')
oculto.chamadas[0].resolve({ texto: texto + 'texto antigo.' })
await oculto.tempo.avancar(0)
oculto.render({ ...props, revisaoContexto: 'm4' })
oculto.contexto.document.visibilityState = 'visible'; oculto.emitir('visibilitychange'); oculto.render()
await oculto.tempo.avancar(8000)
assert.equal(oculto.chamadas.length, 2, 'retorno consulta somente a revisão atual elegível')
assert.equal(oculto.render().complemento, '')
oculto.chamadas[1].resolve({ texto: texto + 'ajudar agora?' })
await oculto.tempo.avancar(0)
const callbackOculto = oculto.render().aceitar
oculto.contexto.document.visibilityState = 'hidden'
assert.equal(callbackOculto(), false, 'clique antigo é recusado antes mesmo do evento de visibilidade')
oculto.emitir('visibilitychange'); oculto.render()
assert.equal(aceitas.length, 0)

const { revisaoDoContexto, contextoDaSugestao } = await import('../src/dominio/contextoDaSugestao.js')
const { criarConsultaProativa, identidadeDaSugestao } = await import('../src/aplicacao/api/agente.js')
const conversa = { id: 'c1', estado: 'Em atendimento', mensagens: [{ id: 'm1', dir: 'in', texto: 'Tem cardápio?', em: '2026-11-01T12:00:00Z' }], pedido: null }
const identidade = 'empresa-a:ana'
const contexto = contextoDaSugestao(conversa, identidade)
assert.ok(contexto?.chave.includes('m1'))
assert.notEqual(contexto.chave, contextoDaSugestao(conversa, 'empresa-b:ana').chave)
assert.notEqual(contexto.chave, contextoDaSugestao(conversa, 'empresa-a:bia').chave)
for (const mudanca of [{ clienteId: 'outra-pessoa' }, { vinculoRevisao: 1 }, { estado: 'Com IA' }, { assumidaPorUsuarioId: 'outra-atendente' }])
  assert.notEqual(contexto.chave, contextoDaSugestao({ ...conversa, ...mudanca }, identidade).chave, 'identidade e situação invalidam a sugestão')
assert.notEqual(revisaoDoContexto(conversa), revisaoDoContexto({ ...conversa, mensagens: [...conversa.mensagens, { id: 'm2', dir: 'in', texto: 'Na verdade, quero retirar.' }] }))
assert.notEqual(revisaoDoContexto(conversa), revisaoDoContexto({ ...conversa, mensagens: [{ ...conversa.mensagens[0], transcricao: 'Dois pratos.' }] }))
const comPedido = { ...conversa, pedido: { pedidoId: 'p1', estado: 'aguardando', totalPagoApi: 0, itens: [] } }
assert.notEqual(revisaoDoContexto(comPedido), revisaoDoContexto({ ...comPedido, pedido: { ...comPedido.pedido, totalPagoApi: 40 } }))
assert.equal(revisaoDoContexto(comPedido), revisaoDoContexto({ ...comPedido, naoLidas: 0, pedido: { ...comPedido.pedido, leitura: 4321, canhotoImpresso: true } }), 'polling e impressão não gastam outra consulta')
assert.equal(contextoDaSugestao({ ...conversa, mensagens: [] }, identidade), null)
assert.equal(contextoDaSugestao({ ...conversa, estado: 'Encerrado' }, identidade), null)
assert.equal(contextoDaSugestao({ ...conversa, mensagens: [...conversa.mensagens, { id: 'saida', dir: 'out', texto: 'Segue.' }] }, identidade), null)

let sessaoApi = { token: 'teste', expiraEm: Date.now() + 600000, empresa: { id: 'empresa-a' }, usuario: { id: 'ana' } }
globalThis.sessionStorage = { getItem: () => JSON.stringify(sessaoApi) }
const requests = []
globalThis.fetch = (url, opcoes = {}) => new Promise(resolve => requests.push({ url, opcoes, resolve }))
const tempo = relogio(), estados = []
const consulta = criarConsultaProativa({ aoMudar: estado => estados.push(estado), agora: tempo.agora, agendar: tempo.agendar, cancelar: tempo.cancelar })
consulta.atualizar(contexto, false)
await tempo.avancar(10000)
assert.equal(requests.length, 0, 'contexto oculto ou campo ocupado não chama IA')
consulta.atualizar(contexto, true)
await tempo.avancar(1199)
assert.equal(requests.length, 0, 'espera a conversa estabilizar')
await tempo.avancar(1)
assert.equal(requests.length, 1)
assert.equal(requests[0].url, '/api/atendimento/conversas/c1/sugestao')
assert.equal(requests[0].opcoes.method, 'POST')
assert.ok(requests[0].opcoes.signal, 'cancelamento chega ao fetch real')
consulta.atualizar(contexto, true)
await tempo.avancar(100)
assert.equal(requests.length, 1, 'mesmo contexto em voo não duplica')
requests[0].resolve(new Response(JSON.stringify({ data: { texto: 'Posso enviar o cardápio.' } })))
await tempo.avancar(0)
assert.equal(consulta.obterSugestaoAtual(contexto)?.texto, 'Posso enviar o cardápio.')
assert.equal(estados.at(-1).estado, 'pronto')
consulta.atualizar(contexto, false)
assert.equal(consulta.obterSugestaoAtual(contexto), null, 'contexto oculto não pode aceitar sugestão')
consulta.atualizar(contexto, true)
assert.equal(consulta.obterSugestaoAtual(contexto)?.texto, 'Posso enviar o cardápio.')
await tempo.avancar(2000)
assert.equal(requests.length, 1, 'retorno usa cache e não paga de novo')
consulta.dispensar(contexto)
assert.equal(consulta.obterSugestaoAtual(contexto), null)
consulta.atualizar(contexto, false); consulta.atualizar(contexto, true)
await tempo.avancar(9000)
assert.equal(requests.length, 1, 'dispensada não reaparece no retorno')

const segunda = contextoDaSugestao({ ...conversa, mensagens: [{ id: 'm2', dir: 'in', texto: 'Quero um prato.' }] }, identidade)
consulta.atualizar(segunda, true)
await tempo.avancar(1200)
assert.equal(requests.length, 2)
const terceira = contextoDaSugestao({ ...conversa, mensagens: [{ id: 'm3', dir: 'in', texto: 'Sem cebola.' }] }, identidade)
consulta.atualizar(terceira, true)
assert.equal(requests[1].opcoes.signal.aborted, true)
requests[1].resolve(new Response(JSON.stringify({ data: { texto: 'Resposta antiga.' } })))
await tempo.avancar(7999)
assert.equal(requests.length, 2, 'mudança de contexto respeita intervalo entre chamadas')
assert.equal(consulta.obterSugestaoAtual(segunda), null)
await tempo.avancar(1)
assert.equal(requests.length, 3)
sessaoApi = { ...sessaoApi, usuario: { id: 'bia' } }
requests[2].resolve(new Response(JSON.stringify({ data: { texto: 'Texto para Ana.' } })))
await tempo.avancar(0)
assert.equal(consulta.obterSugestaoAtual(terceira), null, 'resposta de outro usuário nunca fica utilizável')
consulta.atualizar(contextoDaSugestao(conversa, 'empresa-a:bia'), true)
await tempo.avancar(8000)
assert.equal(requests.length, 4)
consulta.atualizar(null, false)
assert.equal(requests[3].opcoes.signal.aborted, true, 'sair cancela request em voo')
requests[3].resolve(new Response(JSON.stringify({ data: { texto: 'Resposta enquanto oculta.' } })))
await tempo.avancar(0)
assert.equal(estados.at(-1).sugestao, null)
sessaoApi = { ...sessaoApi, usuario: null }
consulta.atualizar(contextoDaSugestao(conversa, 'empresa-a:-'), true)
await tempo.avancar(10000)
assert.equal(requests.length, 4, 'sessão sem empresa e usuário completos não autoriza chamada automática')

sessaoApi = { ...sessaoApi, usuario: { id: 'ana' } }
let proativo
proativo = hookReal('useSugestaoProativa', {
  contextoDaSugestao, identidadeDaSugestao,
  criarConsultaProativa: opcoes => criarConsultaProativa({ ...opcoes, agora: () => proativo.tempo.agora(), agendar: (f, ms) => proativo.tempo.agendar(f, ms), cancelar: id => proativo.tempo.cancelar(id) }),
})
const base = { conversa, ativo: true }
proativo.render(base)
await proativo.tempo.avancar(1200)
assert.equal(requests.length, 5, 'hook real aciona a consulta somente após estabilização')
proativo.contexto.document.visibilityState = 'hidden'
proativo.emitir('visibilitychange')
assert.equal(requests[4].opcoes.signal.aborted, true, 'ocultar página aborta sem esperar render')
proativo.render()
requests[4].resolve(new Response(JSON.stringify({ data: { texto: 'Texto oculto.' } })))
await proativo.tempo.avancar(0)
assert.equal(proativo.render().sugestao, null)
proativo.contexto.document.visibilityState = 'visible'
proativo.emitir('visibilitychange')
proativo.render()
await proativo.tempo.avancar(8000)
assert.equal(requests.length, 5, 'retorno não repete a tentativa cancelada no mesmo contexto')
const novaConversa = { ...conversa, mensagens: [{ id: 'm2', dir: 'in', texto: 'Pode me passar os pratos?' }] }
proativo.render({ ...base, conversa: novaConversa })
await proativo.tempo.avancar(1200)
assert.equal(requests.length, 6)
proativo.emitir('storage')
proativo.render()
assert.equal(requests[5].opcoes.signal.aborted, false, 'storage de outra preferência não cancela a consulta da mesma identidade')
requests[5].resolve(new Response(JSON.stringify({ data: { texto: 'Posso te mostrar os pratos.' } })))
await proativo.tempo.avancar(0)
assert.equal(proativo.render().sugestao?.texto, 'Posso te mostrar os pratos.')
const pegarAntiga = proativo.resultado.obterSugestaoAtual
proativo.render({ ...base, conversa: { ...novaConversa, mensagens: [{ id: 'm3', dir: 'in', texto: 'Tenho uma dúvida.' }] } })
assert.equal(pegarAntiga(), null, 'clique retido de outro contexto não entrega texto')
proativo.contexto.document.activeElement = { matches: () => true }
proativo.emitir('focusin'); proativo.render()
await proativo.tempo.avancar(10000)
assert.equal(requests.length, 6, 'digitação em qualquer campo cancela o agendamento proativo')
proativo.contexto.document.activeElement = null
proativo.emitir('focusout'); proativo.render()
await proativo.tempo.avancar(1200)
assert.equal(requests.length, 7)
proativo.render({ ...base, campoEmUso: true })
assert.equal(requests[6].opcoes.signal.aborted, true, 'campoEmUso permite que a tela suspenda a IA sem trocar o foco')
requests[6].resolve(new Response(JSON.stringify({ data: { texto: 'Não deve aparecer.' } })))
await proativo.tempo.avancar(0)
assert.equal(proativo.render().sugestao, null)

// Cache expira sem disparar nova IA; 503 também não vira repetição ao alternar áreas.
sessaoApi = { ...sessaoApi, usuario: { id: 'cache' } }
const contextoCache = contextoDaSugestao(conversa, 'empresa-a:cache')
const segundaCache = contextoDaSugestao({ ...conversa, mensagens: [{ id: 'm2', dir: 'in', texto: 'Outra pergunta.' }] }, 'empresa-a:cache')
const tempoCache = relogio(), resultadosCache = []
const cacheTestado = criarConsultaProativa({ aoMudar: r => resultadosCache.push(r), agora: tempoCache.agora, agendar: tempoCache.agendar, cancelar: tempoCache.cancelar })
cacheTestado.atualizar(contextoCache, true)
await tempoCache.avancar(1200)
const pedidoCache = requests.at(-1)
pedidoCache.resolve(new Response(JSON.stringify({ data: { texto: 'Sugestão com validade.' } })))
await tempoCache.avancar(0)
assert.equal(resultadosCache.at(-1).estado, 'pronto')
const quantidadeCache = requests.length
await tempoCache.avancar(120000)
assert.equal(resultadosCache.at(-1).estado, 'ocioso', 'validade retira também a sugestão visível')
assert.equal(cacheTestado.obterSugestaoAtual(contextoCache), null)
cacheTestado.atualizar(contextoCache, false); cacheTestado.atualizar(contextoCache, true)
await tempoCache.avancar(10000)
assert.equal(requests.length, quantidadeCache, 'expiração não renova tokens automaticamente')
cacheTestado.atualizar(segundaCache, true)
await tempoCache.avancar(1200)
requests.at(-1).resolve(new Response(JSON.stringify({ error: { message: 'Agente indisponível.' } }), { status: 503 }))
await tempoCache.avancar(0)
assert.equal(resultadosCache.at(-1).estado, 'indisponivel')
const quantidadeFalha = requests.length
cacheTestado.atualizar(segundaCache, false); cacheTestado.atualizar(segundaCache, true)
await tempoCache.avancar(10000)
assert.equal(requests.length, quantidadeFalha, '503 não provoca novas chamadas ao voltar')

// Composer tem key por conversa e desmonta ao ir para HOME. Montagens distintas
// precisam compartilhar o orçamento, sem compartilhar handlers nem cancelamento.
sessaoApi = { ...sessaoApi, usuario: { id: 'remontagem' } }
const tempoMontagens = relogio()
function montar(c) {
  const h = hookReal('useSugestaoProativa', {
    contextoDaSugestao, identidadeDaSugestao,
    criarConsultaProativa: opcoes => criarConsultaProativa({ ...opcoes, agora: tempoMontagens.agora, agendar: tempoMontagens.agendar, cancelar: tempoMontagens.cancelar }),
  })
  h.render({ conversa: c, ativo: true })
  return h
}
const primeiraMontagem = montar(conversa)
await tempoMontagens.avancar(1200)
requests.at(-1).resolve(new Response(JSON.stringify({ data: { texto: 'Sugestão para Marina.' } })))
await tempoMontagens.avancar(0)
assert.equal(primeiraMontagem.render().sugestao?.texto, 'Sugestão para Marina.')
const antesDeTrocar = requests.length
primeiraMontagem.render({ conversa, ativo: false })
const outraConversa = { ...conversa, id: 'c2' }
const montagemB = montar(outraConversa)
await tempoMontagens.avancar(1200)
assert.equal(requests.length, antesDeTrocar, 'trocar de cliente não reinicia o cooldown')
montagemB.render({ conversa: outraConversa, ativo: false })
const segundaMontagem = montar(conversa)
assert.equal(segundaMontagem.render().sugestao?.texto, 'Sugestão para Marina.', 'A→B→A reutiliza a resposta, com outra instância do hook')
await tempoMontagens.avancar(10000)
assert.equal(requests.length, antesDeTrocar, 'A→B→A não repete a mesma consulta')
segundaMontagem.render({ conversa, ativo: false })
const voltaDaHome = montar(conversa)
assert.equal(voltaDaHome.render().sugestao?.texto, 'Sugestão para Marina.', 'sair para HOME e remontar preserva o cache')
assert.equal(requests.length, antesDeTrocar)
voltaDaHome.resultado.dispensar(); voltaDaHome.render({ conversa, ativo: false })
const dispensadaRemontada = montar(conversa)
await tempoMontagens.avancar(10000)
assert.equal(dispensadaRemontada.render().sugestao, null)
assert.equal(requests.length, antesDeTrocar, 'dispensa persiste na remontagem')
dispensadaRemontada.render({ conversa, ativo: false })
const canceladaMontada = montar(outraConversa)
await tempoMontagens.avancar(1200)
const chamadaCancelada = requests.at(-1)
canceladaMontada.render({ conversa: outraConversa, ativo: false })
assert.equal(chamadaCancelada.opcoes.signal.aborted, true)
chamadaCancelada.resolve(new Response(JSON.stringify({ data: { texto: 'Chegou tarde.' } })))
await tempoMontagens.avancar(0)
const aposCancelar = requests.length
const canceladaRemontada = montar(outraConversa)
await tempoMontagens.avancar(10000)
assert.equal(requests.length, aposCancelar, 'contexto cancelado não repete ao retornar em outra montagem')
assert.equal(canceladaRemontada.render().sugestao, null)

// Duas instâncias em voo: cancelar a primeira não aborta a segunda.
const simultaneaA = montar({ ...conversa, id: 'c3' })
await tempoMontagens.avancar(1200)
const requestA = requests.at(-1)
const simultaneaB = montar({ ...conversa, id: 'c4' })
const antesDeB = requests.length
await tempoMontagens.avancar(7999)
assert.equal(requests.length, antesDeB)
await tempoMontagens.avancar(1)
const requestB = requests.at(-1)
assert.notEqual(requestA, requestB)
simultaneaA.render({ conversa: { ...conversa, id: 'c3' }, ativo: false })
assert.equal(requestA.opcoes.signal.aborted, true)
assert.equal(requestB.opcoes.signal.aborted, false, 'cancelamento pertence à instância, não ao cache compartilhado')
requestA.resolve(new Response(JSON.stringify({ data: { texto: 'Resposta A cancelada.' } })))
requestB.resolve(new Response(JSON.stringify({ data: { texto: 'Resposta B válida.' } })))
await tempoMontagens.avancar(0)
assert.equal(simultaneaB.render().sugestao?.texto, 'Resposta B válida.')

// O orçamento observa autenticação mesmo quando o Composer foi desmontado.
globalThis.window = new EventTarget()
sessaoApi = { ...sessaoApi, usuario: { id: 'eventos' } }
const contextoEventos = contextoDaSugestao(conversa, 'empresa-a:eventos')
const tempoEventos = relogio()
const controladorEvento = () => criarConsultaProativa({ aoMudar() {}, agora: tempoEventos.agora, agendar: tempoEventos.agendar, cancelar: tempoEventos.cancelar })
const antesDaSaida = controladorEvento()
antesDaSaida.atualizar(contextoEventos, true)
await tempoEventos.avancar(1200)
requests.at(-1).resolve(new Response(JSON.stringify({ data: { texto: 'Resposta na sessão anterior.' } })))
await tempoEventos.avancar(0)
assert.ok(antesDaSaida.obterSugestaoAtual(contextoEventos))
globalThis.window.dispatchEvent(new Event('storage'))
assert.ok(antesDaSaida.obterSugestaoAtual(contextoEventos), 'evento de storage da mesma identidade preserva o cache')
const sessaoAnterior = sessaoApi
sessaoApi = null
globalThis.window.dispatchEvent(new Event('easystok:sessao-alterada'))
sessaoApi = sessaoAnterior
globalThis.window.dispatchEvent(new Event('easystok:sessao-alterada'))
assert.equal(antesDaSaida.obterSugestaoAtual(contextoEventos), null, 'sair e entrar na mesma identidade não revalida o callback da sessão encerrada')
const aposEntrar = requests.length
const depoisDaEntrada = controladorEvento()
depoisDaEntrada.atualizar(contextoEventos, true)
await tempoEventos.avancar(1200)
assert.equal(requests.length, aposEntrar + 1, 'nova sessão não herda conteúdo guardado da sessão encerrada')
depoisDaEntrada.atualizar(null, false)
requests.at(-1).resolve(new Response(JSON.stringify({ data: { texto: 'Encerrada.' } })))
await tempoEventos.avancar(0)

console.log(`OK #2178: revisão, cache, remontagem, cancelamento e sessão; ${requests.length} POSTs simulados pela rota real de sugestão e ${hook.chamadas.length + oculto.chamadas.length} consultas simuladas do autocomplete. Nenhum envio ao cliente.`)
