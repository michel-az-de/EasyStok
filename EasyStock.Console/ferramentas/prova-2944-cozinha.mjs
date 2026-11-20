// #2944 E1-20 (G16): o pedido registrado na janela chega à Cozinha. É prova, não código novo.
//   Contrato (sempre):  node ferramentas/prova-2944-cozinha.mjs
//   Ao vivo (depois de registrar um pedido pela janela, com a API no ar):
//     COZINHA_API=http://127.0.0.1:5000 COZINHA_TOKEN=<token da sessão> COZINHA_PEDIDO=<id do pedido> node ferramentas/prova-2944-cozinha.mjs
//   A parte ao vivo falha enquanto o pedido não aparecer em `GET /api/kds/pedidos` com status de fila.
import { readFileSync } from 'node:fs'
import { assert, caso, fim, fonte } from './prova-2944-base.mjs'

const kds = await import('../src/dominio/kds.js')
const api = fonte('infra/api/kdsApi.js')
const listagem = readFileSync(new URL('../../EasyStock.Application/UseCases/Operacao/Kds/ListarPedidosKdsUseCase.cs', import.meta.url), 'utf8')
const bloco = listagem.match(/StatusPadrao\s*=([\s\S]*?);/)?.[1] ?? ''

await caso('20a. a Cozinha lê /api/kds/pedidos e a coluna da fila é o status aguardando ("Na fila")', () => {
  assert.match(api, /\/api\/kds\/pedidos/)
  assert.equal(kds.COLUNAS_KDS[0].status, 'aguardando')
  assert.equal(kds.colunaDoStatusKds('aprovado_baba'), 'aguardando')
})
await caso('20b. a lista padrão da API traz a fila e não traz o pedido que espera pagamento', () => {
  assert.match(bloco, /StatusPedidoMapper\.Aguardando\b/)
  assert.doesNotMatch(bloco, /AguardandoPagamento/, 'o pedido só chega à Cozinha depois de pago')
})
const { COZINHA_API: base, COZINHA_TOKEN: token, COZINHA_PEDIDO: id } = process.env
if (base && token && id) {
  await caso('20c. ao vivo: o pedido registrado está na Cozinha, na fila', async () => {
    const r = await fetch(`${base}/api/kds/pedidos`, { headers: { authorization: `Bearer ${token}` } })
    assert.equal(r.status, 200, `GET /api/kds/pedidos devolveu ${r.status}`)
    const corpo = await r.json()
    const pedido = (Array.isArray(corpo) ? corpo : corpo.itens ?? corpo.pedidos ?? []).find((p) => String(p.id ?? p.pedidoId) === id)
    assert.ok(pedido, 'o pedido não aparece na Cozinha (se estiver aguardando pagamento, só entra depois de pago)')
    assert.equal(kds.colunaDoStatusKds(pedido.status), 'aguardando', `status na Cozinha: ${pedido.status}`)
  })
} else console.log('pend  20c. parte ao vivo sem COZINHA_API, COZINHA_TOKEN e COZINHA_PEDIDO: não executada')
fim()
