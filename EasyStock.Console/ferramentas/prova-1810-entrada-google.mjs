/* eslint-disable no-console */
// Prova #1810 (revisão gravada de 14/10): com a conta Google já escolhida antes, o console entra
// sozinho (One Tap com auto_select); depois de "Sair" nesta aba, não entra sozinho de novo até a
// pessoa escolher a conta outra vez. #2070: a tela do convite também oferece o Google, sem entrar sozinha.
//
//   node ferramentas/prova-1810-entrada-google.mjs

import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
import { readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { transformWithOxc } from 'vite'

const guardado = new Map()
globalThis.sessionStorage = {
  getItem: (k) => (guardado.has(k) ? guardado.get(k) : null),
  setItem: (k, v) => guardado.set(k, String(v)),
  removeItem: (k) => guardado.delete(k),
}
const chamadas = []
globalThis.window = { google: { accounts: { id: { disableAutoSelect: () => chamadas.push('disableAutoSelect') } } } }

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

globalThis.sessionStorage = { getItem: () => { throw new Error('bloqueado') }, setItem: () => { throw new Error('bloqueado') }, removeItem: () => {} }
assert.doesNotThrow(() => travarEntradaSozinha())
assert.equal(opcoesDaEntradaGoogle().auto_select, true)
ok('sem armazenamento do navegador, não quebra')

// #2070: a tela do link do convite também oferece "Entrar com Google". Quem recebe o convite entra
// com a conta Google do e-mail convidado; a API só aceita com o convite aberto e não vencido.
const { googleDoConvite, fraseDaFalhaDoGoogleNoConvite } = await import('../src/dominio/usuarios.js')
const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8').replaceAll(String.fromCharCode(13), '')

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
assert.deepEqual(concluidos, ['convite concluído'], 'depois de entrar, o convite guardado é limpo e a tela vai para o hall')
ok('entrar com Google no convite usa a entrada da sessão e só então conclui o convite')

const vencido = Object.assign(new Error('Seu convite venceu: peça um novo'), { status: 401, codigo: 'CONVITE_VENCIDO' })
const recusa = googleDoConvite({ ...googleDaSessao, entrar: async () => { throw vencido } }, () => concluidos.push('não devia'))
await assert.rejects(() => recusa.entrar('id-token'), (erro) => erro === vencido)
assert.deepEqual(concluidos, ['convite concluído'], 'recusado, o convite continua guardado e a tela fica')
assert.equal(fraseDaFalhaDoGoogleNoConvite(vencido), 'Seu convite venceu: peça um novo')
assert.equal(fraseDaFalhaDoGoogleNoConvite({ status: 401, codigo: 'HTTP_401', message: 'Sessão expirada. Entre de novo.' }),
  'Esta conta Google não tem convite. Use a conta Google do e-mail que recebeu o convite, ou crie uma senha.')
assert.equal(fraseDaFalhaDoGoogleNoConvite({ status: 0, codigo: 'SEM_CONEXAO', message: 'Sem conexão com o EasyStok.' }), 'Sem conexão com o EasyStok.')
ok('recusa do Google no convite fica na tela, com a frase da API (convite vencido) ou o que fazer')

assert.equal(googleDoConvite(null, () => {}), null)
ok('sem Google configurado na sessão, o convite fica só com a senha')

const telaConvite = fonte('features/login/TelaConvite.jsx')
assert.match(telaConvite, /export function TelaConvite\(\{ token, google, aoConcluir \}\)/)
assert.match(telaConvite, /<BotaoGoogle buscarClientId=\{buscarClientId\} aoReceberToken=\{entrarComGoogle\} aoFalhar=\{falhaDoGoogle\} opcoes=\{google\.opcoes\} \/>/)
assert.match(telaConvite, /await google\.entrar\(idToken\)/)
assert.match(telaConvite, /fraseDaFalhaDoGoogleNoConvite\(falha\)/)
ok('a TelaConvite reaproveita o BotaoGoogle com o google da sessão e mostra a recusa')

const app = fonte('app/App.jsx')
const blocoDoConvite = app.slice(app.indexOf('const CHAVE_DO_CONVITE'), app.indexOf('if (FONTE_API && !sessao) return <TelaLogin'))
assert.match(blocoDoConvite, /const concluirConvitePeloGoogle = useCallback\(\(\) => \{\n\s+setTokenGuardado\(null\)\n\s+try \{ sessionStorage\.removeItem\(CHAVE_DO_CONVITE\) \} catch \{ \/\* nada a limpar \*\/ \}\n\s+window\.location\.hash = '#\/'\n\s+\}, \[\]\)/)
assert.match(blocoDoConvite, /const googleNoConvite = useMemo\(\(\) => googleDoConvite\(google, concluirConvitePeloGoogle\), \[google, concluirConvitePeloGoogle\]\)/)
assert.match(blocoDoConvite, /<TelaConvite key=\{token \?\? 'sem-token'\} token=\{token\} google=\{googleNoConvite\} aoConcluir=\{aoConcluir\} \/>/)
// A sessão nova é a da convidada: o caminho do Google não encerra sessão nenhuma.
const caminhoGoogle = blocoDoConvite.slice(blocoDoConvite.indexOf('const concluirConvitePeloGoogle'), blocoDoConvite.indexOf('const googleNoConvite'))
assert.ok(caminhoGoogle.length > 0)
assert.doesNotMatch(caminhoGoogle, /encerrarSessao/)
ok('o App passa o google à TelaConvite e, ao entrar, limpa o convite guardado e vai para #/')

// A tela montada de verdade (react-dom/server, JSX pelo transformador do Vite), com e sem o google da sessão.
const raiz = fileURLToPath(new URL('../src', import.meta.url))
const jsx = new Map(await Promise.all(readdirSync(raiz, { recursive: true }).filter((n) => n.endsWith('.jsx')).map(async (nome) => {
  const arquivo = join(raiz, nome)
  return [pathToFileURL(arquivo).href, (await transformWithOxc(readFileSync(arquivo, 'utf8'), arquivo, { jsx: { runtime: 'automatic' } })).code]
})))
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      for (const ext of ['.js', '.jsx']) { try { return proximo(spec + ext, contexto) } catch { /* próximo formato */ } }
    }
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (jsx.has(url)) return { format: 'module', source: jsx.get(url), shortCircuit: true }
    if (url.endsWith('.css')) return { format: 'module', source: 'export default new Proxy({}, { get: (_, p) => String(p) })', shortCircuit: true }
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', source: 'export const FONTE_API = true; export const API_BASE = ""', shortCircuit: true }
    return proximo(url, contexto)
  },
})
const { createElement: h } = await import('react')
const { renderToStaticMarkup } = await import('react-dom/server')
const { TelaConvite } = await import('../src/features/login/TelaConvite.jsx')
const comGoogle = renderToStaticMarkup(h(TelaConvite, { token: 'token-de-teste', google: noConvite, aoConcluir() {} }))
assert.match(comGoogle, /<div class="googleDoConvite" aria-busy="false">/)
assert.match(comGoogle, /Criar senha/)
// Antes do ClientId chegar, o texto não promete o Google (sem ClientId o botão nem aparece).
assert.match(comGoogle, /Escolha a senha que você vai usar para entrar\./)
const semGoogle = renderToStaticMarkup(h(TelaConvite, { token: 'token-de-teste', google: null, aoConcluir() {} }))
assert.doesNotMatch(semGoogle, /googleDoConvite/)
assert.match(semGoogle, /Criar senha/)
ok('a tela do convite monta com o google da sessão e sem ele')

console.log(`\n${passos} passos verdes.`)
