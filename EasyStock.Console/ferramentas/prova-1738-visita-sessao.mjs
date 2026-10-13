import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

registerHooks({ resolve(id, contexto, proximo) {
  if (id.startsWith('.') && !/\.[a-z]+$/i.test(id)) {
    try { return proximo(id + '.js', contexto) } catch { /* Resolução original. */ }
  }
  return proximo(id, contexto)
} })
const { gravarVisita, lerVisita, esquecerVisitas } = await import('../src/infra/api/visitaDaSessao.js')
const dados = new Map()
globalThis.sessionStorage = {
  get length() { return dados.size }, key: i => [...dados.keys()][i],
  getItem: k => dados.get(k) ?? null, setItem: (k, v) => dados.set(k, v), removeItem: k => dados.delete(k),
}
const sessao = { empresa: { id: 'empresa-a' }, usuario: { id: 'usuario-a' } }
const vazio = { ultimaConversa: null, comandas: {} }
gravarVisita(sessao, { ultimaConversa: 'conversa-a', comandas: { 'conversa-a': {
  numero: 72, itens: [{ sku: 'prato', qtd: 2, obs: 'Molho à parte', nome: 'Não guardar nome' }],
  janela: 'janela-dia', meio: 'pix', observacoes: 'Sem talheres',
  cliente: { nome: 'Não guardar cliente', telefone: 'Não guardar telefone', endereco: 'Não guardar endereço' },
  token: 'Não guardar token',
} } })
const restaurada = lerVisita(sessao)
assert.equal(restaurada.ultimaConversa, 'conversa-a')
assert.deepEqual(restaurada.comandas['conversa-a'].itens, [{ sku: 'prato', qtd: 2, obs: 'Molho à parte' }])
assert.equal(restaurada.comandas['conversa-a'].janela, 'janela-dia')
assert.equal(restaurada.comandas['conversa-a'].meio, 'pix')
assert.equal(restaurada.comandas['conversa-a'].observacoes, 'Sem talheres')
assert.equal([...dados.values()][0].includes('Não guardar'), false, 'Não armazena cadastro, endereço, nome do prato ou token')
assert.deepEqual(lerVisita({ ...sessao, empresa: { id: 'outra-empresa' } }), vazio)
assert.deepEqual(lerVisita({ ...sessao, usuario: { id: 'outro-usuario' } }), vazio)
dados.set('null', [...dados.values()][0])
assert.deepEqual(lerVisita(null), vazio, 'Sem identidade não lê rascunhos')
gravarVisita(sessao, { comandas: { criado: { pedidoId: 'pedido-servidor', itens: [{ sku: 'prato', qtd: 1 }] },
  invalido: { itens: [null, { sku: 'prato', qtd: -1 }] } } })
assert.deepEqual(lerVisita(sessao), vazio, 'Pedido persistido e linha inválida não viram rascunho')
dados.set('easystok.visita:empresa-a:usuario-a', '{json inválido')
assert.deepEqual(lerVisita(sessao), vazio)
dados.set('easystok.sessao', 'sessão existente')
esquecerVisitas()
assert.equal([...dados.keys()].some(k => k.startsWith('easystok.visita:')), false)
assert.equal(dados.get('easystok.sessao'), 'sessão existente', 'Limpeza da visita não cria nem altera tokens')
globalThis.sessionStorage = { getItem() { throw new Error('Storage bloqueado') }, setItem() { throw new Error('Storage bloqueado') } }
assert.deepEqual(lerVisita(sessao), vazio)
assert.doesNotThrow(() => gravarVisita(sessao, restaurada))
console.log('ok restauração completa, isolamento por empresa/usuário, ausência de cadastro/token, rejeição de pedido e dados inválidos, limpeza e storage bloqueado')
