// Prova da #1445: o "Pergunte ao assistente" propõe ações (mandar o cardápio, nota interna,
// rascunho, abrir cardápio ou comanda) e nada sai ao cliente antes do clique da atendente.
// Perguntar só chama o assistente; o envio do cardápio, a nota e o rascunho acontecem em
// `executarAcaoDoAssistente`, que é o que o botão do cartão chama.
//
//   node ferramentas/prova-1445-assistente-acoes.mjs
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* resolução padrão */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return proximo(url, contexto)
  },
})

const acao = await import('../src/aplicacao/acoes.js')
const { criarAcoes } = await import('../src/aplicacao/criarAcoes.js')
const { comApi } = await import('../src/aplicacao/acoesApi.js')
const {
  ACOES_DO_ASSISTENTE, descreverAcaoDoAssistente, executarAcaoDoAssistente, respostaDoAssistente,
} = await import('../src/dominio/acoesDoAssistente.js')

globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }
const chamadas = []
const respostas = {
  '/api/atendimento/assistente': () => ({ data: {
    resposta: 'Preparei o cardápio e a nota.',
    tokensEntrada: 300, tokensSaida: 40, latenciaMs: 900,
    acoes: [
      { tipo: 'enviar_cardapio', texto: null, tela: null },
      { tipo: 'nota_interna', texto: 'Prefere sem cebola.', tela: null },
      { tipo: 'rascunho', texto: 'Oi Maria! Segue o cardápio.', tela: null },
      { tipo: 'abrir_tela', texto: null, tela: 'comanda' },
      { tipo: 'criar_pedido', texto: null, tela: null },
      { tipo: 'nota_interna', texto: '  ', tela: null },
      { tipo: 'abrir_tela', texto: null, tela: 'caixa' },
      { tipo: 'enviar_cardapio', texto: null, tela: null },
    ],
  } }),
  '/api/atendimento/conversas/c1/link-cardapio': () => ({ data: { url: 'https://loja.test/c/abc' } }),
  '/api/atendimento/conversas/c1/mensagens': () => ({ data: { id: 'm1', texto: 'ok', direcao: 'Saida', autor: 'Dona', enviadaEm: new Date().toISOString(), status: 'Enviada' } }),
  '/api/clientes/cli1/notas': () => ({ data: { id: 'n1' } }),
  '/api/atendimento/conversas/c1/dossie': () => ({ data: null }),
}
globalThis.fetch = async (url, opcoes = {}) => {
  chamadas.push({ url, metodo: opcoes.method ?? 'GET', corpo: opcoes.body ? JSON.parse(opcoes.body) : null })
  const corpo = respostas[url]
  if (!corpo) return new Response(JSON.stringify({ error: { message: `sem rota ${url}` } }), { status: 404 })
  return new Response(JSON.stringify(corpo()))
}

const conversa = { id: 'c1', nome: 'Maria Souza', canal: 'WhatsApp', clienteId: 'cli1', mensagens: [], cliente: { notas: [], tags: [] } }
const estadoRef = { current: { conversas: [conversa] } }
const despachos = []
const despachar = (a) => despachos.push(a)
const agoraRef = { current: Date.now() }
const locais = criarAcoes({
  despachar, agoraRef, estadoRef, pendentes: { current: [] },
  consultarAgente: async () => {}, perguntarAssistente: async () => 'resposta local', pedirNotificacaoDoNavegador: async () => {},
})
const api = comApi(locais, { despachar, agoraRef, estadoRef })

// 1. Perguntar só pergunta: uma chamada ao assistente, nenhum envio.
const bruta = await api.perguntarAssistente('manda o cardápio e anota que ela prefere sem cebola', conversa)
assert.deepEqual(chamadas.map((c) => c.url), ['/api/atendimento/assistente'], 'perguntar não envia nada')
assert.deepEqual(chamadas[0].corpo, { pergunta: 'manda o cardápio e anota que ela prefere sem cebola', conversaId: 'c1' })
assert.equal(despachos.length, 0, 'perguntar não mexe no estado da conversa')

// 2. As propostas válidas viram cartões; o resto cai fora.
const { texto, acoes } = respostaDoAssistente(bruta)
assert.equal(texto, 'Preparei o cardápio e a nota.')
assert.deepEqual(acoes.map((a) => [a.tipo, a.texto, a.tela]), [
  [ACOES_DO_ASSISTENTE.ENVIAR_CARDAPIO, null, null],
  [ACOES_DO_ASSISTENTE.NOTA_INTERNA, 'Prefere sem cebola.', null],
  [ACOES_DO_ASSISTENTE.RASCUNHO, 'Oi Maria! Segue o cardápio.', null],
  [ACOES_DO_ASSISTENTE.ABRIR_TELA, null, 'comanda'],
], 'tipo desconhecido, texto vazio, tela fora da lista e repetida ficam de fora')
assert.ok(acoes.every((a) => a.estado === 'proposta'))

// Modo local (servidor do agente) segue respondendo só texto.
assert.deepEqual(respostaDoAssistente('só texto'), { texto: 'só texto', acoes: [] })

// 3. Rótulos: só o cardápio sai para o cliente, e o botão diz isso.
const [cardapio, nota, rascunho, comanda] = acoes
const rotulos = descreverAcaoDoAssistente(cardapio, conversa.nome)
assert.equal(rotulos.botao, 'Enviar ao cliente')
assert.equal(rotulos.saiParaCliente, true)
assert.match(rotulos.detalhe, /Maria\./)
assert.equal(acoes.filter((a) => descreverAcaoDoAssistente(a, conversa.nome).saiParaCliente).length, 1)
assert.equal(descreverAcaoDoAssistente(comanda, conversa.nome).botao, 'Ver comanda')

// 4. Clique em "Enviar ao cliente": link da loja e o convite pela mesma rota do compositor.
chamadas.length = 0
let telaAberta = null
const portas = { ...api, abrirTela: (tela) => { telaAberta = tela } }
await executarAcaoDoAssistente(cardapio, conversa, portas)
await new Promise((r) => setTimeout(r, 0))
assert.deepEqual(chamadas.map((c) => `${c.metodo} ${c.url}`), [
  'POST /api/atendimento/conversas/c1/link-cardapio',
  'POST /api/atendimento/conversas/c1/mensagens',
])
assert.match(chamadas[1].corpo.texto, /^Oi Maria! Segue o cardápio de hoje/)
assert.match(chamadas[1].corpo.texto, /https:\/\/loja\.test\/c\/abc$/)

// 5. Nota interna: vai ao cadastro do cliente (#1436), não à conversa.
chamadas.length = 0
await executarAcaoDoAssistente(nota, conversa, portas)
assert.equal(chamadas[0].metodo, 'POST')
assert.equal(chamadas[0].url, '/api/clientes/cli1/notas')
assert.deepEqual(chamadas[0].corpo, { texto: 'Prefere sem cebola.' })
assert.equal(chamadas.some((c) => c.url.endsWith('/mensagens')), false, 'nota não vira mensagem ao cliente')

// 6. Rascunho: só o campo de escrever, nenhuma chamada.
chamadas.length = 0
despachos.length = 0
await executarAcaoDoAssistente(rascunho, conversa, portas)
assert.equal(chamadas.length, 0, 'rascunho não envia')
assert.deepEqual(despachos, [{ tipo: acao.DEFINIR_RASCUNHO, id: 'c1', texto: 'Oi Maria! Segue o cardápio.' }])

// 7. Abrir comanda: a tela recebe o pedido, nada vai ao servidor.
await executarAcaoDoAssistente(comanda, conversa, portas)
assert.equal(telaAberta, 'comanda')
assert.equal(chamadas.length, 0)

// 8. Link que não veio: erro claro no cartão, e nada sai.
respostas['/api/atendimento/conversas/c1/link-cardapio'] = () => { throw new Error('sem link') }
chamadas.length = 0
const semLink = { ...portas, obterLinkCardapio: async () => null }
await assert.rejects(executarAcaoDoAssistente(cardapio, conversa, semLink), /link do cardápio/)
assert.equal(chamadas.length, 0)

// 9. Régua de objetividade da avaliação do agente (avaliar-agente.mjs): 1 a 3 frases, sem floreio.
const { objetividade } = await import('../src/dominio/agente.js')
assert.equal(objetividade('Oi Maria! Segue o cardápio: https://loja.test/c/abc').prolixo, false, 'link não conta como frase')
assert.equal(objetividade('Oi! Tem sim. Chega às 19h. Quer pedir?').frases, 4)
assert.equal(objetividade('Oi! Tem sim. Chega às 19h. Quer pedir?').prolixo, true)
assert.deepEqual(objetividade('Tem sim. Qualquer dúvida, estou aqui.').floreios, ['qualquer duvida'])

console.log('Assistente com ações: proposta sem envio, cardápio, nota, rascunho e comanda só no clique, e régua de objetividade verificados.')
