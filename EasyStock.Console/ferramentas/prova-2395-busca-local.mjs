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

// A busca local inclui texto recebido, tags e itens do histórico: a máscara não muda o conjunto.
const formasEmTexto = ['(11)99999-8888', '(11) 99999-8888', '11999998888', '+5511999998888', '+55 (11) 99999-8888', '5511999998888']
const comCampoTextual = (campo, texto) => ({ ...ana, id: `texto-${campo}`, contato: '+5511988887777',
  cliente: { telefone: '+5511988887777', tags: campo === 'tag' ? [texto] : [] },
  mensagens: campo === 'mensagem' ? [{ texto }] : [],
  historico: campo === 'historico' ? [{ itens: [texto] }] : [],
})
for (const campo of ['mensagem', 'tag', 'historico']) for (const guardado of formasEmTexto) for (const busca of formasEmTexto)
  confere(`telefone em ${campo}: ${guardado} → ${busca}`, () => assert.equal(combinaBusca(comCampoTextual(campo, `Fale com ${guardado} para conferir.`), busca), true))
confere('as três representações produzem o mesmo conjunto de conversas', () => {
  const principal = { ...ana, contato: '+5511999998888' }
  const semCadastro = { ...comCampoTextual('mensagem', 'Pode avisar também o telefone 11999998888.'), clienteId: null }
  const outro = comCampoTextual('mensagem', 'O telefone é 11988887777.')
  const carregadas = [principal, semCadastro, outro]
  const conjuntos = formasEmTexto.map(busca => carregadas.filter(c => combinaBusca(c, busca)).map(c => c.id))
  for (const conjunto of conjuntos) assert.deepEqual(conjunto, [id, semCadastro.id])
})
for (const texto of [
  'Pedido42: telefone (11)99999-8888 ou +55 (21)97777-6666; 3 itens.',
  '11999998888 21977776666',
  '+5511999998888, +5521977776666',
  '(21)97777-6666; (11)99999-8888',
]) confere(`vários telefones permanecem independentes: ${texto}`, () => {
  const conversa = comCampoTextual('mensagem', texto)
  for (const busca of formasEmTexto) assert.equal(combinaBusca(conversa, busca), true)
  for (const busca of ['21977776666', '(21) 97777-6666', '+5521977776666']) assert.equal(combinaBusca(conversa, busca), true)
  for (const busca of ['88882197777', '(88)88219-7777', '+5588882197777', '11999998887']) assert.equal(combinaBusca(conversa, busca), false)
})
for (const campo of ['mensagem', 'tag', 'historico']) for (const texto of [
  'São 11 pessoas, 99999 pedidos e 8888 itens.',
  'Há 11999 pedidos; depois 998888 unidades.',
]) confere(`não junta números separados por palavras em ${campo}: ${texto}`, () => {
  for (const busca of formasEmTexto) assert.equal(combinaBusca(comCampoTextual(campo, texto), busca), false)
})
for (const campo of ['mensagem', 'tag', 'historico']) for (const texto of [
  'Código11999998888',
  'Referências11999998888X e X11999998888.',
  'Lote 1199999888800, sem telefone.',
  'Código(11)99999-8888',
  'Código+55(11)99999-8888X',
]) confere(`preserva substring textual nas seis representações em ${campo}: ${texto}`, () => {
  for (const busca of formasEmTexto) assert.equal(combinaBusca(comCampoTextual(campo, texto), busca), true)
})
confere('telefone fixo e celular no mesmo texto não se unem', () => {
  const conversa = comCampoTextual('mensagem', 'Ligue (21)3333-4444 ou +55 (11)99999-8888.')
  for (const busca of ['2133334444', '(21)3333-4444', '+552133334444']) assert.equal(combinaBusca(conversa, busca), true)
  for (const busca of formasEmTexto) assert.equal(combinaBusca(conversa, busca), true)
  assert.equal(combinaBusca(conversa, '(44)44119-9999'), false)
})
confere('tag que explica a correspondência também aceita máscara e DDI', () => {
  const conversa = comCampoTextual('tag', 'Referência (11)99999-8888')
  for (const busca of formasEmTexto) assert.equal(tagCorrespondida(conversa, busca), 'Referência (11)99999-8888')
})
confere('telefone no texto não ignora canal, responsável, estado e bloqueio', () => {
  const principal = comCampoTextual('mensagem', 'Ligar para 11999998888.')
  const outra = { ...principal, id: 'texto-outra', assumidaPorUsuarioId: 'operadora-b' }
  const email = { ...principal, id: 'texto-email', canal: 'E-mail' }
  const encerrada = { ...principal, id: 'texto-encerrada', estado: 'Encerrado' }
  const bloqueada = { ...principal, id: 'texto-bloqueada', bloqueio: { motivo: 'Conferir' } }
  const carregadas = [principal, outra, email, encerrada, bloqueada], antes = JSON.stringify(carregadas)
  const usuario = { id: 'operadora-a' }
  for (const busca of formasEmTexto) {
    const filtros = { busca, canais: ['WhatsApp'], responsavel: 'minhas', aba: 'todas' }
    assert.deepEqual(conversasDoBalcao(carregadas, filtros, Date.now(), {}, true, [], null, usuario).map(c => c.id), [principal.id])
    assert.deepEqual(conversasEncerradasDoBalcao(carregadas, filtros, usuario).map(c => c.id), [encerrada.id])
    assert.deepEqual(conversasBloqueadasDoBalcao(carregadas, filtros, usuario).map(c => c.id), [bloqueada.id])
  }
  assert.equal(JSON.stringify(carregadas), antes)
})

console.log(JSON.stringify({ casos, passaram: casos - falhas.length, falharam: falhas.length, falhas }))
if (falhas.length) process.exitCode = 1
