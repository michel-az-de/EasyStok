// Executa hooks e adaptadores reais. O hospedeiro mínimo controla o ciclo dos hooks;
// HTTP/SSE são substituídos na fronteira. Foco, campos e pixels exigem a prova no navegador.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

let ativo = null
const depsIguais = (a, b) => a && b && a.length === b.length && a.every((v, i) => Object.is(v, b[i]))
const hooks = {
  useState(inicial) {
    const host = ativo, indice = host.indice++
    if (!(indice in host.valores)) host.valores[indice] = typeof inicial === 'function' ? inicial() : inicial
    return [host.valores[indice], valor => {
      const proximo = typeof valor === 'function' ? valor(host.valores[indice]) : valor
      if (!Object.is(proximo, host.valores[indice])) { host.valores[indice] = proximo; host.sujo = true }
    }]
  },
  useRef(inicial) {
    const host = ativo, indice = host.indice++
    return host.valores[indice] ??= { current: inicial }
  },
  useCallback(fn, deps) {
    const host = ativo, indice = host.indice++, anterior = host.valores[indice]
    if (!anterior || !depsIguais(anterior.deps, deps)) host.valores[indice] = { fn, deps }
    return host.valores[indice].fn
  },
  useEffect(fn, deps) {
    const host = ativo, indice = host.indice++, anterior = host.efeitos.get(indice)
    if (!anterior || !depsIguais(anterior.deps, deps)) {
      host.pendentes.push(() => { anterior?.limpar?.(); host.efeitos.set(indice, { deps, limpar: fn() }) })
    }
  },
}
class Hospedeiro {
  constructor(hook, props) {
    this.hook = hook; this.props = props; this.valores = []; this.efeitos = new Map(); this.sujo = true
    this.renderizar()
  }
  renderizar(props = this.props) {
    this.props = props
    let voltas = 0
    do {
      assert.ok(voltas++ < 20, 'efeitos não devem causar renderização sem fim')
      this.sujo = false; this.indice = 0; this.pendentes = []; ativo = this
      this.resultado = this.hook(this.props); ativo = null
      this.pendentes.forEach(efeito => efeito())
    } while (this.sujo)
    return this.resultado
  }
  async estabilizar() {
    for (let i = 0; i < 24; i++) { await Promise.resolve(); if (this.sujo) this.renderizar() }
    return this.resultado
  }
  desmontar() { for (const efeito of this.efeitos.values()) efeito.limpar?.(); this.efeitos.clear() }
}
const chamadas = [], respostas = []
const responder = (metodo, caminho, executar) => respostas.push({ metodo, caminho, executar })
const pendente = () => {
  let resolver, recusar
  const promise = new Promise((res, rej) => { resolver = res; recusar = rej })
  return { promise, resolver, recusar }
}
globalThis.__provaUxSistema = {
  hooks,
  chamarApi(caminho, opcoes = {}) {
    const metodo = opcoes.metodo ?? 'GET'
    chamadas.push({ metodo, caminho, corpo: structuredClone(opcoes.corpo) })
    const indice = respostas.findIndex(r => r.metodo === metodo && r.caminho === caminho)
    return indice < 0 ? Promise.resolve([]) : Promise.resolve().then(respostas.splice(indice, 1)[0].executar)
  },
}
const moduloDados = codigo => 'data:text/javascript,' + encodeURIComponent(codigo)
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec === 'react' && /use(?:Entregas|Cozinha|CadastroEntrega)Api\.js$/.test(contexto.parentURL)) {
      return { url: moduloDados('const h=globalThis.__provaUxSistema.hooks; export const useState=h.useState,useRef=h.useRef,useCallback=h.useCallback,useEffect=h.useEffect;'), shortCircuit: true }
    }
    if (spec === './cliente' && /\/infra\/api\/(?:entregasApi|kdsApi)\.js$/.test(contexto.parentURL)) {
      return { url: moduloDados('export const chamarApi=(...a)=>globalThis.__provaUxSistema.chamarApi(...a);'), shortCircuit: true }
    }
    if (spec.endsWith('/eventosOperacao')) {
      return { url: moduloDados('export const conectarEventosOperacao=()=>()=>{};'), shortCircuit: true }
    }
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      try { return proximo(spec + '.js', contexto) } catch { /* resolução padrão abaixo */ }
    }
    return proximo(spec, contexto)
  },
})
const { useEntregasApi } = await import('../src/aplicacao/useEntregasApi.js')
const { useCozinhaApi } = await import('../src/aplicacao/useCozinhaApi.js')
const { useCadastroEntregaApi } = await import('../src/aplicacao/useCadastroEntregaApi.js')
let aprovados = 0
const provar = async (nome, executar) => {
  chamadas.length = 0; respostas.length = 0
  await executar()
  assert.equal(respostas.length, 0, 'toda resposta programada foi realmente consumida')
  aprovados++; console.log('ok ' + nome)
}
const post = caminho => chamadas.filter(c => c.metodo === 'POST' && c.caminho === caminho)

for (const [acao, caminho, dados, argumentos] of [
  ['criarEntregador', '/api/atendimento/entregadores', { nome: 'Maria fixture', tipo: 'Proprio', empresa: 'Outra', telefone: '11999999999', veiculo: null, placa: null }, d => [d]],
  ['abrirChamado', '/api/atendimento/chamados-entregador', { texto: 'Retirar as duas encomendas da fixture', viagemId: null }, d => [d.texto, d.viagemId]],
]) {
  await provar(acao + ': falha devolve false, conserva o corpo e libera pendência', async () => {
    const h = new Hospedeiro(useEntregasApi)
    try {
      await h.estabilizar(); chamadas.length = 0
      const espera = pendente()
      responder('POST', caminho, () => espera.promise)
      const resultado = h.resultado.acoes[acao](...argumentos(dados))
      assert.equal(h.renderizar().ocupado, true)
      espera.recusar(new Error('503 controlado'))
      assert.equal(await resultado, false)
      await h.estabilizar()
      assert.equal(h.resultado.ocupado, false)
      assert.match(h.resultado.erro, /503 controlado/)
      assert.deepEqual(post(caminho).map(c => c.corpo), [dados])
      assert.equal(chamadas.filter(c => c.metodo === 'GET').length, 0)
    } finally { h.desmontar() }
  })
  await provar(acao + ': sucesso confirmado devolve true e faz uma única escrita', async () => {
    const h = new Hospedeiro(useEntregasApi)
    try {
      await h.estabilizar(); chamadas.length = 0
      responder('POST', caminho, () => ({ id: 'fixture-id' }))
      assert.equal(await h.resultado.acoes[acao](...argumentos(dados)), true)
      await h.estabilizar()
      assert.equal(h.resultado.ocupado, false)
      assert.equal(h.resultado.erro, null)
      assert.deepEqual(post(caminho).map(c => c.corpo), [dados])
      assert.ok(chamadas.some(c => c.metodo === 'GET'), 'recarga após confirmação')
    } finally { h.desmontar() }
  })
  await provar(acao + ': POST confirmado e recarga falha ainda devolve true, sem repetir POST', async () => {
    const h = new Hospedeiro(useEntregasApi)
    try {
      await h.estabilizar(); chamadas.length = 0
      responder('POST', caminho, () => ({ id: 'fixture-id' }))
      responder('GET', '/api/atendimento/viagens', () => { throw new Error('recarga503') })
      assert.equal(await h.resultado.acoes[acao](...argumentos(dados)), true)
      await h.estabilizar()
      assert.match(h.resultado.erro, /recarga503/)
      assert.equal(post(caminho).length, 1)
    } finally { h.desmontar() }
  })
}
await provar('Cozinha: trocar dia esvazia a fila anterior durante a consulta e após falha', async () => {
  responder('GET', '/api/kds/pedidos?data=2026-10-10', () => [{ id: 'hoje', status: 'aguardando' }])
  const h = new Hospedeiro(useCozinhaApi, { data: '2026-10-10' })
  try {
    await h.estabilizar()
    assert.equal(h.resultado.pedidos[0].id, 'hoje')
    const espera = pendente()
    responder('GET', '/api/kds/pedidos?data=2026-10-11', () => espera.promise)
    h.renderizar({ data: '2026-10-11' })
    assert.equal(h.resultado.pedidos, null)
    assert.equal(h.resultado.erro, null)
    espera.recusar(new Error('amanhã503'))
    await h.estabilizar()
    assert.equal(h.resultado.pedidos, null)
    assert.match(h.resultado.erro, /amanhã503/)
    assert.deepEqual(chamadas.filter(c => c.caminho.startsWith('/api/kds/pedidos')).map(c => c.caminho), [
      '/api/kds/pedidos?data=2026-10-10', '/api/kds/pedidos?data=2026-10-11',
    ])
  } finally { h.desmontar() }
})
await provar('Cozinha: recarregar recupera o mesmo dia sem deixar erro antigo', async () => {
  responder('GET', '/api/kds/pedidos?data=2026-10-11', () => { throw new Error('primeira falhou') })
  const h = new Hospedeiro(useCozinhaApi, { data: '2026-10-11' })
  try {
    await h.estabilizar()
    assert.equal(h.resultado.pedidos, null)
    assert.match(h.resultado.erro, /primeira falhou/)
    responder('GET', '/api/kds/pedidos?data=2026-10-11', () => [{ id: 'amanha', status: 'aguardando' }])
    await h.resultado.recarregar(); await h.estabilizar()
    assert.equal(h.resultado.pedidos[0].id, 'amanha')
    assert.equal(h.resultado.erro, null)
    assert.equal(chamadas.filter(c => c.caminho === '/api/kds/pedidos?data=2026-10-11').length, 2)
  } finally { h.desmontar() }
})
await provar('Cozinha: resposta tardia do dia anterior não sobrescreve o dia atual', async () => {
  const atrasada = pendente()
  responder('GET', '/api/kds/pedidos?data=2026-10-10', () => atrasada.promise)
  const h = new Hospedeiro(useCozinhaApi, { data: '2026-10-10' })
  try {
    await h.estabilizar()
    responder('GET', '/api/kds/pedidos?data=2026-10-11', () => [{ id: 'amanha', status: 'pronto' }])
    h.renderizar({ data: '2026-10-11' }); await h.estabilizar()
    assert.equal(h.resultado.pedidos[0].id, 'amanha')
    atrasada.resolver([{ id: 'hoje-atrasado', status: 'aguardando' }])
    await h.estabilizar()
    assert.deepEqual(h.resultado.pedidos.map(p => p.id), ['amanha'])
  } finally { h.desmontar() }
})
await provar('Cadastro de entrega: falha inicial mantém janelas desconhecidas e retry recupera', async () => {
  responder('GET', '/api/minha-vitrine/entrega/janelas', () => { throw new Error('cadastro503') })
  const h = new Hospedeiro(useCadastroEntregaApi)
  try {
    await h.estabilizar()
    assert.equal(h.resultado.janelas, null)
    assert.match(h.resultado.erro, /cadastro503/)
    responder('GET', '/api/minha-vitrine/entrega/janelas', () => [{ id: 'janela-fixture', label: 'Almoço' }])
    await h.resultado.recarregar(); await h.estabilizar()
    assert.equal(h.resultado.janelas[0].id, 'janela-fixture')
    assert.equal(h.resultado.erro, null)
  } finally { h.desmontar() }
})
await provar('Cadastro de entrega: exclusão recusada devolve false e preserva janela recarregada', async () => {
  const janela = { id: 'janela-com-pedido', label: 'Almoço' }
  responder('GET', '/api/minha-vitrine/entrega/janelas', () => [janela])
  const h = new Hospedeiro(useCadastroEntregaApi)
  try {
    await h.estabilizar()
    responder('DELETE', '/api/minha-vitrine/entrega/janelas/' + janela.id, () => { throw new Error('Janela em uso') })
    responder('GET', '/api/minha-vitrine/entrega/janelas', () => [janela])
    assert.equal(await h.resultado.acoes.excluirJanela(janela.id), false)
    await h.estabilizar()
    assert.deepEqual(h.resultado.janelas, [janela])
    assert.match(h.resultado.erro, /Janela em uso/)
    assert.equal(h.resultado.enviando, false)
  } finally { h.desmontar() }
})
await provar('Cadastro de entrega: segunda criação pendente não duplica POST', async () => {
  const h = new Hospedeiro(useCadastroEntregaApi)
  try {
    await h.estabilizar(); chamadas.length = 0
    const espera = pendente(), corpo = { diaDaSemana: 6, horaInicio: '11:00', horaFim: '13:00', capacidadeMaxima: 10, label: 'Almoço' }
    responder('POST', '/api/minha-vitrine/entrega/janelas', () => espera.promise)
    const primeira = h.resultado.acoes.criarJanela(corpo)
    assert.equal(h.renderizar().enviando, true)
    assert.equal(await h.resultado.acoes.criarJanela(corpo), false)
    espera.resolver({ id: 'janela-fixture', ...corpo })
    assert.equal(await primeira, true)
    await h.estabilizar()
    assert.deepEqual(post('/api/minha-vitrine/entrega/janelas').map(c => c.corpo), [corpo])
    assert.equal(h.resultado.enviando, false)
  } finally { h.desmontar() }
})
await provar('Cadastro de entrega: gravação aceita e recarga falha não convidam a gravar novamente', async () => {
  const h = new Hospedeiro(useCadastroEntregaApi)
  try {
    await h.estabilizar(); chamadas.length = 0
    responder('POST', '/api/minha-vitrine/entrega/bloqueios', () => ({ id: 'bloqueio-fixture' }))
    responder('GET', '/api/minha-vitrine/entrega/janelas', () => { throw new Error('recarga falhou') })
    assert.equal(await h.resultado.acoes.criarBloqueio({ data: '2026-10-11', motivo: 'Manutenção', janelaEspecificaId: null }), true)
    await h.estabilizar()
    assert.match(h.resultado.erro, /recarga falhou/)
    assert.equal(post('/api/minha-vitrine/entrega/bloqueios').length, 1)
  } finally { h.desmontar() }
})
console.log(aprovados + '/' + aprovados + ' provas reais dos hooks/API aprovadas. DOM/foco/teclado: validar no navegador.')
