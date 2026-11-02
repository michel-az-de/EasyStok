/* eslint-disable no-console */
// Prova da #2185, pacote de efeitos (docs/plan/balcao-ux/banca/F-2185-efeitos-paridade.md e
// E-2185-estado-acao.md). Parte sem navegador: regra de domínio, reducer e ações do modo API com o HTTP
// controlado na fronteira. Cada conferência leva o id do achado (Fxx = Axx do relatório F, Exx = Axx do E).
//
//   F2   enviar cardápio, Assistente ou despedida não apaga o que ela estava digitando
//   F23  letras digitadas durante o "assumir" ficam no campo depois do envio
//   F12/E09  uma regra só de assumir antes de qualquer envio humano, no caminho do envio
//   F13  "Usar no rascunho" anexa ao que já estava escrito
//   F3   pedido aguardando aprovação não aparece como "Pago" e conta em "Precisa de você"
//   F4   cliente com duas conversas: bloqueio, nome e notas valem nas duas
//   E05  cadastro bloqueado: "Cobrar" nasce desabilitado com o motivo
//   E20/E22  conversa de colega não diz "Você respondeu"; concluída sem permissão não manda reabrir
//   F18/F19  contagens de "Conversas", "em andamento" e "na esteira" que batem entre si
//   F27  data e hora de notas no fuso da casa
//   F29/F10  nota proposta pela IA anexa ao rascunho; rascunho de nota sobrevive ao F5
//   F28  o aviso na tela não some com o ponteiro em cima
//
//   node ferramentas/prova-2185-efeitos.mjs

process.env.TZ = 'Pacific/Auckland'

import { registerHooks, } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'
import { mock } from 'node:test'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return proximo(url, contexto)
  },
})

// Sessão do usuário u1, para o "assumir" virar PAUSA_POR_ASSUMIR.
const SESSAO = JSON.stringify({ token: 't', expiraEm: Date.now() + 3600000, usuario: { id: 'u1', nome: 'Eu' }, empresa: { id: 'e1' } })
const memoria = new Map()
globalThis.sessionStorage = {
  getItem: (k) => (k === 'easystok.sessao' ? SESSAO : memoria.get(k) ?? null),
  setItem: (k, v) => memoria.set(k, String(v)),
  removeItem: (k) => memoria.delete(k),
}
globalThis.window ??= { dispatchEvent: () => {} }

const acao = await import('../src/aplicacao/acoes.js')
const { reducer } = await import('../src/aplicacao/reducer.js')
const { estadoInicial } = await import('../src/aplicacao/reducer/estadoInicial.js')
const { comApi } = await import('../src/aplicacao/acoesApi.js')
const { CANAIS } = await import('../src/infra/catalogo.js')
const { PAUSA_POR_ASSUMIR, PAUSA_POR_OUTRO, precisaDeVoce, motivoDePrecisar } = await import('../src/dominio/automatico.js')
const { acoesDaConversa } = await import('../src/dominio/acoesDaConversa.js')
const { resumoDoModo } = await import('../src/dominio/resumoDoModo.js')
const { executarAcaoDoAssistente, ACOES_DO_ASSISTENTE } = await import('../src/dominio/acoesDoAssistente.js')
const { juntarAoRascunho, restoDoEnvio } = await import('../src/dominio/rascunho.js').catch(() => ({}))
const { marcaDoPedido } = await import('../src/features/caixa-de-entrada/apresentacaoDoCartao.js')
const { pendenciaAntesDeCobrar } = await import('../src/dominio/resumoPedido.js')
const { acaoDoPix } = await import('../src/features/ficha-cliente/acaoDoPix.js')
const { podeVerCanhoto } = await import('../src/dominio/resumoPedido.js')
const conversaDominio = await import('../src/dominio/conversa.js')
const canalDominio = await import('../src/dominio/canal.js')
const { pedidoMudou } = await import('../src/dominio/cicloDaFicha.js').catch(() => ({}))
const { semOPagamentoDaAberta } = await import('../src/aplicacao/pagamentosNaoVistos.js').catch(() => ({}))
const { diaMesHora, diaMes } = await import('../src/dominio/formato.js')
const { notaDaMesa } = await import('../src/aplicacao/mesaDeNotas.js')
const { clienteDoDossie } = await import('../src/infra/api/traducaoCliente.js')
const { criarRascunhosDeNotas } = await import('../src/aplicacao/rascunhosDeNotas.js')
const { criarRelogiosDeAviso } = await import('../src/aplicacao/relogiosDeAviso.js').catch(() => ({}))

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 4).join(' ')}`)
  }
}

const WHATSAPP = CANAIS.find((c) => c.nome === 'WhatsApp')
const conversa = (extra = {}) => ({ id: 'c1', nome: 'Maria Souza', canal: 'WhatsApp', estado: 'Aberto', mensagens: [], cliente: { notas: [] }, ...extra })
const estadoCom = (conversas, extra = {}) => ({
  ...estadoInicial({ conversas, catalogo: { canais: CANAIS }, regras: [], modoAgente: 'simulado' }), ...extra,
})
const AGORA = Date.parse('2026-11-02T15:00:00Z')

// --- F2: o texto digitado sobrevive ao envio de outra mensagem ------------------------------------
await confere('F2. enviar outro texto (cardápio, Assistente, despedida) não apaga o rascunho digitado', () => {
  const partida = { ...estadoCom([conversa()]), rascunhos: { c1: 'Bom dia, Maria, tudo bem?' } }
  const enviou = reducer(partida, { tipo: acao.ENVIAR_MENSAGEM, id: 'c1', texto: 'Cardápio de hoje: https://x.test/c', mensagemId: 'm1', agora: AGORA })
  assert.equal(enviou.rascunhos.c1, 'Bom dia, Maria, tudo bem?')
  assert.equal(enviou.escritasDeFora.c1 ?? 0, 0, 'sem escrita de fora: o campo não adota nada')
  assert.equal(enviou.conversas[0].mensagens.length, 1, 'a mensagem entrou')
})
await confere('F2. enviar o próprio rascunho continua limpando o campo', () => {
  const partida = { ...estadoCom([conversa()]), rascunhos: { c1: 'Bom dia, Maria' } }
  const enviou = reducer(partida, { tipo: acao.ENVIAR_MENSAGEM, id: 'c1', texto: 'Bom dia, Maria', mensagemId: 'm1', agora: AGORA })
  assert.equal(enviou.rascunhos.c1, '')
  assert.equal(enviou.escritasDeFora.c1, 1)
})
await confere('F2. mensagem automática da demonstração também não apaga o rascunho', () => {
  const partida = { ...estadoCom([conversa()]), rascunhos: { c1: 'Rascunho meu' } }
  const enviou = reducer(partida, { tipo: acao.ENVIAR_MENSAGEM, id: 'c1', texto: 'Aviso do sistema', mensagemId: 'm1', agora: AGORA, automatica: true })
  assert.equal(enviou.rascunhos.c1, 'Rascunho meu')
})

// --- F23: o que ela digita enquanto o servidor assume não se perde ---------------------------------
await confere('F23. restoDoEnvio: só sai do campo o que foi enviado; o digitado depois fica', () => {
  assert.equal(restoDoEnvio('Oi Maria', 'Oi Maria'), '')
  assert.equal(restoDoEnvio('Oi Maria e o pedido', 'Oi Maria'), 'e o pedido')
  assert.equal(restoDoEnvio('Oi Maria\nSobre o pedido', 'Oi Maria'), 'Sobre o pedido')
  assert.equal(restoDoEnvio('Olá Maria', 'Oi Maria'), 'Olá Maria', 'editou o começo: não perde nada')
  assert.equal(restoDoEnvio(undefined, 'Oi'), '')
})

// --- F13: "Usar no rascunho" anexa ----------------------------------------------------------------
await confere('F13. juntarAoRascunho anexa depois do que já estava escrito, com um espaço', () => {
  assert.equal(juntarAoRascunho('Oi Maria,   ', 'posso confirmar às 12h?'), 'Oi Maria, posso confirmar às 12h?')
  assert.equal(juntarAoRascunho('', 'Resposta pronta'), 'Resposta pronta')
  assert.equal(juntarAoRascunho(undefined, 'Resposta pronta'), 'Resposta pronta')
  assert.equal(juntarAoRascunho('   ', 'Resposta pronta'), 'Resposta pronta')
})
await confere('F13. a ação "rascunho" do Assistente não apaga o que ela escreveu', async () => {
  const chamadas = []
  await executarAcaoDoAssistente(
    { tipo: ACOES_DO_ASSISTENTE.RASCUNHO, texto: 'Confirmo para as 12h.' }, { id: 'c1', nome: 'Maria' },
    { rascunhoAtual: () => 'Oi Maria,', definirRascunho: (id, texto) => chamadas.push([id, texto]) },
  )
  assert.deepEqual(chamadas, [['c1', 'Oi Maria, Confirmo para as 12h.']])
})

// --- F12 / E09: assumir antes de qualquer envio humano, no caminho do envio ----------------------------
await confere('F12. a conta de "assumir antes de enviar" vem da mesma função da barra', () => {
  const dados = (c, pausado, extra = {}) => acoesDaConversa({ conversa: c, canal: WHATSAPP, pausado, ...extra }).assumirAntesDeEnviar
  assert.equal(dados(conversa(), false), true, 'Assistente respondendo')
  assert.equal(dados(conversa(), PAUSA_POR_ASSUMIR), false, 'já é dela')
  assert.equal(dados(conversa({ responsavel: 'Ana' }), PAUSA_POR_OUTRO), true, 'conversa de colega')
  assert.equal(dados(conversa({ estado: 'Encerrado' }), false), false, 'concluída')
  assert.equal(dados(conversa(), false, { bloqueada: true }), false, 'bloqueada')
})

const enviada = (id = 'srv-1') => ({ id, direcao: 'Saida', autor: 'Atendente', status: 'Enviada', enviadaEm: '2026-11-02T12:00:00Z', tipoConteudo: 'Texto', texto: 'x' })
const resumoAssumido = { id: 'c1', situacao: 'Assumida', canal: 'WhatsApp', assumidaPorUsuarioId: 'u1', assumidaPorNome: 'Eu' }
const json = (data, status = 200) => new Response(JSON.stringify(status < 300 ? { data } : data), { status })

// Rede falsa: anota "MÉTODO fim-da-url" na ordem e responde por rota. `assumir: 'cair'` recusa o assumir.
function rede({ assumir = 'ok', demora = 0 } = {}) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET' } = {}) => {
    const fim = String(url).split('/').slice(-1)[0].split('?')[0]
    chamadas.push(`${method} ${fim}`)
    if (fim === 'assumir') {
      if (demora) await new Promise((r) => setTimeout(r, demora))
      return assumir === 'ok' ? json(resumoAssumido) : json({ message: 'Conflito ao assumir.' }, 500)
    }
    return json(enviada())
  }
  return chamadas
}
function montar(pausado, extraConversa = {}) {
  const despachados = []
  const estadoRef = { current: estadoCom([conversa(extraConversa)], { automaticoPausado: { c1: pausado } }) }
  const api = comApi({}, { despachar: (a) => despachados.push(a), agoraRef: { current: AGORA }, estadoRef })
  return { api, despachados, estadoRef }
}
const FOTO = 'data:image/png;base64,iVBORw0KGgo='

await confere('F12. texto com o Assistente atendendo: assume e só então envia', async () => {
  const chamadas = rede()
  const { api } = montar(false)
  assert.equal(await api.enviar('c1', 'Segue o cardápio'), true)
  assert.deepEqual(chamadas, ['POST assumir', 'POST mensagens'])
})
await confere('F12. conversa que já é dela envia direto, sem assumir', async () => {
  const chamadas = rede()
  const { api } = montar(PAUSA_POR_ASSUMIR)
  assert.equal(await api.enviar('c1', 'Segue o cardápio'), true)
  assert.deepEqual(chamadas, ['POST mensagens'])
})
await confere('F12/E09. conversa de colega: o envio também assume antes', async () => {
  const chamadas = rede()
  const { api } = montar(PAUSA_POR_OUTRO, { responsavel: 'Ana', situacaoApi: 'Assumida', assumidaPorUsuarioId: 'u2' })
  assert.equal(await api.enviar('c1', 'Posso ajudar?'), true)
  assert.deepEqual(chamadas, ['POST assumir', 'POST mensagens'])
})
await confere('F12. foto (galeria, cardápio ou computador) assume antes de sair', async () => {
  const chamadas = rede()
  const { api } = montar(false)
  assert.equal(await api.enviarMidia('c1', { formato: 'imagem', arte: FOTO, texto: '', nomeArquivo: 'bolo.png' }), true)
  assert.deepEqual(chamadas, ['POST assumir', 'POST imagem'])
})
await confere('F12. se assumir falhar, nada sai e a atendente é avisada', async () => {
  const chamadas = rede({ assumir: 'falha' })
  const { api, despachados } = montar(false)
  assert.equal(await api.enviar('c1', 'Segue o cardápio'), false)
  assert.deepEqual(chamadas, ['POST assumir'], 'a mensagem não pode sair sem assumir')
  assert.equal(despachados.some((d) => d.tipo === acao.ENVIAR_MENSAGEM), false, 'nenhum balão local nasce')
  assert.equal(despachados.filter((d) => d.tipo === acao.AVISO_API).at(-1)?.mensagem, 'Não consegui assumir a conversa, então a mensagem não saiu.')
})
await confere('F12. quem já assumiu pelo botão (estado ainda velho) não assume de novo ao enviar', async () => {
  const chamadas = rede()
  const { api } = montar(false)
  assert.equal(await api.assumirAtendimento('c1'), true)
  assert.equal(await api.enviar('c1', 'Primeira'), true)
  assert.equal(await api.enviar('c1', 'Segunda'), true)
  assert.deepEqual(chamadas, ['POST assumir', 'POST mensagens', 'POST mensagens'])
})
await confere('F12. dois envios juntos durante o assumir assumem uma vez só', async () => {
  const chamadas = rede({ demora: 20 })
  const { api } = montar(false)
  await Promise.all([api.enviar('c1', 'Um'), api.enviarMidia('c1', { formato: 'imagem', arte: FOTO, texto: '', nomeArquivo: 'a.png' })])
  assert.equal(chamadas.filter((c) => c === 'POST assumir').length, 1)
  assert.equal(chamadas[0], 'POST assumir')
  assert.equal(chamadas.length, 3)
})
await confere('F12. "Gerar cobrança e enviar" assume antes de pedir o pedido ao EasyStok', async () => {
  const chamadas = rede()
  const { api, estadoRef } = montar(false)
  estadoRef.current.conversas[0].pedido = { numero: '7', estado: 'aguardando', janela: 'j1|2026-11-05', itens: [{ sku: 'LAS', qtd: 1 }] }
  await Promise.resolve(api.gerarCobranca('c1', estadoRef.current.conversas[0].pedido, null, true)).catch(() => {})
  assert.equal(chamadas[0], 'POST assumir')
})
await confere('F12. trocar para o link online manda o link na conversa: assume antes', async () => {
  const chamadas = rede()
  const { api, estadoRef } = montar(false)
  estadoRef.current.conversas[0].pedido = { numero: '7', pedidoId: 'p1', meio: 'maquininha', estado: 'aguardando', janela: 'j1|2026-11-05', itens: [{ sku: 'LAS', qtd: 1 }] }
  await Promise.resolve(api.gerarCobranca('c1', estadoRef.current.conversas[0].pedido, 'pix', true)).catch(() => {})
  assert.equal(chamadas[0], 'POST assumir')
  chamadas.length = 0
  await Promise.resolve(api.alterarMeioPagamento('c1', 'pix')).catch(() => {})
  assert.equal(chamadas.filter((c) => c === 'POST assumir').length, 0, 'já assumida, não assume de novo')
})
await confere('F12. trocar para pagar na entrega não fala com o cliente: não assume', async () => {
  const chamadas = rede()
  const { api, estadoRef } = montar(false)
  estadoRef.current.conversas[0].pedido = { numero: '7', pedidoId: 'p1', meio: 'pix', estado: 'aguardando', janela: 'j1|2026-11-05', itens: [{ sku: 'LAS', qtd: 1 }] }
  await Promise.resolve(api.alterarMeioPagamento('c1', 'maquininha')).catch(() => {})
  assert.equal(chamadas.includes('POST assumir'), false)
  assert.equal(chamadas.length > 0, true, 'a troca foi ao servidor')
})
await confere('F12. gerar a cobrança SEM enviar ao cliente não assume', async () => {
  const chamadas = rede()
  const { api, estadoRef } = montar(false)
  estadoRef.current.conversas[0].pedido = { numero: '7', estado: 'aguardando', janela: 'j1|2026-11-05', itens: [{ sku: 'LAS', qtd: 1 }] }
  await Promise.resolve(api.gerarCobranca('c1', estadoRef.current.conversas[0].pedido, null, false)).catch(() => {})
  assert.equal(chamadas.includes('POST assumir'), false)
})

// --- F3: pedido aguardando aprovação -------------------------------------------------------------------
const comAprovacao = () => conversa({
  nome: 'Cliente Fora', clienteId: 'cl1',
  pedido: { numero: 'A1B2C3D4', estado: 'pago', pedidoId: 'p1', statusApi: 'aguardando_aprovacao_baba', itens: [{ sku: 'LAS', qtd: 1 }], totalApi: 50 },
})
await confere('F3. a lista não diz "Pago" para pedido que espera a aprovação da loja', () => {
  const marca = marcaDoPedido(comAprovacao(), [{ sku: 'LAS', preco: 50, nome: 'Lasanha' }], { fonteApi: true })
  assert.equal(marca.texto, 'Aguardando aprovação')
})
await confere('F3. pedido aguardando aprovação conta em "Precisa de você" e diz por quê', () => {
  const c = comAprovacao()
  assert.equal(precisaDeVoce(c, AGORA, PAUSA_POR_ASSUMIR), true)
  const motivo = motivoDePrecisar(c, AGORA, PAUSA_POR_ASSUMIR)
  assert.equal(motivo.chave, 'aprovacao')
  assert.equal(motivo.rotulo, 'Aguardando aprovação')
})
await confere('F3. pedido já aprovado não pede nada', () => {
  const c = comAprovacao()
  c.pedido.statusApi = 'aprovado_baba'
  assert.equal(motivoDePrecisar(c, AGORA, PAUSA_POR_ASSUMIR)?.chave ?? null, null)
})

// --- F4: o mesmo cliente em duas conversas -------------------------------------------------------------
await confere('F4. bloqueio e nome lidos numa conversa valem na outra do mesmo cliente', () => {
  const a = conversa({ id: 'a', canal: 'WhatsApp', clienteId: 'cl1', nome: 'Maria' })
  const b = conversa({ id: 'b', canal: 'Chat do site', clienteId: 'cl1', nome: 'Visitante do site' })
  const outra = conversa({ id: 'z', clienteId: 'cl9', nome: 'Zeca' })
  const bloqueio = { motivo: 'Calote', em: '2026-11-02T10:00:00Z' }
  const depois = reducer(estadoCom([a, b, outra]), { tipo: acao.CLIENTE_DA_API, id: 'a', clienteId: 'cl1', nome: 'Maria Souza', cliente: { notas: [{ id: 'n1', texto: 'Alérgica' }] }, bloqueio })
  const por = (id) => depois.conversas.find((c) => c.id === id)
  assert.deepEqual(por('b').bloqueio, bloqueio)
  assert.equal(por('b').nome, 'Maria Souza')
  assert.deepEqual(por('b').cliente.notas, [{ id: 'n1', texto: 'Alérgica' }])
  assert.equal(por('z').bloqueio, undefined, 'outro cliente não muda')
  assert.equal(por('z').nome, 'Zeca')
})
await confere('F4. lead que acaba de ganhar cadastro junta-se às conversas do mesmo cliente', () => {
  const lead = conversa({ id: 'a', clienteId: null, nome: 'Maria' })
  const irma = conversa({ id: 'b', clienteId: 'cl1', nome: 'Maria Antiga', bloqueio: { motivo: 'x', em: 'y' } })
  const depois = reducer(estadoCom([lead, irma]), { tipo: acao.CLIENTE_DA_API, id: 'a', clienteId: 'cl1', nome: 'Maria Souza', cliente: { notas: [] }, bloqueio: null })
  assert.equal(depois.conversas[0].clienteId, 'cl1')
  assert.equal(depois.conversas[1].bloqueio, null, 'o desbloqueio também chega na irmã')
})

// --- E05: cadastro bloqueado e a cobrança --------------------------------------------------------------
await confere('E05. cadastro bloqueado: a cobrança nasce desabilitada com o motivo, nos dois modos', () => {
  const dados = { pedido: { itens: [{ sku: 'X', qtd: 1 }], janela: 'j|d' }, clienteId: 'cl1', endereco: 'Rua A, 1', bloqueado: true }
  for (const fonteApi of [true, false]) {
    const pendencia = pendenciaAntesDeCobrar(dados, { fonteApi })
    assert.equal(pendencia?.motivo, 'Cliente bloqueado: desbloqueie o cadastro para cobrar.', `fonteApi=${fonteApi}`)
  }
  assert.equal(pendenciaAntesDeCobrar({ ...dados, bloqueado: false }, { fonteApi: true }), null)
  assert.equal(pendenciaAntesDeCobrar({ ...dados, pedido: { itens: [] } }, { fonteApi: true }).motivo, 'A comanda está vazia.', 'comanda vazia vem antes')
})

await confere('E05. reenviar a cobrança e mandar link novo também nascem desabilitados com o cliente bloqueado', () => {
  const base = { rotuloGerar: 'Gerar cobrança', aoReenviarPix() {}, aoAbrirBaixa() {}, aoGerarPix() {} }
  const reenviar = acaoDoPix({ chave: 'expirada' }, { ...base, bloqueado: true })
  assert.equal(reenviar.botoes[0].desabilitado, true)
  assert.equal(reenviar.motivo, 'Cliente bloqueado: desbloqueie o cadastro para enviar.')
  const novo = acaoDoPix({ chave: 'expirada' }, { ...base, linkVencidoApi: 'O link venceu.', aoMandarLinkNovo() {}, bloqueado: true })
  assert.equal(novo.botoes[0].desabilitado, true)
  assert.equal(novo.motivo, 'Cliente bloqueado: desbloqueie o cadastro para enviar.')
  assert.equal(acaoDoPix({ chave: 'expirada' }, { ...base, bloqueado: false }).botoes[0].desabilitado, false)
  assert.equal(acaoDoPix({ chave: 'expirada' }, { ...base, bloqueado: false }).motivo, undefined)
})

await confere('E30. o rascunho de comanda no modo API não oferece imprimir o que ainda não é pedido', () => {
  const itens = [{ sku: 'LAS', qtd: 1 }]
  assert.equal(podeVerCanhoto({ itens }, { fonteApi: true }), false, 'rascunho no modo API')
  assert.equal(podeVerCanhoto({ itens, pedidoId: 'p1' }, { fonteApi: true }), true, 'pedido criado')
  assert.equal(podeVerCanhoto({ itens }, { fonteApi: false }), true, 'a demonstração segue igual')
  assert.equal(podeVerCanhoto({ itens: [], pedidoId: 'p1' }, { fonteApi: true }), false, 'sem itens')
})

// --- E20 / E22 --------------------------------------------------------------------------------------------
const respondida = (extra = {}) => conversa({
  mensagens: [{ id: 'm1', dir: 'in', texto: 'Oi', em: '2026-11-02T14:00:00Z' }, { id: 'm2', dir: 'out', texto: 'Olá!', em: '2026-11-02T14:05:00Z' }], ...extra,
})
await confere('E20. conversa de colega não atribui "Você respondeu" a quem não respondeu', () => {
  const doColega = resumoDoModo(respondida({ responsavel: 'Ana' }), PAUSA_POR_OUTRO)
  assert.equal(doColega.proximo, null)
  assert.equal(doColega.sugereEncerrar, false)
  assert.doesNotMatch(doColega.detalhe, /Você respondeu/)
  assert.match(resumoDoModo(respondida(), PAUSA_POR_ASSUMIR).detalhe, /Você respondeu/, 'a dela continua dizendo')
})
await confere('E22. concluída sem permissão de reabrir não manda reabrir', () => {
  const encerrada = respondida({ estado: 'Encerrado' })
  assert.match(resumoDoModo(encerrada, false, false).linha, /Reabra a conversa/)
  const sem = resumoDoModo(encerrada, false, false, { podeReabrir: false })
  assert.equal(sem.linha, 'Peça a quem pode reabrir a conversa.')
  assert.doesNotMatch(sem.detalhe, /Reabra/)
})

// --- F18 / F19: contagens que batem ------------------------------------------------------------------------
await confere('F18. "Conversas" no título, na aba do celular e nos chips de canal dão o mesmo número', () => {
  const abertas = Array.from({ length: 10 }, (_, i) => conversa({ id: `a${i}`, canal: i % 2 ? 'WhatsApp' : 'Chat do site' }))
  const encerradas = Array.from({ length: 5 }, (_, i) => conversa({ id: `e${i}`, estado: 'Encerrado' }))
  const bloqueadas = Array.from({ length: 2 }, (_, i) => conversa({ id: `b${i}`, bloqueio: { motivo: 'x', em: 'y' } }))
  const todas = [...abertas, ...encerradas, ...bloqueadas]
  const filtros = { busca: '', aba: 'todas', canais: [], ordenacao: 'urgencia', responsavel: 'todas' }
  const titulo = conversaDominio.contarAbertasDoBalcao(todas, filtros, null)
  const chips = conversaDominio.contarPorCanal(todas, CANAIS, AGORA).reduce((soma, c) => soma + c.abertas, 0)
  assert.equal(titulo, 10)
  assert.equal(chips, 10)
  assert.equal(conversaDominio.contarAbertasDoBalcao(todas, { ...filtros, aba: 'precisa' }, null), 10, 'a aba "Precisa de você" não muda o total de abertas')
})
await confere('F19. "em andamento" não conta bloqueada e "na esteira" não conta pedido cancelado nem comanda em montagem', () => {
  const lista = [
    conversa({ id: 'a' }),
    conversa({ id: 'b', bloqueio: { motivo: 'x', em: 'y' } }),
    conversa({ id: 'c', estado: 'Encerrado' }),
  ]
  assert.equal(conversaDominio.contarAbertas(lista), 1)
  const pedido = (extra) => ({ itens: [{ sku: 'X', qtd: 1 }], ...extra })
  const comPedidos = [
    conversa({ id: 'p1', pedido: pedido({ estado: 'preparo', pedidoId: 'p1' }) }),
    conversa({ id: 'p2', pedido: pedido({ estado: 'entregue', pedidoId: 'p2' }) }),
    conversa({ id: 'p3', pedido: pedido({ estado: 'cancelado', pedidoId: 'p3' }) }),
    conversa({ id: 'p4', pedido: pedido({ estado: 'aguardando' }) }),
  ]
  assert.equal(conversaDominio.contarNaEsteira(comPedidos, { fonteApi: true }), 1, 'só o que está de fato na esteira')
  assert.equal(conversaDominio.contarNaEsteira(comPedidos, { fonteApi: false }), 2, 'na demonstração a comanda local conta')
})

// --- F5: a Ficha só recomeça quando o pedido é outro, não quando o rascunho vira pedido ----------------------
await confere('F5. rascunho que vira pedido (pelo link do cliente ou pela cobrança) é o mesmo ciclo da Ficha', () => {
  assert.equal(pedidoMudou({ pedidoId: null, numero: '7' }, { pedidoId: 'p1', numero: 'A1B2C3D4' }), false)
  assert.equal(pedidoMudou({ pedidoId: null, numero: null }, { pedidoId: null, numero: '7' }), false, 'primeiro item da comanda')
  assert.equal(pedidoMudou({ pedidoId: null, numero: null }, { pedidoId: 'p1', numero: 'A1' }), false)
})
await confere('F5. outro pedido, comanda nova ou comanda que sumiu recomeçam a Ficha', () => {
  assert.equal(pedidoMudou({ pedidoId: 'p1', numero: 'A1' }, { pedidoId: 'p2', numero: 'B2' }), true)
  assert.equal(pedidoMudou({ pedidoId: 'p1', numero: 'A1' }, { pedidoId: null, numero: '9' }), true, 'nova comanda depois do pedido entregue')
  assert.equal(pedidoMudou({ pedidoId: null, numero: '7' }, { pedidoId: null, numero: '8' }), true)
  assert.equal(pedidoMudou({ pedidoId: null, numero: '7' }, { pedidoId: null, numero: null }), true)
  assert.equal(pedidoMudou({ pedidoId: 'p1', numero: 'A1' }, { pedidoId: 'p1', numero: 'A1' }), false)
})

// --- F24: o ponto de pagamento novo sai quando a conversa aberta está à vista ---------------------------------
await confere('F24. pagamento que cai na conversa já aberta e à vista não deixa o ponto no cartão', () => {
  const naoVistos = new Set(['c1', 'c2'])
  const limpo = semOPagamentoDaAberta(naoVistos, 'c1', true)
  assert.deepEqual([...limpo], ['c2'])
  assert.deepEqual([...naoVistos], ['c1', 'c2'], 'o conjunto antigo não muda')
  assert.equal(semOPagamentoDaAberta(naoVistos, 'c1', false), naoVistos, 'aba escondida não conta como vista')
  assert.equal(semOPagamentoDaAberta(naoVistos, 'c9', true), naoVistos, 'outra conversa não limpa')
  assert.equal(semOPagamentoDaAberta(naoVistos, null, true), naoVistos)
})

// --- E10: conversa concluída não recebe item na comanda ------------------------------------------------------
await confere('E10. item adicionado a conversa concluída não entra na comanda nem baixa o saldo', () => {
  const cardapio = [{ sku: 'LAS', nome: 'Lasanha', preco: 38, saldo: 5, estoque: 5 }]
  const base = estadoCom([conversa({ id: 'e1', estado: 'Encerrado' }), conversa({ id: 'o1' })])
  const partida = { ...base, catalogo: { ...base.catalogo, cardapio } }
  const item = cardapio[0]
  const naConcluida = reducer(partida, { tipo: acao.ADICIONAR_ITEM, id: 'e1', item, numeroPedido: 9, alertaId: 'a1', agora: AGORA })
  assert.equal(naConcluida.conversas.find((c) => c.id === 'e1').pedido ?? null, null)
  assert.equal(naConcluida, partida, 'nada muda')
  const naAberta = reducer(partida, { tipo: acao.ADICIONAR_ITEM, id: 'o1', item, numeroPedido: 9, alertaId: 'a1', agora: AGORA })
  assert.equal(naAberta.conversas.find((c) => c.id === 'o1').pedido.itens.length, 1, 'na aberta continua entrando')
})

// --- F11: o microfone só existe onde o EasyStok envia áudio ----------------------------------------------
await confere('F11. no modo API o áudio é só do WhatsApp; Instagram não oferece gravar para perder o áudio', () => {
  const canal = (nome) => CANAIS.find((c) => c.nome === nome)
  assert.equal(canalDominio.motivoDeAudio(canal('WhatsApp'), { fonteApi: true }), null)
  assert.equal(canalDominio.motivoDeAudio(canal('Instagram'), { fonteApi: true }), 'Instagram ainda não recebe áudio pelo EasyStok.')
  assert.equal(canalDominio.motivoDeAudio(canal('Instagram'), { fonteApi: false }), null, 'a demonstração segue igual')
  assert.equal(canalDominio.motivoDeAudio(canal('Chat do site'), { fonteApi: true }), 'Chat do site não aceita áudio.', 'o canal sem áudio mantém o motivo dele')
})

// --- F27: fuso da casa ---------------------------------------------------------------------------------------
await confere('F27. hora de nota e de histórico é a do fuso da casa, não a do aparelho', () => {
  const iso = '2026-11-02T13:00:00Z' // 10:00 em São Paulo; o aparelho da prova está em Auckland
  assert.equal(diaMesHora(iso), '02/11, 10:00')
  assert.equal(diaMes(iso), '02/11')
  assert.equal(diaMesHora('2026-11-03T01:30:00Z'), '02/11, 22:30', 'passou da meia-noite no aparelho, não na casa')
  assert.equal(notaDaMesa({ id: 'n', texto: 't', autor: 'A', criadoEm: iso }).em, '02/11, 10:00')
  const { cliente } = clienteDoDossie({ cliente: { id: 'cl1', nome: 'Maria', telefone: '1' }, notas: [{ id: 'n', texto: 't', autor: 'A', criadoEm: iso }] })
  assert.equal(cliente.notas[0].em, '02/11, 10:00')
})

// --- F29 / F10: rascunho de nota ------------------------------------------------------------------------------
const armazenamento = () => {
  const m = new Map()
  return { getItem: (k) => m.get(k) ?? null, setItem: (k, v) => m.set(k, String(v)), removeItem: (k) => m.delete(k) }
}
await confere('F29. nota proposta pela IA com rascunho já aberto é anexada, sem apagar e sem duplicar', () => {
  const notas = criarRascunhosDeNotas('e1:u1', armazenamento())
  const primeira = notas.abrir('c1', 'Maria', 'Primeiro')
  notas.guardar(primeira.chave, { texto: 'Primeiro ajustado', inicio: 17, fim: 17, rolagem: 0 })
  const segunda = notas.abrir('c1', 'Maria', 'Proposta da IA')
  assert.equal(notas.ler().rascunhos.length, 1, 'segue sendo a nota desta conversa')
  assert.equal(segunda.chave, primeira.chave, 'a mesma nota: quem já gravando continua dono da gravação')
  assert.equal(segunda.versao, 1, 'a versão avisa o editor aberto de que o texto mudou por fora')
  assert.equal(notas.ler().ativa, segunda.chave)
  assert.equal(segunda.texto, 'Primeiro ajustado\n\nProposta da IA')
  assert.equal(segunda.inicio, segunda.texto.length)
  const terceira = notas.abrir('c1', 'Maria', 'Proposta da IA')
  assert.equal(terceira.texto, 'Primeiro ajustado\n\nProposta da IA', 'a mesma proposta de novo não duplica')
  assert.equal(terceira.chave, segunda.chave)
  assert.equal(terceira.versao, 1, 'sem texto novo a versão não anda')
  assert.equal(notas.abrir('c1', 'Maria').texto, 'Primeiro ajustado\n\nProposta da IA', 'abrir sem proposta só retoma')
})
await confere('F29. proposta que é só um pedaço do que está escrito ainda entra na nota', () => {
  const notas = criarRascunhosDeNotas('e1:u1', armazenamento())
  notas.abrir('c1', 'Maria', 'Sem cebola e sem alho')
  const { texto, versao } = notas.abrir('c1', 'Maria', 'Sem cebola')
  assert.equal(texto, 'Sem cebola e sem alho\n\nSem cebola')
  const repetida = notas.abrir('c1', 'Maria', 'Sem cebola e sem alho')
  assert.deepEqual([repetida.texto, repetida.versao], [texto, versao], 'o parágrafo inteiro repetido não duplica')
})
await confere('F8. abrir com a moldura desmontada: quem montar primeiro recebe a abertura, uma vez', async () => {
  const { criarCanalDeAbertura } = await import('../src/aplicacao/aberturaDeConversa.js')
  const canal = criarCanalDeAbertura(new EventTarget())
  canal.anunciar('c9')
  const moldura = []
  const lista = []
  const sairMoldura = canal.inscrever((id) => moldura.push(id))
  const sairLista = canal.inscrever((id) => lista.push(id))
  assert.deepEqual([moldura, lista], [['c9'], ['c9']], 'os dois da mesma montagem recebem')
  await new Promise((r) => setTimeout(r, 5))
  const tardio = []
  const sairTardio = canal.inscrever((id) => tardio.push(id))
  assert.deepEqual(tardio, [], 'quem monta depois não reabre')
  canal.anunciar('c2')
  assert.deepEqual([moldura, lista, tardio], [['c9', 'c2'], ['c9', 'c2'], ['c2']])
  sairMoldura(); sairLista(); sairTardio()
  await new Promise((r) => setTimeout(r, 5))
  const novo = []
  canal.inscrever((id) => novo.push(id))
  assert.deepEqual(novo, [], 'com alguém ouvindo no anúncio não sobra pendência')
})
await confere('F9. ao trocar de conta a foto em espera da conta anterior sai da memória', async () => {
  const { anexosEmEspera } = await import('../src/aplicacao/anexosEmEspera.js')
  anexosEmEspera.guardar('e1:u1:c1', { arte: 'a' })
  anexosEmEspera.guardar('e1:u2:c1', { arte: 'b' })
  anexosEmEspera.manterSo('e1:u2')
  assert.equal(anexosEmEspera.ler('e1:u1:c1'), null)
  assert.deepEqual(anexosEmEspera.ler('e1:u2:c1'), { arte: 'b' })
  anexosEmEspera.guardar('e1:u2:c1', null)
})
await confere('F10. o rascunho da nota sobrevive ao F5 (mesma sessão e mesma identidade)', () => {
  const guarda = armazenamento()
  const antes = criarRascunhosDeNotas('e1:u1', guarda)
  const nota = antes.abrir('c1', 'Maria', '')
  antes.guardar(nota.chave, { texto: 'Cliente pediu sem cebola', inicio: 24, fim: 24, rolagem: 0 })
  const depois = criarRascunhosDeNotas('e1:u1', guarda)
  assert.equal(depois.ler().rascunhos.length, 1)
  assert.equal(depois.ler().rascunhos[0].id, 'c1')
  assert.equal(depois.ler().rascunhos[0].texto, 'Cliente pediu sem cebola')
  assert.equal(depois.ler().ativa, null, 'o editor não reabre sozinho')
  const nova = depois.abrir('c2', 'Zeca', '')
  assert.notEqual(nova.chave, depois.ler().rascunhos[0].chave, 'a chave continua única')
})
await confere('F10. outro usuário não herda o rascunho; nota salva sai da memória', () => {
  const guarda = armazenamento()
  const antes = criarRascunhosDeNotas('e1:u1', guarda)
  const nota = antes.abrir('c1', 'Maria', '')
  antes.guardar(nota.chave, { texto: 'Rascunho', inicio: 8, fim: 8, rolagem: 0 })
  assert.equal(criarRascunhosDeNotas('e1:u2', guarda).ler().rascunhos.length, 0)
  assert.equal(antes.concluir(nota.chave, true), true)
  assert.equal(criarRascunhosDeNotas('e1:u1', guarda).ler().rascunhos.length, 0, 'salva: não volta no F5')
})
await confere('F10. armazenamento indisponível não derruba o rascunho em memória', () => {
  const quebrado = { getItem() { throw new Error('bloqueado') }, setItem() { throw new Error('bloqueado') }, removeItem() {} }
  const notas = criarRascunhosDeNotas('e1:u1', quebrado)
  const nota = notas.abrir('c1', 'Maria', 'x')
  notas.guardar(nota.chave, { texto: 'xy', inicio: 2, fim: 2, rolagem: 0 })
  assert.equal(notas.ler().rascunhos[0].texto, 'xy')
})

// --- F28: o aviso na tela espera o ponteiro ------------------------------------------------------------------
await confere('F28. o relógio do aviso para com o ponteiro em cima e continua do que faltava', () => {
  mock.timers.enable({ apis: ['setTimeout', 'Date'] })
  try {
    const vencidos = []
    const relogios = criarRelogiosDeAviso({ duracaoMs: 6000, aoVencer: (id) => vencidos.push(id) })
    relogios.iniciar('a')
    mock.timers.tick(4000)
    relogios.pausar()
    mock.timers.tick(60000)
    assert.deepEqual(vencidos, [], 'pausado, nada vence')
    relogios.retomar()
    mock.timers.tick(1999)
    assert.deepEqual(vencidos, [], 'faltavam 2 s')
    mock.timers.tick(1)
    assert.deepEqual(vencidos, ['a'])
    relogios.iniciar('b')
    relogios.cancelar('b')
    mock.timers.tick(10000)
    assert.deepEqual(vencidos, ['a'], 'cancelado não vence')
    relogios.iniciar('c')
    relogios.pausar()
    relogios.iniciar('d')
    mock.timers.tick(10000)
    assert.deepEqual(vencidos, ['a'], 'aviso novo durante a pausa também espera')
    relogios.retomar()
    mock.timers.tick(6000)
    assert.deepEqual(vencidos.sort(), ['a', 'c', 'd'])
  } finally {
    mock.timers.reset()
  }
})

console.log(`\nprova-2185-efeitos: ${passou} conferências ok, ${falhas.length} falha(s).`)
if (falhas.length) process.exit(1)
