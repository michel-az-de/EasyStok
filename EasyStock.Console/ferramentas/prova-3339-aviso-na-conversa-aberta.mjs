/* eslint-disable no-console */
// Prova da #3339 (R2F-06, `docs/dev/anexos-estresse-atendimento-2026-11-25/r2-console-ia.md`): com a conversa da
// Juliana aberta, o aviso "Mensagem nova" ocupava x 1043 a 1410 e escondia o fim da barra, onde mora "Assumir
// conversa". A mensagem já está no fio e o som toca; o toast só repete. Agora ele fica de fora para a conversa que
// está à vista (aba visível e painel na tela). Outros avisos e outras conversas seguem como antes.
//
//   node ferramentas/prova-3339-aviso-na-conversa-aberta.mjs

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

let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok  ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA  ${nome}\n      ${String(erro.message).split('\n')[0]}`) }
}

const { avisoJaEstaNaTela, TIPOS_DE_AVISO } = await import('../src/dominio/aviso.js')

const aVista = { selecionadaId: 'c1', painelNaTela: true, abaVisivel: true }
const mensagemNova = (conversaId) => ({ tipo: TIPOS_DE_AVISO.MENSAGEM_NOVA, conversaId })

caso('R2F-06: mensagem nova da conversa à vista não vira toast', () => {
  assert.equal(typeof avisoJaEstaNaTela, 'function', 'avisoJaEstaNaTela não existe')
  assert.equal(avisoJaEstaNaTela(mensagemNova('c1'), aVista), true)
})

caso('mensagem nova de outra conversa continua avisando', () => {
  assert.equal(avisoJaEstaNaTela(mensagemNova('c2'), aVista), false)
})

caso('aba escondida ou painel fora da tela continua avisando', () => {
  assert.equal(avisoJaEstaNaTela(mensagemNova('c1'), { ...aVista, abaVisivel: false }), false)
  assert.equal(avisoJaEstaNaTela(mensagemNova('c1'), { ...aVista, painelNaTela: false }), false)
})

caso('passou para você, pagamento e contato novo nunca são calados', () => {
  for (const tipo of [TIPOS_DE_AVISO.PASSOU_PARA_VOCE, TIPOS_DE_AVISO.PAGAMENTO_CONFIRMADO, TIPOS_DE_AVISO.CONTATO_NOVO, TIPOS_DE_AVISO.ETAPA_DO_PEDIDO]) {
    assert.equal(avisoJaEstaNaTela({ tipo, conversaId: 'c1' }, aVista), false, tipo)
  }
})

caso('sem conversa aberta, nada é calado', () => {
  assert.equal(avisoJaEstaNaTela(mensagemNova('c1'), { ...aVista, selecionadaId: null }), false)
  assert.equal(avisoJaEstaNaTela(null, aVista), false)
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
