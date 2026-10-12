// Prova da issue #8 (rodada 11, registro 92): atendimento automático visível.
// Duas partes:
//   1. domínio puro (`dominio/captura.js`): nome, telefone, endereço, gosto e
//      itens saem de frases naturais, e o que não é dado não vira dado;
//   2. reducer de verdade (`aplicacao/reducer.js`): a conversa do cenário
//      "Cliente novo chega" termina com o cadastro criado NO ESTADO, o pedido
//      anotado e as respostas saindo pela fila "digitando"; Assumir no meio
//      para tudo na hora, inclusive a resposta que estava sendo digitada.
//
// Mesmo gancho de `prova-bloqueio-preparo.mjs`: o código de `src/` importa sem
// extensão (o Vite resolve, o Node puro não).
//
// Roda com: node ferramentas/prova-r11-atendimento.mjs

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
const captura = await import('../src/dominio/captura.js')
const { REGRAS_PADRAO } = await import('../src/dominio/automacao.js')
const { CARDAPIO, CANAIS, JANELAS_ENTREGA, PREFIXOS_CEP_ATENDIDOS } = await import('../src/infra/catalogo.js')
const { FALAS } = await import('../src/infra/roteirosSimulacao.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

// --- 1. Domínio: extração de frases naturais -----------------------------------
confere('domínio: nome de "me chamo", "meu nome é" e "aqui é a"', () => {
  assert.equal(captura.extrairNome('Me chamo Rafael Tavares, prazer!'), 'Rafael Tavares')
  assert.equal(captura.extrairNome('meu nome é Ana Clara'), 'Ana Clara')
  assert.equal(captura.extrairNome('Aqui é a Júlia de Souza'), 'Júlia de Souza')
})
confere('domínio: nome solto só vale quando o automático perguntou', () => {
  assert.equal(captura.extrairNome('Rafael Tavares'), null)
  assert.equal(captura.extrairNome('Rafael Tavares', { aguardandoNome: true }), 'Rafael Tavares')
})
confere('domínio: saudação e "sou celíaca" não viram nome', () => {
  assert.equal(captura.extrairNome('Olá Thatiane', { aguardandoNome: true }), null)
  assert.equal(captura.extrairNome('sou celíaca', { aguardandoNome: true }), null)
  assert.equal(captura.extrairNome('Oi, sou eu de novo'), null)
})
confere('domínio: telefone com DDD em qualquer grafia, sem DDD não', () => {
  assert.equal(captura.extrairTelefone('Claro, é (11) 98765-4321'), '(11) 98765-4321')
  assert.equal(captura.extrairTelefone('11 98765-4321'), '(11) 98765-4321')
  assert.equal(captura.extrairTelefone('liga no 11987654321'), '(11) 98765-4321')
  assert.equal(captura.extrairTelefone('fixo (11) 3456-7890'), '(11) 3456-7890')
  assert.equal(captura.extrairTelefone('98765-4321'), null)
})
confere('domínio: CEP e número de endereço não viram telefone', () => {
  assert.equal(captura.extrairTelefone('Rua Girassol, 300, Vila Madalena, 05433-002'), null)
})
confere('domínio: endereço exige logradouro, número e CEP (US-011)', () => {
  assert.equal(
    captura.extrairEndereco('Rua Girassol, 300, apto 12, Vila Madalena, 05433-002'),
    'Rua Girassol, 300, apto 12, Vila Madalena, CEP 05433-002',
  )
  assert.equal(
    captura.extrairEndereco('fica na Av. Pompeia 1870, Perdizes, CEP 05022-001'),
    'Av. Pompeia 1870, Perdizes, CEP 05022-001',
  )
  assert.equal(captura.extrairEndereco('Rua Girassol, 300, Vila Madalena'), null, 'sem CEP')
  assert.equal(captura.extrairEndereco('meu CEP é 05433-002'), null, 'sem logradouro')
})
confere('domínio: gosto vira tag "Gosta de X", a forma que a saudação do recorrente lê', () => {
  assert.equal(captura.extrairGosto('Quero uma lasanha clássica, adoro lasanha!'), 'lasanha')
  assert.equal(captura.tagDeGosto('lasanha'), 'Gosta de lasanha')
  assert.equal(captura.extrairGosto('Tem glúten no ravióli?'), null)
})
confere('domínio: item citado só com saldo; "lasanha" sozinha não decide', () => {
  const skus = (texto) => captura.itensCitados(texto, CARDAPIO).map((i) => i.sku)
  assert.deepEqual(skus('Quero uma lasanha clássica'), ['LAS-CLA'])
  assert.deepEqual(skus('pode ser a lasanha verde?'), [], 'lasanha verde está com saldo zero')
  assert.deepEqual(skus('quero uma lasanha'), [], 'lasanha sozinha é ambígua')
})
confere('domínio: nenhum texto da captura tem travessão', () => {
  const textos = [
    captura.TEXTO_CADASTRO_CRIADO, captura.SELO_CAPTADO,
    ...Object.values(captura.CAMPOS).map((c) => captura.perguntaPara(c, 'Rafael Tavares')),
  ]
  for (const t of textos) assert.ok(!/[\u2013\u2014]/.test(t), t)
})

// --- 2. Reducer: o cenário "Cliente novo chega" de ponta a ponta --------------
const AGORA = Date.parse('2026-09-26T11:05:00-03:00')
const catalogo = {
  cardapio: CARDAPIO, canais: CANAIS, janelas: JANELAS_ENTREGA, prefixosCepAtendidos: PREFIXOS_CEP_ATENDIDOS,
}
const leadDoInstagram = (id) => ({
  id, cadastroId: id, conta: 'lead', nome: 'Rafa', canal: 'Instagram', estado: 'Aberto',
  responsavel: null, atrasada: false, bloqueio: null, pedido: null, mensagens: [],
  ultimaEm: new Date(AGORA).toISOString(),
  cliente: {
    desde: null, endereco: null, enderecoCapturado: null, telefone: null, pedidos: 0,
    tags: ['Chegou pelo Instagram'], notas: [], usuario: 'rafa.tavares',
  },
})
// Loja aberta na mão: a prova não depende da hora em que roda.
let estado = estadoInicial({
  conversas: [], catalogo, regras: REGRAS_PADRAO, modoAgente: 'sugerir', lojaAberta: true,
})
let seq = 0
const falar = (id, texto, extra = {}) => {
  seq += 1
  estado = reducer(estado, { tipo: acao.SIMULAR_MENSAGEM, id, texto, agora: AGORA + seq * 1000, mensagemId: `m${seq}`, ...extra })
}
// O que `useDigitacaoAutomatica` faz depois do tempo de digitação: solta a fila.
const soltarDigitacao = (id) => {
  let guarda = 0
  while ((conversa(id).respostaPendente ?? []).length > 0 && guarda < 10) {
    guarda += 1
    estado = reducer(estado, { tipo: acao.ENTREGAR_RESPOSTA_AUTOMATICA, id, agora: AGORA + seq * 1000 + 500 })
  }
}
const conversa = (id) => estado.conversas.find((c) => c.id === id)
const automaticas = (id) => conversa(id).mensagens.filter((m) => m.dir === 'out' && m.automatica)

estado = reducer(estado, { tipo: acao.SIMULAR_CONVERSA, conversa: leadDoInstagram('sim-prova') })
const falas = FALAS['lead-novo'].map((f) => f.texto)
assert.equal(falas.length, 5, 'o roteiro do cenário tem as cinco falas')

confere('reducer: primeira fala deixa a casa "digitando" (resposta na fila, ainda não na conversa)', () => {
  falar('sim-prova', falas[0])
  assert.equal(conversa('sim-prova').respostaPendente.length, 2, 'boas-vindas e a pergunta do nome')
  assert.equal(automaticas('sim-prova').length, 0)
})
confere('reducer: ENTREGAR solta as duas, com selo automática, na ordem', () => {
  soltarDigitacao('sim-prova')
  const [boasVindas, pergunta] = automaticas('sim-prova')
  assert.equal(boasVindas.regra, 'boas-vindas')
  assert.equal(pergunta.regra, 'captura')
  assert.match(pergunta.texto, /como você se chama/)
  assert.equal(conversa('sim-prova').captura.aguardando, 'nome')
})
confere('reducer: nome captado da conversa troca o apelido do perfil e ganha a marca', () => {
  falar('sim-prova', falas[1]); soltarDigitacao('sim-prova')
  const c = conversa('sim-prova')
  assert.equal(c.nome, 'Rafael Tavares')
  assert.ok(c.cliente.captado.nome)
  assert.match(automaticas('sim-prova').at(-1).texto, /telefone com DDD/)
})
confere('reducer: telefone captado da conversa', () => {
  falar('sim-prova', falas[2]); soltarDigitacao('sim-prova')
  const c = conversa('sim-prova')
  assert.equal(c.cliente.telefone, '(11) 98765-4321')
  assert.ok(c.cliente.captado.telefone)
  assert.match(automaticas('sim-prova').at(-1).texto, /rua, número e CEP/)
})
confere('reducer: endereço dentro da área cria o cadastro no estado, sem clique', () => {
  falar('sim-prova', falas[3]); soltarDigitacao('sim-prova')
  const c = conversa('sim-prova')
  assert.equal(c.cliente.endereco, 'Rua Girassol, 300, apto 12, Vila Madalena, CEP 05433-002')
  assert.equal(c.cliente.enderecoCapturado, null)
  assert.ok(c.captura.cadastroEm, 'captura.cadastroEm gravado')
  assert.ok(c.mensagens.some((m) => m.dir === 'sistema' && m.texto === captura.TEXTO_CADASTRO_CRIADO))
  assert.equal(c.conta, 'lead', 'RN-03: cadastro criado não é compra paga')
  assert.match(automaticas('sim-prova').at(-1).texto, /cadastro está feito/)
})
confere('reducer: a fala do prato anota o pedido, baixa o saldo e grava o gosto como tag', () => {
  const saldoAntes = estado.catalogo.cardapio.find((i) => i.sku === 'LAS-CLA').estoque
  falar('sim-prova', falas[4], { numeroPedido: '2026-0900' }); soltarDigitacao('sim-prova')
  const c = conversa('sim-prova')
  assert.equal(c.pedido.numero, '2026-0900')
  assert.deepEqual(c.pedido.itens.map((i) => [i.sku, i.qtd]), [['LAS-CLA', 1]])
  assert.equal(c.pedido.estado, 'aguardando')
  assert.equal(estado.catalogo.cardapio.find((i) => i.sku === 'LAS-CLA').estoque, saldoAntes - 1)
  assert.ok(c.cliente.tags.includes('Gosta de lasanha'))
  assert.match(automaticas('sim-prova').at(-1).texto, /Anotei: 1× Lasanha clássica/)
  assert.equal(c.captura.aguardando, null, 'nada mais a pedir')
})

// --- Assumir para o automático na hora (US-004, RN-04) ---------------------------
estado = reducer(estado, { tipo: acao.SIMULAR_CONVERSA, conversa: leadDoInstagram('sim-assume') })
confere('reducer: Assumir no meio da digitação descarta a resposta que ia sair', () => {
  falar('sim-assume', falas[0])
  assert.equal(conversa('sim-assume').respostaPendente.length, 2)
  estado = reducer(estado, { tipo: acao.ASSUMIR_ATENDIMENTO, id: 'sim-assume' })
  soltarDigitacao('sim-assume')
  assert.equal(automaticas('sim-assume').length, 0)
  assert.equal(conversa('sim-assume').respostaPendente.length, 0)
})
confere('reducer: com ela no controle, a fala do cliente não é captada nem respondida', () => {
  falar('sim-assume', falas[1]); falar('sim-assume', falas[2])
  soltarDigitacao('sim-assume')
  const c = conversa('sim-assume')
  assert.equal(c.nome, 'Rafa')
  assert.equal(c.cliente.telefone, null)
  assert.equal(automaticas('sim-assume').length, 0)
})

// --- Endereço fora da área não vira cadastro (UC-02) ----------------------------
estado = reducer(estado, { tipo: acao.SIMULAR_CONVERSA, conversa: leadDoInstagram('sim-fora') })
confere('reducer: endereço fora da área fica para a dona decidir, sem cadastro', () => {
  falar('sim-fora', 'Me chamo Diego Farias')
  falar('sim-fora', 'meu número é 11 91234-5678')
  falar('sim-fora', 'Rua Voluntários da Pátria, 1200, Freguesia do Ó, 02420-000')
  soltarDigitacao('sim-fora')
  const c = conversa('sim-fora')
  assert.equal(c.cliente.endereco, null)
  assert.match(c.cliente.enderecoCapturado, /02420-000/)
  assert.equal(c.captura.cadastroEm, null)
})

console.log(`\n${passou} verificações passaram.`)
