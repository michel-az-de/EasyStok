/* eslint-disable no-console */
// Prova da issue #1875 (rodada 3, frente C1: sobras do Balcão e da ficha, banca de atendimento de 16/10).
//
//   1  o meio de pagamento escolhido na barra só marca: o pedido é criado e a cobrança enviada no botão
//      de confirmação, com o resumo do que vai acontecer (Cartão por link, Pix, maquininha);
//   2  link do Mercado Pago pendente: "Marcar pago" deixa de ser o botão primário e a nota diz por quê;
//      na demonstração e sem link ele segue primário;
//   3  antes de gerar a cobrança a comanda diz o total dos itens e, na encomenda, o sinal de 50%;
//   4  em "Seleção do atendimento" a escolha da janela vem antes da comanda (1026 px de conteúdo e a lista
//      de 10 janelas começando a 541 px: nenhuma cabia sem rolar em 1366x768).
//
// A busca que abria a conversa errada não reproduziu (195 cliques conferidos pelo DOM em 6 cenários, 0
// divergências) e por isso não tem prova nem código: ver a #1875.
//
//   node ferramentas/prova-1875-balcao.mjs

import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    try {
      return proximo(especificador, contexto)
    } catch (erro) {
      if (especificador.startsWith('.') && !/\.m?jsx?$/.test(especificador)) return proximo(especificador + '.js', contexto)
      throw erro
    }
  },
})

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok  ' + descricao) }
  catch (erro) { falhas.push(descricao); console.error('FALHOU  ' + descricao + '\n        ' + String(erro.message).split('\n')[0]) }
}

const { moeda } = await import('../src/dominio/formato.js')
const AGORA = Date.parse('2026-10-17T15:00:00-03:00')
const encomenda = { pedidoId: null, estado: 'aguardando', itens: [{ sku: 'a', qtd: 1 }], janela: 'j1|2026-10-20', meio: null }
const hoje = { ...encomenda, janela: 'j1|2026-10-17' }

// --- 1. Resumo antes de cobrar -----------------------------------------------------------------

await confere('1  sinalDeEncomenda: metade em centavos, arredondando para cima como o servidor', async () => {
  const { sinalDeEncomenda } = await import('../src/dominio/cobranca.js')
  assert.equal(sinalDeEncomenda(118), 59)
  assert.equal(sinalDeEncomenda(118.01), 59.01)
  assert.equal(sinalDeEncomenda(0), 0)
})

await confere('1  resumoDaCobrancaEscolhida diz o total, o meio e que o frete entra ao gerar', async () => {
  const { resumoDaCobrancaEscolhida } = await import('../src/dominio/resumoPedido.js')
  const texto = resumoDaCobrancaEscolhida(hoje, { meio: 'cartao-link', rotuloMeio: 'Cartão por link', total: 118, fonteApi: true, agora: AGORA })
  assert.ok(texto.includes(moeda(118)), texto)
  assert.ok(texto.includes('Cartão por link'), texto)
  assert.match(texto, /frete/i)
  assert.doesNotMatch(texto, /sinal/i, 'o mesmo dia não tem sinal')
})

await confere('1  encomenda para outro dia na maquininha: o resumo traz o sinal de 50% em reais', async () => {
  const { resumoDaCobrancaEscolhida } = await import('../src/dominio/resumoPedido.js')
  const texto = resumoDaCobrancaEscolhida(encomenda, { meio: 'maquininha', rotuloMeio: 'Maquininha na entrega', total: 118, fonteApi: true, agora: AGORA })
  assert.ok(texto.includes(moeda(59)), texto)
  assert.match(texto, /sinal de 50% do total com o frete/i)
})

await confere('1  encomenda no Pix ou cartão: o resumo diz que o link cobra o sinal sobre o total com o frete, não o total', async () => {
  const { resumoDaCobrancaEscolhida } = await import('../src/dominio/resumoPedido.js')
  for (const [meio, rotuloMeio] of [['pix', 'Pix'], ['cartao-link', 'Cartão por link']]) {
    const texto = resumoDaCobrancaEscolhida(encomenda, { meio, rotuloMeio, total: 118, fonteApi: true, agora: AGORA })
    assert.match(texto, /sinal de 50% do total com o frete/i, texto)
    assert.ok(texto.includes(moeda(59)), texto)
    assert.ok(texto.includes(rotuloMeio), texto)
  }
  // O mesmo dia cobra o total.
  assert.doesNotMatch(resumoDaCobrancaEscolhida(hoje, { meio: 'pix', rotuloMeio: 'Pix', total: 118, fonteApi: true, agora: AGORA }), /sinal/i)
})

await confere('1  tocar no meio na barra não cria o pedido: só o botão de confirmação', () => {
  const barra = fonte('features/ficha-cliente/BarraProximoPasso.jsx')
  assert.doesNotMatch(barra, /aoEscolher=\{\(meio\) => \{ setPedindoMeio\(false\); executar/, 'o chip ainda gera na hora')
  assert.match(barra, /aoEscolher=\{setMeioNaBarra\}/)
  assert.match(barra, /resumoDaCobrancaEscolhida\(/)
  assert.match(barra, /data-confirmar-cobranca/)
  assert.match(barra, /const meio = meioNaBarra; setPedindoMeio\(false\); setMeioNaBarra\(null\); executar\(\(\) => aoGerarPix\(meio\), GERANDO_COBRANCA\)/)
})

// --- 2. Marcar pago com link pendente ----------------------------------------------------------

await confere('2  com link do Mercado Pago pendente "Marcar pago" é secundário e a nota diz por quê', async () => {
  const { acaoDoPix } = await import('../src/features/ficha-cliente/acaoDoPix.js')
  const situacao = { chave: 'aguardando' }
  const comLink = acaoDoPix(situacao, { linkPendenteApi: true, aoAbrirBaixa() {} })
  assert.equal(comLink.botoes.length, 1)
  assert.equal(comLink.botoes[0].rotulo, 'Marcar pago')
  assert.equal(comLink.botoes[0].secundario, true)
  assert.match(comLink.nota, /Mercado Pago confirma sozinho/)
})

await confere('2  sem link (demonstração, maquininha, sinal manual) "Marcar pago" segue primário', async () => {
  const { acaoDoPix } = await import('../src/features/ficha-cliente/acaoDoPix.js')
  for (const situacao of [{ chave: 'aguardando' }, { chave: 'combinada' }]) {
    const info = acaoDoPix(situacao, { linkPendenteApi: false, aoAbrirBaixa() {} })
    assert.equal(info.botoes[0].rotulo, 'Marcar pago')
    assert.notEqual(info.botoes[0].secundario, true, `${situacao.chave} deve ser primário`)
    assert.equal(info.nota, undefined)
  }
  // Cobrança vencida e em conferência não mudam.
  assert.equal(acaoDoPix({ chave: 'em-conferencia' }, { aoAbrirBaixa() {} }).botoes[0].secundario, undefined)
})

await confere('2  a barra desenha o botão secundário e a nota', () => {
  const barra = fonte('features/ficha-cliente/BarraProximoPasso.jsx')
  assert.match(barra, /variante=\{b\.secundario \? 'secundario' : 'primario'\}/)
  assert.match(barra, /\{info\.nota && <p className=\{css\.motivoDesabilitado\}>\{info\.nota\}<\/p>\}/)
  assert.match(barra, /linkPendenteApi: fonteApi && Boolean\(pedido\.cobranca\?\.link\)/)
})

// --- 3. Quanto fica ----------------------------------------------------------------------------

await confere('3  previaDoSinal: encomenda de outro dia no modo API antes do pedido; senão nada', async () => {
  const { previaDoSinal } = await import('../src/dominio/resumoPedido.js')
  assert.deepEqual(previaDoSinal(encomenda, { fonteApi: true, agora: AGORA, total: 118 }), { sinal: 59, restante: 59 })
  assert.equal(previaDoSinal(hoje, { fonteApi: true, agora: AGORA, total: 118 }), null)
  assert.equal(previaDoSinal({ ...encomenda, pedidoId: 'p1' }, { fonteApi: true, agora: AGORA, total: 118 }), null)
  assert.equal(previaDoSinal(encomenda, { fonteApi: false, agora: AGORA, total: 118 }), null)
})

await confere('3  a comanda mostra a prévia do sinal junto da nota do frete', () => {
  const bloco = fonte('features/ficha-cliente/BlocoPedido.jsx')
  assert.match(bloco, /previaDoSinal\(pedido,/)
  assert.match(bloco, /sinal de 50% do total com o frete/)
  assert.match(bloco, /O frete entra ao gerar a cobrança/)
})

// --- 4. Janelas ---------------------------------------------------------------------------------

await confere('4  na Seleção do atendimento a escolha da janela vem antes da comanda', () => {
  const bloco = fonte('features/ficha-cliente/BlocoPedido.jsx')
  const seletor = bloco.indexOf('const seletorDeJanela')
  const comanda = bloco.indexOf('id="comanda-pedido"')
  assert.ok(seletor > 0 && comanda > 0, 'seletor e comanda no arquivo')
  assert.match(bloco, /janelaPrimeiro && seletorDeJanela/)
  assert.match(bloco, /!janelaPrimeiro && seletorDeJanela/)
  assert.match(bloco, /const janelaPrimeiro = !fechado && fonteApi && !pedido\.pedidoId/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
