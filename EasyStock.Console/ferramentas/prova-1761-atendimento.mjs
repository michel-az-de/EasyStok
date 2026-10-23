/* eslint-disable no-console */
// Prova da issue #1761 (simulação de atendimento de 13/10/2026): o que a tela dizia errado ou em
// língua de sistema durante um atendimento de ponta a ponta. Só regra pura e tradução da API,
// sem navegador.
//
//   A  resumo de encerramento com o dinheiro do pedido real: total com frete, o meio que entrou,
//      "Sinal pago" em vez de "Pago" e cliente do dia como conversão;
//   B  tique de lida distinto de entregue;
//   C  motivo da passagem e nota de sistema sem "LLM", "escalado" nem "esteira";
//   D  erro do canal em português, com o caminho do modelo aprovado fora da janela;
//   F  encerrar não aceita a caixa da despedida marcada com o campo vazio;
//   G  endereço que a Ficha não separa é recusado com o formato, sem ir nulo ao EasyStok;
//   H  painel de lembretes dentro da janela; linha do aviso de prazo sem duas contas de tempo;
//   I  perfil: o que a matriz de ações já nega não é pedido; textos da Ficha e da faixa.
//   J  layout do Balcão: prazo no cabeçalho, modelos à vista, devolver ao automático, linha
//      zerada da Ficha, chip de canal, porção repetida e a hora de fechar o cardápio.
//
//   node ferramentas/prova-1761-atendimento.mjs

import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

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

globalThis.sessionStorage = {
  getItem: () => JSON.stringify({ token: 't', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa' } }),
  setItem: () => {}, removeItem: () => {},
}
globalThis.window = globalThis.window ?? { dispatchEvent: () => {} }

const { criarAcoesClienteApi } = await import('../src/aplicacao/api/cliente.js')
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const { conversaDaApi, mensagemDaApi, naoEntregueDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { lembreteDaApi } = await import('../src/infra/api/traducaoNotificacoes.js')
const resumoDom = await import('../src/dominio/resumoAtendimento.js')
const { tiqueDaMensagem } = await import('../src/dominio/mensagem.js')
const { AUTOMATICO_DESLIGADO, motivoDeEscalada, notaDeSistema } = await import('../src/dominio/motivoDeEscalada.js')
const { origemDaPassagem } = await import('../src/dominio/passagem.js')
const { resumoDoModo } = await import('../src/dominio/resumoDoModo.js')
const { PAUSA_POR_ASSUMIR, modoDoAtendimento, proximoPassoDepoisDeResponder } = await import('../src/dominio/automatico.js')
const { FRASE_DA_FALHA, TIPOS_DE_FALHA, falhaDeEnvio, fraseDoReenvio, pedeModeloAprovado, textoDaFalhaDeEnvio } = await import('../src/dominio/falhaDeEnvio.js')
const { FORMATO_DO_ENDERECO, recusaDoEndereco } = await import('../src/dominio/cliente.js')
const { encaixeNaJanela, tempoDoItem } = await import('../src/dominio/lembrete.js')
const { podeVerAvisosDoCliente, podeVerItensFora } = await import('../src/dominio/acessoModulos.js')
const { pedidoAcabouDeNascer, porcaoForaDoNome, tituloDaComanda } = await import('../src/dominio/pedido.js')
const { estadoDaJanela, modelosAVista, prazoNoCabecalho } = await import('../src/dominio/janela.js')
const { automaticoDesligadoNaConversa } = await import('../src/dominio/motivoDeEscalada.js')
const { mostraHistorico, resumoDasTags } = await import('../src/dominio/ficha.js')
const { mostraChipDoCanal } = await import('../src/dominio/conversa.js')

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

// ── Massa: o pedido da Marina como a API do laboratório devolveu em 13/10 ─────
const LASANHA = '15c12288-4f1e-41b0-97bf-cba8a0187f82'
const TALHARIM = '71762197-c6f8-407a-ac56-5f340ae7a4ed'
const CARDAPIO = [{ sku: LASANHA, nome: 'Lasanha Bolonhesa Clássica 600 g', preco: 38 }, { sku: TALHARIM, nome: 'Talharim 500 g', preco: 19 }]
const PEDIDO_COM_SINAL = {
  pedidoId: '55426955-4bff-4810-b919-19726ddec4e0', status: 'aguardando', total: 69, frete: 12, numeroComanda: 3,
  itens: [
    { cardapioItemId: LASANHA, nome: 'lasanha bolonhesa clássica 600 g', quantidade: 1, precoUnitario: 38 },
    { cardapioItemId: TALHARIM, nome: 'talharim 500 g', quantidade: 1, precoUnitario: 19 },
  ],
  // Depois do sinal a cobrança ativa é a do restante, na entrega: era dela que a folha lia o meio.
  cobranca: { cobrancaId: 'cb2', provedor: 'na_entrega', status: 'Pendente', valor: 34.5, tentativa: 1, criadaEm: '2026-10-13T21:28:37Z' },
  totalPago: 34.5, alocado: 34.5, sinalNaReserva: true,
  pagamentos: [{ id: 'pg1', valor: 34.5, metodo: 'pix', pagoEm: '2026-10-13T21:28:38.519288Z' }],
}
const AGORA = Date.parse('2026-10-13T21:45:00Z')
const conversaDaMarina = (pedidoApi = PEDIDO_COM_SINAL, cliente = { pedidos: 1 }) => ({
  id: 'c3', numeroComanda: 3, nome: 'Marina Souza', canal: 'WhatsApp', conta: 'cliente', clienteId: 'cl1', estado: 'Em atendimento',
  situacaoApi: 'Assumida',
  mensagens: [
    { id: 'm1', dir: 'in', texto: 'Oi, boa tarde!', em: '2026-10-13T21:16:37Z' },
    { id: 'm2', dir: 'out', texto: 'Oi, Marina!', em: '2026-10-13T21:17:46Z', status: 'lida' },
  ],
  cliente: { telefone: '+5511977770001', notas: [], tags: [], ...cliente },
  pedido: pedidoDaApi(pedidoApi),
})

// ── A  Resumo de encerramento ────────────────────────────────────────────────
await confere('A  pedido de R$ 69,00 com sinal de R$ 34,50 por Pix: receita 34,50 e a receber 34,50 (era 22,50)', () => {
  const resumo = resumoDom.montarResumoAtendimento(conversaDaMarina(), CARDAPIO, AGORA, {})
  assert.equal(resumo.receita.liquido, 34.5)
  assert.equal(resumo.receita.faltaReceber, 34.5)
})
await confere('A  linha de Vendas: Pix, R$ 69,00, "Sinal pago"; total R$ 69,00 (era Maquininha, R$ 57,00, Pago)', () => {
  const resumo = resumoDom.montarResumoAtendimento(conversaDaMarina(), CARDAPIO, AGORA, {})
  assert.equal(resumo.vendas.length, 1)
  const [venda] = resumo.vendas
  assert.equal(venda.meio, 'Pix')
  assert.equal(venda.valor, 69)
  assert.equal(venda.situacao, 'Sinal pago')
  assert.equal(venda.situacaoTom, 'aviso')
  assert.equal(resumo.vendasTotal, 69)
})
await confere('A  cliente do dia (um pedido só, o desta conversa) converteu; com histórico já era cliente', () => {
  assert.equal(resumoDom.montarResumoAtendimento(conversaDaMarina(), CARDAPIO, AGORA, {}).converteu, 'sim')
  assert.equal(resumoDom.montarResumoAtendimento(conversaDaMarina(PEDIDO_COM_SINAL, { pedidos: 4 }), CARDAPIO, AGORA, {}).converteu, 'ja-cliente')
  const semPagar = { ...PEDIDO_COM_SINAL, status: 'aguardando_pagamento', totalPago: 0, alocado: 0, pagamentos: [], cobranca: { ...PEDIDO_COM_SINAL.cobranca, provedor: 'mercadopago' } }
  assert.equal(resumoDom.montarResumoAtendimento(conversaDaMarina(semPagar), CARDAPIO, AGORA, {}).converteu, 'nao')
})
await confere('A  pedido quitado segue "Pago"; pedido do EasyStok sem pagamento não vira receita', () => {
  const quitado = { ...PEDIDO_COM_SINAL, totalPago: 69, alocado: 69, cobranca: null, pagamentos: [{ id: 'pg1', valor: 69, metodo: 'dinheiro', pagoEm: '2026-10-13T21:28:38Z' }] }
  const resumo = resumoDom.montarResumoAtendimento(conversaDaMarina(quitado), CARDAPIO, AGORA, {})
  assert.equal(resumo.vendas[0].situacao, 'Pago')
  assert.equal(resumo.vendas[0].meio, 'Dinheiro')
  assert.equal(resumo.receita.faltaReceber, 0)
  // Sem cobrança e sem pagamento, já na fila: antes o avanço do estado contava o total como recebido.
  const semDinheiro = { ...PEDIDO_COM_SINAL, totalPago: 0, alocado: 0, cobranca: null, pagamentos: [] }
  const vazio = resumoDom.montarResumoAtendimento(conversaDaMarina(semDinheiro), CARDAPIO, AGORA, {})
  assert.equal(vazio.receita.liquido, 0)
  assert.equal(vazio.receita.faltaReceber, 69)
})
await confere('A  demonstração (sem total do servidor) segue somando pelo cardápio', () => {
  const conversa = { ...conversaDaMarina(), situacaoApi: undefined, pedido: { numero: '2026-0184', estado: 'preparo', itens: [{ sku: LASANHA, qtd: 1 }], cobranca: null, pagamentos: [] } }
  const resumo = resumoDom.montarResumoAtendimento(conversa, CARDAPIO, AGORA, {})
  assert.equal(resumo.vendasTotal, 38)
  assert.equal(resumo.receita.liquido, 38)
})

// ── B  Tique ─────────────────────────────────────────────────────────────────
await confere('B  lida e entregue têm rótulos diferentes; só a lida ganha a palavra na tela', () => {
  assert.deepEqual(tiqueDaMensagem('lida'), { marca: '✓✓', rotulo: 'lida', lida: true })
  assert.deepEqual(tiqueDaMensagem('entregue'), { marca: '✓✓', rotulo: 'entregue', lida: false })
  assert.deepEqual(tiqueDaMensagem('enviada'), { marca: '✓', rotulo: 'enviada', lida: false })
  assert.equal(tiqueDaMensagem('falhou'), null)
  assert.equal(tiqueDaMensagem('enviando'), null)
})
await confere('B  Status 4 do banco (Lida) chega ao balão como "lida"', () => {
  const m = mensagemDaApi({ id: 'm', direcao: 'Saida', autor: 'Dona', status: 'Lida', texto: 'oi', enviadaEm: '2026-10-13T21:17:46Z', externoId: 'wamid.FAKE' })
  assert.equal(tiqueDaMensagem(m.status).rotulo, 'lida')
})

// ── C  Motivo da passagem ────────────────────────────────────────────────────
const MOTIVO_CRU = 'agente desligado (LLM indisponível)'
await confere('C  motivo conhecido vira frase da casa; desconhecido perde o trecho entre parênteses', () => {
  assert.equal(motivoDeEscalada(MOTIVO_CRU), 'o atendimento automático está desligado')
  assert.equal(motivoDeEscalada('atendimento automático desligado na configuração'), AUTOMATICO_DESLIGADO)
  assert.equal(motivoDeEscalada('cliente pediu prazo especial (tool_call falhou: 503)'), 'cliente pediu prazo especial')
  assert.equal(motivoDeEscalada('cliente avaliou o pedido como negativo'), 'cliente avaliou o pedido como negativo')
  assert.equal(motivoDeEscalada(''), null)
  assert.equal(motivoDeEscalada(null), null)
})
await confere('C  nota de sistema da conversa: "Passou para você: ...", sem "escalado" nem "LLM"', () => {
  assert.equal(notaDeSistema('escalado para a dona: ' + MOTIVO_CRU), 'Passou para você: o atendimento automático está desligado')
  assert.equal(notaDeSistema('Conversa reaberta por Ana.'), 'Conversa reaberta por Ana.')
  const m = mensagemDaApi({ id: 'n', direcao: 'Saida', autor: 'Sistema', status: 'Pendente', texto: 'escalado para a dona: ' + MOTIVO_CRU, enviadaEm: '2026-10-13T21:16:38Z' })
  assert.equal(m.dir, 'sistema')
  assert.equal(m.texto, 'Passou para você: o atendimento automático está desligado')
})
await confere('C  cartão, faixa e linha longa da conversa da API não trazem "LLM" nem "não soube responder"', () => {
  const resumo = { id: 'c7', situacao: 'Assumida', assumidaPorUsuarioId: null, motivoEscalada: MOTIVO_CRU, canal: 'WhatsApp', contatoNome: 'Marina', ultimaMensagemEm: '2026-10-13T21:40:00Z', naoLidas: 1 }
  const conversa = conversaDaApi(resumo, [], { id: 'u1', nome: 'Dona' }, AGORA)
  const origem = origemDaPassagem(conversa.passagem)
  assert.equal(origem.curto, 'Sem automático')
  assert.equal(resumoDoModo(conversa, false, false).linha, 'o atendimento automático está desligado')
  for (const texto of [origem.curto, origem.longo, origem.motivo]) assert.ok(!/LLM|agente desligado|não soube responder/i.test(texto), texto)
  // Motivo de verdade do agente segue com a frase de antes.
  assert.equal(origemDaPassagem({ motivo: 'Cliente perguntou sobre alergia' }).curto, 'Automático não soube responder: cliente perguntou sobre alergia')
})
await confere('C  "esteira" saiu da barra de modo e do aviso de encerrar; o número é o da comanda', () => {
  const pausada = modoDoAtendimento({ estado: 'Em atendimento', mensagens: [] }, PAUSA_POR_ASSUMIR, false)
  assert.ok(!/esteira/i.test(pausada.detalhe), pausada.detalhe)
  assert.ok(/preparo e entrega/.test(pausada.detalhe))
  const conversa = conversaDaMarina()
  assert.equal(resumoDom.avisoDoPedidoAoEncerrar(conversa, { fonteApi: true }), 'A comanda 3 segue em preparo e entrega depois de encerrar.')
  const aguardando = { ...conversa, pedido: { ...conversa.pedido, estado: 'aguardando' } }
  assert.equal(resumoDom.avisoDoPedidoAoEncerrar(aguardando, { fonteApi: true }), 'A comanda 3 ainda não foi paga. Ela continua valendo depois de encerrar.')
  const demo = { pedido: { numero: '2026-0184', estado: 'preparo' } }
  assert.equal(resumoDom.avisoDoPedidoAoEncerrar(demo), 'O pedido Nº 2026-0184 segue em preparo e entrega depois de encerrar.')
})

// ── D  Erro do canal ─────────────────────────────────────────────────────────
const TEMPO = "The operation didn't complete within the allowed timeout of '00:00:30'."
await confere('D  os três erros crus do provedor viram frase em português, sem o inglês', () => {
  assert.equal(textoDaFalhaDeEnvio('Message undeliverable'), 'O WhatsApp não conseguiu entregar: confira se o número está certo.')
  assert.equal(textoDaFalhaDeEnvio('Re-engagement message'), 'Passaram 24 h desde a última mensagem do cliente: só modelo aprovado chega.')
  assert.equal(textoDaFalhaDeEnvio(TEMPO), 'O WhatsApp demorou a responder. Pode ter chegado: confira com o cliente antes de reenviar.')
  assert.equal(textoDaFalhaDeEnvio('Falha ao enviar pelo canal: ' + TEMPO), FRASE_DA_FALHA[TIPOS_DE_FALHA.TEMPO_ESGOTADO])
  assert.equal(textoDaFalhaDeEnvio('(#131026) Recipient cannot be reached'), FRASE_DA_FALHA[TIPOS_DE_FALHA.NAO_ENTREGAVEL])
})
await confere('D  erro desconhecido: "Não foi enviada. Tente de novo."; recusa já em português segue como veio', () => {
  assert.equal(textoDaFalhaDeEnvio('Internal server error: upstream reset'), 'Não foi enviada. Tente de novo.')
  assert.equal(textoDaFalhaDeEnvio(null), 'Não foi enviada. Tente de novo.')
  assert.equal(textoDaFalhaDeEnvio('Não enviada: Sem conexão com o EasyStok.'), 'Não enviada: Sem conexão com o EasyStok.')
  const retomada = 'Fora da janela de 24 h: o modelo de retomada saiu; reenvia quando o cliente responder.'
  assert.equal(textoDaFalhaDeEnvio(retomada), retomada)
  // Frase já traduzida passa de novo pelo mapa sem mudar (a bolha lê o que a ação gravou).
  for (const frase of Object.values(FRASE_DA_FALHA)) assert.equal(textoDaFalhaDeEnvio(frase), frase)
})
await confere('D  só a falha de janela pede modelo aprovado; a lista "Não entregues" lê o mesmo mapa', () => {
  assert.equal(pedeModeloAprovado('Re-engagement message'), true)
  assert.equal(pedeModeloAprovado(FRASE_DA_FALHA[TIPOS_DE_FALHA.FORA_DA_JANELA]), true)
  assert.equal(pedeModeloAprovado('Message undeliverable'), false)
  assert.equal(pedeModeloAprovado(TEMPO), false)
  const linha = naoEntregueDaApi({ conversaId: 'c4', contatoNome: 'Carlos Nove', canal: 'WhatsApp', conversaAberta: true,
    mensagem: { id: 'x', direcao: 'Saida', autor: 'Dona', status: 'Falhou', texto: 'Oi!', erro: 'Message undeliverable', enviadaEm: '2026-10-13T21:31:00Z' } })
  assert.equal(falhaDeEnvio(linha.mensagem.erro).tipo, TIPOS_DE_FALHA.NAO_ENTREGAVEL)
})

// ── F  Encerramento ──────────────────────────────────────────────────────────
await confere('F  caixa da despedida marcada com o campo vazio não encerra', () => {
  assert.equal(resumoDom.faltaTextoDaDespedida(true, ''), true)
  assert.equal(resumoDom.faltaTextoDaDespedida(true, '   \n'), true)
  assert.equal(resumoDom.faltaTextoDaDespedida(true, 'Obrigada!'), false)
  assert.equal(resumoDom.faltaTextoDaDespedida(false, ''), false)
  assert.ok(/Escreva a mensagem de encerramento/.test(resumoDom.PEDE_TEXTO_DA_DESPEDIDA))
})

// ── G  Endereço ──────────────────────────────────────────────────────────────
// #1989: hífen entre espaços passou a separar como a vírgula; o que a Ficha não separa é o texto sem CEP no fim.
const SEM_VIRGULA = 'Rua Cardeal Arcoverde 500 Pinheiros 05400-000'
await confere('G  texto que a Ficha não separa é recusado com o formato e um exemplo', () => {
  assert.equal(recusaDoEndereco(SEM_VIRGULA), FORMATO_DO_ENDERECO)
  assert.ok(/Exemplo: Rua das Flores, 120, apto 12, Pinheiros, 05400-000/.test(FORMATO_DO_ENDERECO))
  assert.equal(recusaDoEndereco('Rua Cardeal Arcoverde, 500, Pinheiros, 05400-000'), null)
  assert.equal(recusaDoEndereco(''), null)
  assert.equal(recusaDoEndereco(null), null)
})
await confere('G  a ação devolve a recusa e não manda POST com endereço nulo', async () => {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET', body } = {}) => {
    chamadas.push({ metodo: method, url: String(url), corpo: body ? JSON.parse(body) : null })
    return new Response(JSON.stringify({ data: { novo: false, nome: 'Joao Pedro', dentroDaArea: true, cliente: { id: 'cl2' }, enderecos: [] } }), { status: 200 })
  }
  const estadoRef = { current: { conversas: [{ id: 'c2', clienteId: 'cl2', canal: 'WhatsApp', nome: 'Joao Pedro', cliente: {} }] } }
  const acoes = criarAcoesClienteApi({ despachar: () => {}, estadoRef })
  const recusado = await acoes.editarDadoCliente('c2', 'endereco', SEM_VIRGULA)
  assert.equal(recusado?.erro, FORMATO_DO_ENDERECO)
  assert.equal(chamadas.filter((c) => c.metodo === 'POST').length, 0, 'nenhum POST saiu')
  await acoes.editarDadoCliente('c2', 'endereco', 'Rua Cardeal Arcoverde, 500, Pinheiros, 05400-000')
  const post = chamadas.find((c) => c.metodo === 'POST')
  assert.equal(post.corpo.endereco.cep, '05400000')
  assert.equal(post.corpo.endereco.logradouro, 'Rua Cardeal Arcoverde')
})

// ── H  Lembretes ─────────────────────────────────────────────────────────────
await confere('H  painel de 630 px aberto em 476 px numa janela de 900 sobe e cabe; em 720 também', () => {
  const em900 = encaixeNaJanela({ topo: 476, altura: 630, janela: 900 })
  assert.ok(476 - em900.sobe + em900.alturaMaxima <= 900 - 12, JSON.stringify(em900))
  assert.equal(em900.alturaMaxima, 630)
  const em720 = encaixeNaJanela({ topo: 476, altura: 504, janela: 720 })
  assert.ok(476 - em720.sobe + em720.alturaMaxima <= 720 - 12, JSON.stringify(em720))
  // Não cabe nem subindo tudo: encosta na margem de cima e rola por dentro.
  const apertado = encaixeNaJanela({ topo: 100, altura: 900, janela: 720 })
  assert.deepEqual(apertado, { sobe: 88, alturaMaxima: 696 })
  assert.equal(encaixeNaJanela({ topo: 60, altura: 300, janela: 900 }), null)
})
await confere('H  aviso de cliente sem resposta diz a hora do aviso, sem segunda conta de minutos', () => {
  const quando = '2026-10-13T21:31:36Z'
  const aviso = lembreteDaApi({ id: 'l1', tipo: 'ClienteSemResposta', texto: 'Sergio Sete está há 5 min sem resposta.', venceEm: quando, conversaId: 'c6' })
  const linha = tempoDoItem(aviso, Date.parse(quando) + 30000)
  assert.ok(/^avisado às \d{2}:\d{2}$/.test(linha), linha)
  const manual = lembreteDaApi({ id: 'l2', tipo: 'Manual', texto: 'Ligar para o fornecedor', venceEm: quando })
  assert.equal(tempoDoItem(manual, Date.parse(quando) + 12 * 60000), 'há 12 min')
})

// ── I  Perfil e textos ───────────────────────────────────────────────────────
const OPERADOR = { editarAtendimento: false, controlarLoja: false, reabrirConversa: true }
const ADMIN = { editarAtendimento: true, controlarLoja: true, reabrirConversa: true }
await confere('I  operador não lê avisos do cliente nem itens tirados do cardápio; o administrador lê', () => {
  assert.equal(podeVerAvisosDoCliente(OPERADOR), false)
  assert.equal(podeVerItensFora(OPERADOR), false)
  assert.equal(podeVerAvisosDoCliente(ADMIN), true)
  assert.equal(podeVerItensFora(ADMIN), true)
  assert.equal(podeVerAvisosDoCliente({}), false)
  assert.equal(podeVerItensFora(undefined), false)
})
await confere('I  título da comanda segue o pedido: montagem só antes de ele existir', () => {
  assert.equal(tituloDaComanda(null), 'Pedido em montagem')
  assert.equal(tituloDaComanda({ itens: [] }), 'Pedido em montagem')
  assert.equal(tituloDaComanda({ pedidoId: 'p', estado: 'aguardando' }), 'Pedido aguardando pagamento')
  assert.equal(tituloDaComanda({ pedidoId: 'p', estado: 'pago' }), 'Pedido confirmado')
  assert.equal(tituloDaComanda({ pedidoId: 'p', estado: 'cancelado' }), 'Pedido cancelado')
  assert.equal(tituloDaComanda({ pedidoId: 'p', estado: 'pago' }, { temLinkComPedido: true }), 'Link do cliente')
})
await confere('I  "Respondido." só depois de uma resposta dela que saiu; automática e falha não contam', () => {
  const base = { estado: 'Em atendimento', pedido: { estado: 'pago', itens: [{ sku: LASANHA, qtd: 1 }] } }
  const entrada = { id: 'a', dir: 'in', texto: 'quero uma lasanha' }
  const automatica = { id: 'b', dir: 'out', automatica: true, status: 'lida', texto: 'Resumo do seu pedido' }
  assert.equal(proximoPassoDepoisDeResponder({ ...base, mensagens: [entrada, automatica] }, PAUSA_POR_ASSUMIR), null)
  assert.equal(proximoPassoDepoisDeResponder({ ...base, mensagens: [entrada, { id: 'c', dir: 'out', status: 'falhou', texto: 'Oi' }] }, PAUSA_POR_ASSUMIR), null)
  assert.equal(proximoPassoDepoisDeResponder({ ...base, mensagens: [entrada, { id: 'd', dir: 'out', status: 'enviada', texto: 'Oi' }] }, PAUSA_POR_ASSUMIR).chave, 'aguardar')
})

// ── J  Layout do Balcão ──────────────────────────────────────────────────────
const WHATSAPP = { nome: 'WhatsApp', temJanela: true, horasJanela: 24, aceitaModelo: true }
const SITE = { nome: 'Chat do site', temJanela: false }
const janelaCom = (minutosRestantes) => estadoDaJanela(
  { janelaExpiraEm: new Date(AGORA + minutosRestantes * 60000).toISOString(), mensagens: [] }, AGORA, WHATSAPP,
)
await confere('J  prazo no cabeçalho só com menos de 2 h, ou com a janela fechada', () => {
  assert.equal(prazoNoCabecalho(janelaCom(23 * 60 + 55)), null)
  assert.equal(prazoNoCabecalho(janelaCom(120)), null)
  assert.equal(prazoNoCabecalho(janelaCom(119)), 'escreve livre por 1 h 59 min')
  assert.equal(prazoNoCabecalho(janelaCom(20)), 'escreve livre por 20 min')
  assert.equal(prazoNoCabecalho(janelaCom(-5)), 'Janela fechada')
  assert.equal(prazoNoCabecalho(estadoDaJanela({ mensagens: [] }, AGORA, SITE)), null)
  assert.equal(prazoNoCabecalho(estadoDaJanela({ mensagens: [] }, AGORA, null)), 'Canal sem regra cadastrada')
})
await confere('J  "Modelos aprovados" fixo só com menos de 1 h de janela, ou com ela fechada', () => {
  assert.equal(modelosAVista(janelaCom(23 * 60)), false)
  assert.equal(modelosAVista(janelaCom(60)), false)
  assert.equal(modelosAVista(janelaCom(59)), true)
  assert.equal(modelosAVista(janelaCom(-5)), true)
  assert.equal(modelosAVista(estadoDaJanela({ mensagens: [] }, AGORA, SITE)), false)
  assert.equal(modelosAVista(null), false)
})
await confere('J  "Devolver ao automático" some quando a conversa veio com o automático desligado', () => {
  const nota = (motivo) => ({ dir: 'sistema', texto: notaDeSistema('escalado para a dona: ' + motivo) })
  assert.equal(automaticoDesligadoNaConversa({ passagem: { motivo: AUTOMATICO_DESLIGADO } }), true)
  assert.equal(automaticoDesligadoNaConversa({ passagem: { motivo: 'cliente perguntou sobre alergia' } }), false)
  // Depois de assumir a passagem some do resumo; vale a nota que o EasyStok deixou na conversa.
  assert.equal(automaticoDesligadoNaConversa({ passagem: null, mensagens: [{ dir: 'in', texto: 'oi' }, nota(MOTIVO_CRU)] }), true)
  // A passagem mais recente manda: se o agente passou depois por outro motivo, ele está ligado.
  assert.equal(automaticoDesligadoNaConversa({ passagem: null, mensagens: [nota(MOTIVO_CRU), nota('cliente pediu desconto')] }), false)
  assert.equal(automaticoDesligadoNaConversa({ passagem: null, mensagens: [{ dir: 'in', texto: 'oi' }] }), false)
  assert.equal(automaticoDesligadoNaConversa(null), false)
})
await confere('J  linha zerada da Ficha: tags vira convite, Histórico vazio sai', () => {
  assert.equal(resumoDasTags(0), 'Adicionar tag')
  assert.equal(resumoDasTags(1), '1 tag')
  assert.equal(resumoDasTags(3), '3 tags')
  assert.equal(mostraHistorico({ pedidos: 0, fonteApi: true }), false)
  assert.equal(mostraHistorico({ pedidos: 2, fonteApi: true }), true)
  assert.equal(mostraHistorico({ pedidos: 0, fonteApi: true, resgataPontos: true }), true)
  assert.equal(mostraHistorico({ pedidos: 0, fonteApi: false }), true, 'na demonstração o modal tem a aba de atendimentos')
})
await confere('J  chip de canal só para canal com conversa aberta, ou ligado como filtro', () => {
  assert.equal(mostraChipDoCanal({ abertas: 0, precisam: 0 }), false)
  assert.equal(mostraChipDoCanal({ abertas: 2, precisam: 0 }), true)
  assert.equal(mostraChipDoCanal({ abertas: 0, precisam: 0 }, true), true)
})
await confere('J  porção só aparece quando o nome ainda não diz', () => {
  assert.equal(porcaoForaDoNome({ nome: 'Lasanha Bolonhesa Clássica 600 g', porcao: '600 g' }), '')
  assert.equal(porcaoForaDoNome({ nome: 'Lasanha Bolonhesa Clássica 600 g', porcao: '600g' }), '')
  assert.equal(porcaoForaDoNome({ nome: 'Lasanha Bolonhesa Clássica', porcao: '600 g' }), '600 g')
  assert.equal(porcaoForaDoNome({ nome: 'Talharim 500 g', porcao: '' }), '')
  assert.equal(porcaoForaDoNome(null), '')
})
await confere('J  o cardápio fecha quando o pedido nasce na conversa aberta, e só então', () => {
  assert.equal(pedidoAcabouDeNascer({ conversaId: 'c1', pedidoId: null }, { conversaId: 'c1', pedidoId: 'p1' }), true)
  assert.equal(pedidoAcabouDeNascer({ conversaId: 'c1', pedidoId: 'p1' }, { conversaId: 'c1', pedidoId: 'p1' }), false)
  assert.equal(pedidoAcabouDeNascer({ conversaId: 'c1', pedidoId: null }, { conversaId: 'c2', pedidoId: 'p9' }), false, 'trocar para conversa com pedido não fecha')
  assert.equal(pedidoAcabouDeNascer({ conversaId: 'c1', pedidoId: null }, { conversaId: 'c1', pedidoId: null }), false)
})

await confere('K  automático desligado continua valendo depois de reabrir a conversa', () => {
  const nota = { dir: 'sistema', texto: 'Passou para você: o atendimento automático está desligado' }
  assert.equal(automaticoDesligadoNaConversa({ passagem: { motivo: 'Conversa reaberta para atendimento.' }, mensagens: [nota] }), true)
  assert.equal(automaticoDesligadoNaConversa({ passagem: { motivo: 'cliente pediu atendente' }, mensagens: [] }), false)
})

await confere('K  reenvio que demora de novo não afirma que não saiu', () => {
  assert.match(fraseDoReenvio("The operation didn't complete within the allowed timeout of '00:00:30'."), /Pode ter chegado/)
  assert.equal(fraseDoReenvio('Message undeliverable'), 'Tentei de novo e não saiu.')
})

await confere('K  a prévia do cartão passa pelo tradutor da nota', () => {
  assert.equal(notaDeSistema('escalado para a dona: agente desligado (LLM indisponível)'),
    'Passou para você: o atendimento automático está desligado')
})

await confere('M  maquininha na entrega sem nada recebido não é "Pago em parte" nem "Sinal pago"', () => {
  const naEntrega = { ...PEDIDO_COM_SINAL, totalPago: 0, alocado: 0, pagamentos: [], cobranca: { ...PEDIDO_COM_SINAL.cobranca, provedor: 'na_entrega', status: 'Pendente' } }
  const resumo = resumoDom.montarResumoAtendimento(conversaDaMarina(naEntrega), CARDAPIO, AGORA, {})
  for (const venda of resumo.vendas) assert.notEqual(venda.situacao, 'Pago em parte'), assert.notEqual(venda.situacao, 'Sinal pago'), assert.notEqual(venda.situacao, 'Pago')
  assert.equal(resumo.receita.liquido, 0)
})
await confere('M  demonstração: pedido nascido pago, sem registro de pagamento, segue "Pago"', () => {
  const venda = resumoDom.vendaDoTrecho({ numero: '2026-0001', estado: 'pago', itens: [{ sku: LASANHA, qtd: 1 }] }, CARDAPIO, 0)
  if (venda) assert.equal(venda.situacao ?? venda[2], 'Pago')
})
await confere('M  o sinal de automático desligado acaba quando o automático volta a responder', () => {
  const nota = { dir: 'sistema', texto: 'Passou para você: o atendimento automático está desligado' }
  assert.equal(automaticoDesligadoNaConversa({ passagem: null, mensagens: [nota, { dir: 'out', origemAutomatica: 'sistema' }] }), true)
  assert.equal(automaticoDesligadoNaConversa({ passagem: null, mensagens: [nota, { dir: 'out', origemAutomatica: 'agente' }] }), false)
})
await confere('M  "envio incerto" não vira "não foi enviada"; porção só some com o nome trazendo ela inteira', () => {
  assert.equal(falhaDeEnvio('Envio incerto (sem resposta do canal): The operation was canceled.').tipo, TIPOS_DE_FALHA.TEMPO_ESGOTADO)
  assert.equal(porcaoForaDoNome({ nome: 'Sopa', porcao: 'P' }), 'P')
  assert.equal(porcaoForaDoNome({ nome: 'Lasanha 1600 g', porcao: '600 g' }), '600 g')
  assert.equal(porcaoForaDoNome({ nome: 'Lasanha Bolonhesa Clássica 600 g', porcao: '600 g' }), '')
  assert.equal(porcaoForaDoNome({ nome: 'Talharim 500g', porcao: '500 g' }), '')
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) {
  console.log(falhas.map((f) => ' - ' + f).join('\n'))
  process.exit(1)
}
