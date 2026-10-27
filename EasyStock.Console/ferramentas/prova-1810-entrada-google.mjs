/* eslint-disable no-console */
// Prova #1810 (revisão gravada de 14/10): com a conta Google já escolhida antes, o console entra
// sozinho (One Tap com auto_select); depois de "Sair" nesta aba, não entra sozinho de novo até a
// pessoa escolher a conta outra vez. #2070: a tela do convite também oferece o Google, sem entrar sozinha.
// O convite é conferido com o App montado de verdade na rota do link (DOM mínimo de `tela-no-node.mjs`),
// a API simulada no envelope real e o Google Identity Services falso: tocar no botão é chamar o callback dele.
//
//   node ferramentas/prova-1810-entrada-google.mjs

import assert from 'node:assert/strict'
import { botaoDe, digitar, elementos, enviar, esperar, google, guardado, janela, montar, resposta, textoDe } from './tela-no-node.mjs'

const chamadas = []
google.accounts.id.disableAutoSelect = () => chamadas.push('disableAutoSelect')

const { opcoesDaEntradaGoogle, travarEntradaSozinha, liberarEntradaSozinha } = await import('../src/infra/entradaGoogle.js')

let passos = 0
const ok = (texto) => { passos += 1; console.log(`ok ${passos} - ${texto}`) }

assert.equal(opcoesDaEntradaGoogle().auto_select, true, 'primeira visita: entra sozinho com a conta já escolhida')
assert.equal(opcoesDaEntradaGoogle().oferecer, true)
ok('sem saída nesta aba, o login com Google tenta entrar sozinho')

travarEntradaSozinha()
assert.deepEqual(chamadas, ['disableAutoSelect'], 'avisa o Google para não escolher a conta sozinho')
assert.equal(opcoesDaEntradaGoogle().auto_select, false, 'depois de Sair não entra sozinho')
assert.equal(opcoesDaEntradaGoogle().oferecer, false)
ok('depois de "Sair", a tela de login fica parada até a pessoa escolher')

liberarEntradaSozinha()
assert.equal(opcoesDaEntradaGoogle().auto_select, true)
ok('quem entra de novo pelo botão volta a entrar sozinho nas próximas visitas')

const armazenamento = globalThis.sessionStorage
globalThis.sessionStorage = { getItem: () => { throw new Error('bloqueado') }, setItem: () => { throw new Error('bloqueado') }, removeItem: () => {} }
assert.doesNotThrow(() => travarEntradaSozinha())
assert.equal(opcoesDaEntradaGoogle().auto_select, true)
globalThis.sessionStorage = armazenamento
ok('sem armazenamento do navegador, não quebra')

// #2070: a tela do link do convite também oferece "Entrar com Google". Quem recebe o convite entra
// com a conta Google do e-mail convidado; a API só aceita com o convite aberto e não vencido.
const { googleDoConvite, fraseDaFalhaDoGoogleNoConvite } = await import('../src/dominio/usuarios.js')

const buscarClientId = async () => 'client-id-de-teste'
const entradas = []
const googleDaSessao = { buscarClientId, entrar: async (idToken) => { entradas.push(idToken) }, opcoes: opcoesDaEntradaGoogle }
const concluidos = []
const noConvite = googleDoConvite(googleDaSessao, () => concluidos.push('convite concluído'))

assert.equal(noConvite.buscarClientId, buscarClientId, 'o mesmo ClientId do login: o botão não é desenhado de novo')
assert.equal(typeof noConvite.opcoes, 'function', 'mesmo formato do google da TelaLogin')
assert.deepEqual(noConvite.opcoes(), { auto_select: false, oferecer: false })
ok('no convite o Google nunca entra sozinho: a conta já escolhida neste aparelho pode ser de outra pessoa')

await noConvite.entrar('id-token-da-convidada')
assert.deepEqual(entradas, ['id-token-da-convidada'], 'entra pela mesma sessão do login')
assert.deepEqual(concluidos, ['convite concluído'], 'depois de entrar, o convite é concluído')
const vencidoAMao = Object.assign(new Error('Seu convite venceu: peça um novo'), { status: 401, codigo: 'INVALID_CREDENTIALS' })
const recusa = googleDoConvite({ ...googleDaSessao, entrar: async () => { throw vencidoAMao } }, () => concluidos.push('não devia'))
await assert.rejects(() => recusa.entrar('id-token'), (erro) => erro === vencidoAMao)
assert.deepEqual(concluidos, ['convite concluído'], 'recusado, o convite não é concluído')
assert.equal(googleDoConvite(null, () => {}), null)
ok('entrar com Google no convite usa a entrada da sessão e só conclui o convite se ela der certo; sem Google, nada')

// A recusa passa pela camada da API de verdade (`chamarApi`), com a resposta no envelope que a
// EasyStock.Api manda no `POST api/auth/google/login`: 401 `INVALID_CREDENTIALS` com a frase do caso.
const { entrarComGoogle } = await import('../src/infra/api/autenticacao.js')
const recusaDaApi = async (fetchFalso) => {
  globalThis.fetch = fetchFalso
  try { await entrarComGoogle('id-token') } catch (erro) { return erro }
  throw new Error('a API devia ter recusado')
}
const invalida = (message) => async () => resposta(401, { error: { code: 'INVALID_CREDENTIALS', message } })
const CONFIRA = 'Confira se escolheu a conta Google do e-mail que recebeu o convite.'
assert.equal(fraseDaFalhaDoGoogleNoConvite(await recusaDaApi(invalida('Esta conta Google não tem acesso ao EasyStok.'))),
  `Esta conta Google não tem acesso ao EasyStok. ${CONFIRA}`)
assert.equal(fraseDaFalhaDoGoogleNoConvite(await recusaDaApi(invalida('Seu convite venceu: peça um novo'))),
  `Seu convite venceu: peça um novo. ${CONFIRA}`)
assert.equal(fraseDaFalhaDoGoogleNoConvite(await recusaDaApi(async () => { throw new TypeError('rede') })), 'Sem conexão com o EasyStok.')
ok('recusa do Google no convite: a frase da API e o que conferir; sem rede, a frase da rede')

// ---------------------------------------------------------------- o convite no App montado

const { createElement: h } = await import('react')
const { App } = await import('../src/app/App.jsx')
const jwt = (carga) => `cabecalho.${Buffer.from(JSON.stringify(carga)).toString('base64url')}.assinatura`
const SESSAO_DA_CONVIDADA = {
  token: jwt({ empresaId: 'empresa-1', exp: Math.floor(Date.now() / 1000) + 3600 }), refreshToken: 'refresh-da-convidada', expiresIn: 3600,
  usuario: { id: 'bia', nome: 'Bia Lima', email: 'bia@exemplo.test', nivel: 'Operador' },
}
// Resposta que a prova solta quando quer: mostra a tela no meio da espera.
const adiada = () => { let soltar; const promessa = new Promise((r) => { soltar = r }); return { promessa, soltar } }
const api = { clientId: 'client-id-de-teste', google: null, aceite: null, chamadas: [] }
const fetchDoConvite = async (url, opcoes = {}) => {
  api.chamadas.push(`${opcoes.method ?? 'GET'} ${url}`)
  if (url === '/api/auth/google/config') return resposta(200, { data: { habilitado: Boolean(api.clientId), clientId: api.clientId } })
  if (url === '/api/auth/google/login') return api.google()
  if (url === '/api/auth/aceitar-convite') return api.aceite()
  return resposta(404)
}
let prompts = 0
google.accounts.id.prompt = () => { prompts += 1 }
const abrirLink = async (token) => {
  guardado.clear()
  api.chamadas.length = 0
  google.iniciados.length = 0
  google.desenhados.length = 0
  prompts = 0
  globalThis.fetch = fetchDoConvite
  janela.location.hash = `#/convite?t=${token}`
  await esperar()
  const tela = montar(h(App))
  await esperar()
  return tela
}
const alertas = (tela) => elementos(tela, (no) => no.getAttribute('role') === 'alert').map((no) => no.textContent)
const desabilitado = (tela, texto) => botaoDe(tela, texto).hasAttribute('disabled')
const SENHA_BOA = 'Casa-da-Baba1'

api.clientId = null
let tela = await abrirLink('token-sem-google')
assert.equal(google.desenhados.length, 0, 'sem ClientId na API, o botão do Google não aparece')
assert.match(textoDe(tela), /Escolha a senha que você vai usar para entrar\./)
assert.doesNotMatch(textoDe(tela), /conta Google/)
tela.desmontar()
ok('sem o Google ligado na API, o convite fica só com a senha')

api.clientId = 'client-id-de-teste'
api.google = invalida('Esta conta Google não tem acesso ao EasyStok.')
tela = await abrirLink('token-1')
assert.equal(guardado.get('easystok.convite'), 'token-1', 'o token fica guardado nesta aba')
assert.equal(janela.location.hash, '#/convite', 'e sai da barra de endereço')
assert.equal(google.desenhados.length, 1, 'o botão do Google é desenhado')
assert.equal(google.iniciados.at(-1).auto_select, false, 'no convite não entra sozinho')
assert.equal(prompts, 0, 'nem oferece o One Tap')
assert.match(textoDe(tela), /Entre com a conta Google do e-mail que recebeu o convite, ou crie uma senha\./)
await google.escolherConta('id-token-de-outra-conta')
await esperar()
assert.deepEqual(alertas(tela), [`Esta conta Google não tem acesso ao EasyStok. ${CONFIRA}`])
assert.equal(guardado.get('easystok.convite'), 'token-1', 'recusado, o convite continua guardado')
assert.equal(janela.location.hash, '#/convite')
digitar(tela, 'Senha', SENHA_BOA)
assert.equal(desabilitado(tela, 'Criar senha'), false, 'depois da recusa, criar senha volta a valer')
tela.desmontar()
ok('recusa do Google no convite: a frase na tela, o convite guardado e a senha liberada')

const entrada = adiada()
api.google = () => entrada.promessa
tela = await abrirLink('token-2')
digitar(tela, 'Senha', SENHA_BOA)
const tocou = google.escolherConta('id-token-da-convidada')
await esperar()
const [situacao] = elementos(tela, (no) => no.localName === 'output' && no.textContent === 'Entrando com o Google…')
assert.ok(situacao, 'enquanto entra, a tela diz que está entrando (região ao vivo)')
assert.equal(desabilitado(tela, 'Criar senha'), true, 'e criar senha espera')
entrada.soltar(resposta(200, { data: SESSAO_DA_CONVIDADA }))
await tocou
await esperar()
assert.equal(guardado.has('easystok.convite'), false, 'entrou: o convite guardado sai')
assert.equal(janela.location.hash, '#/', 'e a tela vai para o hall')
assert.equal(JSON.parse(guardado.get('easystok.sessao')).refreshToken, 'refresh-da-convidada', 'com a sessão da convidada')
tela.desmontar()
ok('entrar com Google no convite: "Entrando com o Google…", depois a sessão da convidada, sem o convite guardado, no hall')

const aceite = adiada()
api.aceite = () => aceite.promessa
api.google = async () => resposta(200, { data: SESSAO_DA_CONVIDADA })
tela = await abrirLink('token-3')
digitar(tela, 'Senha', SENHA_BOA)
enviar(tela)
await esperar()
await google.escolherConta('id-token-no-meio-da-senha')
await esperar()
assert.deepEqual(api.chamadas.filter((c) => c.startsWith('POST')), ['POST /api/auth/aceitar-convite'], 'com a senha no ar, o toque no Google não sai')
aceite.soltar(resposta(200, { data: null }))
await esperar()
assert.match(textoDe(tela), /Senha criada/)
tela.desmontar()
ok('com a senha sendo criada, tocar no Google não abre uma segunda entrada')

console.log(`\n${passos} passos verdes.`)
process.exit(0)
