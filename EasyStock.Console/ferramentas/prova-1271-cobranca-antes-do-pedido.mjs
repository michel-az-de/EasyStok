/* eslint-disable no-console */
// Prova da issue #1271: no modo API, antes de o pedido existir no EasyStok, a cobrança não
// pode fingir o pedido. "Gerar cobrança" cria o pedido (mesmo caminho de "Enviar ao cliente");
// pagamento e esteira avisam e não mexem na memória do navegador.
//
//   node ferramentas/prova-1271-cobranca-antes-do-pedido.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  // `fonteDados.js` lê `import.meta.env` do Vite; no node a fonte é fixa.
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    return proximo(url, contexto)
  },
})

const acao = await import('../src/aplicacao/acoes.js')
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${erro.message.split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

const JANELA = '22222222-2222-2222-2222-222222222222'
const ITEM = '11111111-1111-1111-1111-111111111111'
const rascunho = () => ({ pedidoId: null, janela: `${JANELA}|2026-10-02`, meio: 'maquininha', itens: [{ sku: ITEM, qtd: 1, obs: '' }] })

// Monta as ações com um rascunho de comanda (sem pedido no EasyStok), uma rede falsa e
// ações locais que só registram que foram chamadas: chamar a local é a prova do defeito.
function montar() {
  const chamadas = []
  const locais = []
  const despachados = []
  globalThis.fetch = async (url, { method }) => {
    chamadas.push(`${method} ${url}`)
    return new Response(JSON.stringify({ error: { code: 'X', message: 'sem servidor na prova' } }), { status: 500 })
  }
  const estadoRef = { current: { conversas: [{ id: 'conv-1', pedido: rascunho() }] } }
  const local = (nome) => (...args) => { locais.push(nome); return args }
  const nomesLocais = ['gerarCobranca', 'reenviarCobranca', 'confirmarPagamento', 'marcarComprovante', 'aceitarDivergencia',
    'desfazerPagamento', 'marcarEstorno', 'cancelarPedido', 'avancarEsteira', 'corrigirPasso', 'desfazerEsteira',
    'adicionarItem', 'escolherJanela']
  const acoesLocais = Object.fromEntries(nomesLocais.map((n) => [n, local(n)]))
  const acoes = criarAcoesComandaApi(acoesLocais, { despachar: (a) => despachados.push(a), estadoRef })
  return { acoes, chamadas, locais, despachados }
}
const esperar = () => new Promise((r) => setTimeout(r, 20))

await confere('Gerar cobrança sem pedido cria o pedido no EasyStok (POST .../pedido)', async () => {
  const { acoes, chamadas, locais } = montar()
  acoes.gerarCobranca('conv-1', rascunho(), 'maquininha')
  await esperar()
  assert.ok(chamadas.some((c) => /^POST .*\/conversas\/conv-1\/pedido$/.test(c)), `sem POST do pedido: ${chamadas.join(', ') || 'nenhuma chamada'}`)
  assert.ok(!locais.includes('gerarCobranca'), 'rodou a cobrança local do protótipo')
})

await confere('Reenviar cobrança sem pedido também cria o pedido, não roda a local', async () => {
  const { acoes, chamadas, locais } = montar()
  acoes.reenviarCobranca('conv-1', rascunho(), null, 'maquininha')
  await esperar()
  assert.ok(chamadas.some((c) => /^POST .*\/pedido$/.test(c)), `sem POST do pedido: ${chamadas.join(', ') || 'nenhuma chamada'}`)
  assert.ok(!locais.includes('reenviarCobranca'), 'rodou o reenvio local do protótipo')
})

for (const nome of ['confirmarPagamento', 'marcarComprovante', 'aceitarDivergencia', 'desfazerPagamento', 'marcarEstorno',
  'cancelarPedido', 'avancarEsteira', 'corrigirPasso', 'desfazerEsteira']) {
  await confere(`${nome} sem pedido avisa e não mexe na memória`, async () => {
    const { acoes, locais, despachados, chamadas } = montar()
    acoes[nome]('conv-1')
    await esperar()
    assert.ok(!locais.includes(nome), `rodou ${nome} local sem pedido no EasyStok`)
    assert.ok(despachados.some((a) => a.tipo === acao.AVISO_API), 'não avisou')
    assert.equal(chamadas.length, 0, 'chamou a API sem pedido')
  })
}

await confere('Editar a comanda sem pedido continua local (rascunho)', async () => {
  const { acoes, locais } = montar()
  acoes.adicionarItem('conv-1', ITEM)
  acoes.escolherJanela('conv-1', `${JANELA}|2026-10-02`)
  assert.deepEqual(locais, ['adicionarItem', 'escolherJanela'])
})

console.log(`\n${passou} verificações passaram, ${falhas.length} falharam.`)
if (falhas.length > 0) process.exit(1)
