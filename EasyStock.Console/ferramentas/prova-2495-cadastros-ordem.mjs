/* eslint-disable no-console */
// #2495 (S1, I6): ordem das listas de Cadastros. Cada tela só oferece o que a API ordena, a ordem padrão não
// manda `sort` (a lista abre como hoje) e o cabeçalho Nome alterna A a Z e Z a A.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})
const { ORDENS_CADASTRO, ordemDoCabecalhoNome, parametrosDaOrdem, sortDoNome } = await import('../src/dominio/cadastros.js')

// Só valores que o repositório da API conhece (ClienteRepository, FornecedorRepository, ProdutoRepository).
const ACEITOS = { clientes: ['nome', 'criadoem', 'lastorderat', 'ordercount'], fornecedores: ['nome', 'criadoem'], produtos: ['nome', 'criadoem', 'alteradoem'] }
for (const [tela, aceitos] of Object.entries(ACEITOS)) {
  const ordens = ORDENS_CADASTRO[tela]
  assert.ok(ordens.length >= 3, `${tela}: tem escolha de ordem`)
  assert.equal(ordens[0].valor, '', `${tela}: a primeira é a padrão da API`)
  assert.deepEqual(parametrosDaOrdem(tela, ''), {}, `${tela}: a padrão não manda sort`)
  assert.equal(new Set(ordens.map((o) => o.valor)).size, ordens.length, `${tela}: valores únicos`)
  for (const o of ordens.slice(1)) {
    const p = parametrosDaOrdem(tela, o.valor)
    assert.ok(aceitos.includes(p.sort), `${tela}/${o.valor}: sort ${p.sort} existe na API`)
    assert.ok(['asc', 'desc'].includes(p.order), `${tela}/${o.valor}: order válido`)
  }
}
// Postgres põe NULL primeiro no DESC: "último pedido" decrescente abriria com quem nunca comprou. Só o crescente.
assert.ok(!ORDENS_CADASTRO.clientes.some((o) => o.sort === 'lastorderat' && o.order === 'desc'), 'Sem lastorderat desc')
assert.deepEqual(parametrosDaOrdem('clientes', 'mais-pedidos'), { sort: 'ordercount', order: 'desc' })
// Ordem que não existe (memória velha) cai na padrão em vez de mandar lixo.
assert.deepEqual(parametrosDaOrdem('clientes', 'inexistente'), {})
assert.deepEqual(parametrosDaOrdem('bancos', 'nome-za'), {}, 'Tela sem ordem não manda sort')

// Cabeçalho Nome: estado para aria-sort e próximo clique.
assert.equal(sortDoNome('clientes', ''), 'ascending', 'Clientes já abre por nome A a Z')
assert.equal(sortDoNome('fornecedores', ''), 'none', 'Fornecedores abre por cadastro mais recente')
assert.equal(sortDoNome('produtos', 'nome-za'), 'descending')
assert.equal(ordemDoCabecalhoNome('clientes', ''), 'nome-za', 'Clientes por nome A a Z: o clique inverte')
assert.equal(ordemDoCabecalhoNome('clientes', 'nome-za'), '', 'Volta ao A a Z, que é a padrão de clientes')
assert.equal(ordemDoCabecalhoNome('fornecedores', ''), 'nome-az')
assert.equal(ordemDoCabecalhoNome('fornecedores', 'nome-az'), 'nome-za')
assert.equal(ordemDoCabecalhoNome('produtos', 'nome-za'), 'nome-az')
assert.equal(ordemDoCabecalhoNome('produtos', 'alterados'), 'nome-az')
console.log('#2495 S1 domínio: ordens só com sort da API, padrão sem sort e cabeçalho Nome alternando.')
