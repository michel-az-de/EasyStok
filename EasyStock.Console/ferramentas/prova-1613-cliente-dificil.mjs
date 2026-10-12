/* eslint-disable no-console */
// Prova da issue #1613: o cliente difícil (reembolso, cancelamento, reclamação,
// ameaça, desconto, pedido de falar com gente, restrição dita sem a palavra
// "alergia") nunca recebe o texto de venda ("Fechado! Confirma o endereço") e
// sobe para a dona com o motivo escrito. E o cliente leigo (Maria) e o que
// pede jantar (João) recebem resposta que ajuda, não "me conta um pouco mais".
// Importa as MESMAS funções que o Balcão e o agente usam:
//
//   node ferramentas/prova-1613-cliente-dificil.mjs
//
// Mesmo gancho de resolução de `prova-1606-triagem-urgente.mjs`.

process.env.TZ = 'America/Sao_Paulo'

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    try {
      return proximo(especificador, contexto)
    } catch (erro) {
      if (especificador.startsWith('.') && !/\.m?jsx?$/.test(especificador)) {
        return proximo(especificador + '.js', contexto)
      }
      throw erro
    }
  },
})

const {
  ACOES, INTENCOES, acaoSugerida, classificarIntencao, montarPrompt, rascunhoSugerido, sinalDoCliente,
} = await import('../src/dominio/agente.js')
const { motivoDePrecisar } = await import('../src/dominio/automatico.js')
const { respostaParaMensagemDeCliente } = await import('../src/dominio/simulacoes.js')
const { moeda } = await import('../src/dominio/formato.js')
const { CARDAPIO, JANELAS_ENTREGA, PREFIXOS_CEP_ATENDIDOS } = await import('../src/infra/catalogo.js')

const catalogo = { cardapio: CARDAPIO, janelas: JANELAS_ENTREGA, prefixosCepAtendidos: PREFIXOS_CEP_ATENDIDOS }

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const AGORA = new Date('2026-10-11T19:00:00-03:00').getTime()
const ha = (minutos) => new Date(AGORA - minutos * 60000).toISOString()
const cliente = (texto, minutos = 2) => ({ dir: 'in', texto, em: ha(minutos) })
const casa = (texto, minutos = 5) => ({ dir: 'out', texto, em: ha(minutos), status: 'entregue' })
const conversa = (mensagens, extra = {}) => ({
  id: 'c1', nome: 'Joana Teste', estado: 'Aberto', cliente: { tags: [] }, mensagens, ...extra,
})
const fala = (texto, extra = {}) => conversa([cliente(texto)], extra)

const analisa = (c) => {
  const leitura = classificarIntencao(c)
  return {
    leitura,
    acao: acaoSugerida(leitura, c, catalogo),
    texto: rascunhoSugerido(leitura, c, catalogo, [], AGORA),
    sinal: sinalDoCliente(c),
  }
}
const PAGO = { numero: '2026-0189', estado: 'pago', janela: 'j2', itens: [] }

// --- A dona precisa ver -------------------------------------------------------
const sobe = [
  ['dinheiro de volta', 'Quero meu dinheiro de volta imediatamente', 'devolucao', null],
  ['massa muito cozida', 'O ragu tava ótimo, mas a massa veio muito cozida, quase desmanchando', 'devolucao', null],
  ['faltou item', 'Faltou o molho no pedido', 'devolucao', null],
  ['chegou frio', 'Chegou gelado, que lixo', 'devolucao', null],
  ['atraso já ocorrido', 'Chegou 20h10 e a gente tinha combinado 18h30 às 19h30', 'devolucao', null],
  ['cancelar', 'Cancela meu pedido, demorou demais', 'pede-a-dona', 'Quer cancelar ou mudar o pedido'],
  ['trocar endereço', 'Troca o endereço de entrega, vai ser na casa da minha mãe', 'pede-a-dona', 'Quer cancelar ou mudar o pedido'],
  ['falar com gente', 'Quero falar com o responsável agora', 'pede-a-dona', 'Quer falar com você, não com o atendimento automático'],
  ['ameaça', 'Vou reclamar no Procon e no Reclame Aqui', 'pede-a-dona', 'Ameaça reclamar fora (Procon, advogado ou rede social), responda você'],
  ['desconto', 'Faz um desconto que eu compro sempre aqui', 'pede-a-dona', 'Pede desconto ou reclama do valor'],
  ['frete absurdo', 'Esse frete é um absurdo, vocês estão me roubando', 'pede-a-dona', 'Pede desconto ou reclama do valor'],
  ['ligar', 'Moça, posso ligar pra senhora? Não consigo digitar direito', 'pede-a-dona', 'Pede para falar por telefone'],
  ['retirar', 'Posso buscar aí em vez de entregar?', 'pede-a-dona', 'Quer retirar o pedido na loja'],
  ['pressa', 'Preciso disso pra JÁ!! Não tenho o dia todo', 'pede-a-dona', 'Pede pressa, veja se dá para adiantar'],
  ['restrição por verbo', 'Meu neto não pode comer ovo, tem alguma coisa pra ele?', 'restricao', null],
  ['diabética', 'Eu sou diabética, o que posso pedir?', 'restricao', null],
  ['grávida', 'Minha esposa está grávida, tem algo sem risco?', 'restricao', null],
]
for (const [rotulo, texto, paraDona, textoParaDona] of sobe) {
  confere(`sobe para a dona: ${rotulo}`, () => {
    const c = fala(texto)
    const r = analisa(c)
    assert.equal(r.acao, ACOES.PASSAR_PARA_DONA, 'ação')
    assert.equal(r.sinal.paraDona, paraDona, 'motivo do sinal')
    if (textoParaDona) assert.equal(r.sinal.textoParaDona, textoParaDona, 'texto do cartão')
    assert.doesNotMatch(r.texto, /Fechado|Confirma o endereço|gero o pedido/, 'rascunho de venda')
    // E o cartão do Balcão acende na hora, com o automático LIGADO e sem "?".
    const motivo = motivoDePrecisar(c, AGORA, false)
    assert.ok(motivo, 'cartão "Precisa de você"')
    assert.equal(motivo.chave === 'devolucao' || motivo.chave === 'restricao' || motivo.chave === 'pede-a-dona', true)
    if (textoParaDona) assert.equal(motivo.texto, textoParaDona)
  })
}

confere('acrescentar item em pedido já pago sobe; sem pedido é conversa normal', () => {
  const comPedido = analisa(fala('Thati, dá pra acrescentar uma lasanha clássica no mesmo pedido?', { pedido: PAGO }))
  assert.equal(comPedido.acao, ACOES.PASSAR_PARA_DONA)
  assert.equal(comPedido.sinal.textoParaDona, 'Quer acrescentar ou mexer no pedido que já está andando')
  const semPedido = analisa(fala('Pode adicionar parmesão extra?'))
  assert.equal(semPedido.acao, ACOES.PROPOR)
})

confere('rascunho de reembolso acolhe e não promete devolução, prazo nem desconto', () => {
  const { texto } = analisa(fala('Quero meu dinheiro de volta imediatamente'))
  assert.match(texto, /Sinto muito, Joana/)
  assert.doesNotMatch(texto, /devol|reembols|desconto|minutos|prazo/i)
})

// --- Não pode virar alarme falso ----------------------------------------------
const naoSobe = [
  'Quero 2 lasanhas pra janta',
  'Oi dona Thati, tudo bem? Tem lasanha hoje?',
  'Tem refrigerante gelado?',
  'Meu telefone é (11) 99999-0000, pode salvar',
  'Pode adicionar parmesão extra?',
  'Não posso receber antes das 18h, pode ser depois?',
]
for (const texto of naoSobe) {
  confere(`não vira alarme falso: "${texto}"`, () => {
    const c = fala(texto)
    const r = analisa(c)
    assert.equal(r.acao, ACOES.PROPOR)
    assert.notEqual(r.leitura.intencao.chave, INTENCOES.PEDIDO_ESPECIAL.chave)
    assert.equal(r.sinal.paraDona, null)
  })
}

// --- Maria, leiga: resposta que ajuda -----------------------------------------
confere('pergunta de pagamento diz o caminho do Pix, sem pedir "mais detalhe"', () => {
  for (const texto of ['Como eu faço pra pagar? não sei mexer nisso', 'Pode mandar o pix', 'Quero pagar no Pix']) {
    const r = analisa(fala(texto))
    assert.equal(r.leitura.intencao.chave, INTENCOES.PAGAMENTO.chave, texto)
    assert.match(r.texto, /Pix ou cartão/)
  }
})

confere('preço de item citado sai com o valor do cardápio', () => {
  const item = CARDAPIO.find((i) => i.nome === 'Lasanha clássica')
  const r = analisa(fala('Quanto custa a lasanha clássica?'))
  assert.equal(r.acao, ACOES.PROPOR)
  assert.ok(r.texto.includes(moeda(item.preco)), r.texto)
})

confere('"quero comer" sem item vai para o cardápio, nunca para "Fechado"', () => {
  for (const texto of ['Boa tarde, queria comprar uma coisa pra comer', 'Voces tem marmita?', 'Quero um jantar para mim e minha esposa hoje']) {
    const r = analisa(fala(texto))
    assert.doesNotMatch(r.texto, /Fechado/, texto)
    assert.match(r.texto, /cardápio/, texto)
  }
})

confere('"pode ser" depois de a casa oferecer o item continua fechando', () => {
  const c = conversa([casa('Temos Lasanha clássica hoje, 500 g. Quer?'), cliente('Pode ser!')])
  const r = analisa(c)
  assert.match(r.texto, /^Fechado/)
})

confere('saudação solta oferece o cardápio em vez de pedir "mais detalhe"', () => {
  const r = analisa(fala('Oi'))
  assert.doesNotMatch(r.texto, /Me conta um pouco mais/)
  assert.match(r.texto, /cardápio/)
})

// --- João: pagou -------------------------------------------------------------
confere('"paguei" não confirma pagamento: promete conferir', () => {
  const r = analisa(fala('Pronto, paguei'))
  assert.match(r.texto, /conferir/)
  assert.doesNotMatch(r.texto, /confirmado|caiu|recebi/i)
})

// --- Revisão da PR: precedência e cortesia ------------------------------------
confere('restrição com pista própria ganha de pedido especial na mesma fala', () => {
  for (const texto of ['Sou celíaca, posso retirar aí na loja?', 'Tenho alergia a amendoim, quero falar com o responsável']) {
    const r = analisa(fala(texto))
    assert.equal(r.leitura.intencao.chave, INTENCOES.RESTRICAO.chave, texto)
    assert.equal(r.sinal.paraDona, 'restricao', texto)
    assert.equal(r.acao, ACOES.PASSAR_PARA_DONA, texto)
  }
})

confere('"como tinha combinado" é cortesia, não reclamação', () => {
  const r = analisa(fala('Como tinha combinado, quero a lasanha clássica de sempre'))
  assert.equal(r.acao, ACOES.PROPOR)
  assert.equal(r.sinal.paraDona, null)
})

confere('pedido com a forma de pagamento no meio segue o fechamento', () => {
  const r = analisa(fala('Quero a lasanha clássica e vou pagar no pix'))
  assert.equal(r.leitura.intencao.chave, INTENCOES.PAGAMENTO.chave)
  assert.match(r.texto, /^Fechado/)
})

confere('com cobrança já enviada, "como pago?" aponta o link da conversa', () => {
  const pedido = { numero: '2026-0190', estado: 'aguardando', cobranca: { valor: 85, criadaEm: AGORA - 60000, expiraEm: AGORA + 600000 } }
  const r = analisa(fala('Como eu pago?', { pedido }))
  assert.match(r.texto, /pedido 2026-0190/)
  assert.doesNotMatch(r.texto, /quando o pedido fecha/)
})

confere('nota interna e aviso do sistema não "oferecem" item para virar Fechado', () => {
  const c = conversa([
    { dir: 'nota', texto: 'Gosta de lasanha clássica', em: ha(30) },
    cliente('Quero jantar hoje'),
  ])
  assert.doesNotMatch(analisa(c).texto, /^Fechado/)
})

confere('o motivo escrito chega à passagem pelo mesmo caminho do automático', () => {
  const r = respostaParaMensagemDeCliente({
    conversaComMensagem: fala('Cancela meu pedido, demorou demais'),
    primeiraMensagemDoCliente: false,
    cadastroConhecido: true,
    catalogo,
    regras: [],
    agora: AGORA,
    prefixosCepAtendidos: PREFIXOS_CEP_ATENDIDOS,
  })
  assert.equal(r.respostaAutomatica, null)
  assert.equal(r.passarParaDona, 'Quer cancelar ou mudar o pedido')
})

// --- Modelo (modo Claude) recebe a mesma regra --------------------------------
confere('o prompt do modelo manda acolher cancelamento, desconto e ameaça', () => {
  const prompt = montarPrompt(fala('Cancela meu pedido'), catalogo, [])
  assert.match(prompt, /Procon/)
  assert.match(prompt, /Cancelar ou mudar pedido/)
})

console.log(`\n${passou} verificações ok`)
