// Prova do pacote A da #1839 (Balcão: ciclo e painel), com as funções reais do console.
// Uso: node ferramentas/prova-1839-balcao.mjs
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'

// O console importa sem extensão (Vite); no Node a extensão entra aqui.
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) return proximo(spec + '.js', contexto)
    return proximo(spec, contexto)
  },
})

const ler = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8').replace(/\r\n/g, '\n')
let passou = 0
const provar = async (nome, fn) => { await fn(); passou++; console.log('ok ' + nome) }

const { extratoDoPedido } = await import('../src/dominio/pagamento.js')
await provar('extrato separa itens e frete, lista os pagamentos em ordem e mostra o saldo', () => {
  const extrato = extratoDoPedido({
    pedidoId: 'p', totalApi: 263, frete: 100, totalPagoApi: 263,
    pagamentosApi: [{ pagamentoId: 'b', em: 2000, meio: 'pix', valor: 163 }, { pagamentoId: 'a', em: 1000, meio: 'pix', valor: 100 }],
  })
  assert.deepEqual([extrato.itens, extrato.frete, extrato.total, extrato.pago, extrato.saldo], [163, 100, 263, 263, 0])
  assert.deepEqual(extrato.pagamentos.map((p) => p.id), ['a', 'b'])
  assert.equal(extratoDoPedido({ pedidoId: 'p', totalApi: 50, frete: 0, pagamentosApi: [{ pagamentoId: 'x', em: 1, valor: 20 }] }).saldo, 30)
  assert.equal(extratoDoPedido({ totalApi: 50 }), null, 'sem pedido no EasyStok não há extrato')
  const estornado = extratoDoPedido({ pedidoId: 'p', totalApi: 100, frete: 0, totalPagoApi: 100, alocadoApi: 60, pagamentosApi: [{ pagamentoId: 'x', em: 1, valor: 100 }] })
  assert.deepEqual([estornado.ajuste, estornado.pago, estornado.saldo], [-40, 60, 40], 'estorno sai do pago e volta a receber')
})

const { clienteDoDossie } = await import('../src/infra/api/traducaoCliente.js')
await provar('a ficha usa total gasto e favorito do servidor (todas as compras, não só as 10 da lista)', () => {
  const c = clienteDoDossie({ cliente: { id: 'c', nome: 'Maria' }, totalPedidos: 12, totalGasto: 980.5, itemFavorito: { nome: 'Lasanha', pedidos: 9 } })
  assert.deepEqual([c.cliente.pedidos, c.cliente.totalGasto, c.cliente.favorito], [12, 980.5, 'Lasanha'])
  const novo = clienteDoDossie({ cliente: { id: 'n', nome: 'Ana' }, totalPedidos: 0 })
  assert.deepEqual([novo.cliente.totalGasto, novo.cliente.favorito], [null, null])
  assert.equal(clienteDoDossie({ cliente: { id: null, nome: 'Visitante' } }), null, 'lead sem cadastro segue com o cliente da conversa')
})

await provar('pagamento já recebido ao montar entra recolhido: reabrir a modal não anima de novo', () => {
  assert.match(ler('features/ficha-cliente/RelogioPix.jsx'), /useState\(Boolean\(cobranca\.pagaEm\)\)/)
})

await provar('a Ficha mantém o cliente, identifica a comanda ativa e abre o extrato no pagamento', () => {
  const painel = ler('features/ficha-cliente/PainelFicha.jsx')
  assert.match(painel, /<h2>Ficha<\/h2>/)
  assert.match(painel, /editavel && numero && <small>Comanda \{numero\}<\/small>/)
  assert.match(painel, /<CartaoDoCliente conversa=\{selecionada\}/)
  assert.match(painel, /\{secao === 'pagamento' && editavel && pedido && <Modal[\s\S]*?\{fonteApi && <ExtratoDoPedido pedido=\{pedido\} aDevolver=\{aDevolver\} \/>\}/)
})

await provar('a nota interna abre como post-it pequeno, foca só ao abrir e o Esc é dela', () => {
  const nota = ler('features/notas/ModalNota.jsx')
  assert.doesNotMatch(nota, /from '..\/..\/componentes\/Modal'/)
  assert.match(nota, /<dialog ref=\{postit\} open=\{visivel\} className=\{css\.postit\} aria-label="Nota interna" onKeyDown=\{aoTeclar\}>/)
  assert.match(nota, /useEffect\(\(\) => \{ if \(visivel\) campo\.current\?\.focus\(\) \}, \[visivel\]\)/)
  assert.doesNotMatch(nota, /document\.addEventListener/)
})

console.log(`${passou} cenários da #1839 (Balcão) aprovados.`)
