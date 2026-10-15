/* eslint-disable no-console */
// Prova da issue #1442: Balcão, banner de pausa e Ficha mais simples.
//   1. o Balcão abre em "Todas" (estado inicial do reducer);
//   2. a linha da conversa diz o estado em uma palavra (automático, com você,
//      outro atendente, encerrada, bloqueado) e o motivo de precisar vence;
//   3. o número da linha é o de mensagens sem resposta, com a API de reserva;
//   4. o contexto da linha (pedido, entrega, janela, tag, origem) sai numa lista curta;
//   5. o banner de pausa cabe em uma linha e guarda a frase inteira no detalhe;
//   6. a memória das seções recolhidas da Ficha sobrevive a lixo no localStorage.
//
//   node ferramentas/prova-1442-balcao-simples.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

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
const importar = (caminho) => import(caminho).catch(() => ({}))

const { estadoInicial } = await importar('../src/aplicacao/reducer.js')
const linha = await importar('../src/dominio/linhaDoBalcao.js')
const modo = await importar('../src/dominio/resumoDoModo.js')
const recolhidos = await importar('../src/hooks/recolhidos.js')
const { PAUSA_POR_ASSUMIR, PAUSA_POR_OUTRO } = await importar('../src/dominio/automatico.js')

const T0 = new Date('2026-10-07T15:00:00Z').getTime()
const iso = (minAtras) => new Date(T0 - minAtras * 60000).toISOString()
const conversa = (extra = {}) => ({
  id: 'c1', nome: 'Marina Souza', canal: 'WhatsApp', estado: 'Aberto', conta: 'cliente',
  cliente: { tags: [], notas: [], pedidos: 2 }, pedido: null, passagem: null,
  mensagens: [{ id: 'm1', dir: 'in', texto: 'Oi, tem lasanha?', em: iso(2) }],
  ultimaEm: iso(2),
  ...extra,
})

// --- 1. Todas é a aba padrão ------------------------------------------------------
await confere('1. o Balcão abre em "Todas", não em "Precisa de você"', () => {
  assert.ok(estadoInicial, 'estadoInicial não exportado')
  const inicio = estadoInicial({ conversas: [], catalogo: { cardapio: [], janelas: [], canais: [] }, regras: [] })
  assert.equal(inicio.filtros.aba, 'todas')
})

// --- 2. Estado da linha ---------------------------------------------------------
await confere('2. automático ligado: "Automático"', () => {
  const e = linha.estadoDaLinha(conversa(), { pausado: false })
  assert.equal(e.chave, 'automatico')
  assert.equal(e.texto, 'Automático')
})
await confere('2. ela assumiu ou escreveu: "Com você"', () => {
  assert.equal(linha.estadoDaLinha(conversa(), { pausado: PAUSA_POR_ASSUMIR }).texto, 'Com você')
  assert.equal(linha.estadoDaLinha(conversa(), { pausado: true }).chave, 'voce')
})
await confere('2. outro atendente assumiu: não diz que é ela', () => {
  const e = linha.estadoDaLinha(conversa(), { pausado: PAUSA_POR_OUTRO })
  assert.equal(e.chave, 'outro')
  assert.equal(e.texto, 'Outro atendente')
})
await confere('2. encerrada e bloqueado vencem a pausa', () => {
  assert.equal(linha.estadoDaLinha(conversa({ estado: 'Encerrado' }), { pausado: true }).texto, 'Encerrada')
  assert.equal(linha.estadoDaLinha(conversa(), { grupo: 'encerradas' }).chave, 'encerrada')
  assert.equal(linha.estadoDaLinha(conversa({ bloqueio: { motivo: 'x' } }), { pausado: true }).texto, 'Bloqueado')
})
await confere('2. motivo de precisar vence o estado e mantém o tom', () => {
  const motivo = { chave: 'esperando', tom: 'perigo', rotulo: 'Esperando você', texto: 'Há 9 min' }
  const e = linha.estadoDaLinha(conversa(), { pausado: true, motivo })
  assert.equal(e.chave, 'esperando')
  assert.equal(e.texto, 'Esperando você')
  assert.equal(e.tom, 'perigo')
  assert.equal(e.titulo, 'Há 9 min')
})
await confere('2. passou para você e ninguém pegou: o estado é dela', () => {
  const e = linha.estadoDaLinha(conversa({ passagem: { motivo: 'Glúten', assumida: false } }), { pausado: false })
  assert.equal(e.chave, 'voce')
})

// --- 3. Número da linha ------------------------------------------------------------
await confere('3. conta as mensagens do cliente depois da última resposta', () => {
  const c = conversa({
    mensagens: [
      { id: 'a', dir: 'out', texto: 'Oi!', em: iso(10) },
      { id: 'b', dir: 'in', texto: 'Tem lasanha?', em: iso(5) },
      { id: 'c', dir: 'in', texto: 'E nhoque?', em: iso(4) },
    ],
  })
  assert.equal(linha.pendentesDaLinha(c), 2)
})
await confere('3. respondida: zero, nada de selo', () => {
  const c = conversa({ mensagens: [{ id: 'b', dir: 'in', texto: 'Oi', em: iso(5) }, { id: 'a', dir: 'out', texto: 'Oi!', em: iso(4) }] })
  assert.equal(linha.pendentesDaLinha(c), 0)
})
await confere('3. sem mensagens carregadas (encerrada na API): usa `naoLidas`', () => {
  assert.equal(linha.pendentesDaLinha(conversa({ mensagens: [], naoLidas: 3 })), 3)
  assert.equal(linha.pendentesDaLinha(conversa({ mensagens: [] })), 0)
})

// --- 4. Contexto da linha ------------------------------------------------------------
await confere('4. contexto em ordem: pedido, entrega, janela, tag; vazio quando nada', () => {
  assert.deepEqual(linha.contextoDaLinha({}), [])
  const itens = linha.contextoDaLinha({
    pedido: { texto: 'Em preparo', icone: 'cooking-pot', tom: 'neutro' },
    entregaAte: '19h30',
    tagAchada: 'Gosta de lasanha',
  })
  assert.deepEqual(itens.map((i) => i.chave), ['pedido', 'entrega', 'tag'])
  assert.equal(itens[1].texto, 'entrega até 19h30')
  assert.equal(itens[2].texto, 'Tag: Gosta de lasanha')
})
await confere('4. a origem do "Passou para você" vem primeiro', () => {
  const itens = linha.contextoDaLinha({ origem: 'Automático não soube responder: glúten', pedido: { texto: 'Pago', tom: 'ok' } })
  assert.deepEqual(itens.map((i) => i.chave), ['origem', 'pedido'])
  assert.equal(itens[0].tom, 'aviso')
})
await confere('4. janela fechada esconde a hora da entrega e diz "Só modelo"', () => {
  const itens = linha.contextoDaLinha({ entregaAte: '19h30', janelaFechada: true })
  assert.deepEqual(itens.map((i) => i.texto), ['Só modelo'])
})

// --- 5. Banner de pausa ------------------------------------------------------------
const respondeu = (extra = {}) => conversa({
  mensagens: [
    { id: 'm1', dir: 'in', texto: 'Tem sem glúten?', em: iso(5) },
    { id: 'm2', dir: 'out', texto: 'Temos sim!', em: iso(1) },
  ],
  ...extra,
})
const LIMITE = 64
await confere(`5. todas as linhas do banner cabem em ${LIMITE} caracteres`, () => {
  const casos = [
    [conversa(), false, false],
    [conversa(), PAUSA_POR_ASSUMIR, false],
    [conversa(), true, false],
    [conversa(), PAUSA_POR_OUTRO, false],
    [respondeu(), PAUSA_POR_ASSUMIR, false],
    [respondeu({ pedido: { estado: 'preparo', itens: [] } }), PAUSA_POR_ASSUMIR, false],
    [conversa({ estado: 'Encerrado' }), false, false],
    [conversa(), false, true],
  ]
  for (const [c, pausado, bloqueada] of casos) {
    const r = modo.resumoDoModo(c, pausado, bloqueada)
    assert.ok(r.linha.length <= LIMITE, `"${r.linha}" tem ${r.linha.length}`)
    assert.ok(r.rotulo.length <= 24, `"${r.rotulo}" longo demais`)
    assert.doesNotMatch(r.linha, /Próximo passo sugerido/)
  }
})
await confere('5. você assumiu: "Com você", automático pausado, aviso do pedido segue', () => {
  const r = modo.resumoDoModo(conversa(), PAUSA_POR_ASSUMIR, false)
  assert.equal(r.rotulo, 'Com você')
  assert.match(r.linha, /pausado/i)
  assert.match(r.detalhe, /Pausado porque você assumiu/)
})
await confere('5. você respondeu, sem pedido: encerrar ou devolver, e oferece Encerrar', () => {
  const r = modo.resumoDoModo(respondeu(), PAUSA_POR_ASSUMIR, false)
  assert.match(r.linha, /encerre/i)
  assert.match(r.linha, /devolva/i)
  assert.equal(r.sugereEncerrar, true)
  assert.match(r.detalhe, /Próximo passo sugerido/, 'a frase inteira fica no detalhe (title)')
})
await confere('5. você respondeu com pedido em andamento: aguardar, sem Encerrar', () => {
  const r = modo.resumoDoModo(respondeu({ pedido: { estado: 'preparo', itens: [] } }), PAUSA_POR_ASSUMIR, false)
  assert.match(r.linha, /aguarde/i)
  assert.equal(r.sugereEncerrar, false)
})
await confere('5. passou para você: a linha é o motivo', () => {
  const r = modo.resumoDoModo(conversa({ passagem: { motivo: 'Restrição alimentar', assumida: false } }), false, false)
  assert.equal(r.rotulo, 'Passou para você')
  assert.match(r.linha, /Restrição alimentar/)
})

// --- 6. Seções recolhidas da Ficha ------------------------------------------------
await confere('6. lixo ou nada no localStorage vira mapa vazio', () => {
  assert.deepEqual(recolhidos.lerRecolhidos(null), {})
  assert.deepEqual(recolhidos.lerRecolhidos('não é json'), {})
  assert.deepEqual(recolhidos.lerRecolhidos('[1,2]'), {})
  assert.deepEqual(recolhidos.lerRecolhidos('{"tags":true,"x":"sim"}'), { tags: true })
})
await confere('6. alternar recolhe e abre de novo, sem mexer nas outras seções', () => {
  const um = recolhidos.alternarRecolhido({ historico: true }, 'tags')
  assert.deepEqual(um, { historico: true, tags: true })
  assert.deepEqual(recolhidos.alternarRecolhido(um, 'tags'), { historico: true, tags: false })
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
