import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

// A raiz alternativa e o overlay permitem provar RED/GREEN sem alterar a candidata em uso.
const raiz = process.env.R4_CONSOLE_FONTE ?? fileURLToPath(new URL('../', import.meta.url))
const conversaUrl = pathToFileURL(resolve(raiz, 'src/dominio/conversa.js')).href
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) return proximo(spec + '.js', contexto)
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (url === conversaUrl && process.env.R4_BUSCA_LOCAL_PROPOSTA)
      return { format: 'module', source: readFileSync(process.env.R4_BUSCA_LOCAL_PROPOSTA, 'utf8'), shortCircuit: true }
    if (url.endsWith('.json')) return { format: 'module', source: 'export default ' + readFileSync(new URL(url), 'utf8'), shortCircuit: true }
    return proximo(url, contexto)
  },
})
const { combinaBusca, tagCorrespondida, conversasDoBalcao, conversasEncerradasDoBalcao, conversasBloqueadasDoBalcao } = await import(conversaUrl)
let casos = 0
const falhas = []
const confere = (nome, executar) => { casos++; try { executar() } catch (erro) { falhas.push({ nome, erro: erro.message }) } }
const id = '663c88ec-8b90-4d36-8137-53c1fb2fe956'
const clienteId = '1b211795-7ad3-4bfe-9efb-a0b03a55a482'
const pedidoId = '68c58d85-c50a-436c-8c37-55b3a3cfd069'
const ana = { id, clienteId, nome: 'Ana da Conferência', numeroComanda: 390, canal: 'WhatsApp', contato: '',
  cliente: { telefone: '', tags: ['Revisão móvel'] }, mensagens: [{ texto: 'Preferência por café' }],
  historico: [{ itens: ['Macarrão à bolonhesa'] }], pedido: { pedidoId }, situacaoApi: 'Assumida', assumidaPorUsuarioId: 'operadora-a' }
const formas = ['(11) 99999-8888', '11999998888', '+55 (11) 99999-8888', '5511999998888']
for (const guardado of formas) for (const busca of formas) {
  confere(`telefone cadastro ${guardado} → ${busca}`, () => assert.equal(combinaBusca({ ...ana, cliente: { telefone: guardado } }, busca), true))
  confere(`telefone canal ${guardado} → ${busca}`, () => assert.equal(combinaBusca({ ...ana, contato: guardado }, busca), true))
}
confere('telefone informado e telefoneCanal usam o normalizador existente', () => {
  assert.equal(combinaBusca({ ...ana, contatoInformado: { telefone: '+5511999998888' } }, '(11) 99999-8888'), true)
  assert.equal(combinaBusca({ ...ana, cliente: { telefoneCanal: '+5511999998888' } }, '11999998888'), true)
})
confere('telefone diferente não corresponde', () => assert.equal(combinaBusca({ ...ana, contato: '+5511999998888' }, '11999998887'), false))
for (const busca of ['conferência', 'CONFERENCIA', '  aNA DA CONFERENCIA  ', 'revisao MOVEL', 'preferencia', 'cafe', 'macarrao', 'bolonhesa'])
  confere(`texto normalizado ${busca}`, () => assert.equal(combinaBusca(ana, busca), true))
confere('tag exibida corresponde à busca sem acento', () => assert.equal(tagCorrespondida(ana, 'REVISAO MOVEL'), 'Revisão móvel'))
for (const real of [id, clienteId, pedidoId]) confere(`ID real ${real}`, () => assert.equal(combinaBusca(ana, real.toUpperCase()), true))
confere('ID parcial não abre correspondência acidental', () => assert.equal(combinaBusca(ana, id.slice(0, 8)), false))
confere('comanda e busca vazia preservadas', () => {
  assert.equal(combinaBusca(ana, 'COMANDA 390'), true)
  assert.equal(combinaBusca(ana, '  '), true)
  assert.equal(combinaBusca(ana, 'COMANDA 391'), false)
})
confere('texto misto e pontuação não viram telefone vazio', () => {
  assert.equal(combinaBusca({ ...ana, contato: '+5511999998888' }, 'pedido 11999998888'), false)
  assert.equal(combinaBusca(ana, '---'), false)
})
confere('canal, responsável, encerradas e bloqueadas conservam seus filtros', () => {
  const principal = { ...ana, contato: '+5511999998888' }
  const outra = { ...principal, id: 'outra', assumidaPorUsuarioId: 'operadora-b' }
  const email = { ...principal, id: 'email', canal: 'E-mail', cliente: { telefone: '+5511999998888' } }
  const encerrada = { ...principal, id: 'encerrada', estado: 'Encerrado' }
  const bloqueada = { ...principal, id: 'bloqueada', bloqueio: { motivo: 'Conferir' } }
  const carregadas = [principal, outra, email, encerrada, bloqueada]
  const filtros = { busca: '(11) 99999-8888', canais: ['WhatsApp'], responsavel: 'minhas', aba: 'todas' }
  const usuario = { id: 'operadora-a' }, antes = JSON.stringify(carregadas)
  assert.deepEqual(conversasDoBalcao(carregadas, filtros, Date.now(), {}, true, [], null, usuario).map(c => c.id), [id])
  assert.deepEqual(conversasEncerradasDoBalcao(carregadas, filtros, usuario).map(c => c.id), ['encerrada'])
  assert.deepEqual(conversasBloqueadasDoBalcao(carregadas, filtros, usuario).map(c => c.id), ['bloqueada'])
  assert.equal(JSON.stringify(carregadas), antes)
})
confere('busca local só usa as conversas recebidas, sem ampliar tenant ou fazer requisição', () => {
  const anterior = globalThis.fetch
  globalThis.fetch = () => { throw new Error('Busca local não pode consultar a API') }
  try {
    const filtros = { busca: id, canais: [], responsavel: 'todas', aba: 'todas' }
    assert.deepEqual(conversasDoBalcao([], filtros, Date.now()), [])
    assert.deepEqual(conversasDoBalcao([ana], filtros, Date.now()).map(c => c.id), [id])
  } finally { globalThis.fetch = anterior }
})
console.log(JSON.stringify({ casos, passaram: casos - falhas.length, falharam: falhas.length, falhas }))
if (falhas.length) process.exitCode = 1
