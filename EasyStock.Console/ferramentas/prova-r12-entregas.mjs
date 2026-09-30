// Prova da issue #17 (rodada 12, feedback da Thatiane de 26/09/2026 sobre o
// vídeo do protótipo): entregas e despacho.
//   1. Número do pedido que o entregador confere, separado da comanda interna.
//   2. Entregador como registro (nome, veículo, placa, empresa) preenchido à
//      mão, no mesmo formato que a integração com 99/Lalamove/iFood Entregas
//      devolveria depois. Quem levou fica gravado no pedido e chega ao
//      histórico do cliente, para separar três "José" no mesmo dia.
//   3. Entregas por bairro: agrupamento do dia e alcance por período.
// Domínio puro e reducer de verdade, sem tela.
//
// Mesmo gancho de `prova-bloqueio-preparo.mjs`: o código de `src/` importa
// sem extensão (o Vite resolve, o Node puro não).
//
// Roda com: node ferramentas/prova-r12-entregas.mjs

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

const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const acao = await import('../src/aplicacao/acoes.js')
const { numeroCurto } = await import('../src/dominio/pedido.js')
const { historicoComPedidoVivo } = await import('../src/dominio/cliente.js')
const { entregadorResolvido } = await import('../src/dominio/viagem.js')
const despacho = await import('../src/dominio/despacho.js')
const alcance = await import('../src/dominio/alcance.js')
const { carregarHistorico } = await import('../src/infra/historicoPedidos.js')
const { ENTREGADORES_CADASTRADOS } = await import('../src/infra/entregadoresSemente.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }
const semTravessao = (texto) => assert.ok(!/[–—]/.test(texto ?? ''), texto)

// --- 1. Número do pedido para o entregador ------------------------------------
confere('número do pedido: quatro dígitos com #, diferente da comanda interna', () => {
  const pedido = { numero: '2026-0186' }
  const numero = despacho.numeroParaEntregador(pedido)
  assert.match(numero, /^#\d{4}$/)
  assert.notEqual(numero.slice(1), numeroCurto(pedido.numero))
  assert.equal(despacho.numeroParaEntregador(pedido), numero, 'estável: o mesmo pedido dá o mesmo número')
})

confere('número do pedido: não repete entre os 9000 números de comanda possíveis', () => {
  const vistos = new Set()
  for (let n = 1; n <= 9000; n += 1) {
    vistos.add(despacho.numeroParaEntregador({ numero: `2026-${String(n).padStart(4, '0')}` }))
  }
  assert.equal(vistos.size, 9000)
})

confere('número do pedido: o que a plataforma mandar tem precedência (ponto da integração)', () => {
  assert.equal(despacho.numeroParaEntregador({ numero: '2026-0186', numeroEntrega: '7731' }), '#7731')
})

// --- 2. Entregador como registro -------------------------------------------------
confere('entregador: nome, veículo, placa e empresa, placa normalizada', () => {
  const e = despacho.montarEntregador({ nome: '  José Carlos ', veiculo: 'moto', placa: 'fqr-3c21', empresa: '99' })
  assert.deepEqual(e, { tipo: 'plataforma', nome: 'José Carlos', veiculo: 'moto', placa: 'FQR3C21', empresa: '99' })
  const proprio = despacho.montarEntregador({ nome: 'Beto', veiculo: 'moto', placa: 'ABC1234', empresa: 'propria' })
  assert.equal(proprio.tipo, 'motoboy', 'motoboy da casa segue o tipo que a esteira já conhece')
})

confere('entregador: recusa sem nome, placa torta e moto ou carro sem placa; bicicleta dispensa placa', () => {
  const v = despacho.motivoDoEntregador
  assert.ok(v({ nome: '', veiculo: 'moto', placa: 'ABC1234', empresa: 'propria' }))
  assert.ok(v({ nome: 'José', veiculo: 'moto', placa: 'AB12', empresa: 'propria' }))
  assert.ok(v({ nome: 'José', veiculo: 'carro', placa: '', empresa: 'lalamove' }))
  assert.equal(v({ nome: 'José', veiculo: 'bicicleta', placa: '', empresa: 'propria' }), null)
  assert.equal(v({ nome: 'José', veiculo: 'moto', placa: 'ABC1D23', empresa: 'propria' }), null)
  assert.equal(v({ nome: 'José', veiculo: 'moto', placa: 'ABC1234', empresa: 'propria' }), null)
  for (const dados of [{ nome: '' }, { nome: 'J', veiculo: 'moto', placa: 'x' }, { nome: 'J', veiculo: 'carro' }]) semTravessao(v(dados))
})

confere('entregador: texto de leitura para registro novo, "eu mesma" e o texto antigo sem registro', () => {
  const e = despacho.montarEntregador({ nome: 'José Carlos', veiculo: 'moto', placa: 'FQR3C21', empresa: '99' })
  assert.equal(despacho.textoDoEntregador(e), 'José Carlos · Moto FQR3C21 · 99')
  assert.equal(despacho.textoDoEntregador({ tipo: 'propria' }), 'Eu mesma (Thatiane)')
  assert.match(despacho.textoDoEntregador('motoboy da casa'), /motoboy da casa.*sem placa/)
  assert.equal(despacho.textoDoEntregador(null), null)
  semTravessao(despacho.textoDoEntregador('motoboy da casa'))
})

confere('entregador: três José no cadastro, cada um distinto pela placa na lista de conhecidos', () => {
  const josés = ENTREGADORES_CADASTRADOS.filter((e) => e.nome.startsWith('José'))
  assert.ok(josés.length >= 3)
  const conhecidos = despacho.entregadoresConhecidos([], ENTREGADORES_CADASTRADOS)
  const rotulos = conhecidos.filter((c) => c.entregador.nome.startsWith('José')).map((c) => c.rotulo)
  assert.equal(new Set(rotulos).size, rotulos.length, 'rótulos iguais não separariam os três')
  for (const r of rotulos) assert.match(r, /[A-Z]{3}\d[A-Z0-9]\d{2}/)
})

confere('entregador: quem foi digitado hoje entra nos conhecidos, sem repetir o já cadastrado', () => {
  const novo = despacho.montarEntregador({ nome: 'Rita', veiculo: 'moto', placa: 'RIT4A00', empresa: 'propria' })
  const repetido = { ...ENTREGADORES_CADASTRADOS[0] }
  const conversas = [
    { id: 'a', pedido: { entregador: novo } },
    { id: 'b', pedido: { entregador: repetido } },
    { id: 'c', pedido: { entregador: 'motoboy da casa' } },
    { id: 'd', pedido: { entregador: { tipo: 'propria' } } },
  ]
  const conhecidos = despacho.entregadoresConhecidos(conversas, ENTREGADORES_CADASTRADOS)
  assert.equal(conhecidos.length, ENTREGADORES_CADASTRADOS.length + 1)
  assert.ok(conhecidos.some((c) => c.entregador.nome === 'Rita'))
})

// --- Reducer: quem levou fica gravado no pedido ----------------------------------
const AGORA = Date.parse('2026-09-26T12:00:00-03:00')
const pedidoBase = (numero, estado) => ({
  numero, estado, janela: 'j1', entregador: null, itens: [{ sku: 'LAS-CLA', qtd: 1, obs: '' }],
  agradecimentoEnviado: false, pagamentos: [],
})
const conversaBase = (id, numero, endereco, estado = 'embalado') => ({
  id, cadastroId: 'cad-' + id, nome: 'Cliente ' + id, estado: 'Em atendimento', responsavel: 'Thatiane',
  mensagens: [], bloqueio: null, cliente: { endereco }, pedido: pedidoBase(numero, estado),
})
const inicio = estadoInicial({
  conversas: [
    conversaBase('c1', '2026-0301', 'Rua A, 10, Pinheiros, 05422-000'),
    conversaBase('c2', '2026-0302', 'Rua B, 20, Vila Madalena, 05433-000'),
    conversaBase('c3', '2026-0303', 'Rua C, 30, Vila Madalena, 05433-000'),
  ],
  catalogo: { janelas: [{ id: 'j1', faixa: '12h30 às 13h30', capacidade: 5 }], cardapio: [] },
  regras: [], modoAgente: 'sugerir',
})
const achar = (estado, id) => estado.conversas.find((c) => c.id === id)
const jose99 = despacho.montarEntregador({ nome: 'José Ribamar', veiculo: 'moto', placa: 'EZT8H40', empresa: '99' })

const saiu = reducer(inicio, {
  tipo: acao.AVANCAR_ESTEIRA, id: 'c1', passo: 'entrega', agora: AGORA, entregador: jose99,
  mensagemId: 'm-saiu', posEntregaId: 'p-saiu',
})

confere('reducer: despacho grava o registro inteiro do entregador no pedido e avisa com o nome', () => {
  const c = achar(saiu, 'c1')
  assert.equal(c.pedido.estado, 'entrega')
  assert.deepEqual(c.pedido.entregador, jose99)
  assert.ok(c.mensagens.at(-1).texto.includes('José Ribamar'))
})

const entregue = reducer(saiu, {
  tipo: acao.AVANCAR_ESTEIRA, id: 'c1', passo: 'entregue', agora: AGORA + 30 * 60000,
  mensagemId: 'm-entregue', posEntregaId: 'p-entregue',
})

confere('registro da entrega: quem levou, a hora que saiu e a hora que chegou', () => {
  const registro = despacho.registroDaEntrega(achar(entregue, 'c1'))
  assert.equal(registro.entregador.placa, 'EZT8H40')
  assert.equal(Date.parse(registro.saiuEm), AGORA)
  assert.equal(Date.parse(registro.entregueEm), AGORA + 30 * 60000)
  assert.equal(despacho.registroDaEntrega(achar(inicio, 'c2')), null, 'sem despacho, sem registro')
})

confere('viagem: o entregador achado pelo chamado chega ao pedido com veículo, placa e empresa', () => {
  const carlos = ENTREGADORES_CADASTRADOS.find((e) => e.empresa === 'lalamove')
  let e = reducer(inicio, { tipo: acao.CRIAR_VIAGEM, id: 'c2', viagemId: 'v1', modo: 'entregador' })
  e = reducer(e, { tipo: acao.POR_NA_VIAGEM, viagemId: 'v1', id: 'c3' })
  e = reducer(e, { tipo: acao.CHAMAR_ENTREGADOR, viagemId: 'v1', agora: AGORA })
  e = reducer(e, { tipo: acao.ATUALIZAR_CHAMADO, viagemId: 'v1', status: 'achado', entregador: carlos })
  assert.equal(entregadorResolvido(achar(e, 'c2').pedido).placa, carlos.placa)
  e = reducer(e, { tipo: acao.SAIR_PARA_ENTREGA, viagemId: 'v1', agora: AGORA, mensagensPorId: { c2: 'x2', c3: 'x3' } })
  for (const id of ['c2', 'c3']) {
    const p = achar(e, id).pedido
    assert.equal(p.estado, 'entrega')
    assert.equal(p.entregador.placa, carlos.placa)
    assert.equal(p.entregador.empresa, 'lalamove')
    assert.ok(achar(e, id).mensagens.at(-1).texto.includes(carlos.nome))
  }
})

confere('viagem: chamado antigo com o nome em texto ainda resolve (compatível com o que já existe)', () => {
  const pedido = { viagem: { modo: 'entregador', chamado: { entregador: 'Carlos · moto' } } }
  assert.equal(entregadorResolvido(pedido).nome, 'Carlos · moto')
})

confere('histórico do cliente: o pedido vivo leva quem entregou para a linha do histórico', () => {
  const pedido = achar(entregue, 'c1').pedido
  const historico = [{ numero: '2026-0301', em: '2026-09-26', total: 85, estado: 'pago', itens: [] }]
  const [linha] = historicoComPedidoVivo(historico, pedido)
  assert.equal(linha.estado, 'entregue')
  assert.deepEqual(linha.entregador, jose99)
})

confere('histórico semeado: pedido entregue antigo tem entregador com placa e empresa', () => {
  const entregues = carregarHistorico('c1').filter((h) => h.estado === 'entregue')
  assert.ok(entregues.length > 0)
  for (const h of entregues) {
    assert.ok(h.entregador, h.numero)
    if (h.entregador.tipo !== 'propria') {
      assert.ok(h.entregador.placa && h.entregador.empresa, h.numero)
    }
  }
  const naoEntregue = carregarHistorico('c1').find((h) => h.estado !== 'entregue')
  assert.equal(naoEntregue?.entregador ?? null, null, 'pedido que não saiu não ganha entregador inventado')
})

// --- 3. Bairro ---------------------------------------------------------------------
confere('bairro: agrupa as entregas do dia por bairro, com contagem', () => {
  const grupos = alcance.agruparPorBairro(inicio.conversas)
  assert.deepEqual(grupos.map((g) => [g.bairro, g.itens.length]), [['Pinheiros', 1], ['Vila Madalena', 2]])
  const semEndereco = alcance.agruparPorBairro([{ id: 'z', cliente: {}, pedido: {} }])
  assert.equal(semEndereco[0].bairro, 'Sem bairro')
})

const DIA = (iso) => iso
const conversasDeAlcance = [
  {
    id: 'a', cliente: { endereco: 'Rua A, 1, Pinheiros, 05422-000' },
    historico: [
      { numero: '2026-0010', em: DIA('2026-09-25'), total: 100, estado: 'entregue', itens: [] },
      { numero: '2026-0009', em: DIA('2026-09-01'), total: 50, estado: 'entregue', itens: [] },
      { numero: '2026-0008', em: DIA('2026-09-24'), total: 70, estado: 'cancelado', itens: [] },
    ],
    pedido: null,
  },
  {
    id: 'b', cliente: { endereco: 'Rua B, 2, Vila Madalena, 05433-000' },
    historico: [{ numero: '2026-0020', em: DIA('2026-09-26'), total: 80, estado: 'pago', itens: [] }],
    // O pedido vivo é o espelho da última linha do histórico: conta uma vez só.
    pedido: { numero: '2026-0020', estado: 'preparo', itens: [{ sku: 'X', qtd: 1 }] },
  },
  {
    id: 'c', cliente: { endereco: 'Rua C, 3, Vila Madalena, 05433-000' },
    historico: [],
    pedido: { numero: '2026-0030', estado: 'aguardando', itens: [{ sku: 'X', qtd: 2 }] },
  },
  { id: 'd', cliente: { endereco: 'Rua D, 4, Perdizes, 05022-000' }, historico: [
    { numero: '2026-0040', em: DIA('2026-06-01'), total: 90, estado: 'entregue', itens: [] },
  ], pedido: null },
]
const cardapioAlcance = [{ sku: 'X', preco: 40 }]
const AGORA_ALCANCE = Date.parse('2026-09-26T15:00:00-03:00')

confere('alcance 7 dias: pedidos, valor e clientes por bairro; cancelado fora; espelho não dobra', () => {
  const r = alcance.alcancePorBairro(conversasDeAlcance, { agora: AGORA_ALCANCE, dias: 7, cardapio: cardapioAlcance })
  const por = Object.fromEntries(r.bairros.map((b) => [b.bairro, b]))
  assert.equal(por['Vila Madalena'].pedidos, 2)
  assert.equal(por['Vila Madalena'].total, 80 + 80)
  assert.equal(por['Vila Madalena'].clientes, 2)
  assert.equal(por.Pinheiros.pedidos, 1)
  assert.equal(por.Pinheiros.total, 100)
  assert.equal(r.bairros[0].bairro, 'Vila Madalena', 'quem vende mais vem primeiro')
  assert.equal(r.totalPedidos, 3)
})

confere('alcance: bairro sem venda no período aparece com zero (onde vende menos)', () => {
  const r = alcance.alcancePorBairro(conversasDeAlcance, { agora: AGORA_ALCANCE, dias: 7, cardapio: cardapioAlcance })
  const perdizes = r.bairros.find((b) => b.bairro === 'Perdizes')
  assert.equal(perdizes.pedidos, 0)
  assert.equal(r.bairros.at(-1).bairro, 'Perdizes')
})

confere('alcance: período maior soma mais, e cada bairro traz o período anterior para comparar', () => {
  const r30 = alcance.alcancePorBairro(conversasDeAlcance, { agora: AGORA_ALCANCE, dias: 30, cardapio: cardapioAlcance })
  assert.equal(r30.bairros.find((b) => b.bairro === 'Pinheiros').pedidos, 2)
  const tudo = alcance.alcancePorBairro(conversasDeAlcance, { agora: AGORA_ALCANCE, dias: null, cardapio: cardapioAlcance })
  assert.equal(tudo.bairros.find((b) => b.bairro === 'Perdizes').pedidos, 1)
  const r7 = alcance.alcancePorBairro(conversasDeAlcance, { agora: AGORA_ALCANCE, dias: 7, cardapio: cardapioAlcance })
  // 7 dias anteriores (13 a 19/09) não têm pedido de Pinheiros; 01/09 fica fora.
  assert.equal(r7.bairros.find((b) => b.bairro === 'Pinheiros').anterior, 0)
  const hoje = alcance.alcancePorBairro(conversasDeAlcance, { agora: AGORA_ALCANCE, dias: 1, cardapio: cardapioAlcance })
  assert.equal(hoje.totalPedidos, 2, 'hoje: só os dois pedidos de 26/09')
})

confere('textos novos sem travessão', () => {
  for (const p of alcance.PERIODOS_DE_ALCANCE) semTravessao(p.rotulo)
  semTravessao(despacho.EXPLICACAO_JANELA)
  semTravessao(despacho.AVISO_SEM_INTEGRACAO)
})

console.log(`\n${passou} verificações passaram.`)
