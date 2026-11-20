// #2930: cliente sem telefone no cadastro travava o Novo pedido ("Corrija o telefone na Ficha"). O Novo pedido de Pedidos
// pede o celular ali mesmo, e a API grava no cadastro antes de abrir a conversa. HTTP controlado, sem servidor.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
  load(url, c, next) { return url.endsWith('/infra/fonteDados.js') ? { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" } : next(url, c) },
})
const { erroDoCelular } = await import('../src/dominio/celularDoPedido.js')
const { abrirConversaDoCliente } = await import('../src/infra/api/conversasApi.js')

// 1. Só celular BR com DDD passa; o fixo e o número curto dizem o que falta.
assert.equal(erroDoCelular('(11) 98888-7777'), null)
assert.equal(erroDoCelular('+55 11 98888-7777'), null)
assert.match(erroDoCelular(''), /celular com DDD/i)
assert.match(erroDoCelular('98888-7777'), /DDD/)
assert.match(erroDoCelular('(11) 3333-4444'), /celular/i)

// 2. O celular vai no corpo de do-cliente; sem ele o corpo segue só com o cliente.
globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 't', expiraEm: Date.now() + 60000, empresa: { id: 'e' } }) }
const corpos = []
globalThis.fetch = async (url, opcoes = {}) => { corpos.push([String(url), JSON.parse(opcoes.body)]); return new Response(JSON.stringify({ data: { id: 'conversa' } })) }
await abrirConversaDoCliente('c1', '(11) 98888-7777')
await abrirConversaDoCliente('c2')
assert.deepEqual(corpos.map(([, corpo]) => corpo), [{ clienteId: 'c1', celular: '(11) 98888-7777' }, { clienteId: 'c2' }])
assert.ok(corpos.every(([url]) => url.endsWith('/api/atendimento/conversas/do-cliente')))

// 3. O campo "Celular com DDD" mora num componente só, usado na escolha do cliente (Pedidos) e na linha de Clientes.
const ler = (caminho) => readFileSync(new URL(`../src/features/gestao/cadastros/${caminho}`, import.meta.url), 'utf8')
const campo = ler('CelularDoPedido.jsx')
assert.match(campo, /rotulo="Celular com DDD"/)
assert.match(campo, /novoPedido\.iniciar\(clienteId, \{ celular: celular\.texto \}\)/)
assert.match(ler('EscolherClienteDoPedido.jsx'), /<CelularDoPedido clienteId=\{cliente\.id\}/)
const painel = ler('PainelCadastros.jsx')
assert.match(painel, /telefoneCadastro\(item\) \? novoPedido\.iniciar\(id\) : pedirCelular\(/, 'linha sem telefone pede o celular')
assert.match(painel, /<PedirCelularDoPedido cliente=\{celularDe\}/)
const hook = readFileSync(new URL('../src/aplicacao/novoPedidoDoCliente.js', import.meta.url), 'utf8')
assert.match(hook, /abrirConversaDoCliente\(clienteId, celular\)/)

console.log('prova-2930: 3 blocos ok')
