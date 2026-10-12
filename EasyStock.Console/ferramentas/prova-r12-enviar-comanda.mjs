/* eslint-disable no-console */
// Prova da issue #14 (rodada 12, feedback da Thatiane no vídeo): o botão
// "Enviar comanda" não reagia. Ele manda o resumo e a cobrança para o
// CLIENTE, na conversa (GERAR_PEDIDO); a cozinha recebe o pedido sozinha
// quando o pagamento entra (US-035). Depois de enviado ou pago, ficava na
// tela desabilitado e sem motivo.
//
// Exercita a regra de domínio que a tela lê (`situacaoDoEnvio` e
// `avisoDoEnvio`, dominio/resumoPedido.js) e o reducer de verdade.
//
//   node ferramentas/prova-r12-enviar-comanda.mjs

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

const { situacaoDoEnvio, avisoDoEnvio, ROTULO_ENVIAR } = await import('../src/dominio/resumoPedido.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const acao = await import('../src/aplicacao/acoes.js')
const { pedidosNaCozinha } = await import('../src/dominio/cozinha.js')
const catalogo = await import('../src/infra/catalogo.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const AGORA = Date.parse('2026-09-26T15:00:00-03:00')
const pedidoBase = {
  numero: '2026-0901', estado: 'aguardando', janela: 'j3', entregador: null, meio: null,
  itens: [{ sku: 'LAS-CLA', qtd: 1, obs: '' }], agradecimentoEnviado: false, pagamentos: [],
  cobranca: null,
}

// --- Domínio -----------------------------------------------------------------
confere('rótulo diz para quem vai: "Enviar ao cliente"', () => {
  assert.equal(ROTULO_ENVIAR, 'Enviar ao cliente')
})

confere('pedido aguardando, com item e sem cobrança: pode enviar', () => {
  const s = situacaoDoEnvio(pedidoBase)
  assert.equal(s.pode, true)
  assert.equal(s.chave, 'pode')
})

confere('comanda vazia: não pode, e diz por quê', () => {
  const s = situacaoDoEnvio({ ...pedidoBase, itens: [] })
  assert.equal(s.pode, false)
  assert.equal(s.chave, 'vazia')
  assert.ok(s.texto.length > 0)
})

confere('cliente bloqueado: não pode, e diz por quê', () => {
  const s = situacaoDoEnvio(pedidoBase, { bloqueado: true })
  assert.equal(s.pode, false)
  assert.equal(s.chave, 'bloqueado')
  assert.match(s.texto, /bloquead/i)
})

confere('já enviada: não pode reenviar por aqui, e diz quando saiu', () => {
  const s = situacaoDoEnvio({ ...pedidoBase, cobranca: { criadaEm: AGORA, link: 'x' } })
  assert.equal(s.pode, false)
  assert.equal(s.chave, 'enviada')
  assert.match(s.texto, /^Enviada ao cliente às \d{2}:\d{2}/)
})

confere('pedido que já andou sem cobrança (pago): não pode, e diz por quê', () => {
  const s = situacaoDoEnvio({ ...pedidoBase, estado: 'pago' })
  assert.equal(s.pode, false)
  assert.equal(s.chave, 'andou')
  assert.ok(s.texto.length > 0)
})

confere('aviso com link: cita o cliente e que a cozinha recebe quando o pagamento cair', () => {
  const texto = avisoDoEnvio({ meio: 'pix', link: 'https://x' }, 'José')
  assert.match(texto, /José/)
  assert.match(texto, /Pix/)
  assert.match(texto, /cozinha quando o pagamento cair/)
})

confere('aviso sem link (maquininha, vale): o pedido já está na cozinha', () => {
  const texto = avisoDoEnvio({ meio: 'vale-refeicao', link: null }, 'José')
  assert.match(texto, /José/)
  assert.match(texto, /já está na cozinha/)
})

confere('textos sem travessão', () => {
  const textos = [
    avisoDoEnvio({ meio: 'pix', link: 'x' }, 'A'), avisoDoEnvio({ meio: 'maquininha', link: null }, 'A'),
    situacaoDoEnvio({ ...pedidoBase, itens: [] }).texto, situacaoDoEnvio(pedidoBase, { bloqueado: true }).texto,
    situacaoDoEnvio({ ...pedidoBase, estado: 'pago' }).texto,
  ]
  for (const t of textos) assert.ok(!/[–—]/.test(t), t)
})

// --- Reducer: o que o clique despacha -----------------------------------------
const conversa = {
  id: 'c1', cadastroId: 'cad-1', nome: 'José Teste', canal: 'WhatsApp', estado: 'Em atendimento',
  responsavel: 'Thatiane', mensagens: [], bloqueio: null, cliente: { notas: [] },
  pedido: pedidoBase,
}
const inicio = estadoInicial({
  conversas: [conversa],
  catalogo: {
    canais: catalogo.CANAIS, cardapio: catalogo.CARDAPIO, janelas: catalogo.JANELAS_ENTREGA,
    linhas: catalogo.LINHAS_PRODUTO,
  },
  regras: [], modoAgente: 'sugerir',
})
const enviar = (estado, emissao) => {
  let depois = reducer(estado, {
    tipo: acao.GERAR_PEDIDO, id: 'c1', agora: AGORA, mensagemId: 'm1', cobrancaId: 'm2', emissao,
  })
  // O provider despacha DESPACHAR_MESMO_ASSIM quando a emissão volta sem link.
  if (!emissao.link) depois = reducer(depois, { tipo: acao.DESPACHAR_MESMO_ASSIM, id: 'c1', agora: AGORA })
  return depois
}

confere('reducer: enviar com vale põe o pedido na cozinha na hora', () => {
  const depois = enviar(inicio, { identificador: null, copiaECola: null, link: null, meio: 'vale-refeicao' })
  assert.equal(pedidosNaCozinha(depois.conversas).length, 1)
  assert.equal(situacaoDoEnvio(depois.conversas[0].pedido).chave, 'enviada')
})

confere('reducer: enviar com Pix manda resumo e cobrança ao cliente e espera o pagamento', () => {
  const depois = enviar(inicio, { identificador: 'tx1', copiaECola: 'cc', link: 'https://pix', meio: 'pix' })
  const c = depois.conversas[0]
  assert.equal(c.mensagens.filter((m) => m.dir === 'out').length, 2)
  assert.equal(pedidosNaCozinha(depois.conversas).length, 0)
  assert.equal(situacaoDoEnvio(c.pedido).chave, 'enviada')
})

console.log(`\n${passou} verificações passaram.`)
