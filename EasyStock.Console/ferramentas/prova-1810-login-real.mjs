/* eslint-disable no-console */
// Prova #1810, parte da TelaLogin real (#2167): as entradas por senha e por Google executam a tela e a
// autenticação de verdade; só hooks, elementos e HTTP são controlados. Fica num arquivo próprio porque
// simula o React por hooks, e a prova do convite (#2070) usa o React real com `tela-no-node.mjs`:
// os dois no mesmo processo disputam o módulo `react`.
//
//   node ferramentas/prova-1810-login-real.mjs

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { transformWithOxc } from 'vite'

let passos = 0
const ok = (texto) => { passos += 1; console.log(`ok ${passos} - ${texto}`) }

// Executa TelaLogin e autenticação reais; somente hooks/elementos e HTTP são controlados.
// As duas entradas gravam a sessão real em armazenamento isolado, sem rede ou conta real.
let hostAtual
const hooks = {
  useState(inicial) {
    const h = hostAtual, indice = h.indice++
    if (!(indice in h.valores)) h.valores[indice] = typeof inicial === 'function' ? inicial() : inicial
    return [h.valores[indice], valor => { h.valores[indice] = typeof valor === 'function' ? valor(h.valores[indice]) : valor }]
  },
  useRef(inicial) { const h = hostAtual, indice = h.indice++; return h.valores[indice] ??= { current: inicial } },
  useCallback(fn) { return fn },
}
globalThis.__provaEntrada = hooks
const arquivoLogin = new URL('../src/features/login/TelaLogin.jsx', import.meta.url)
const compilado = (await transformWithOxc(readFileSync(arquivoLogin, 'utf8'), arquivoLogin.pathname, { jsx: { runtime: 'automatic' } })).code
const codigo = texto => 'data:text/javascript,' + encodeURIComponent(texto)
registerHooks({
  resolve(spec, ctx, proximo) {
    if (spec === 'react') return { url: codigo('const h=globalThis.__provaEntrada;export const useState=h.useState,useRef=h.useRef,useCallback=h.useCallback'), shortCircuit: true }
    if (spec === 'react/jsx-runtime') return { url: codigo('export const jsx=(type,props)=>({type,props});export const jsxs=jsx'), shortCircuit: true }
    for (const nome of ['Botao', 'CampoTexto', 'Marca', 'InterruptorTema', 'BotaoGoogle']) {
      if (spec.endsWith('/' + (nome === 'CampoTexto' ? 'Campo' : nome))) return { url: codigo(`export const ${nome}='${nome}'`), shortCircuit: true }
    }
    if (spec.endsWith('.css')) return { url: codigo('export default {}'), shortCircuit: true }
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) return proximo(spec + '.js', ctx)
    return proximo(spec, ctx)
  },
  load(url, ctx, proximo) {
    if (url === arquivoLogin.href) return { format: 'module', shortCircuit: true, source: compilado }
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const API_BASE=''" }
    return proximo(url, ctx)
  },
})
const { TelaLogin } = await import(arquivoLogin.href)
const { entrar, entrarComGoogle } = await import('../src/infra/api/autenticacao.js')
const { lerSessao, limparSessao } = await import('../src/infra/api/sessao.js')
const armazenamento = () => {
  const valores = new Map()
  return { getItem: chave => valores.get(chave) ?? null, setItem: (chave, valor) => valores.set(chave, String(valor)), removeItem: chave => valores.delete(chave) }
}
globalThis.sessionStorage = armazenamento()
globalThis.localStorage = armazenamento()
globalThis.window = new EventTarget()
const esperar = async () => { for (let i = 0; i < 30; i++) await Promise.resolve() }
const respostaLogin = usuario => new Response(JSON.stringify({ data: {
  token: `cab.${Buffer.from(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600, empresaId: 'empresa-fixture', sub: usuario })).toString('base64url')}.assinatura`,
  refreshToken: 'refresh-fixture', expiresIn: 3600, usuario: { id: usuario, nome: usuario },
} }))
function elementos(no, destino = []) {
  if (Array.isArray(no)) no.forEach(filho => elementos(filho, destino))
  else if (no && typeof no === 'object') { destino.push(no); elementos(no.props?.children, destino) }
  return destino
}
function montarLogin() {
  limparSessao()
  const h = { valores: [], indice: 0, requisicoes: [] }
  globalThis.fetch = (url, opcoes) => new Promise(resolve => h.requisicoes.push({ url, opcoes, resolve }))
  h.renderizar = () => {
    hostAtual = h; h.indice = 0
    h.arvore = TelaLogin({ entrarNaEmpresa: entrar, google: { entrar: entrarComGoogle } })
    return elementos(h.arvore)
  }
  h.encontrar = (tipo, rotulo) => h.renderizar().find(no => no.type === tipo && (!rotulo || no.props.rotulo === rotulo))
  h.encontrar('CampoTexto', 'E-mail').props.onChange({ target: { value: 'qa@example.invalid' } })
  h.encontrar('CampoTexto', 'Senha').props.onChange({ target: { value: 'senha-fixture' } })
  return h
}
for (const primeira of ['senha', 'google']) {
  const h = montarLogin(), nos = h.renderizar()
  const senha = nos.find(no => no.type === 'form').props.onSubmit
  const google = nos.find(no => no.type === 'BotaoGoogle').props.aoReceberToken
  const iniciarSenha = () => senha({ preventDefault() {} })
  const iniciarGoogle = () => google('token-fixture')
  if (primeira === 'senha') { iniciarSenha(); iniciarGoogle() } else { iniciarGoogle(); iniciarSenha() }
  await esperar()
  const primeiraRequisicao = h.requisicoes[0], concorrente = h.requisicoes[1]
  assert.ok(primeiraRequisicao.url.endsWith(primeira === 'senha' ? '/auth/login' : '/auth/google/login'))
  primeiraRequisicao.resolve(respostaLogin(primeira))
  await esperar()
  assert.equal(lerSessao().usuario.id, primeira, 'a primeira entrada conclui a autenticação real')
  if (concorrente) { concorrente.resolve(respostaLogin('concorrente')); await esperar() }
  assert.equal(lerSessao().usuario.id, primeira, 'uma entrada concorrente não pode substituir a conta já autenticada')
  assert.equal(h.requisicoes.length, 1, 'somente uma tentativa chega à fronteira HTTP')
  assert.equal(h.encontrar('form').props['aria-busy'], false)
  ok(`${primeira} pendente bloqueia a outra entrada sem trocar a sessão`)
}
{
  const h = montarLogin()
  h.encontrar('BotaoGoogle').props.aoReceberToken('token-fixture')
  await esperar()
  h.requisicoes[0].resolve(new Response(JSON.stringify({ error: { code: 'RECUSADO', message: 'Entrada recusada fixture' } }), { status: 401 }))
  await esperar()
  assert.equal(lerSessao(), null)
  assert.equal(h.encontrar('form').props['aria-busy'], false)
  assert.equal(h.renderizar().find(no => no.props?.role === 'alert').props.children, 'Entrada recusada fixture')
  h.encontrar('form').props.onSubmit({ preventDefault() {} })
  await esperar()
  h.requisicoes[1].resolve(respostaLogin('retentativa'))
  await esperar()
  assert.equal(lerSessao().usuario.id, 'retentativa')
  assert.equal(h.encontrar('form').props['aria-busy'], false)
  ok('falha de autenticação libera nova tentativa e mantém o erro legível')
}
console.log(`\n${passos} passos verdes.`)
