// Prova da issue #46 (rodada 13, registro 108): entrega integrada simulada
// com Lalamove e 99 Entregas. Cobre o Aceite da issue:
//   1. Provedor liga/desliga na aba de Gestão, credencial nunca volta em
//      texto puro, provedor padrão só entre os ligados.
//   2. Escolher provedor no despacho, cotação e chamada simuladas.
//   3. Status da corrida move a esteira sozinho: "coletado" tira o pedido
//      para "Em entrega" com aviso ao cliente (RN-32/D8); "entregue" fecha
//      com agradecimento (UC-10), sem toque da dona.
//   4. Corrida liga ao NÚMERO DO PEDIDO, como o iFood (áudio 06/homologação).
// Domínio puro, infra simulada e reducer de verdade, sem tela.
//
// Mesmo gancho de `prova-bloqueio-preparo.mjs`: o código de `src/` importa
// sem extensão (o Vite resolve, o Node puro não).
//
// Roda com: node ferramentas/prova-r13-integracoes.mjs

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
const integracoes = await import('../src/dominio/integracoes.js')
const corrida = await import('../src/dominio/corrida.js')
const provedores = await import('../src/infra/provedoresDeEntrega.js')
const { numeroParaEntregador } = await import('../src/dominio/despacho.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }
const semTravessao = (texto) => assert.ok(!/[–—]/.test(texto ?? ''), texto)

// --- 1. Configuração dos provedores (dominio/integracoes.js) -------------------
confere('configuração padrão: só "próprio" ligado, sem credencial (comportamento que já existia)', () => {
  const cfg = integracoes.configuracaoPadraoIntegracoes()
  assert.equal(cfg.padrao, 'proprio')
  assert.equal(cfg.provedores.proprio.ativo, true)
  assert.equal(cfg.provedores.lalamove.ativo, false)
  assert.equal(cfg.provedores['99entrega'].ativo, false)
})

confere('Lalamove/99 não ligam sem credencial, e o motivo explica por quê', () => {
  const cfg = integracoes.configuracaoPadraoIntegracoes()
  assert.ok(integracoes.motivoParaAtivar(cfg, 'lalamove'))
  const depois = integracoes.alternarProvedor(cfg, 'lalamove')
  assert.equal(depois.provedores.lalamove.ativo, false, 'recusa ligar sem credencial')
  semTravessao(integracoes.motivoParaAtivar(cfg, 'lalamove'))
})

confere('com credencial, liga; a única leitura pública é o booleano, nunca o texto', () => {
  const cfg = integracoes.salvarCredencial(integracoes.configuracaoPadraoIntegracoes(), 'lalamove', 'chave-secreta-123')
  assert.equal(integracoes.temCredencial(cfg, 'lalamove'), true)
  const ligado = integracoes.alternarProvedor(cfg, 'lalamove')
  assert.equal(ligado.provedores.lalamove.ativo, true)
  // Nenhuma função exportada do domínio devolve a credencial em texto: só o
  // booleano `temCredencial`. Aba (`AbaIntegracoes.jsx`) nunca lê o campo
  // `credencial` de volta, só chama esta função.
  assert.equal(typeof integracoes.temCredencial(cfg, 'lalamove'), 'boolean')
  assert.equal(integracoes.lerCredencial, undefined, 'não existe leitor de credencial em texto puro')
})

confere('desligar o provedor padrão devolve o padrão para "próprio"', () => {
  let cfg = integracoes.salvarCredencial(integracoes.configuracaoPadraoIntegracoes(), 'lalamove', 'chave')
  cfg = integracoes.alternarProvedor(cfg, 'lalamove')
  cfg = integracoes.definirPadrao(cfg, 'lalamove')
  assert.equal(cfg.padrao, 'lalamove')
  cfg = integracoes.alternarProvedor(cfg, 'lalamove')
  assert.equal(cfg.padrao, 'proprio', 'padrão nunca aponta para provedor desligado')
})

confere('só provedor ligado entra na lista do despacho, padrão primeiro', () => {
  let cfg = integracoes.salvarCredencial(integracoes.configuracaoPadraoIntegracoes(), '99entrega', 'chave-99')
  cfg = integracoes.alternarProvedor(cfg, '99entrega')
  cfg = integracoes.definirPadrao(cfg, '99entrega')
  const lista = integracoes.provedoresParaDespacho(cfg)
  assert.deepEqual(lista.map((p) => p.chave), ['99entrega', 'proprio'])
})

confere('textos novos sem travessão', () => {
  semTravessao(integracoes.AVISO_SIMULADO)
  for (const status of Object.keys(corrida.PASSOS_CORRIDA)) semTravessao(corrida.rotuloDoStatusCorrida(status))
})

// --- 2. Contrato do adapter simulado (infra/provedoresDeEntrega.js) ------------
confere('cotação: preço e prazo estáveis para o mesmo pedido e provedor (demo reproduzível)', async () => {
  const pedido = { numero: '2026-0421' }
  const c1 = await provedores.cotar({ provedor: 'lalamove', pedido, bairro: 'Pinheiros' })
  const c2 = await provedores.cotar({ provedor: 'lalamove', pedido, bairro: 'Pinheiros' })
  assert.deepEqual(c1, c2)
  assert.ok(c1.preco > 0)
  assert.ok(c1.prazoMin > 0)
  const outroProvedor = await provedores.cotar({ provedor: '99entrega', pedido, bairro: 'Pinheiros' })
  assert.notDeepEqual(c1, outroProvedor, 'provedores diferentes cotam diferente')
})

confere('chamar: código com o prefixo do provedor, um por corrida', async () => {
  const pedido = { numero: '2026-0421' }
  const { codigo: c1 } = await provedores.chamar({ provedor: 'lalamove', pedido })
  const { codigo: c2 } = await provedores.chamar({ provedor: 'lalamove', pedido })
  assert.match(c1, /^LM\d{5}$/)
  assert.notEqual(c1, c2, 'corridas diferentes não repetem código')
  const { codigo: c99 } = await provedores.chamar({ provedor: '99entrega', pedido })
  assert.match(c99, /^99E\d{5}$/)
})

confere('consultarStatus: sem sorte de falha, avança a sequência feliz até "entregue"', async () => {
  const semFalha = () => 1 // nunca cai na chance de falha
  let status = 'procurando'
  let entregador = null
  const vistos = [status]
  for (let i = 0; i < 5 && status !== 'entregue'; i += 1) {
    const r = await provedores.consultarStatus({ provedor: 'lalamove', status, aleatorio: semFalha })
    status = r.status
    entregador = entregador ?? r.entregador
    vistos.push(status)
  }
  assert.deepEqual(vistos, ['procurando', 'a-caminho-coleta', 'coletado', 'entregue'])
  assert.ok(entregador?.nome, 'o provedor "achou" alguém na virada de procurando')
})

confere('consultarStatus: com a sorte virada, cai em falha antes de coletar', async () => {
  const semprefalha = () => 0
  const r = await provedores.consultarStatus({ provedor: '99entrega', status: 'procurando', aleatorio: semprefalha })
  assert.equal(r.status, 'falha')
  // Depois de coletado a falha não é mais sorteada (corrida já está com a
  // casa, só falta terminar).
  const semFalha = await provedores.consultarStatus({ provedor: '99entrega', status: 'coletado', aleatorio: semprefalha })
  assert.equal(semFalha.status, 'entregue')
})

// --- 3. Reducer: a corrida move a esteira sozinha -------------------------------
const AGORA = Date.parse('2026-09-27T12:00:00-03:00')
const pedidoBase = (numero, estado) => ({
  numero, estado, janela: 'j1', entregador: null, itens: [{ sku: 'LAS-CLA', qtd: 1, obs: '' }],
  agradecimentoEnviado: false, pagamentos: [],
})
const conversaBase = (id, numero, estado = 'embalado') => ({
  id, cadastroId: 'cad-' + id, nome: 'Cliente ' + id, estado: 'Em atendimento', responsavel: 'Thatiane',
  mensagens: [], bloqueio: null, cliente: { endereco: 'Rua A, 10, Pinheiros, 05422-000' },
  pedido: pedidoBase(numero, estado),
})
const inicio = estadoInicial({
  conversas: [conversaBase('c1', '2026-0421')],
  catalogo: { janelas: [{ id: 'j1', faixa: '12h30 às 13h30', capacidade: 5 }], cardapio: [], regras: [] },
  regras: [{ id: 'agradecimento-pos-entrega', gatilho: 'pos-entrega', ativa: false, texto: '', esperaMin: 0 }],
  modoAgente: 'sugerir',
})
const achar = (estado, id) => estado.conversas.find((c) => c.id === id)

confere('chamar com provedor integrado grava cotação, código e status "procurando" no pedido', () => {
  let e = reducer(inicio, { tipo: acao.CRIAR_VIAGEM, id: 'c1', viagemId: 'v1', modo: 'entregador' })
  e = reducer(e, {
    tipo: acao.CHAMAR_ENTREGADOR_PROVEDOR, viagemId: 'v1', agora: AGORA, provedor: 'lalamove',
    cotacao: { preco: 12.9, prazoMin: 24 }, codigo: 'LM00042',
  })
  const chamado = achar(e, 'c1').pedido.viagem.chamado
  assert.equal(chamado.status, 'procurando')
  assert.equal(chamado.provedor, 'lalamove')
  assert.equal(chamado.codigo, 'LM00042')
  assert.equal(chamado.cotacao.preco, 12.9)
})

confere('"coletado" tira o pedido para "Em entrega" sozinho, com aviso ao cliente (RN-32/D8)', () => {
  let e = reducer(inicio, { tipo: acao.CRIAR_VIAGEM, id: 'c1', viagemId: 'v1', modo: 'entregador' })
  e = reducer(e, {
    tipo: acao.CHAMAR_ENTREGADOR_PROVEDOR, viagemId: 'v1', agora: AGORA, provedor: 'lalamove',
    cotacao: { preco: 12.9, prazoMin: 24 }, codigo: 'LM00042',
  })
  e = reducer(e, {
    tipo: acao.AVANCAR_CORRIDA, viagemId: 'v1', agora: AGORA, status: 'a-caminho-coleta',
    entregador: { nome: 'Rafael Souza', veiculo: 'moto', placa: 'LLM4A12' }, mensagemId: 'm1',
  })
  assert.equal(achar(e, 'c1').pedido.estado, 'embalado', 'a caminho da coleta ainda não mexe na esteira')
  e = reducer(e, { tipo: acao.AVANCAR_CORRIDA, viagemId: 'v1', agora: AGORA, status: 'coletado', mensagemId: 'm2' })
  const c1 = achar(e, 'c1')
  assert.equal(c1.pedido.estado, 'entrega', 'coletado = saiu para entrega, sem ela clicar em nada')
  assert.equal(c1.pedido.entregador.nome, 'Rafael Souza', 'o entregador achado pela corrida chega ao pedido')
  assert.ok(c1.mensagens.at(-1).automatica, 'aviso automático ao cliente')
})

confere('"entregue" fecha sozinho com agradecimento (UC-10), sem confirmação da dona', () => {
  let e = reducer(inicio, { tipo: acao.CRIAR_VIAGEM, id: 'c1', viagemId: 'v1', modo: 'entregador' })
  e = reducer(e, {
    tipo: acao.CHAMAR_ENTREGADOR_PROVEDOR, viagemId: 'v1', agora: AGORA, provedor: '99entrega',
    cotacao: { preco: 9.5, prazoMin: 20 }, codigo: '99E00071',
  })
  e = reducer(e, {
    tipo: acao.AVANCAR_CORRIDA, viagemId: 'v1', agora: AGORA, status: 'coletado', mensagemId: 'm1',
  })
  e = reducer(e, { tipo: acao.AVANCAR_CORRIDA, viagemId: 'v1', agora: AGORA + 10 * 60000, status: 'entregue', mensagemId: 'm2' })
  const c1 = achar(e, 'c1')
  assert.equal(c1.pedido.estado, 'entregue')
  assert.ok(c1.mensagens.some((m) => m.automatica), 'mensagem automática de entrega saiu sozinha (UC-10, sem toque)')
})

confere('falha fica visível (não desaparece calada) e "escolher outro provedor" zera o chamado', () => {
  let e = reducer(inicio, { tipo: acao.CRIAR_VIAGEM, id: 'c1', viagemId: 'v1', modo: 'entregador' })
  e = reducer(e, {
    tipo: acao.CHAMAR_ENTREGADOR_PROVEDOR, viagemId: 'v1', agora: AGORA, provedor: 'lalamove',
    cotacao: { preco: 12.9, prazoMin: 24 }, codigo: 'LM00042',
  })
  e = reducer(e, { tipo: acao.AVANCAR_CORRIDA, viagemId: 'v1', agora: AGORA, status: 'falha', mensagemId: 'm1' })
  assert.equal(achar(e, 'c1').pedido.viagem.chamado.status, 'falha', 'motivo visível, não some sozinho')
  assert.equal(achar(e, 'c1').pedido.estado, 'embalado', 'falha não mexe na esteira')
  e = reducer(e, { tipo: acao.CANCELAR_CHAMADO, viagemId: 'v1' })
  assert.equal(achar(e, 'c1').pedido.viagem.chamado, null, 'reusa a ação genérica da R12 para zerar e escolher de novo')
})

confere('corrida liga ao número do pedido, como o iFood (áudio 06/homologação)', () => {
  const pedido = { numero: '2026-0421' }
  const chamado = { codigo: 'LM00042' }
  assert.ok(corrida.resumoDaCorrida(chamado, pedido, 'Lalamove').includes(numeroParaEntregador(pedido)))
  assert.ok(corrida.resumoDaCorrida(chamado, pedido, 'Lalamove').includes('LM00042'))
})

confere('config e reducer não se misturam com o chamado manual da R12 (própria segue igual)', () => {
  let e = reducer(inicio, { tipo: acao.CRIAR_VIAGEM, id: 'c1', viagemId: 'v1', modo: 'entregador' })
  e = reducer(e, { tipo: acao.CHAMAR_ENTREGADOR, viagemId: 'v1', agora: AGORA })
  const chamado = achar(e, 'c1').pedido.viagem.chamado
  assert.equal(chamado.status, 'procurando')
  assert.equal(chamado.provedor, undefined, 'chamado manual não carrega provedor: não é a corrida integrada')
  assert.equal(corrida.ehProvedorIntegrado(chamado.provedor), false)
})

console.log(`\n${passou} verificações passaram.`)
