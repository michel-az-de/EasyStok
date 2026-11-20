// #2974: CEP fora da área (sem zona ativa nem raio) volta da simulação como `freteACombinar`. A janela "Novo pedido"
// pede o frete combinado na etapa Entrega, como a comanda do Atendimento (#2219), e não deixa avançar sem ele
// (R$ 0,00 vale). Sem servidor.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
  load(url, c, next) { return url.endsWith('/infra/fonteDados.js') ? { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" } : next(url, c) },
})
const j = await import('../src/aplicacao/janelaDoPedido.js')
const jsx = readFileSync(new URL('../src/app/NovoPedidoJanela.jsx', import.meta.url), 'utf8')

const aCombinar = { freteACombinar: true, frete: null, total: 50 }
const cotado = { freteACombinar: false, frete: 8, total: 58 }
const base = { janela: 'j1|2026-11-21', meio: 'pix' }

// 1. Fora da área sem frete combinado: não avança, e o motivo diz o que falta.
assert.equal(j.faltaFreteCombinado(aCombinar, base), true)
assert.equal(j.podeAvancarDaEntrega({ pedido: base, simulacao: aCombinar }), false)
assert.equal(j.motivoDeBloqueio({ passo: 'entrega', itens: [{ sku: 'a', qtd: 1 }], pedido: base, simulacao: aCombinar }), 'Informe o frete combinado')

// 2. Frete combinado informado (zero vale): avança.
for (const freteCombinado of [12.5, 0]) {
  const pedido = { ...base, freteCombinado }
  assert.equal(j.faltaFreteCombinado(aCombinar, pedido), false, `frete ${freteCombinado}`)
  assert.equal(j.podeAvancarDaEntrega({ pedido, simulacao: aCombinar }), true)
  assert.equal(j.motivoDeBloqueio({ passo: 'entrega', itens: [{ sku: 'a', qtd: 1 }], pedido, simulacao: aCombinar }), null)
}

// 3. Com cotação, nada muda.
assert.equal(j.faltaFreteCombinado(cotado, base), false)
assert.equal(j.faltaFreteCombinado(null, base), false, 'sem simulação ainda não acusa')
assert.equal(j.podeAvancarDaEntrega({ pedido: base, simulacao: cotado }), true)

// 4. A tela usa o mesmo campo do Atendimento e grava pelo mesmo `definirFreteCombinado`.
assert.match(jsx, /<CampoReais[\s\S]*rotulo="Frete combinado"/)
assert.match(jsx, /definirFreteCombinado\(janela\.conversaId, centavos == null \? null : centavos \/ 100\)/)
assert.match(jsx, /podeAvancarDaEntrega\(\{[^}]*simulacao/)
assert.match(jsx, /motivoDeBloqueio\(\{[^}]*simulacao/)

console.log('prova-2974-frete-combinado-janela: ok')
