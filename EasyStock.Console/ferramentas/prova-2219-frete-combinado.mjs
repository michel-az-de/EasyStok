/* eslint-disable no-console */
// Prova da issue #2219: CEP fora da área deixa o frete "a combinar" e a cobrança do atendimento recusava.
// A equipe combina o valor com o cliente e digita na comanda; o valor vai na simulação e no pedido.
//
//   1  corpoDoPedido e corpoDaSimulacao levam freteCombinado quando há (zero vale), e omitem quando não;
//   2  DEFINIR_FRETE_COMBINADO grava no rascunho do pedido e não mexe em pedido já criado;
//   3  a ação é da tela (naoLigadas) e a comanda mostra o campo só com o frete a combinar;
//   4  a linha do frete diz que o valor foi combinado.
//
//   node ferramentas/prova-2219-frete-combinado.mjs

import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    return proximo(url, contexto)
  },
})

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8').replaceAll(String.fromCharCode(13), '')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok  ' + descricao) }
  catch (erro) { falhas.push(descricao); console.error('FALHOU  ' + descricao + '\n        ' + String(erro.message).split('\n')[0]) }
}

const semEspacoDuro = (texto) => String(texto ?? '').replace(/ /g, ' ')
const { corpoDoPedido, corpoDaSimulacao } = await import('../src/infra/api/comandaApi.js')
const acao = await import('../src/aplicacao/acoes.js')
const { casosMeioDePagamento } = await import('../src/aplicacao/casos/meioDePagamento.js')
const { linhasDaSimulacao } = await import('../src/dominio/simulacaoPedido.js')

const rascunho = { pedidoId: null, estado: 'aguardando', itens: [{ sku: 'massa', qtd: 1, obs: '' }], janela: 'j1|2026-11-05', meio: 'pix' }

await confere('1  freteCombinado vai no corpo do pedido e da simulação; zero vale; nulo não manda', () => {
  assert.equal(corpoDoPedido({ ...rascunho, freteCombinado: 8 }).freteCombinado, 8)
  assert.equal(corpoDoPedido({ ...rascunho, freteCombinado: 0 }).freteCombinado, 0, 'frete combinado grátis é zero, não ausente')
  assert.equal('freteCombinado' in corpoDoPedido(rascunho), false)
  assert.equal('freteCombinado' in corpoDoPedido({ ...rascunho, freteCombinado: null }), false)
  assert.equal(corpoDaSimulacao({ ...rascunho, freteCombinado: 12.5 }).freteCombinado, 12.5)
  assert.equal('freteCombinado' in corpoDaSimulacao(rascunho), false)
})

await confere('2  DEFINIR_FRETE_COMBINADO grava no rascunho e não mexe em pedido criado', () => {
  assert.equal(acao.DEFINIR_FRETE_COMBINADO, 'definir-frete-combinado')
  const caso = casosMeioDePagamento[acao.DEFINIR_FRETE_COMBINADO]
  assert.equal(typeof caso, 'function')
  const estado = { conversas: [{ id: 'c1', pedido: rascunho }, { id: 'c2', pedido: { ...rascunho, pedidoId: 'p' } }] }
  const depois = caso(estado, { id: 'c1', freteCombinado: 8 })
  assert.equal(depois.conversas[0].pedido.freteCombinado, 8)
  assert.equal(caso(depois, { id: 'c1', freteCombinado: null }).conversas[0].pedido.freteCombinado, null)
  assert.equal(caso(estado, { id: 'c2', freteCombinado: 8 }), estado, 'pedido criado: vale o que ficou gravado')
  assert.equal(caso(estado, { id: 'nenhuma', freteCombinado: 8 }), estado)
})

await confere('3  a ação é da tela e a comanda mostra o campo só com o frete a combinar', () => {
  assert.match(fonte('aplicacao/api/naoLigadas.js'), /'definirFreteCombinado'/)
  assert.match(fonte('aplicacao/acoes/locais.js'), /definirFreteCombinado: \(id, freteCombinado\) => despachar\(\{ tipo: acao\.DEFINIR_FRETE_COMBINADO, id, freteCombinado \}\)/)
  const bloco = fonte('features/ficha-cliente/BlocoPedido.jsx')
  assert.match(bloco, /simulacao\?\.freteACombinar \|\| pedido\.freteCombinado !== undefined/, 'apagar não some com o campo')
  assert.match(bloco, /rotulo="Frete combinado"/)
  assert.match(bloco, /definirFreteCombinado\(conversaId, centavos == null \? null : centavos \/ 100\)/)
})

await confere('4  a linha do frete diz que o valor foi combinado', () => {
  const linhas = linhasDaSimulacao({ itens: 38, frete: 0, freteACombinar: false, total: 38, encomenda: false, sinal: null, restante: null }, { combinado: true })
  assert.equal(semEspacoDuro(linhas[0].texto), 'Frete combinado R$ 0,00')
  assert.equal(semEspacoDuro(linhasDaSimulacao({ frete: 5, total: 25, freteACombinar: false })[0].texto), 'Frete R$ 5,00', 'cotação segue igual')
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
