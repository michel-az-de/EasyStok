// #2944 E1-01 (motivo nos desabilitados) e E1-02 (foco na troca de passo e no descarte).
//   node ferramentas/prova-2944-motivo-foco.mjs
import { assert, caso, fim, fonte } from './prova-2944-base.mjs'

const j = await import('../src/aplicacao/janelaDoPedido.js')
const jsx = fonte('app/NovoPedidoJanela.jsx')
const item = [{ sku: 'a', qtd: 1 }]

await caso('01a. pedido vazio no passo Itens diz "Some ao menos um item"', () => {
  assert.equal(typeof j.motivoDeBloqueio, 'function', 'motivoDeBloqueio ainda não existe')
  assert.equal(j.motivoDeBloqueio({ passo: 'itens', itens: [] }), 'Some ao menos um item')
  assert.equal(j.motivoDeBloqueio({ passo: 'itens', itens: item }), null)
})
await caso('01b. Entrega: sem forma de pagamento e sem endereço dizem o que falta', () => {
  assert.equal(j.motivoDeBloqueio({ passo: 'entrega', itens: item, pedido: { janela: 'j', meio: null } }), 'Escolha a forma de pagamento')
  const erro = { status: 409, message: 'Falta o endereço' }
  assert.equal(j.motivoDeBloqueio({ passo: 'entrega', itens: item, pedido: { janela: 'j', meio: 'pix' }, erroSimulacao: erro }), 'Falta o endereço de entrega')
  assert.equal(j.motivoDeBloqueio({ passo: 'entrega', itens: item, pedido: { janela: 'j', meio: 'pix' } }), null)
})
await caso('01c. todo Botao desabilitado da janela traz motivo na mesma linha', () => {
  const soltos = jsx.split('\n').filter((l) => /<Botao\b/.test(l) && /disabled=/.test(l) && !/motivo=/.test(l))
  assert.deepEqual(soltos.map((l) => l.trim().slice(0, 70)), [])
})
await caso('02a. troca de passo move o foco para o título do passo', () => {
  assert.match(jsx, /tabIndex=\{[^}]*-1[^}]*\}/, 'título do passo sem tabIndex -1')
  assert.match(jsx, /useEffect\(\(\) => \{[\s\S]{0,200}tituloDoPasso\.current\?\.focus\(\)[\s\S]{0,40}\}, \[passo\]\)/)
})
await caso('02b. descartar devolve o foco ao "Manter rascunho"', () => {
  assert.match(jsx, /useEffect\(\(\) => \{[^}]*manter\.current\?\.focus\(\)[^}]*\}, \[descartando\]\)/s)
  assert.match(jsx, /<Botao[^\n]*ref=\{manter\}[^\n]*>Manter rascunho/)
})
fim()
