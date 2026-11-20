/* eslint-disable no-console */
// Prova da #2944 (E1-12): o vocabulário novo está nas telas e os rótulos velhos saíram.
//   node ferramentas/prova-2944-vocabulario.mjs
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')

const casos = [
  ['features/ficha-cliente/BlocoEntrega.jsx', /rotulo: 'Saiu para entrega'/, /rotulo: 'Saiu',/],
  ['app/NovoPedidoJanela.jsx', /'Registrar pedido'/, /: 'Registrar'\}/],
  ['features/ficha-cliente/BarraProximoPasso.jsx', /Entregar e cobrar depois/, /Marcar entregue mesmo assim/],
  ['features/caixa-de-entrada/NaoEntregues.jsx', /titulo="Mensagens não entregues"/, /titulo="Não entregues"/],
  ['features/gestao/cadastros/CelularDoPedido.jsx', /rotulo="Celular com DDD \(para enviar o resumo\)"/, /rotulo="Celular com DDD"/],
]
for (const [arquivo, novo, velho] of casos) {
  const texto = fonte(arquivo)
  assert.match(texto, novo, `${arquivo} diz o texto novo`)
  assert.doesNotMatch(texto, velho, `${arquivo} não diz o texto velho`)
}
console.log('prova-2944-vocabulario: ok')
