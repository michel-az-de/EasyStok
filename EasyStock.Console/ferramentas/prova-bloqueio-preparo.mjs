// Prova da issue #3 (RN-14 / US-019): pedido pago de cliente bloqueado não
// entra em preparo; depois de desbloquear, entra. Exercita a função de domínio
// (`dominio/esteira.js: motivoParaNaoAvancar`) e o reducer de verdade.
//
// O código de `src/` importa sem extensão (o Vite resolve, o Node puro não).
// O gancho abaixo tenta `.js` quando o import relativo não tem extensão; nada
// de JSX nem CSS entra na árvore do reducer, então nada mais é preciso.
//
// Roda com: node ferramentas/prova-bloqueio-preparo.mjs

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

const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const acao = await import('../src/aplicacao/acoes.js')
const { MOTIVO_CLIENTE_BLOQUEADO, motivoParaNaoAvancar } = await import('../src/dominio/esteira.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

// --- Domínio -----------------------------------------------------------------
confere('domínio: bloqueado recusa preparo, embalado e entrega', () => {
  for (const passo of ['preparo', 'embalado', 'entrega']) {
    assert.equal(motivoParaNaoAvancar(passo, { bloqueado: true }), MOTIVO_CLIENTE_BLOQUEADO)
  }
})
confere('domínio: bloqueado deixa "pago" e "entregue" passarem', () => {
  assert.equal(motivoParaNaoAvancar('pago', { bloqueado: true }), null)
  assert.equal(motivoParaNaoAvancar('entregue', { bloqueado: true }), null)
})
confere('domínio: desbloqueado não recusa passo nenhum', () => {
  for (const passo of ['pago', 'preparo', 'embalado', 'entrega', 'entregue']) {
    assert.equal(motivoParaNaoAvancar(passo, { bloqueado: false }), null)
  }
})
confere('domínio: motivo sem travessão', () => {
  assert.ok(!/[–—]/.test(MOTIVO_CLIENTE_BLOQUEADO))
})

// --- Reducer -----------------------------------------------------------------
const AGORA = Date.parse('2026-09-26T15:00:00-03:00')
const conversa = {
  id: 'c1', cadastroId: 'cad-1', nome: 'Cliente Teste', estado: 'Em atendimento',
  responsavel: 'Thatiane', mensagens: [], bloqueio: null,
  pedido: {
    numero: '2026-0001', estado: 'pago', janela: 'j3', entregador: null,
    itens: [], agradecimentoEnviado: false, pagamentos: [],
  },
}
const inicio = estadoInicial({
  conversas: [conversa], catalogo: { janelas: [] }, regras: [], modoAgente: 'sugerir',
})
const estadoDoPedido = (estado) => estado.conversas.find((c) => c.id === 'c1').pedido.estado
const avancar = (estado, passo) => reducer(estado, {
  tipo: acao.AVANCAR_ESTEIRA, id: 'c1', passo, agora: AGORA, mensagemId: 'm-' + passo, posEntregaId: 'p-' + passo,
})

const bloqueado = reducer(inicio, {
  tipo: acao.BLOQUEAR_CLIENTE, cadastroId: 'cad-1', motivo: 'Teste de bloqueio.',
  em: '26/09/2026', por: 'Thatiane', agora: AGORA, mensagemId: 'mb',
})

confere('reducer: BLOQUEAR_CLIENTE grava o bloqueio no cadastro', () => {
  assert.ok(bloqueado.conversas[0].bloqueio)
})

confere('reducer: pedido pago de cliente bloqueado NÃO vai para preparo (estado intocado)', () => {
  const depois = avancar(bloqueado, 'preparo')
  assert.equal(depois, bloqueado, 'o reducer devolve o mesmo objeto de estado')
  assert.equal(estadoDoPedido(depois), 'pago')
})

confere('reducer: bloqueado também não pula para embalado nem entrega', () => {
  assert.equal(avancar(bloqueado, 'embalado'), bloqueado)
  assert.equal(avancar(bloqueado, 'entrega'), bloqueado)
})

const desbloqueado = reducer(bloqueado, {
  tipo: acao.DESBLOQUEAR_CLIENTE, cadastroId: 'cad-1', por: 'Thatiane', agora: AGORA, mensagemId: 'md',
})

confere('reducer: depois de DESBLOQUEAR_CLIENTE o pedido vai para preparo', () => {
  assert.equal(desbloqueado.conversas[0].bloqueio, null)
  const depois = avancar(desbloqueado, 'preparo')
  assert.equal(estadoDoPedido(depois), 'preparo')
})

confere('reducer: controle, sem bloqueio nenhum o pedido vai para preparo', () => {
  assert.equal(estadoDoPedido(avancar(inicio, 'preparo')), 'preparo')
})

console.log(`\n${passou} verificações passaram.`)
