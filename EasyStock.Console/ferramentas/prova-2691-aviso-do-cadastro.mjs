/* eslint-disable no-console */
// Prova da #2691 (homologação #1769, Pedidos): no chat do site, Salvar cadastro sem nome avisa
// "Escreva o nome do cliente (lápis…)". Escrito o nome e salvo, o aviso seguia na barra, contradizendo o
// "Salvo" da ficha. A regra é de `casos/api.js`: a ação que deu certo apaga o aviso de ação; o fixo fica.
//
// Roda com: node ferramentas/prova-2691-aviso-do-cadastro.mjs
// Contra outra árvore (ex.: o master, para ver o Red): PROVA_SRC=<pasta src> node ferramentas/prova-2691-aviso-do-cadastro.mjs
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
import { pathToFileURL } from 'node:url'

// A API de conversas responde o cadastro salvo e o dossiê; nada vai à rede.
const CONVERSAS_API = `
export const cadastrarClienteDaConversa = async () => ({ clienteId: 'cli-1', vinculoRevisao: 1, novo: true, nome: 'Bruna' })
export const obterDossie = async () => ({})
export const adicionarNotaCliente = async () => ({})
export const bloquearClienteApi = async () => ({})
export const desbloquearClienteApi = async () => ({})
`
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    if (url.endsWith('/infra/api/conversasApi.js')) return { format: 'module', shortCircuit: true, source: CONVERSAS_API }
    return proximo(url, contexto)
  },
})
const base = process.env.PROVA_SRC ? pathToFileURL(process.env.PROVA_SRC.replace(/[\\/]?$/, '/')) : new URL('../src/', import.meta.url)
const { criarAcoesClienteApi } = await import(new URL('aplicacao/api/cliente.js', base).href)
const { casosApi } = await import(new URL('aplicacao/casos/api.js', base).href)
const acao = await import(new URL('aplicacao/acoes.js', base).href)

let falhas = 0
const caso = async (nome, fn) => { try { await fn(); console.log(`ok   ${nome}`) } catch (e) { falhas++; console.log(`FALHA ${nome}\n     ${e.message}`) } }

// Estado mínimo: a conversa do visitante e a faixa de avisos. O despachar aplica AVISO_API e FECHAR_AVISO_API
// pelos mesmos casos da tela; o resto só registra.
function montar() {
  const estado = {
    conversas: [{ id: 'c1', canal: 'Chat do site', nome: 'Visitante do site', clienteId: null, vinculoRevisao: 0, nomeDaDona: false, contatoInformado: null, cliente: {} }],
    sincronizacao: { aviso: null, avisoFixo: 'Loja online desligada.' },
  }
  const estadoRef = { current: estado }
  const despachar = (a) => {
    if (a.tipo === acao.AVISO_API || a.tipo === acao.FECHAR_AVISO_API) estadoRef.current = casosApi[a.tipo](estadoRef.current, a)
  }
  return { estadoRef, acoes: criarAcoesClienteApi({ despachar, estadoRef }) }
}
const identidade = (c) => ({ conversaId: c.id, clienteId: c.clienteId, vinculoRevisao: c.vinculoRevisao, nome: c.nome })

await caso('sem nome, o Salvar avisa como escrever o nome', async () => {
  const { estadoRef, acoes } = montar()
  const c = estadoRef.current.conversas[0]
  const r = await acoes.salvarCadastroRapido('c1', { nome: 'Visitante do site', telefone: '11987650027' }, null, identidade(c))
  assert.match(r?.erro ?? '', /lápis/)
  assert.match(estadoRef.current.sincronizacao.aviso ?? '', /lápis/)
})

await caso('#2691: escrito o nome e salvo, o aviso de ação sai e o fixo fica', async () => {
  const { estadoRef, acoes } = montar()
  const c = estadoRef.current.conversas[0]
  await acoes.salvarCadastroRapido('c1', { nome: 'Visitante do site', telefone: '11987650027' }, null, identidade(c))
  // A dona escreve o nome no lápis.
  estadoRef.current = { ...estadoRef.current, conversas: [{ ...c, nome: 'Bruna', nomeDaDona: true }] }
  const r = await acoes.salvarCadastroRapido('c1', { nome: 'Bruna', telefone: '11987650027' }, null, identidade(c))
  assert.equal(r, undefined, `cadastro recusado: ${r?.erro}`)
  assert.equal(estadoRef.current.sincronizacao.aviso, null, `o aviso ficou: "${estadoRef.current.sincronizacao.aviso}"`)
  assert.equal(estadoRef.current.sincronizacao.avisoFixo, 'Loja online desligada.')
})

console.log(falhas ? `\n${falhas} falha(s)` : '\nprova-2691 ok')
process.exit(falhas ? 1 : 0)
