// Prova pura da #3209 (S84): o bloco "Quem é" do cartão do cliente. Telefone a mostrar, último pedido e mídias
// de imagem da conversa, com dados no formato da API. Sem navegador: roda com `node`.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const { resumoQuemE, LIMITE_DE_MIDIAS } = await import('../src/dominio/quemE.js')

let falhas = 0
function caso(nome, fn) {
  try { fn(); console.log(`ok  ${nome}`) } catch (e) { falhas += 1; console.error(`ERRO ${nome}\n  ${e.message}`) }
}

const msg = (id, em, mime, extra = {}) => ({ id, dir: 'in', texto: 'Foto', em, midia: { conversaId: 'c1', mensagemId: id, mime }, ...extra })
const lead = (canal, contato, extra = {}) => ({ id: 'c1', canal, contato, conta: 'lead', clienteId: null, mensagens: [], cliente: { tags: [], notas: [], pedidos: 0, telefone: '', endereco: '' }, ...extra })

caso('cliente vinculado: telefone do dossiê, pedidos e último pedido (o mais recente, sem cancelado)', () => {
  const r = resumoQuemE({
    ...lead('WhatsApp', '5511900000000'), conta: 'cliente', clienteId: 'cli1',
    cliente: { telefone: '(11) 98765-4321', pedidos: 3, historico: [
      { numero: '#a', em: '2026-10-01', total: 40, estado: 'entregue' },
      { numero: '#b', em: '2026-11-12', total: 54.5, estado: 'entregue' },
      { numero: '#c', em: '2026-11-20', total: 99, estado: 'cancelado' },
    ] },
  })
  assert.equal(r.telefone, '(11) 98765-4321')
  assert.equal(r.pedidos.quantidade, 3)
  assert.deepEqual(r.pedidos.ultimo, { em: '2026-11-12', total: 54.5 })
})
caso('cliente sem pedido: quantidade 0 e nenhum último pedido', () => {
  const r = resumoQuemE({ ...lead('WhatsApp', '5511987654321'), conta: 'cliente', clienteId: 'cli1', cliente: { telefone: '(11) 98765-4321', pedidos: 0, historico: [] } })
  assert.equal(r.pedidos.quantidade, 0)
  assert.equal(r.pedidos.ultimo, null)
})
caso('lead do WhatsApp: o número vem do contato, formatado', () => {
  const r = resumoQuemE(lead('WhatsApp', '5511987654321'))
  assert.equal(r.telefone, '(11) 98765-4321')
})
caso('lead do WhatsApp com lid ("@" ou 15 dígitos): sem telefone', () => {
  assert.equal(resumoQuemE(lead('WhatsApp', '203645728109@lid')).telefone, '')
  assert.equal(resumoQuemE(lead('WhatsApp', '203645728109577')).telefone, '')
})
caso('lead do Instagram ou do site: o contato é id externo, sem telefone', () => {
  assert.equal(resumoQuemE(lead('Instagram', '17841400000000000')).telefone, '')
  assert.equal(resumoQuemE(lead('Instagram', '5511987654321')).telefone, '')
  assert.equal(resumoQuemE(lead('Site', 'visitante-9f3a')).telefone, '')
})
caso('mídias: só imagem carregável, mais recente primeiro, limite e "+N"', () => {
  const mensagens = [
    msg('m1', 1000, 'image/jpeg'), msg('m2', 2000, 'audio/ogg'), msg('m3', 3000, 'image/png'),
    { id: 'm4', dir: 'in', texto: 'Foto', em: 4000, midia: { falhou: true, erro: 'x' } },
    { id: 'm5', dir: 'in', texto: 'oi', em: 5000 },
    ...Array.from({ length: 6 }, (_, i) => msg(`n${i}`, 6000 + i, 'image/jpeg')),
  ]
  const r = resumoQuemE({ ...lead('WhatsApp', '5511987654321'), mensagens })
  assert.equal(LIMITE_DE_MIDIAS, 6)
  assert.equal(r.midias.itens.length, 6)
  assert.deepEqual(r.midias.itens.map((m) => m.mensagemId), ['n5', 'n4', 'n3', 'n2', 'n1', 'n0'])
  assert.equal(r.midias.restantes, 2)
})
caso('sem mídia de imagem: lista vazia e nada sobrando', () => {
  const r = resumoQuemE({ ...lead('WhatsApp', '5511987654321'), mensagens: [msg('m2', 2000, 'audio/ogg'), { id: 'm5', dir: 'in', texto: 'oi', em: 5000 }] })
  assert.deepEqual(r.midias, { itens: [], restantes: 0 })
})

if (falhas) { console.error(`${falhas} caso(s) falharam`); process.exit(1) }
console.log('prova-3209-quem-e: tudo certo')
