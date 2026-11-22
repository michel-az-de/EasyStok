// #3154 (re-pontuação da #3076): com a loja online desligada, o Novo pedido parava na etapa Itens com
// "até ligar a vitrine", sem dizer quem liga, e o Continuar culpava "Some ao menos um item". A Busca na Casa
// não achava "loja online" nem "vitrine".
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
})
const { avisoDoCardapio } = await import('../src/aplicacao/planoDeSincronizacao.js')
const { motivoDeBloqueio, motivoDoNovoPedido } = await import('../src/aplicacao/janelaDoPedido.js')
const { buscarDestinosDoConsole, modulosDoHall } = await import('../src/dominio/modulos.js')

// 1. O aviso diz quem liga, sem o termo interno "vitrine", e cabe na faixa do rodapé como o texto antigo (74).
const aviso = avisoDoCardapio({ status: 200, codigo: 'VITRINE_DESLIGADA', message: 'A empresa não tem vitrine ativa.' })
assert.match(aviso, /suporte do EasyStok/, 'diz quem liga a loja')
assert.ok(aviso.length <= 76, `faixa não cresce: ${aviso.length} caracteres`)
assert.doesNotMatch(aviso, /vitrine/i, 'sem o termo interno')
assert.equal(avisoDoCardapio({ status: 500, message: 'Fora' }), 'Cardápio: Fora', 'outra falha segue como antes')

// 2. Sem itens e com o cardápio recusado, o Continuar diz o motivo real, curto (a frase inteira fica no corpo da etapa).
assert.equal(motivoDeBloqueio({ passo: 'itens', itens: [], cardapio: { tipo: 'falhou', mensagem: aviso } }), 'Loja online desligada')
assert.equal(motivoDeBloqueio({ passo: 'itens', itens: [], cardapio: { tipo: 'falhou', mensagem: 'Cardápio: Fora' } }), 'O cardápio não carregou')
assert.equal(motivoDeBloqueio({ passo: 'itens', itens: [], cardapio: { tipo: 'carregando' } }), 'Carregando o cardápio…')
assert.equal(motivoDeBloqueio({ passo: 'itens', itens: [], cardapio: { tipo: 'pronto' } }), 'Some ao menos um item')
assert.equal(motivoDeBloqueio({ passo: 'itens', itens: [] }), 'Some ao menos um item', 'quem não passa a leitura segue igual')
assert.equal(motivoDeBloqueio({ passo: 'itens', itens: [{ sku: 'a', qtd: 1 }], cardapio: { tipo: 'falhou', mensagem: aviso } }), null,
  'itens já na tela valem mesmo com falha depois')

// 3. O botão Novo pedido diz antes de abrir a janela, só com a loja desligada.
assert.equal(motivoDoNovoPedido({ cardapio: [], cardapioLido: true, cardapioFalha: aviso }), aviso)
assert.equal(motivoDoNovoPedido({ cardapio: [], cardapioLido: true, cardapioFalha: 'Cardápio: Fora' }), null, 'falha passageira não trava')
assert.equal(motivoDoNovoPedido({ cardapio: [{ sku: 'a' }], cardapioLido: true, cardapioFalha: aviso }), null, 'itens na tela valem')
assert.equal(motivoDoNovoPedido({ cardapio: [], galeria: [] }), null, 'carregando não trava')

// 4. A Busca na Casa acha "loja online" e "vitrine" e leva às telas do Cardápio, que dizem quem liga.
const modulos = modulosDoHall({ fonteApi: true, acoes: {} })
for (const termo of ['loja online', 'vitrine', 'Loja Online']) {
  const achados = buscarDestinosDoConsole(modulos, termo)
  assert.deepEqual(achados.map((d) => d.href), ['#/m/cardapio/itens'], `"${termo}" leva só a Itens do cardápio`)
}
console.log('ok prova-3154-loja-desligada')
