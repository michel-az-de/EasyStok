/* eslint-disable no-console */
// Prova da issue #1440 (homologação de 07/10): janelas de entrega pela Gestão no modo API,
// entregas do dia organizadas por janela e o roteiro impresso do dia.
//   1. o formulário de janela vira um corpo por dia marcado (a API guarda uma por dia) e
//      edição/exclusão falam com PUT/DELETE de `api/minha-vitrine/entrega/janelas/{id}`;
//   2. `roteiroDoDia` agrupa os pedidos do dia por janela, com status, pagamento, endereço,
//      quem leva e o que falta para sair;
//   3. o roteiro sai em PDF A4 (paginado) e na bobina de 80 mm, só em preto.
//
//   node ferramentas/prova-1440-janelas-e-roteiro.mjs

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
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    return proximo(url, contexto)
  },
})

const chamadas = []
globalThis.fetch = async (url, opcoes = {}) => {
  chamadas.push({ url, metodo: opcoes.method ?? 'GET', corpo: opcoes.body ? JSON.parse(opcoes.body) : undefined })
  if ((opcoes.method ?? 'GET') === 'DELETE') return new Response(null, { status: 204 })
  return new Response(JSON.stringify({ data: { id: 'j1' } }), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const entregasApi = await import('../src/dominio/entregasApi.js')
const roteiro = await import('../src/dominio/roteiroDoDia.js').catch(() => ({}))
const impressao = await import('../src/dominio/impressao.js')
const { A4, LARGURA_BOBINA } = await import('../src/dominio/pdf.js')
const infra = await import('../src/infra/api/entregasApi.js')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

// --- 1. Cadastro de janela ----------------------------------------------------------------
await confere('formulário com três dias vira três janelas, uma por dia, no formato da API', () => {
  const corpos = entregasApi.corposJanela({
    dias: [5, 4, 4, 6], horaInicio: '12:00', horaFim: '14:00', capacidadeMaxima: '4', label: ' Almoço ',
  })
  assert.deepEqual(corpos, [4, 5, 6].map((diaDaSemana) => ({
    diaDaSemana, horaInicio: '12:00:00', horaFim: '14:00:00', capacidadeMaxima: 4, label: 'Almoço',
  })))
})
await confere('sem nome, a janela leva a própria faixa como nome', () => {
  const [corpo] = entregasApi.corposJanela({ dias: [1], horaInicio: '16:00', horaFim: '18:30', capacidadeMaxima: '5', label: '' })
  assert.equal(corpo.label, '16h00 às 18h30')
})
await confere('validação do formulário diz o motivo antes de chamar a API', () => {
  const ok = { dias: [1], horaInicio: '12:00', horaFim: '14:00', capacidadeMaxima: '4', label: '' }
  assert.equal(entregasApi.erroDoFormularioJanela(ok), null)
  assert.match(entregasApi.erroDoFormularioJanela({ ...ok, horaFim: '11:00' }), /depois do início/)
  assert.match(entregasApi.erroDoFormularioJanela({ ...ok, capacidadeMaxima: '0' }), /Capacidade/)
  assert.match(entregasApi.erroDoFormularioJanela({ ...ok, dias: [] }), /dia/)
})
await confere('editar parte dos campos da janela que veio da API', () => {
  const campos = entregasApi.camposDaJanela({
    id: 'j1', diaDaSemana: 4, horaInicio: '12:00:00', horaFim: '14:00:00', capacidadeMaxima: 4, label: 'Almoço', ativa: true,
  })
  assert.deepEqual(campos, { dias: [4], horaInicio: '12:00', horaFim: '14:00', capacidadeMaxima: '4', label: 'Almoço' })
})
await confere('editar faz PUT e excluir faz DELETE na janela da loja', async () => {
  chamadas.length = 0
  await infra.atualizarJanela('j1', { diaDaSemana: 4, horaInicio: '12:00:00', horaFim: '14:00:00', capacidadeMaxima: 6, label: 'Almoço' })
  await infra.excluirJanela('j1')
  assert.deepEqual(chamadas.map((c) => `${c.metodo} ${c.url}`), [
    'PUT /api/minha-vitrine/entrega/janelas/j1',
    'DELETE /api/minha-vitrine/entrega/janelas/j1',
  ])
  assert.equal(chamadas[0].corpo.capacidadeMaxima, 6)
})
await confere('o dia do roteiro pede ao KDS todos os status do dia, com a data', async () => {
  chamadas.length = 0
  await infra.listarPedidosEntrega(entregasApi.STATUS_KDS_DIA, '2026-10-08')
  const url = decodeURIComponent(chamadas[0].url)
  for (const status of ['aguardando_pagamento', 'aguardando_aprovacao_baba', 'aguardando', 'preparando', 'pronto', 'saiu_para_entrega', 'entregue']) {
    assert.ok(url.includes(status), `falta ${status} em ${url}`)
  }
  assert.ok(!url.includes('cancelado'), 'cancelado não entra no roteiro')
  assert.match(url, /[?&]data=2026-10-08/)
})

// --- 2. Entregas do dia por janela -------------------------------------------------------
const DATA = '2026-10-08' // quinta-feira
const janela = (label, inicio, fim) => ({ label, data: DATA, inicio: `${inicio}:00`, fim: `${fim}:00` })
const pedido = (id, status, extra = {}) => ({
  id, numeroCurto: id.toUpperCase().padEnd(8, '0'), clienteNome: `Cliente ${id}`, clienteApt: null, status,
  pagoEm: null, criadoEm: '2026-10-08T12:00:00Z', agendadoParaEm: null, endereco: `Rua ${id}, 10, Icaraí, Niterói`, ...extra,
})
const PEDIDOS = [
  pedido('a', 'aguardando_pagamento', { janela: janela('Almoço', '12:00', '14:00') }),
  pedido('b', 'aguardando', { janela: janela('Almoço', '12:00', '14:00'), pagoEm: '2026-10-08T11:00:00Z' }),
  pedido('c', 'pronto', { janela: janela('Almoço', '12:00', '14:00'), pagoEm: '2026-10-08T11:00:00Z' }),
  pedido('d', 'pronto', { janela: janela('Almoço', '12:00', '14:00'), pagoEm: '2026-10-08T11:00:00Z', clienteApt: '12' }),
  pedido('e', 'saiu_para_entrega', { janela: janela('Tarde', '17:00', '19:00'), pagoEm: '2026-10-08T11:00:00Z' }),
  pedido('f', 'preparando', { janela: janela('Tarde', '17:00', '19:00'), pagoEm: '2026-10-08T11:00:00Z', endereco: null }),
  // De outro dia (aprovação não tem corte de data no KDS): não entra no roteiro de hoje.
  pedido('g', 'aguardando_aprovacao_baba', { janela: { label: 'Almoço', data: '2026-10-10', inicio: '12:00:00', fim: '14:00:00' } }),
  // Pedido para já, sem janela, criado hoje.
  pedido('h', 'pronto', { criadoEm: '2026-10-08T15:00:00Z' }),
  pedido('i', 'cancelado', { janela: janela('Almoço', '12:00', '14:00') }),
]
const ENTREGADORES = [
  { id: 'm1', nome: 'Fulano', tipo: 'Motoboy', empresa: 'Ifood', veiculo: 'Moto', placa: 'ABC1D23' },
]
const VIAGENS = [
  { id: 'v1', situacao: 'Montando', entregadorId: null, paradas: [{ pedidoId: 'c', ordem: 1 }] },
  { id: 'v2', situacao: 'EmRota', entregadorId: 'm1', paradas: [{ pedidoId: 'e', ordem: 1, entregadorNome: 'Fulano', empresaEntregador: 'Ifood' }] },
  { id: 'v3', situacao: 'Desfeita', entregadorId: 'm1', paradas: [{ pedidoId: 'd', ordem: 1 }] },
]
const JANELAS = [
  { id: 'j1', diaDaSemana: 4, horaInicio: '12:00:00', horaFim: '14:00:00', capacidadeMaxima: 6, label: 'Almoço', ativa: true },
  { id: 'j2', diaDaSemana: 4, horaInicio: '17:00:00', horaFim: '19:00:00', capacidadeMaxima: 5, label: 'Tarde', ativa: true },
  { id: 'j3', diaDaSemana: 4, horaInicio: '20:00:00', horaFim: '21:00:00', capacidadeMaxima: 3, label: 'Noite', ativa: true },
  { id: 'j4', diaDaSemana: 5, horaInicio: '12:00:00', horaFim: '14:00:00', capacidadeMaxima: 6, label: 'Almoço', ativa: true },
  { id: 'j5', diaDaSemana: 4, horaInicio: '09:00:00', horaFim: '10:00:00', capacidadeMaxima: 2, label: 'Manhã', ativa: false },
]
const BLOQUEIOS = [{ id: 'b1', data: DATA, motivo: 'Chuva forte', janelaEspecificaId: 'j3' }]

const dia = () => roteiro.roteiroDoDia({
  data: DATA, pedidos: PEDIDOS, viagens: VIAGENS, entregadores: ENTREGADORES, janelas: JANELAS, bloqueios: BLOQUEIOS,
})

await confere('uma seção por janela do dia, na ordem do horário, e "Sem janela" por último', () => {
  const r = dia()
  assert.deepEqual(r.grupos.map((g) => g.faixa), ['12h00 às 14h00', '17h00 às 19h00', '20h00 às 21h00', 'Sem janela'])
  assert.deepEqual(r.grupos.map((g) => g.label), ['Almoço', 'Tarde', 'Noite', 'Sem janela'])
  assert.equal(r.total, 7, 'a..f e h; cancelado e o de outro dia ficam de fora')
})
await confere('janela de outro dia da semana e janela pausada sem pedido não aparecem', () => {
  const r = dia()
  assert.ok(!r.grupos.some((g) => g.label === 'Manhã'))
  assert.equal(r.grupos.filter((g) => g.label === 'Almoço').length, 1)
})
await confere('cada janela diz a ocupação e o bloqueio do dia', () => {
  const [almoco, , noite] = dia().grupos
  assert.equal(almoco.capacidade, 6)
  assert.equal(almoco.pedidos.length, 4)
  assert.equal(noite.pedidos.length, 0)
  assert.equal(noite.bloqueio, 'Chuva forte')
})
await confere('cada pedido traz status, pagamento e o que falta para sair', () => {
  const [almoco, tarde] = dia().grupos
  const por = (g, id) => g.pedidos.find((p) => p.id === id)
  assert.equal(por(almoco, 'a').rotulo, 'Aguardando pagamento')
  assert.equal(por(almoco, 'a').pago, false)
  assert.match(por(almoco, 'a').falta, /pagamento/)
  assert.equal(por(almoco, 'b').rotulo, 'Pago, agendado')
  assert.match(por(almoco, 'b').falta, /preparar/)
  assert.match(por(almoco, 'c').falta, /quem leva/, 'na viagem sem entregador')
  assert.match(por(almoco, 'd').falta, /viagem/, 'pronto solto (a viagem dele foi desfeita)')
  assert.equal(por(almoco, 'd').podePorNaViagem, true)
  assert.equal(por(almoco, 'c').podePorNaViagem, false)
  assert.equal(por(tarde, 'e').rotulo, 'Saiu para entrega')
  assert.equal(por(tarde, 'e').falta, null)
  assert.equal(por(tarde, 'f').endereco, null)
})
await confere('quem leva vem do retrato da saída ou do entregador da viagem, com a empresa', () => {
  const [almoco, tarde] = dia().grupos
  assert.deepEqual(tarde.pedidos.find((p) => p.id === 'e').entregador, { nome: 'Fulano', empresa: 'iFood Entregas' })
  assert.equal(almoco.pedidos.find((p) => p.id === 'c').entregador, null)
  assert.deepEqual(tarde.quemLeva, ['Fulano (iFood Entregas)'])
  assert.deepEqual(almoco.quemLeva, [])
})
await confere('resumo do dia conta as entregas por janela', () => {
  assert.equal(roteiro.resumoDoRoteiro(dia()), '4 entregas das 12h00 às 14h00, 2 das 17h00 às 19h00 e 1 sem janela')
})
await confere('sem cadastro de janelas (operador sem Admin), agrupa pelo que vem no pedido', () => {
  const r = roteiro.roteiroDoDia({ data: DATA, pedidos: PEDIDOS, viagens: [], entregadores: [], janelas: null, bloqueios: null })
  assert.deepEqual(r.grupos.map((g) => g.faixa), ['12h00 às 14h00', '17h00 às 19h00', 'Sem janela'])
  assert.equal(r.grupos[0].capacidade, null)
})

// --- 3. Roteiro impresso ----------------------------------------------------------------
const comoTexto = (bytes) => String.fromCharCode(...bytes)
const lidos = (bytes) => [...comoTexto(bytes).matchAll(/\(((?:\\.|[^\\)])*)\) Tj/g)]
  .map((m) => m[1].replace(/\\([0-7]{3})/g, (_, o) => String.fromCharCode(parseInt(o, 8))).replace(/\\(.)/g, '$1'))
  .join('\n')
const AGORA = Date.parse('2026-10-08T10:30:00Z')

await confere('roteiro em A4: página A4, resumo, janela por janela, pedido, endereço e quem leva', () => {
  const pdf = impressao.pdfDoRoteiro({ roteiro: dia(), papel: 'a4', agora: AGORA })
  const texto = lidos(pdf.bytes)
  const caixa = comoTexto(pdf.bytes).match(/\/MediaBox \[0 0 ([\d.]+) ([\d.]+)\]/)
  assert.equal(Number(caixa[1]), Math.round(A4.largura * 100) / 100)
  for (const esperado of [
    'Roteiro de entregas', 'quinta-feira, 08/10/2026', '12h00 às 14h00 · Almoço', '4 entregas', 'Rua a, 10, Icaraí, Niterói',
    'Fulano (iFood Entregas)', 'Bloqueada: Chuva forte', 'Sem endereço no cadastro', 'Página 1 de',
  ]) assert.ok(texto.includes(esperado), `falta "${esperado}"`)
  assert.equal(pdf.nomeArquivo, 'roteiro-2026-10-08.pdf')
})
await confere('roteiro na bobina: 80 mm de largura, uma página só, tudo em preto', () => {
  const pdf = impressao.pdfDoRoteiro({ roteiro: dia(), papel: 'bobina', agora: AGORA })
  const bruto = comoTexto(pdf.bytes)
  const caixas = [...bruto.matchAll(/\/MediaBox \[0 0 ([\d.]+) ([\d.]+)\]/g)]
  assert.equal(caixas.length, 1)
  assert.equal(Number(caixas[0][1]), Math.round(LARGURA_BOBINA * 100) / 100)
  const cinzas = [...bruto.matchAll(/BT ([\d.]+) g/g)].map((m) => Number(m[1]))
  assert.ok(cinzas.length > 0 && cinzas.every((c) => c === 0), `só preto na térmica: ${[...new Set(cinzas)]}`)
  assert.ok(lidos(pdf.bytes).includes('Fulano (iFood Entregas)'))
  assert.equal(pdf.nomeArquivo, 'roteiro-2026-10-08-80mm.pdf')
})
await confere('A4 com muitos pedidos quebra em páginas sem partir um pedido', () => {
  const muitos = Array.from({ length: 60 }, (_, i) => pedido(`p${i}`, 'pronto', { janela: janela('Almoço', '12:00', '14:00') }))
  const r = roteiro.roteiroDoDia({ data: DATA, pedidos: muitos, viagens: [], entregadores: [], janelas: JANELAS, bloqueios: [] })
  const pdf = impressao.pdfDoRoteiro({ roteiro: r, papel: 'a4', agora: AGORA })
  const paginas = [...comoTexto(pdf.bytes).matchAll(/\/MediaBox/g)].length
  assert.ok(paginas > 1, `esperava mais de uma página, veio ${paginas}`)
  assert.ok(lidos(pdf.bytes).includes(`Página ${paginas} de ${paginas}`))
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length > 0) process.exit(1)
