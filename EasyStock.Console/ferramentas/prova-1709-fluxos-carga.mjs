/* eslint-disable no-console */
// Prova da #1709 (console tela por tela sob carga): o que quebrou na passagem pela tela real, em
// função pura. Os componentes só chamam essas funções.
//
//   node ferramentas/prova-1709-fluxos-carga.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const ficha = await import('../src/dominio/ficha.js')
const pedido = await import('../src/dominio/pedido.js')
const kds = await import('../src/dominio/kds.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }
const funcao = (modulo, caminho, nome) => {
  assert.equal(typeof modulo[nome], 'function', `${caminho} ainda não exporta ${nome}`)
  return modulo[nome]
}

const CONVERSA = '1ec6ad41-4b4e-406c-8289-c3f8a55c2ea0'
const PEDIDO = '3de01345-6a53-4f33-8018-118308d1d2cd'

// 1. Blocos irmãos da Ficha com a mesma chave: o React avisava a cada render e a seção
//    "Ocorrências" aparecia duas vezes com o pedido criado.
confere('cada bloco irmão da Ficha tem chave própria, com e sem pedido', () => {
  const chavesDaFicha = funcao(ficha, 'dominio/ficha.js', 'chavesDaFicha')
  for (const pedidoId of [PEDIDO, null]) {
    const chaves = Object.values(chavesDaFicha({ conversaId: CONVERSA, pedidoId, pagamentos: ['p1', 'p2'] }))
    assert.ok(chaves.length >= 6, 'faltou bloco na conta')
    assert.equal(new Set(chaves).size, chaves.length, `chave repetida: ${chaves.join(' | ')}`)
  }
})

confere('a chave ainda troca com a conversa, o pedido e os pagamentos (o bloco remonta)', () => {
  const chavesDaFicha = funcao(ficha, 'dominio/ficha.js', 'chavesDaFicha')
  const base = chavesDaFicha({ conversaId: CONVERSA, pedidoId: PEDIDO, pagamentos: ['p1'] })
  const outraConversa = chavesDaFicha({ conversaId: 'outra', pedidoId: PEDIDO, pagamentos: ['p1'] })
  const outroPedido = chavesDaFicha({ conversaId: CONVERSA, pedidoId: 'outro', pagamentos: ['p1'] })
  const maisUmPagamento = chavesDaFicha({ conversaId: CONVERSA, pedidoId: PEDIDO, pagamentos: ['p1', 'p2'] })
  assert.notEqual(base.cobranca, outraConversa.cobranca)
  assert.notEqual(base.comandaCompartilhada, outraConversa.comandaCompartilhada)
  assert.notEqual(base.ocorrenciasApi, outroPedido.ocorrenciasApi)
  assert.notEqual(base.frete, outroPedido.frete)
  assert.notEqual(base.devolucoes, maisUmPagamento.devolucoes)
})

// 2. Sem cotação: o EasyStok responde com a frase do cliente ("Vamos combinar a
//    entrega e o frete com a equipe."). Quem lê no console É a equipe, e ficava sem saída.
confere('pedido recusado sem cotação diz ao operador o que fazer sem presumir área', () => {
  const motivo = funcao(pedido, 'dominio/pedido.js', 'motivoDoPedidoNaoCriado')
  const texto = motivo({ status: 409, message: 'Vamos combinar a entrega e o frete com a equipe.' })
  assert.match(texto, /^Pedido não criado: /)
  assert.match(texto, /frete está a combinar/)
  assert.doesNotMatch(texto, /fora da área de entrega/)
  assert.match(texto, /Link de compra/)
  assert.match(texto, /Janelas de entrega/)
  assert.doesNotMatch(texto, /com a equipe/)
})

confere('outros motivos seguem com a mensagem do EasyStok', () => {
  const motivo = funcao(pedido, 'dominio/pedido.js', 'motivoDoPedidoNaoCriado')
  assert.equal(motivo({ status: 409, message: 'A janela escolhida não tem mais vaga.' }),
    'Pedido não criado: A janela escolhida não tem mais vaga.')
  assert.equal(motivo({ status: 0, message: 'Sem conexão com o EasyStok.' }),
    'Pedido não criado: Sem conexão com o EasyStok.')
})

// 3. A fila da cozinha vem cortada em 200 pedidos, dos mais antigos para os mais novos. Com a
//    fila no teto, o pedido novo fica fora da tela sem ninguém saber.
confere('fila da cozinha no teto avisa que há pedido fora da tela', () => {
  const aviso = funcao(kds, 'dominio/kds.js', 'avisoDeFilaNoTeto')
  assert.equal(aviso(0), null)
  assert.equal(aviso(199), null)
  const texto = aviso(200)
  assert.match(texto, /200 pedidos/)
  assert.match(texto, /mais novos/)
  assert.ok(aviso(250), 'acima do teto também avisa')
})

console.log(`\n${passou} verificações passaram.`)
