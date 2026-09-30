// Prova da issue #7 (rodada 11, cozinha com arrastar e soltar): a regra de
// destino do soltar. Só a coluna do PRÓXIMO passo aceita (a mesma ação do
// botão, RN-31); outra coluna recusa com motivo; cliente bloqueado recusa
// com o motivo do RN-14; soltar na própria coluna não muda nada e não é
// recusa. Depois confere, no reducer de verdade, que o que a regra aceita
// muda o pedido igual ao botão e que o que ela recusa por bloqueio o reducer
// também recusa (as duas pontas dão a mesma resposta).
//
// Mesmo gancho de `prova-bloqueio-preparo.mjs`: o código de `src/` importa
// sem extensão (o Vite resolve, o Node puro não).
//
// Roda com: node ferramentas/prova-r11-cozinha.mjs

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
const { MOTIVO_CLIENTE_BLOQUEADO, proximoPasso } = await import('../src/dominio/esteira.js')
const { PASSOS_DA_COZINHA, respostaAoSoltar } = await import('../src/dominio/cozinha.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const LIVRE = { bloqueado: false }
const BLOQUEADO = { bloqueado: true }
const ids = PASSOS_DA_COZINHA.map((p) => p.id)

// --- Domínio -----------------------------------------------------------------
confere('domínio: próximo passo aceita, em todas as colunas que têm próximo', () => {
  for (const atual of ids) {
    const proximo = proximoPasso(atual)
    if (!proximo) continue
    assert.deepEqual(respostaAoSoltar(atual, proximo.id, LIVRE), { aceita: true, motivo: null })
  }
})

confere('domínio: qualquer outra coluna recusa, com motivo que nomeia o próximo passo', () => {
  for (const atual of ids) {
    const proximo = proximoPasso(atual)
    for (const destino of ids) {
      if (destino === atual || destino === proximo?.id) continue
      const resposta = respostaAoSoltar(atual, destino, LIVRE)
      assert.equal(resposta.aceita, false, `${atual} -> ${destino}`)
      assert.ok(resposta.motivo, `${atual} -> ${destino} sem motivo`)
      if (proximo) assert.ok(resposta.motivo.includes(proximo.rotulo), resposta.motivo)
    }
  }
})

confere('domínio: pular passo (pago direto para embalado) recusa', () => {
  assert.equal(respostaAoSoltar('pago', 'embalado', LIVRE).aceita, false)
})

confere('domínio: voltar passo (preparo para pago) recusa', () => {
  assert.equal(respostaAoSoltar('preparo', 'pago', LIVRE).aceita, false)
})

confere('domínio: soltar na própria coluna não muda nada e não é recusa', () => {
  for (const atual of ids) {
    assert.deepEqual(respostaAoSoltar(atual, atual, LIVRE), { aceita: false, motivo: null })
  }
})

confere('domínio: cliente bloqueado recusa preparo, embalado e entrega com o motivo do RN-14', () => {
  for (const [atual, destino] of [['pago', 'preparo'], ['preparo', 'embalado'], ['embalado', 'entrega']]) {
    assert.deepEqual(respostaAoSoltar(atual, destino, BLOQUEADO), { aceita: false, motivo: MOTIVO_CLIENTE_BLOQUEADO })
  }
})

confere('domínio: bloqueado deixa "Entregue" passar (mesma exceção do botão)', () => {
  assert.deepEqual(respostaAoSoltar('entrega', 'entregue', BLOQUEADO), { aceita: true, motivo: null })
})

confere('domínio: "Entregue" não tem próximo, toda coluna recusa', () => {
  for (const destino of ids.filter((id) => id !== 'entregue')) {
    const resposta = respostaAoSoltar('entregue', destino, LIVRE)
    assert.equal(resposta.aceita, false)
    assert.ok(resposta.motivo)
  }
})

confere('domínio: motivos sem travessão', () => {
  for (const atual of ids) {
    for (const destino of ids) {
      for (const contexto of [LIVRE, BLOQUEADO]) {
        const { motivo } = respostaAoSoltar(atual, destino, contexto)
        assert.ok(!/[\u2013\u2014]/.test(motivo ?? ''), motivo)
      }
    }
  }
})

// --- Reducer: soltar aceito = o mesmo AVANCAR_ESTEIRA do botão ---------------
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
const doPedido = (estado) => estado.conversas.find((c) => c.id === 'c1')
const soltar = (estado, destino) => reducer(estado, {
  tipo: acao.AVANCAR_ESTEIRA, id: 'c1', passo: destino, agora: AGORA, mensagemId: 'm-' + destino, posEntregaId: 'p-' + destino,
})

confere('reducer: soltar aceito em "Em preparo" muda o pedido e avisa o cliente, igual ao botão', () => {
  const antes = doPedido(inicio).mensagens.length
  assert.equal(respostaAoSoltar('pago', 'preparo', LIVRE).aceita, true)
  const depois = soltar(inicio, 'preparo')
  assert.equal(doPedido(depois).pedido.estado, 'preparo')
  assert.ok(doPedido(depois).mensagens.length > antes, 'o aviso ao cliente (RN-32) saiu')
})

const bloqueado = reducer(inicio, {
  tipo: acao.BLOQUEAR_CLIENTE, cadastroId: 'cad-1', motivo: 'Teste de bloqueio.',
  em: '26/09/2026', por: 'Thatiane', agora: AGORA, mensagemId: 'mb',
})

confere('reducer: o que a regra recusa por bloqueio o reducer também recusa (estado intocado)', () => {
  assert.equal(respostaAoSoltar('pago', 'preparo', BLOQUEADO).aceita, false)
  assert.equal(soltar(bloqueado, 'preparo'), bloqueado)
})

console.log(`\n${passou} verificações passaram.`)
