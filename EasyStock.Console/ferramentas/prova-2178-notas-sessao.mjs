// Hook e memória reais; React reduzido ao ciclo montar/desmontar, sem navegador.
// Regressão: logout pela HOME ocorria sem observador e reapresentava notas ao reentrar.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

const limpezas = []
globalThis.bancaReact = {
  useMemo: f => f(),
  useSyncExternalStore: (_assinar, ler) => ler(),
  useEffect: f => { const limpar = f(); if (limpar) limpezas.push(limpar) },
}
registerHooks({
  resolve(nome, contexto, proximo) {
    if (nome === 'react') return { shortCircuit: true, url: 'banca:react' }
    if (nome.startsWith('.') && !/\.[a-z]+$/i.test(nome)) {
      try { return proximo(nome + '.js', contexto) } catch { /* resolução padrão */ }
    }
    return proximo(nome, contexto)
  },
  load(url, contexto, proximo) {
    if (url === 'banca:react') return { shortCircuit: true, format: 'module', source: 'export const {useMemo,useEffect,useSyncExternalStore}=globalThis.bancaReact' }
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    return proximo(url, contexto)
  },
})

globalThis.window = new EventTarget()
let sessao = { token: 'teste', expiraEm: Date.now() + 600000, empresa: { id: 'empresa-a' }, usuario: { id: 'ana' } }
globalThis.sessionStorage = { getItem: () => sessao ? JSON.stringify(sessao) : null }
globalThis.localStorage = { getItem: () => null }
const { EVENTO_SESSAO_ALTERADA } = await import('../src/infra/api/sessao.js')
const { useRascunhosDeNotas } = await import('../src/aplicacao/useRascunhosDeNotas.js')
const montar = () => useRascunhosDeNotas(sessao)
const desmontar = () => { for (const limpar of limpezas.splice(0)) limpar() }
const mudarSessao = valor => { sessao = valor; window.dispatchEvent(new Event(EVENTO_SESSAO_ALTERADA)) }

const primeira = montar()
const nota = primeira.memoria.abrir('marina', 'Marina', 'Retirar depois das 18h')
primeira.memoria.guardar(nota.chave, { texto: 'Retirar depois das 18h', inicio: 2, fim: 8, rolagem: 72 })
window.dispatchEvent(new Event('storage'))
assert.equal(montar().memoria, primeira.memoria, 'storage não muda identidade nem descarta texto')
mudarSessao({ ...sessao, token: 'renovado' })
assert.equal(montar().memoria, primeira.memoria, 'renovação da mesma pessoa conserva a visita')
assert.deepEqual([nota.inicio, nota.fim, nota.rolagem], [2, 8, 72])

// Sai do Atendimento para a HOME e só então encerra a sessão.
desmontar()
const pessoaAnterior = sessao
mudarSessao(null)
mudarSessao(pessoaAnterior)
const segunda = montar()
assert.equal(segunda.rascunhos.length, 0, 'login da mesma pessoa após sair pela HOME não reabre notas da sessão encerrada')
assert.notEqual(segunda.memoria, primeira.memoria)
const novaNota = segunda.memoria.abrir('marina', 'Marina', 'Texto da nova sessão')
primeira.memoria.concluir(nota.chave, true)
assert.equal(segunda.memoria.ler().rascunhos[0].texto, 'Texto da nova sessão', 'confirmação antiga afeta somente a memória que iniciou a gravação')
assert.equal(segunda.memoria.ler().ativa, novaNota.chave)

desmontar()
mudarSessao({ ...pessoaAnterior, empresa: { id: 'empresa-b' } })
assert.equal(montar().rascunhos.length, 0, 'outra empresa começa sem notas anteriores')
desmontar()
mudarSessao(pessoaAnterior)
assert.equal(montar().rascunhos.length, 0, 'voltar à empresa anterior após troca não restaura a sessão descartada')

const antesDeOutraAba = montar()
antesDeOutraAba.memoria.abrir('carlos', 'Carlos', 'Rascunho antes da saída por outra aba')
desmontar()
sessao = null; window.dispatchEvent(new Event('storage'))
sessao = pessoaAnterior; window.dispatchEvent(new Event('storage'))
assert.equal(montar().rascunhos.length, 0, 'saída e reentrada por outra aba também encerram a memória privada sem editor montado')

console.log('OK #2178: 10 assertivas de sessão das notas, incluindo HOME/desmontagem, reentrada, outra aba, empresa, posição e confirmação tardia. Sem API externa.')
