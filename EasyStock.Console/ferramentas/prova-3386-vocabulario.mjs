// #3386 (r5, item 1): o Atendimento e a Ficha dizem cada estado do pagamento com um nome só ("Pix vencido",
// "Aguardando Pix", "Falta R$ X") e o pedido com um rótulo só ("Pedido 0194", sem o prefixo de ano).
// Cartão, barra e ficha usam as mesmas funções; texto que vai ao cliente (WhatsApp, link) não entra aqui.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      try { return proximo(spec + '.js', contexto) } catch { /* resolução normal */ }
    }
    return proximo(spec, contexto)
  },
})
const { rotuloDoPedido } = await import('../src/dominio/rotulos.js')
const { motivoDePrecisar } = await import('../src/dominio/automatico.js')
const { situacaoDaCobranca } = await import('../src/dominio/cobranca.js')
const { moeda } = await import('../src/dominio/formato.js')
const { motivoResumido } = await import('../src/features/caixa-de-entrada/apresentacaoDoCartao.js')
const { acaoDoPix } = await import('../src/features/ficha-cliente/acaoDoPix.js')
const { resumoDoPagamento } = await import('../src/features/ficha-cliente/apresentacaoDaFicha.js')
const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')

const AGORA = new Date('2026-11-27T11:05:00-03:00').getTime()
const ha = (min) => new Date(AGORA - min * 60000).toISOString()
const NUMERO = '2026-0194'
const conversa = (pedido, texto = 'oi') => ({
  id: 'c1', nome: 'Sandra', estado: 'Aberto', cliente: { tags: [] },
  mensagens: [{ dir: 'in', texto, em: ha(1) }], pedido,
})

// Pedido: um rótulo só, o número curto.
assert.equal(rotuloDoPedido(NUMERO), 'Pedido 0194')
assert.equal(rotuloDoPedido('12'), 'Pedido 12')
for (const [arq, ruim] of [
  ['features/ficha-cliente/AreaColaborativaIa.jsx', /`Pedido \$\{comanda\.numero\}`/],
  ['features/atendimento/ModalEnviarCardapio.jsx', /Pedido \$\{numero\}/],
]) assert.ok(!ruim.test(fonte(arq)), `${arq} mostra o número inteiro do pedido`)

// Cartão: o motivo da lista e o do domínio dizem a mesma coisa.
assert.equal(motivoResumido('cobranca').texto, 'Pix vencido')
const vencida = motivoDePrecisar(conversa({ numero: NUMERO, cobranca: { valor: 106, criadaEm: AGORA - 60 * 60000, expiraEm: AGORA - 30 * 60000 } }), AGORA, false)
assert.equal(vencida?.chave, 'cobranca')
assert.equal(vencida.rotulo, 'Pix vencido')
assert.equal(vencida.texto, 'Pedido 0194: Pix vencido')
const atrasado = motivoDePrecisar(conversa({ numero: NUMERO, cobranca: { valor: 106, criadaEm: AGORA - 60 * 60000, expiraEm: AGORA + 30 * 60000 } }, 'já paguei o pix'), AGORA, false)
assert.ok(atrasado && !/pagamento não caiu/.test(atrasado.texto) && !atrasado.texto.includes(NUMERO), `diz-que-pagou: ${atrasado?.texto}`)
assert.match(atrasado.texto, /0194/)

// Barra (Ficha): vencido e aguardando com o mesmo nome do cartão.
const pix = (chave) => acaoDoPix({ chave }, { aoAbrirBaixa() {}, aoReenviarPix() {}, linkVencidoApi: null })
assert.equal(pix('expirada').rotuloEstado, 'Pix vencido')
assert.equal(pix('aguardando').rotuloEstado, 'Aguardando Pix')
assert.equal(situacaoDaCobranca({ valor: 10, expiraEm: AGORA + 60 * 60000 }, AGORA).rotulo, 'Aguardando Pix')

// Valor: "Falta R$ X" na ficha.
assert.equal(resumoDoPagamento({ saldo: 69 }), `Falta ${moeda(69)}`)
assert.match(fonte('features/ficha-cliente/PainelFicha.jsx'), /Falta \{moeda\(saldo\)\}/)
assert.ok(!/\} a receber<\/p>/.test(fonte('features/ficha-cliente/PainelFicha.jsx')), 'PainelFicha ainda diz "a receber"')
console.log('prova-3386-vocabulario: ok')
