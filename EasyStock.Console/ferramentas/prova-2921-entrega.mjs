// #2921 (fase 1): a janela "Novo pedido" pede o endereço de entrega como a comanda do Atendimento (a API usa o endereço
// do cadastro do cliente; o Console o cadastra pelo mesmo `editarDadoCliente('endereco')` da Ficha). Entrega exige
// endereço; o 409 da simulação e do Registrar aparece na janela, com o caminho para resolver; o rascunho guarda
// entrega e endereço. Sem servidor.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
  load(url, c, next) { return url.endsWith('/infra/fonteDados.js') ? { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" } : next(url, c) },
})
const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
const j = await import('../src/aplicacao/janelaDoPedido.js')

const e409 = Object.assign(new Error('Cadastre o endereço de entrega do cliente antes de gerar o pedido.'), { status: 409 })
const outro409 = Object.assign(new Error('Janela esgotada.'), { status: 409 })

// 1. Entrega exige endereço; retirada não; o 409 de endereço trava o Continuar.
assert.equal(j.faltaEndereco(e409), true)
assert.equal(j.faltaEndereco(outro409), false, 'outro 409 não é de endereço')
assert.equal(j.faltaEndereco(null), false)
const pedido = { janela: 'j1', meio: 'pix' }
assert.equal(j.podeAvancarDaEntrega({ pedido, entrega: 'entrega', erroSimulacao: e409 }), false, 'entrega sem endereço não avança')
assert.equal(j.podeAvancarDaEntrega({ pedido, entrega: 'retirada', erroSimulacao: e409 }), true, 'retirada não exige endereço')
assert.equal(j.podeAvancarDaEntrega({ pedido, entrega: 'entrega', erroSimulacao: null }), true)
assert.equal(j.podeAvancarDaEntrega({ pedido: { janela: 'j1' }, entrega: 'entrega', erroSimulacao: null }), false, 'sem meio não avança')

// 2. Cadastrar o endereço usa a ação da Ficha, com a identidade da conversa; recusa vira texto.
{
  const chamadas = []
  const conversa = { id: 'c1', clienteId: 'cli1', vinculoRevisao: 3, nome: 'Ana' }
  const acoes = { editarDadoCliente: async (...a) => { chamadas.push(a); return undefined } }
  const r = await j.cadastrarEnderecoDaJanela({ acoes, conversa, texto: ' Rua A, 10, Centro ' })
  assert.deepEqual(r, { ok: true })
  assert.deepEqual(chamadas[0].slice(0, 3), ['c1', 'endereco', 'Rua A, 10, Centro'])
  assert.equal(chamadas[0][4].clienteId, 'cli1')
  assert.deepEqual(await j.cadastrarEnderecoDaJanela({ acoes, conversa, texto: '  ' }), { erro: 'Escreva o endereço de entrega.' })
  const recusa = await j.cadastrarEnderecoDaJanela({ acoes: { editarDadoCliente: async () => ({ erro: 'CEP inválido' }) }, conversa, texto: 'x' })
  assert.deepEqual(recusa, { erro: 'CEP inválido' })
  const queda = await j.cadastrarEnderecoDaJanela({ acoes: { editarDadoCliente: async () => { throw new Error('rede') } }, conversa, texto: 'x' })
  assert.deepEqual(queda, { erro: 'rede' })
}

// 3. O 409 do Registrar volta como texto (e o que é de endereço aponta o caminho).
{
  const r = await j.registrarDaJanela({ acoes: { gerarPedido: async () => ({ erro: e409.message }) }, janela: { conversaId: 'c1' }, meio: 'pix' })
  assert.match(r.erro, /endereço/i)
  assert.match(j.textoDoErro(e409), /Cadastre o endereço/)
  assert.match(j.textoDoErro(e409), /Entrega e pagamento/, 'diz onde resolver')
  assert.match(j.textoDoErro(outro409), /Janela esgotada/)
  assert.match(j.textoDoErro({ status: 400 }), /Não foi possível/, '4xx sem mensagem ainda diz algo')
}

// 4. O rascunho guarda entrega e endereço, e a volta os devolve.
{
  const janela = { clienteId: 'cli1', nome: 'Ana' }
  const r = j.rascunhoDaMontagem(janela, { itens: [{ sku: 'a', qtd: 1 }], janela: 'j1', meio: 'pix' }, 'entrega', [], { entrega: 'retirada', endereco: 'Rua A, 10' })
  assert.equal(r.entrega, 'retirada')
  assert.equal(r.endereco, 'Rua A, 10')
  const volta = j.restaurarRascunho(r, [{ sku: 'a', nome: 'A', preco: 1, ativo: true }])
  assert.equal(volta.entrega, 'retirada')
  assert.equal(volta.endereco, 'Rua A, 10')
}

// 5. A janela mostra o erro da simulação (hook com erro) e o bloco de endereço, na tela.
{
  const hook = fonte('aplicacao/useSimulacaoDoPedido.js')
  assert.match(hook, /export function useSimulacaoDoPedidoComErro/)
  const jsx = fonte('app/NovoPedidoJanela.jsx')
  assert.match(jsx, /useSimulacaoDoPedidoComErro/)
  assert.match(jsx, /podeAvancarDaEntrega/)
  assert.match(jsx, /cadastrarEnderecoDaJanela/)
  assert.match(jsx, /role="alert"/)
}
console.log('prova-2921-entrega: ok')
