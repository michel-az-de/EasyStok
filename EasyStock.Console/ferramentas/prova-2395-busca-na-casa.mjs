import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

// #2478: a busca usa o domínio de pedidos, que importa sem extensão (padrão do Vite); o Node puro precisa do `.js`.
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})
const { categoriasDaBusca, resultadoDaBusca, requisicoesDaBusca } = await import('../src/dominio/buscaNaCasa.js')

const acesso = { permite: id => ['cadastros', 'atendimento'].includes(id), acoes: { verCadastrosProdutos: true, verPedidos: true } }
assert.deepEqual(categoriasDaBusca(acesso), ['clientes', 'conversas', 'pedidos', 'produtos'])
// #2478: sem `verPedidos` (Visualizador) a busca não pede pedidos, que a API negaria com 403.
assert.deepEqual(categoriasDaBusca({ ...acesso, acoes: { verCadastrosProdutos: true } }), ['clientes', 'conversas', 'produtos'])
assert.equal(resultadoDaBusca('pedidos', { id: 'p', status: 'saiu_para_entrega' }).descricao.includes('saiu_para_entrega'), false, 'situação legível, não o código')
assert.deepEqual(categoriasDaBusca({ permite: () => false, acoes: {} }), [])
assert.deepEqual(categoriasDaBusca({ permite: id => id === 'cadastros', acoes: {} }), ['clientes'])
const consultas = requisicoesDaBusca(' +55 (11) 99999-1234 ', acesso, 'empresa')
assert.equal(consultas.length, 4)
for (const c of consultas) {
  const url = new URL(c.url, 'https://local.invalid')
  assert.equal(url.searchParams.get(c.tipo === 'conversas' ? 'q' : c.tipo === 'pedidos' ? 'search' : 'termo'), '+55 (11) 99999-1234')
  assert.equal(url.searchParams.get(c.tipo === 'clientes' ? 'max' : c.tipo === 'pedidos' ? 'pageSize' : 'limite'), '10')
}
assert.deepEqual(requisicoesDaBusca('', acesso, 'empresa'), [])
const pessoa = resultadoDaBusca('clientes', { id: 'a', nome: 'João', telefone: '11999991234' })
assert.equal(pessoa.id, 'clientes:a')
assert.equal(pessoa.rotulo, 'João')
const conversa = resultadoDaBusca('conversas', { id: 'a', contatoNome: 'João', contatoIdExterno: '5511999991234' })
assert.notEqual(pessoa.id, conversa.id, 'Identidades entre categorias nunca colidem')
assert.equal(conversa.item.id, 'a')
assert.match(resultadoDaBusca('pedidos', { id: 'p', numeroDoDia: 42, clienteNome: 'João' }).rotulo, /42/)
assert.match(resultadoDaBusca('pedidos', { id: 'p', clienteNome: 'João' }).descricao, /p/, 'Pedido sem número continua identificável')
assert.equal(resultadoDaBusca('produtos', { id: 'x', nome: 'Lasanha' }).item.id, 'x')
assert.throws(() => resultadoDaBusca('clientes', { nome: 'Sem ID' }), /identidade/)
console.log('Busca na Casa: capacidades, contratos reais, identidade por categoria e telefone preservado passaram.')
