// Executa as ações reais e controla HTTP na fronteira: aceitar uma proposta
// não pode exibir sucesso antes da confirmação, nem duplicar uma nota já salva.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'

registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      try { return proximo(spec + '.js', contexto) } catch { /* resolução padrão */ }
    }
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API=true; export const API_BASE=''" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return proximo(url, contexto)
  },
})
const { comApi } = await import('../src/aplicacao/acoesApi.js')
const { executarAcaoDoAssistente, ACOES_DO_ASSISTENTE } = await import('../src/dominio/acoesDoAssistente.js')
const acao = await import('../src/aplicacao/acoes.js')
globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'fixture', expiraEm: Date.now() + 60000, empresa: { id: 'empresa-fixture' } }) }
const conversa = { id: 'c1', nome: 'Cliente fixture', clienteId: 'cli1', mensagens: [] }
const estadoRef = { current: { conversas: [conversa] } }
const despachos = [], chamadas = []
const api = comApi({}, { despachar: d => despachos.push(d), agoraRef: { current: Date.now() }, estadoRef })
const resposta = (data, status = 200) => new Response(JSON.stringify(status === 200 ? { data } : { error: { message: data } }), { status })
const mensagem = { id: 'm1', texto: 'Mensagem fixture', direcao: 'Saida', autor: 'Dona', enviadaEm: new Date().toISOString(), status: 'Enviada' }
const pendente = () => { let resolver; const promise = new Promise(r => { resolver = r }); return { promise, resolver } }
let atender
globalThis.fetch = async (url, opcoes) => { chamadas.push({ url, metodo: opcoes.method, corpo: opcoes.body }); return atender(url, opcoes) }
let provas = 0
const provar = async (nome, fn) => { chamadas.length = 0; despachos.length = 0; await fn(); provas++; console.log('ok ' + nome) }
const turno = async () => { for (let i = 0; i < 12; i++) await Promise.resolve() }

await provar('enviar devolve a Promise e espera confirmação do HTTP', async () => {
  const espera = pendente()
  atender = () => espera.promise
  const envio = api.enviar('c1', 'Mensagem fixture')
  assert.ok(envio instanceof Promise, 'a tela deve poder aguardar a confirmação real')
  await turno()
  assert.ok(despachos.some(d => d.tipo === acao.ENVIAR_MENSAGEM))
  assert.ok(!despachos.some(d => d.tipo === acao.CONFIRMAR_ENVIO_API))
  espera.resolver(resposta(mensagem))
  assert.equal(await envio, true)
  assert.equal(despachos.filter(d => d.tipo === acao.CONFIRMAR_ENVIO_API).length, 1)
})
await provar('envio recusado e envio interrompido não viram sucesso', async () => {
  atender = () => resposta('Canal indisponível', 503)
  assert.equal(await api.enviar('c1', 'Não confirmada'), false)
  assert.ok(despachos.some(d => d.tipo === acao.FALHAR_ENVIO_API))
  assert.ok(!despachos.some(d => d.tipo === acao.CONFIRMAR_ENVIO_API))
  atender = () => { throw new TypeError('Rede interrompida fixture') }
  assert.equal(await api.enviar('c1', 'Conferir na conversa'), false)
})
await provar('variável pendente ou texto vazio não chama HTTP e retorna false', async () => {
  assert.equal(await api.enviar('c1', ''), false)
  assert.equal(await api.enviar('c1', 'Olá {nome}'), false)
  assert.equal(chamadas.length, 0)
})
await provar('nota sem cliente ou POST recusado não é confirmada', async () => {
  assert.equal(await api.salvarNota('inexistente', 'Nota fixture'), false)
  assert.equal(chamadas.length, 0)
  atender = () => resposta('Nota recusada', 503)
  assert.equal(await api.salvarNota('c1', 'Nota fixture'), false)
  assert.equal(chamadas.length, 1)
  assert.ok(despachos.some(d => d.tipo === acao.AVISO_API && /Nota/.test(d.mensagem)))
})
await provar('nota confirmada continua confirmada quando a recarga da ficha falha', async () => {
  atender = (url, opcoes) => opcoes.method === 'POST' ? resposta({ id: 'nota-fixture' }) : resposta('Falha ao atualizar ficha', 503)
  assert.equal(await api.salvarNota('c1', 'Nota já salva'), true)
  assert.equal(chamadas.filter(c => c.metodo === 'POST').length, 1)
  assert.ok(despachos.some(d => d.tipo === acao.AVISO_API && /salva/i.test(d.mensagem)))
})
await provar('nota só é confirmada após o POST, com corpo e cliente corretos', async () => {
  const espera = pendente()
  atender = (url, opcoes) => opcoes.method === 'POST' ? espera.promise : resposta(null)
  let terminou = false
  const nota = api.salvarNota('c1', '  Prefere sem cebola fixture.  ').then(ok => { terminou = true; return ok })
  await turno()
  assert.equal(terminou, false)
  espera.resolver(resposta({ id: 'nota-fixture' }))
  assert.equal(await nota, true)
  assert.equal(chamadas[0].url, '/api/clientes/cli1/notas')
  assert.equal(JSON.parse(chamadas[0].corpo).texto, 'Prefere sem cebola fixture.')
})
await provar('executor não conclui proposta cujo efeito foi recusado', async () => {
  await assert.rejects(executarAcaoDoAssistente({ tipo: ACOES_DO_ASSISTENTE.ENVIAR_CARDAPIO }, conversa, {
    obterLinkCardapio: async () => ({ url: 'https://fixture.test/cardapio' }), enviar: async () => false,
  }), /confirma|envio/i)
  await assert.rejects(executarAcaoDoAssistente({ tipo: ACOES_DO_ASSISTENTE.NOTA_INTERNA, texto: 'Nota' }, conversa, { salvarNota: async () => false }), /confirma|nota/i)
})
await provar('executor aguarda envio real, mantém conversa de origem e compatibilidade local', async () => {
  const espera = pendente(); let terminou = false, alvo
  const efeito = executarAcaoDoAssistente({ tipo: ACOES_DO_ASSISTENTE.ENVIAR_CARDAPIO }, conversa, {
    obterLinkCardapio: async () => ({ url: 'https://fixture.test/cardapio' }), enviar: (id, texto) => { alvo = { id, texto }; return espera.promise },
  }).then(() => { terminou = true })
  await turno(); assert.equal(terminou, false); assert.equal(alvo.id, conversa.id)
  espera.resolver(true); await efeito; assert.equal(terminou, true)
  await executarAcaoDoAssistente({ tipo: ACOES_DO_ASSISTENTE.NOTA_INTERNA, texto: 'Nota local' }, conversa, { salvarNota: () => undefined })
})
console.log(`${provas} cenários de confirmação das ações reais aprovados.`)
