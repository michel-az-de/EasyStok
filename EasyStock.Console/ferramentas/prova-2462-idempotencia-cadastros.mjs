// #2462 (M5 da R4): criar cliente, fornecedor, produto ou modelo manda Idempotency-Key estável por abertura do
// editor. Depois de um timeout que gravou, salvar de novo devolve o mesmo cadastro em vez de criar outro.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(nome, contexto, proximo) {
    if (nome.startsWith('.') && !/\.[a-z]+$/i.test(nome)) { try { return proximo(nome + '.js', contexto) } catch { /* resolução normal */ } }
    return proximo(nome, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = '';" }
    return proximo(url, contexto)
  },
})
const { salvarCadastro, contextoCadastro } = await import('../src/infra/api/cadastrosApi.js')
const { bloqueiaRepeticaoDoCadastro, novoModeloCadastro } = await import('../src/dominio/cadastros.js')

const chamadas = []
globalThis.fetch = async (url, { method = 'GET', headers = {} } = {}) => {
  chamadas.push({ url, metodo: method, chave: headers['Idempotency-Key'] ?? null })
  return new Response(JSON.stringify({ data: { id: 'novo-1' } }), { status: 200 })
}
const contexto = { ...contextoCadastro(), empresaId: 'empresa-1' }
const ultima = () => chamadas.at(-1)

for (const tela of ['clientes', 'fornecedores', 'produtos']) {
  const dados = { nome: 'Novo', categoriaId: 'cat-1', status: 'Inativo', requestId: `op-${tela}` }
  await salvarCadastro(tela, dados, contexto)
  assert.equal(ultima().metodo, 'POST')
  assert.equal(ultima().chave, `op-${tela}`, `${tela}: a criação manda a chave da abertura do editor`)
  await salvarCadastro(tela, { ...dados }, contexto)
  assert.equal(ultima().chave, `op-${tela}`, `${tela}: salvar de novo na mesma abertura repete a chave`)
}

await salvarCadastro('clientes', { id: 'c1', revisao: 1, nome: 'Editado', requestId: 'op-x' }, contexto)
assert.equal(ultima().metodo, 'PATCH')
assert.equal(ultima().chave, null, 'edição não usa a chave de criação')

const modelo = novoModeloCadastro(() => 'op-modelo')
assert.equal(modelo.requestId, 'op-modelo', 'novo modelo nasce com a chave da abertura')
await salvarCadastro('modelos', { ...modelo, nome: 'Aviso', codigo: 'aviso', corpoTemplate: 'Oi' }, contexto)
assert.equal(ultima().chave, 'op-modelo')
assert.notEqual(novoModeloCadastro().requestId, novoModeloCadastro().requestId, 'cada abertura gera outra chave')

// Timeout sem resposta: com a chave, repetir é seguro; sem ela (bancos usa requestId próprio), o bloqueio fica.
const timeout = { status: 0 }
assert.equal(bloqueiaRepeticaoDoCadastro('clientes', { nome: 'x', requestId: 'op' }, timeout), false)
assert.equal(bloqueiaRepeticaoDoCadastro('clientes', { nome: 'x' }, timeout), true)
assert.equal(bloqueiaRepeticaoDoCadastro('clientes', { nome: 'x', requestId: 'op' }, { status: 422, codigo: 'IDEMPOTENCY_PAYLOAD_MISMATCH' }), true,
  'mudou os dados depois do timeout: confira a lista antes de repetir')
assert.equal(bloqueiaRepeticaoDoCadastro('clientes', { id: 'c1', nome: 'x' }, timeout), false, 'edição não bloqueia')
assert.equal(bloqueiaRepeticaoDoCadastro('clientes', { nome: 'x', requestId: 'op' }, { status: 400 }), false)

console.log('ok prova-2462-idempotencia-cadastros')
