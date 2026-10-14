/* eslint-disable no-console */
// Prova da issue #1586 (banca de navegação e UX de 2026-10-11): encerramento, ficha e dinheiro
// com o retorno no lugar da ação. No modo API a faixa do rodapé fica atrás da cortina do modal.
//
//   A  Encerrar trava o segundo toque (na ação e no modal) e devolve o erro para o modal;
//   B  avaliação, anotação e a aba "Atendimentos" não aparecem no modo API; a faixa não acende
//      depois de encerrar que deu certo, nem diz "segue aberto" de pedido que o modal cancelou;
//   C  a edição em linha só mostra "Salvo" depois da resposta e mantém o texto na recusa;
//   D  "Marcar pago" e "Desfazer pagamento" só fecham no sucesso;
//   E  "Bloquear cliente" nasce desabilitado, com o motivo, para quem não tem cadastro;
//   F  link vencido no modo API diz o que o EasyStok faz, sem botão que só avisava.
//
// Duas partes: as ações reais com o HTTP controlado na fronteira (`fetch`), e os componentes
// reais (JSX compilado) com hooks e contextos controlados, no molde da prova-premium-comanda.
// Não prova pixels, foco nativo nem o serviço remoto.
//
//   node ferramentas/prova-1586-encerrar-ficha-ux.mjs

import assert from 'node:assert/strict'
import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { transformWithOxc } from 'vite'

const src = new URL('../src/', import.meta.url)

// JSX real das duas pastas da issue, compilado antes dos ganchos (o `load` é síncrono).
const compilados = new Map()
for (const pasta of ['features/encerramento/', 'features/ficha-cliente/']) {
  for (const arquivo of readdirSync(new URL(pasta, src)).filter((f) => f.endsWith('.jsx'))) {
    const url = new URL(pasta + arquivo, src)
    compilados.set(url.href, (await transformWithOxc(readFileSync(url, 'utf8'), url.pathname, { jsx: { runtime: 'automatic' } })).code)
  }
}

let ativo
let sequencia = 0
const mesmasDeps = (a, b) => a?.length === b?.length && a?.every((v, i) => Object.is(v, b[i]))
const hooks = {
  useState(inicial) {
    const host = ativo
    const i = host.indice++
    if (!(i in host.valores)) host.valores[i] = typeof inicial === 'function' ? inicial() : inicial
    return [host.valores[i], (valor) => {
      if (host.encerrado) return
      const novo = typeof valor === 'function' ? valor(host.valores[i]) : valor
      if (!Object.is(novo, host.valores[i])) { host.valores[i] = novo; host.sujo = true }
    }]
  },
  useRef(inicial) { const host = ativo; const i = host.indice++; return host.valores[i] ??= { current: inicial } },
  useId() { const host = ativo; const i = host.indice++; return host.valores[i] ??= `id-${++sequencia}` },
  useMemo(fn, deps) {
    const host = ativo
    const i = host.indice++
    if (!host.valores[i] || !mesmasDeps(host.valores[i].deps, deps)) host.valores[i] = { deps, valor: fn() }
    return host.valores[i].valor
  },
  useEffect(fn, deps) {
    const host = ativo
    const i = host.indice++
    const anterior = host.efeitos.get(i)
    if (!mesmasDeps(anterior?.deps, deps)) host.pendentes.push(() => { anterior?.limpar?.(); host.efeitos.set(i, { deps, limpar: fn() }) })
  },
}
const ctx = { hooks, atendimento: {}, catalogo: {}, acoes: {} }
globalThis.__prova1586 = ctx

const codigo = (texto) => 'data:text/javascript,' + encodeURIComponent(texto)
const REACT = 'const h=globalThis.__prova1586.hooks; export const useState=h.useState,useRef=h.useRef,useId=h.useId,'
  + 'useMemo=h.useMemo,useEffect=h.useEffect,useLayoutEffect=h.useEffect,useCallback=(f)=>f,forwardRef=(f)=>f,memo=(f)=>f'
const fronteiras = new Map([
  ['/aplicacao/contextos', 'const c=globalThis.__prova1586; export const useAtendimento=()=>c.atendimento, useCatalogo=()=>c.catalogo, useAcoes=()=>c.acoes'],
  ['/aplicacao/acessoModulos', 'export const useAcessoModulos=()=>({acoes:{cancelarPedidoPago:true}})'],
])
// Componente de `componentes/` vira casca com o mesmo nome: a árvore guarda o tipo e as props.
function cascaDoComponente(arquivo) {
  const nomes = [...readFileSync(arquivo, 'utf8').matchAll(/export\s+(?:const|function)\s+(\w+)/g)].map((m) => m[1])
  return nomes.map((nome) => `export function ${nome}(){return null}`).join(';')
}
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec === 'react') return { url: codigo(REACT), shortCircuit: true }
    if (spec === 'react/jsx-runtime') return { url: codigo('export const Fragment="fragment"; export const jsx=(type,props,key)=>({type,props,key}); export const jsxs=jsx'), shortCircuit: true }
    if (spec.endsWith('.css')) return { url: codigo('export default new Proxy({},{get:(_,chave)=>String(chave)})'), shortCircuit: true }
    if (spec.startsWith('.') && contexto.parentURL?.startsWith('file:')) {
      const base = new URL(spec, contexto.parentURL).href.replace(/\.(jsx?|mjs)$/, '')
      for (const [fim, fonte] of fronteiras) if (base.endsWith(fim)) return { url: codigo(fonte), shortCircuit: true }
      if (/\/src\/componentes\/[^/]+$/.test(base)) {
        const arquivo = ['.jsx', '.js'].map((ext) => new URL(base + ext)).find((url) => existsSync(url))
        if (arquivo) return { url: codigo(cascaDoComponente(arquivo)), shortCircuit: true }
      }
      if (!/\.[a-z]+$/i.test(spec)) {
        const ext = ['.js', '.jsx'].find((e) => existsSync(new URL(base + e)))
        if (ext) return proximo(spec + ext, contexto)
      }
    }
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (compilados.has(url)) return { format: 'module', shortCircuit: true, source: compilados.get(url) }
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return proximo(url, contexto)
  },
})

globalThis.sessionStorage = {
  getItem: () => JSON.stringify({ token: 't', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa' } }),
  setItem: () => {}, removeItem: () => {},
}
globalThis.window = globalThis.window ?? { dispatchEvent: () => {} }

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
const pendente = () => { let resolver; const promise = new Promise((r) => { resolver = r }); return { promise, resolver } }
const turno = async () => { for (let i = 0; i < 20; i += 1) await Promise.resolve() }

// ── Parte 1: ações reais, HTTP controlado ───────────────────────────────────
const chamadas = []
let atender = () => resposta(null)
const resposta = (dados, status = 200) => new Response(
  JSON.stringify(status < 400 ? { data: dados } : { error: { message: dados } }), { status, headers: { 'Content-Type': 'application/json' } },
)
globalThis.fetch = async (url, opcoes = {}) => {
  const chamada = { url: String(url), metodo: opcoes.method ?? 'GET', corpo: typeof opcoes.body === 'string' ? JSON.parse(opcoes.body) : undefined }
  chamadas.push(chamada)
  return atender(chamada)
}

const acao = await import('../src/aplicacao/acoes.js')
const { comApi } = await import('../src/aplicacao/acoesApi.js')
const { acaoDisponivel } = await import('../src/aplicacao/api/naoLigadas.js')
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const clienteDom = await import('../src/dominio/cliente.js')
const cobrancaDom = await import('../src/dominio/cobranca.js')
const { GATILHOS } = await import('../src/dominio/automacao.js')
const { montarResumoAtendimento } = await import('../src/dominio/resumoAtendimento.js')

const AGORA = Date.parse('2026-10-11T15:00:00Z')
const MENSAGEM_API = { id: 'm-api', direcao: 'Saida', status: 'Enviada', autor: 'Dona', enviadaEm: '2026-10-11T15:00:00Z', tipoConteudo: 'Texto', texto: 'ok' }
const PEDIDO_NA_ENTREGA = {
  pedidoId: '11111111-1111-1111-1111-111111111111', status: 'aguardando', total: 25, frete: 5, itens: [], totalPago: 25,
  pagamentos: [{ id: 'pg1', valor: 25, metodo: 'dinheiro', pagoEm: '2026-10-11T14:50:00Z' }],
  cobranca: { cobrancaId: 'cb', provedor: 'na_entrega', status: 'Paga', valor: 25, valorPago: 25, criadaEm: '2026-10-11T14:40:00Z', pagaEm: '2026-10-11T14:50:00Z', metodoPagamento: 'dinheiro' },
}
const DOSSIE = { cliente: { id: 'cl1', nome: 'Ana Souza', telefone: '+5511911112222' }, enderecos: [], tags: [], notas: [], totalPedidos: 0 }
const conversaBase = (extra = {}) => ({
  id: 'c1', canal: 'WhatsApp', nome: 'Ana Souza', estado: 'Em atendimento', conta: 'cliente', clienteId: 'cl1',
  mensagens: [{ id: 'm1', dir: 'in', texto: 'Oi', em: '2026-10-11T14:00:00Z' }], pedido: null,
  cliente: { telefone: '(11) 90000-0000', endereco: null, enderecoCapturado: null, pedidos: 0, notas: [], tags: [] }, ...extra,
})

function montarAcoes() {
  const despachos = []
  const estadoRef = { current: { conversas: [
    conversaBase(),
    conversaBase({ id: 'c2', pedido: { pedidoId: 'p-aberto', estado: 'aguardando', itens: [] } }),
    conversaBase({ id: 'pg', pedido: pedidoDaApi(PEDIDO_NA_ENTREGA) }),
    conversaBase({ id: 'lead', conta: 'lead', clienteId: null, nomeDaDona: true, cliente: { telefone: null, endereco: null, enderecoCapturado: 'Rua X, 1, Centro, 01001-000', telefoneCanal: '5511988887777', notas: [], tags: [] } }),
  ] } }
  const api = comApi({}, { despachar: (d) => despachos.push(d), agoraRef: { current: AGORA }, estadoRef })
  chamadas.length = 0
  return { api, despachos }
}
const avisos = (despachos) => despachos.filter((d) => d.tipo === acao.AVISO_API).map((d) => d.mensagem)
const posts = (fim) => chamadas.filter((c) => c.metodo === 'POST' && c.url.endsWith(fim))

await confere('A  encerrar recusado devolve { erro } ao modal e não fecha a conversa na tela', async () => {
  const { api, despachos } = montarAcoes()
  atender = (c) => (c.url.endsWith('/encerrar') ? resposta('A conversa mudou em outra tela.', 409) : resposta(MENSAGEM_API))
  const r = await api.encerrarComResumo('c1', AGORA, { enviarMensagem: true, textoMensagem: 'Obrigada!' })
  assert.equal(r?.erro, 'Atendimento não encerrado: A conversa mudou em outra tela.')
  assert.equal(r.despedidaSaiu, true, 'o modal precisa saber que a despedida já foi')
  assert.ok(!despachos.some((d) => d.tipo === acao.ENCERRAR_ATENDIMENTO || d.tipo === acao.FECHAR_ENCERRAMENTO))
})
await confere('A  dois toques com o primeiro em voo: uma despedida e um encerrar só', async () => {
  const { api } = montarAcoes()
  const espera = pendente()
  atender = async (c) => { if (c.url.endsWith('/mensagens')) await espera.promise; return resposta(MENSAGEM_API) }
  const opcoes = { enviarMensagem: true, textoMensagem: 'Obrigada!' }
  const primeiro = api.encerrarComResumo('c1', AGORA, opcoes)
  const segundo = api.encerrarComResumo('c1', AGORA, opcoes)
  assert.equal(segundo, primeiro, 'o segundo toque recebe a mesma promessa')
  espera.resolver()
  await Promise.all([primeiro, segundo])
  assert.equal(posts('/c1/mensagens').length, 1, 'despedida saiu mais de uma vez')
  assert.equal(posts('/c1/encerrar').length, 1)
  // Terminada a primeira, a trava solta: um novo encerramento roda.
  await api.encerrarComResumo('c1', AGORA, { enviarMensagem: false })
  assert.equal(posts('/c1/encerrar').length, 2)
})
await confere('B  encerrar que deu certo não acende a faixa nem fala em "não são guardados"', async () => {
  const { api, despachos } = montarAcoes()
  atender = () => resposta(MENSAGEM_API)
  const r = await api.encerrarComResumo('c1', AGORA, { enviarMensagem: true, textoMensagem: 'Obrigada!' })
  assert.equal(r?.erro, undefined)
  assert.deepEqual(avisos(despachos), [])
  for (const tipo of [acao.ENCERRAR_ATENDIMENTO, acao.FECHAR_ENCERRAMENTO, acao.FECHAR_AVISO_API]) {
    assert.ok(despachos.some((d) => d.tipo === tipo), tipo)
  }
})
await confere('B  "segue aberto" só sai quando o modal não cancelou o pedido sem pagamento', async () => {
  atender = () => resposta(MENSAGEM_API)
  const cancelado = montarAcoes()
  await cancelado.api.encerrarComResumo('c2', AGORA, { pedidoCancelado: true })
  assert.deepEqual(avisos(cancelado.despachos), [])
  const aberto = montarAcoes()
  await aberto.api.encerrarComResumo('c2', AGORA, { pedidoCancelado: false })
  assert.equal(avisos(aberto.despachos).length, 1)
  assert.match(avisos(aberto.despachos)[0], /^Atendimento encerrado no EasyStok\. O pedido sem pagamento segue aberto/)
  assert.doesNotMatch(avisos(aberto.despachos)[0], /não são guardados/)
})
await confere('B  avaliação e anotação do encerramento não estão disponíveis no modo API', () => {
  for (const nome of ['salvarAvaliacaoCliente', 'salvarAutoavaliacao', 'salvarAnotacaoFechamento']) {
    assert.equal(acaoDisponivel(nome, { fonteApi: true }), false, nome)
    assert.equal(acaoDisponivel(nome, { fonteApi: false }), true, nome)
  }
})
await confere('C  cadastro recusado devolve { erro }; gravado devolve sem erro', async () => {
  const recusado = montarAcoes()
  atender = (c) => (c.url.endsWith('/cliente') ? resposta('Informe o telefone do cliente para cadastrar.', 400) : resposta(DOSSIE))
  assert.equal((await recusado.api.editarDadoCliente('c1', 'telefone', '', AGORA))?.erro, 'Informe o telefone do cliente para cadastrar.')
  assert.equal((await recusado.api.salvarCadastroRapido('lead', { nome: 'Lia', telefone: '', endereco: null }, AGORA))?.erro, 'Informe o telefone do cliente para cadastrar.')
  assert.equal((await recusado.api.cadastrarEndereco('lead'))?.erro, 'Informe o telefone do cliente para cadastrar.')
  assert.ok(!recusado.despachos.some((d) => d.tipo === acao.CLIENTE_DA_API))
  const gravado = montarAcoes()
  atender = (c) => (c.url.endsWith('/cliente') ? resposta({ clienteId: 'cl1', nome: 'Ana Souza', novo: false, dentroDaArea: true }) : resposta(DOSSIE))
  assert.equal((await gravado.api.editarDadoCliente('c1', 'telefone', '(11) 91111-2222', AGORA))?.erro, undefined)
  assert.ok(gravado.despachos.some((d) => d.tipo === acao.CLIENTE_DA_API))
})
await confere('C  cadastro gravado com a leitura da ficha falhando não vira recusa', async () => {
  const { api, despachos } = montarAcoes()
  atender = (c) => (c.url.endsWith('/cliente') ? resposta({ clienteId: 'cl1', nome: 'Ana Souza', novo: false, dentroDaArea: true }) : resposta('Indisponível', 503))
  assert.equal((await api.editarDadoCliente('c1', 'telefone', '(11) 91111-2222', AGORA))?.erro, undefined)
  assert.match(avisos(despachos).at(-1) ?? '', /^Cadastro salvo, mas a ficha não atualizou/)
})
await confere('D  baixa e desfazer recusados devolvem { erro }; aceitos, não', async () => {
  const recusado = montarAcoes()
  atender = (c) => (c.metodo === 'POST' ? resposta('Falha ao gravar', 500) : resposta(PEDIDO_NA_ENTREGA))
  assert.equal((await recusado.api.confirmarPagamento('pg', 25, 'dinheiro'))?.erro, 'Pagamento não registrado: Falha ao gravar')
  assert.equal((await recusado.api.desfazerPagamento('pg', 'Lançamento errado'))?.erro, 'Pagamento não desfeito: Falha ao gravar')
  const aceito = montarAcoes()
  atender = () => resposta(PEDIDO_NA_ENTREGA)
  assert.equal((await aceito.api.confirmarPagamento('pg', 25, 'dinheiro'))?.erro, undefined)
  assert.equal((await aceito.api.desfazerPagamento('pg', 'Lançamento errado'))?.erro, undefined)
  assert.deepEqual(avisos(aceito.despachos), [])
})
await confere('E  bloquear sem cadastro: motivo escrito só no modo API e só para quem não tem cliente', () => {
  assert.equal(clienteDom.motivoParaNaoBloquear({ clienteId: null }, { fonteApi: true }), 'Salve o cadastro do cliente antes de bloquear.')
  assert.equal(clienteDom.motivoParaNaoBloquear({ clienteId: 'cl1' }, { fonteApi: true }), null)
  assert.equal(clienteDom.motivoParaNaoBloquear({ clienteId: null }, { fonteApi: false }), null)
  const fonte = readFileSync(new URL('features/ficha-cliente/BlocoCliente.jsx', src), 'utf8')
  assert.match(fonte, /motivoSemBloqueio = bloqueada \? null : motivoParaNaoBloquear\(conversa, \{ fonteApi \}\)/)
  assert.match(fonte, /disabled=\{Boolean\(motivoSemBloqueio\)\}/)
  assert.match(fonte, /\{motivoSemBloqueio && <p id=\{idMotivoBloqueio\}[^>]*>\{motivoSemBloqueio\}<\/p>\}/)
})
await confere('F  link vencido: o texto diz o que o job do EasyStok faz em cada tentativa', () => {
  // #1683: o job só reemite e cancela com o pedido esperando o pagamento; o status entra na pergunta.
  assert.match(cobrancaDom.avisoDeLinkVencidoApi({ tentativa: 1 }, 'aguardando_pagamento'), /manda um link novo ao cliente sozinho/)
  assert.match(cobrancaDom.avisoDeLinkVencidoApi({ tentativa: 2 }, 'aguardando_pagamento'), /cancela o pedido sozinho e avisa o cliente/)
  // A regra é do backend: se a tentativa máxima mudar lá, este texto precisa mudar junto.
  const dominio = readFileSync(new URL('../../EasyStock.Domain/Entities/Pagamentos/CobrancaPedido.cs', src), 'utf8')
  assert.match(dominio, /public const int TentativaMaxima = 2;/)
})

// ── Parte 2: componentes reais, hooks e contextos controlados ───────────────
const { ModalEncerrar } = await import('../src/features/encerramento/ModalEncerrar.jsx')
const { FolhaResumo } = await import('../src/features/encerramento/FolhaResumo.jsx')
const { CampoEmLinha } = await import('../src/features/ficha-cliente/CampoEmLinha.jsx')
const { BarraProximoPasso } = await import('../src/features/ficha-cliente/BarraProximoPasso.jsx')
const { BaixaAMao } = await import('../src/features/ficha-cliente/BlocoCobranca.jsx')
const { ModalHistorico } = await import('../src/features/ficha-cliente/ModalHistorico.jsx')
const { BlocoEnderecoCapturado } = await import('../src/features/ficha-cliente/BlocoEnderecoCapturado.jsx')

class Tela {
  constructor(Componente, props) { this.Componente = Componente; this.props = props; this.valores = []; this.efeitos = new Map(); this.renderizar() }
  renderizar(props = this.props) {
    this.props = props
    let voltas = 0
    do {
      assert.ok(++voltas < 12, 'render deve estabilizar')
      ativo = this; this.sujo = false; this.indice = 0; this.pendentes = []
      this.arvore = this.Componente(this.props)
      this.pendentes.forEach((fn) => fn())
    } while (this.sujo)
    return this.arvore
  }
  encerrar() { this.encerrado = true; for (const efeito of this.efeitos.values()) efeito.limpar?.() }
}
// O rodapé do Modal chega por prop, não por `children`.
const nos = (arvore) => (Array.isArray(arvore) ? arvore.flatMap(nos)
  : !arvore || typeof arvore !== 'object' ? [] : [arvore, ...nos(arvore.props?.children), ...nos(arvore.props?.rodape)])
const texto = (arvore) => (Array.isArray(arvore) ? arvore.map(texto).join('')
  : arvore == null || typeof arvore === 'boolean' ? '' : typeof arvore !== 'object' ? String(arvore) : texto(arvore.props?.children))
const doTipo = (tela, nome) => nos(tela.arvore).filter((n) => n.type === nome || n.type?.name === nome)
const botao = (tela, rotulo) => nos(tela.arvore).find((n) => (n.type === 'button' || n.type?.name === 'Botao')
  && (texto(n).includes(rotulo) || n.props['aria-label'] === rotulo))
const alertas = (tela) => nos(tela.arvore).filter((n) => n.props?.role === 'alert').map(texto)

const REGRA_ENCERRAMENTO = { id: 'encerramento', gatilho: GATILHOS.ENCERRAMENTO, ativa: true, texto: 'Obrigada, {nome}!' }
const comoApi = (conversas = [conversaBase()]) => {
  ctx.atendimento = { fonteApi: true, conversas, agora: AGORA, regras: [REGRA_ENCERRAMENTO] }
  ctx.catalogo = { canais: [], cardapio: [], entregadores: [] }
}
const comoDemonstracao = (conversas = [conversaBase()]) => { comoApi(conversas); ctx.atendimento.fonteApi = false }
const nada = () => {}
const acoesDoModal = (extra) => ({ salvarAvaliacaoCliente: nada, salvarAutoavaliacao: nada, salvarAnotacaoFechamento: nada, cancelarPedido: nada, ...extra })

await confere('A  modal: o segundo toque em Encerrar não chama de novo, e o botão espera', async () => {
  comoApi()
  const espera = pendente()
  const idas = []
  ctx.acoes = acoesDoModal({ encerrarComResumo: (...args) => { idas.push(args); return espera.promise } })
  const tela = new Tela(ModalEncerrar, { conversaId: 'c1', aoFechar: nada })
  const toque = botao(tela, 'Encerrar')
  assert.equal(toque.props.disabled, false)
  const primeiro = toque.props.onClick()
  await turno()
  await toque.props.onClick()
  tela.renderizar()
  assert.equal(idas.length, 1, 'encerrarComResumo chamado mais de uma vez')
  assert.equal(botao(tela, 'Encerrando…').props.disabled, true)
  assert.deepEqual(idas[0][2], { enviarMensagem: true, textoMensagem: 'Obrigada, Ana!', avisoEmail: false, avisoSms: false, pedidoCancelado: false })
  espera.resolver(undefined); await primeiro; tela.encerrar()
})
await confere('A  modal: o erro aparece dentro dele e a nova tentativa não repete a despedida', async () => {
  comoApi()
  const idas = []
  let devolver = { erro: 'Atendimento não encerrado: Sem conexão com o EasyStok.', despedidaSaiu: true }
  ctx.acoes = acoesDoModal({ encerrarComResumo: async (...args) => { idas.push(args); return devolver } })
  const tela = new Tela(ModalEncerrar, { conversaId: 'c1', aoFechar: nada })
  await botao(tela, 'Encerrar').props.onClick()
  tela.renderizar()
  const rodape = doTipo(tela, 'Modal')[0].props.rodape
  assert.match(nos(rodape).filter((n) => n.props?.role === 'alert').map(texto).join(''),
    /Atendimento não encerrado: Sem conexão com o EasyStok\. A despedida já foi enviada: tentar de novo não manda outra\./)
  assert.equal(botao(tela, 'Encerrar').props.disabled, false, 'o botão volta para a nova tentativa')
  devolver = undefined
  await botao(tela, 'Encerrar').props.onClick()
  tela.renderizar()
  assert.equal(idas[1][2].enviarMensagem, false, 'a despedida que já saiu foi pedida de novo')
  assert.deepEqual(alertas(tela), [])
  tela.encerrar()
})
await confere('A  modal: promessa rejeitada também vira erro dentro dele e solta o botão', async () => {
  comoApi()
  ctx.acoes = acoesDoModal({ encerrarComResumo: () => Promise.reject(new Error('Conexão interrompida')) })
  const tela = new Tela(ModalEncerrar, { conversaId: 'c1', aoFechar: nada })
  await botao(tela, 'Encerrar').props.onClick()
  tela.renderizar()
  assert.deepEqual(alertas(tela), ['Conexão interrompida'])
  assert.equal(botao(tela, 'Encerrar').props.disabled, false)
  tela.encerrar()
})
// #1683: o cancelamento do pedido sem pagamento é escolha da atendente, numa caixa que nasce desmarcada.
const caixaDeCancelar = (tela) => nos(tela.arvore).find((n) => n.type === 'label' && texto(n).includes('Cancelar também o pedido'))
  ?.props.children.find?.((f) => f?.type === 'input')
const marcarCancelar = (tela) => { caixaDeCancelar(tela).props.onChange({ target: { checked: true } }); tela.renderizar() }
await confere('A  modal: pedido sem link em aberto nasce com a caixa de cancelar marcada, à vista', () => {
  // Sem link, nenhum job do EasyStok fecha o pedido depois que a conversa encerra.
  comoApi([conversaBase({ pedido: { pedidoId: 'p1', estado: 'aguardando', numero: 'P1ABCDEF', itens: [], cobranca: null } })])
  ctx.acoes = acoesDoModal({})
  const tela = new Tela(ModalEncerrar, { conversaId: 'c1', aoFechar: nada })
  assert.equal(caixaDeCancelar(tela).props.checked, true)
  tela.encerrar()
})
await confere('A  modal: com link em aberto a caixa de cancelar nasce desmarcada, e desmarcada não cancela', async () => {
  const comPedido = conversaBase({ pedido: { pedidoId: 'p1', estado: 'aguardando', numero: 'P1ABCDEF', itens: [], cobranca: { link: 'https://mp/x' } } })
  comoApi([comPedido])
  const ordem = []
  ctx.acoes = acoesDoModal({
    cancelarPedido: async () => { ordem.push('cancelar'); return { ok: true } },
    encerrarComResumo: async (_id, _agora, opcoes) => { ordem.push('encerrar'); assert.equal(opcoes.pedidoCancelado, false) },
  })
  const tela = new Tela(ModalEncerrar, { conversaId: 'c1', aoFechar: nada })
  const caixa = nos(tela.arvore).find((n) => n.type === 'label' && texto(n).includes('Cancelar também o pedido'))
  assert.ok(caixa, 'falta a caixa de cancelar o pedido')
  assert.equal(texto(caixa), 'Cancelar também o pedido Nº P1ABCDEF, que ainda não foi pago')
  assert.equal(caixaDeCancelar(tela).props.checked, false)
  assert.ok(!/será cancelado/.test(texto(tela.arvore)), 'a tela ainda promete cancelar sem perguntar')
  await botao(tela, 'Encerrar').props.onClick()
  assert.deepEqual(ordem, ['encerrar'])
  tela.encerrar()
})
await confere('A  modal: com a caixa marcada o pedido é cancelado antes, e o resultado segue para a ação', async () => {
  const comPedido = conversaBase({ pedido: { pedidoId: 'p1', estado: 'aguardando', numero: 'P1ABCDEF', itens: [], cobranca: null } })
  comoApi([comPedido])
  const ordem = []
  ctx.acoes = acoesDoModal({
    cancelarPedido: async () => { ordem.push('cancelar'); return { ok: true } },
    encerrarComResumo: async (_id, _agora, opcoes) => { ordem.push('encerrar'); assert.equal(opcoes.pedidoCancelado, true) },
  })
  const tela = new Tela(ModalEncerrar, { conversaId: 'c1', aoFechar: nada })
  marcarCancelar(tela)
  await botao(tela, 'Encerrar').props.onClick()
  assert.deepEqual(ordem, ['cancelar', 'encerrar'])
  tela.encerrar()
})
await confere('A  modal: cancelamento que falha não encerra, e o erro aparece dentro dele', async () => {
  const comPedido = conversaBase({ pedido: { pedidoId: 'p1', estado: 'aguardando', numero: 'P1ABCDEF', itens: [], cobranca: null } })
  comoApi([comPedido])
  const ordem = []
  let devolver = { erro: 'Sem conexão com o EasyStok.' }
  ctx.acoes = acoesDoModal({
    cancelarPedido: async () => { ordem.push('cancelar'); return devolver },
    encerrarComResumo: async (_id, _agora, opcoes) => { ordem.push('encerrar'); assert.equal(opcoes.pedidoCancelado, true) },
  })
  const tela = new Tela(ModalEncerrar, { conversaId: 'c1', aoFechar: nada })
  marcarCancelar(tela)
  await botao(tela, 'Encerrar').props.onClick()
  tela.renderizar()
  assert.deepEqual(ordem, ['cancelar'], 'encerrou mesmo com o cancelamento recusado')
  assert.deepEqual(alertas(tela), ['Não deu para cancelar o pedido Nº P1ABCDEF: Sem conexão com o EasyStok. O atendimento continua aberto. Tente de novo ou desmarque a caixa de cancelar.'])
  assert.equal(botao(tela, 'Encerrar').props.disabled, false, 'o botão volta para a nova tentativa')
  devolver = { ok: true }
  await botao(tela, 'Encerrar').props.onClick()
  tela.renderizar()
  assert.deepEqual(ordem, ['cancelar', 'cancelar', 'encerrar'])
  assert.deepEqual(alertas(tela), [])
  tela.encerrar()
})
await confere('A  modal: pedido já cancelado não é cancelado de novo quando só o encerramento falhou', async () => {
  const comPedido = conversaBase({ pedido: { pedidoId: 'p1', estado: 'aguardando', numero: 'P1ABCDEF', itens: [], cobranca: null } })
  comoApi([comPedido])
  const ordem = []
  let devolver = { erro: 'Atendimento não encerrado: Sem conexão com o EasyStok.' }
  ctx.acoes = acoesDoModal({
    cancelarPedido: async () => { ordem.push('cancelar'); return { ok: true } },
    encerrarComResumo: async (_id, _agora, opcoes) => { ordem.push('encerrar'); assert.equal(opcoes.pedidoCancelado, true); return devolver },
  })
  const tela = new Tela(ModalEncerrar, { conversaId: 'c1', aoFechar: nada })
  marcarCancelar(tela)
  await botao(tela, 'Encerrar').props.onClick()
  devolver = undefined
  await botao(tela, 'Encerrar').props.onClick()
  assert.deepEqual(ordem, ['cancelar', 'encerrar', 'encerrar'])
  tela.encerrar()
})
await confere('A  modal: conversa da API encerrada por fora não derruba a tela', () => {
  comoApi([conversaBase({ estado: 'Encerrado' })])
  ctx.acoes = acoesDoModal({ encerrarComResumo: nada })
  const tela = new Tela(ModalEncerrar, { conversaId: 'c1', aoFechar: nada })
  assert.equal(tela.arvore, null)
  tela.encerrar()
})
await confere('B  modal: no modo API a folha vai sem avaliação e sem anotação, na tela e na impressão', () => {
  comoApi()
  ctx.acoes = acoesDoModal({ encerrarComResumo: nada })
  const api = new Tela(ModalEncerrar, { conversaId: 'c1', aoFechar: nada })
  assert.deepEqual(doTipo(api, 'FolhaResumo').map((n) => n.props.comAvaliacaoEAnotacao), [false, false])
  api.encerrar()
  comoDemonstracao()
  const demo = new Tela(ModalEncerrar, { conversaId: 'c1', aoFechar: nada })
  assert.deepEqual(doTipo(demo, 'FolhaResumo').map((n) => n.props.comAvaliacaoEAnotacao), [true, true])
  demo.encerrar()
})
await confere('B  folha: sem avaliação e anotação não há chip, campo nem "Guardar também nas notas"', () => {
  const resumo = montarResumoAtendimento(conversaBase(), [], AGORA, {})
  const props = { nomeCliente: 'Ana Souza', canalNome: 'WhatsApp', telefone: null, resumo, editavel: true }
  const cheia = { arvore: FolhaResumo(props) }
  assert.equal(doTipo(cheia, 'GrupoAvaliacao').length, 2)
  assert.equal(doTipo(cheia, 'CampoArea').length, 1)
  assert.ok(texto(cheia.arvore).includes('Guardar também nas notas do cliente'))
  const enxuta = { arvore: FolhaResumo({ ...props, comAvaliacaoEAnotacao: false }) }
  assert.equal(doTipo(enxuta, 'GrupoAvaliacao').length, 0)
  assert.equal(doTipo(enxuta, 'CampoArea').length, 0)
  assert.equal(doTipo(enxuta, 'input').length, 0)
  assert.ok(!/Anotação|Guardar também/.test(texto(enxuta.arvore)))
  assert.ok(texto(enxuta.arvore).includes('Resumo de atendimento'), 'o resto da folha continua')
})
await confere('B  histórico: a aba "Atendimentos" some no modo API e fica na demonstração', () => {
  const abasDe = () => {
    const tela = new Tela(ModalHistorico, { conversa: conversaBase(), historico: [], agora: AGORA, aoFechar: nada })
    const ids = doTipo(tela, 'BarraDeAbas')[0].props.abas.map((a) => a.id)
    tela.encerrar()
    return ids
  }
  comoApi()
  assert.deepEqual(abasDe(), ['pedidos', 'notas', 'automaticas'])
  comoDemonstracao()
  assert.deepEqual(abasDe(), ['pedidos', 'atendimentos', 'notas', 'automaticas'])
})
await confere('A  modal na demonstração: ações síncronas seguem valendo, sem erro e sem espera presa', async () => {
  const comPedido = conversaBase({ pedido: { estado: 'aguardando', numero: '2026-0001', itens: [], cobranca: null } })
  comoDemonstracao([comPedido])
  const ordem = []
  ctx.acoes = acoesDoModal({ cancelarPedido: () => { ordem.push('cancelar') }, encerrarComResumo: () => { ordem.push('encerrar') } })
  const tela = new Tela(ModalEncerrar, { conversaId: 'c1', aoFechar: nada })
  marcarCancelar(tela)
  await botao(tela, 'Encerrar').props.onClick()
  tela.renderizar()
  assert.deepEqual(ordem, ['cancelar', 'encerrar'])
  assert.deepEqual(alertas(tela), [])
  assert.equal(botao(tela, 'Encerrar').props.disabled, false)
  tela.encerrar()
})

const editar = (tela, valor) => {
  botao(tela, 'Editar Telefone').props.onClick()
  tela.renderizar()
  doTipo(tela, 'input')[0].props.onChange({ target: { value: valor } })
  tela.renderizar()
}
await confere('C  campo em linha: "Salvando…" até a resposta, sem "Salvo" antes e sem segundo envio', async () => {
  const espera = pendente()
  const idas = []
  const tela = new Tela(CampoEmLinha, { rotulo: 'Telefone', valor: '(11) 90000-0000', aoSalvar: (v) => { idas.push(v); return espera.promise } })
  editar(tela, '(11) 91111-2222')
  botao(tela, 'Salvar').props.onClick()
  doTipo(tela, 'input')[0].props.onBlur()
  doTipo(tela, 'input')[0].props.onKeyDown({ key: 'Enter' })
  tela.renderizar()
  assert.deepEqual(idas, ['(11) 91111-2222'])
  assert.ok(texto(tela.arvore).includes('Salvando…'))
  assert.ok(!texto(tela.arvore).includes('Salvo'))
  assert.equal(doTipo(tela, 'input')[0].props.readOnly, true)
  espera.resolver(undefined); await turno(); tela.renderizar()
  assert.equal(doTipo(tela, 'input').length, 0)
  assert.ok(texto(tela.arvore).includes('Salvo'))
  tela.encerrar()
})
await confere('C  campo em linha: na recusa o erro fica no campo, com o texto digitado', async () => {
  for (const [recusa, esperado] of [
    [Promise.resolve({ erro: 'Este telefone já é de outro cadastro.' }), 'Este telefone já é de outro cadastro.'],
    [Promise.resolve(false), 'Não foi possível salvar. Tente de novo.'],
    [Promise.reject(new Error('Sem conexão com o EasyStok.')), 'Sem conexão com o EasyStok.'],
  ]) {
    const tela = new Tela(CampoEmLinha, { rotulo: 'Telefone', valor: '(11) 90000-0000', aoSalvar: () => recusa })
    editar(tela, '(11) 91111-2222')
    botao(tela, 'Salvar').props.onClick()
    await turno(); tela.renderizar()
    assert.deepEqual(alertas(tela), [esperado])
    assert.equal(doTipo(tela, 'input')[0].props.value, '(11) 91111-2222')
    assert.ok(!texto(tela.arvore).includes('Salvo') && !texto(tela.arvore).includes('Salvando…'))
    tela.encerrar()
  }
})
await confere('C  campo em linha na demonstração: retorno que não é promessa vale como salvo', () => {
  const idas = []
  const tela = new Tela(CampoEmLinha, { rotulo: 'Telefone', valor: '(11) 90000-0000', aoSalvar: (v) => { idas.push(v) } })
  editar(tela, '(11) 91111-2222')
  botao(tela, 'Salvar').props.onClick()
  tela.renderizar()
  assert.deepEqual(idas, ['(11) 91111-2222'])
  assert.equal(doTipo(tela, 'input').length, 0)
  assert.ok(texto(tela.arvore).includes('Salvo'))
  tela.encerrar()
})
await confere('C  "Confirmar e cadastrar": espera, trava o segundo toque e mostra a recusa', async () => {
  const espera = pendente()
  let idas = 0
  const tela = new Tela(BlocoEnderecoCapturado, { endereco: 'Rua X, 1, Centro, 01001-000', situacaoArea: null, aoConfirmar: () => { idas += 1; return espera.promise }, aoDecidirArea: nada })
  const ida = botao(tela, 'Confirmar e cadastrar').props.onClick()
  botao(tela, 'Confirmar e cadastrar').props.onClick()
  tela.renderizar()
  assert.equal(idas, 1)
  assert.equal(botao(tela, 'Cadastrando…').props.disabled, true)
  espera.resolver({ erro: 'Informe o telefone do cliente para cadastrar.' }); await ida; tela.renderizar()
  assert.deepEqual(alertas(tela), ['Informe o telefone do cliente para cadastrar.'])
  assert.equal(botao(tela, 'Confirmar e cadastrar').props.disabled, false)
  tela.encerrar()
})
await confere('C  "Salvar cadastro" espera a resposta, trava o segundo toque e mostra a recusa', () => {
  const fonte = readFileSync(new URL('features/ficha-cliente/BlocoCliente.jsx', src), 'utf8')
  assert.match(fonte, /async function aoSalvarCadastro\(\) \{\s+if \(cadastrando\.current\) return\s+cadastrando\.current = true/)
  assert.match(fonte, /const resultado = await salvarCadastroRapido\(/)
  assert.match(fonte, /disabled=\{!podeSalvarCadastro \|\| cadastro\.salvando\}/)
  assert.match(fonte, /\{salvandoCadastro \? 'Salvando…' : 'Salvar cadastro'\}/)
  assert.match(fonte, /\{erroCadastro && <p className=\{css\.erroEmLinha\} role="alert">\{erroCadastro\}<\/p>\}/)
})

const pedidoComLink = (extra = {}) => ({
  estado: 'aguardando', statusApi: 'aguardando_pagamento', pedidoId: 'p1', numero: 'P1ABCDEF', itens: [{ sku: 'massa', qtd: 1 }], meio: 'pix',
  cobranca: { id: 'cb', meio: 'pix', valor: 50, link: 'https://mp/x', criadaEm: AGORA - 60000, expiraEm: AGORA + 600000, tentativa: 1 },
  totalApi: 50, totalPagoApi: 0, ...extra,
})
const propsDaBarra = (pedido, extras = {}) => ({ pedido, conversaId: 'c1', nomeCliente: 'Ana', agora: AGORA, ...extras })

await confere('D  "Marcar pago" só fecha o formulário no sucesso e devolve a recusa a ele', async () => {
  comoApi()
  let devolver = { erro: 'Pagamento não registrado: Falha ao gravar' }
  const tela = new Tela(BarraProximoPasso, propsDaBarra(pedidoComLink(), { aoConfirmarPagamento: async () => devolver }))
  botao(tela, 'Marcar pago').props.onClick()
  tela.renderizar()
  const recusado = await doTipo(tela, 'BaixaAMao')[0].props.aoConfirmar(50, 'pix')
  tela.renderizar()
  assert.equal(recusado.erro, 'Pagamento não registrado: Falha ao gravar')
  assert.equal(doTipo(tela, 'BaixaAMao').length, 1, 'o formulário fechou na recusa')
  devolver = undefined
  await doTipo(tela, 'BaixaAMao')[0].props.aoConfirmar(50, 'pix')
  tela.renderizar()
  assert.equal(doTipo(tela, 'BaixaAMao').length, 0)
  tela.encerrar()
})
await confere('D  formulário da baixa mostra a recusa e guarda valor e método', async () => {
  let devolver = { erro: 'Pagamento não registrado: Falha ao gravar' }
  const idas = []
  const tela = new Tela(BaixaAMao, { valorCobrado: 50, pedido: pedidoComLink(), aoCancelar: nada, aoConfirmar: async (...args) => { idas.push(args); return devolver } })
  // #1683: a baixa é formulário (Enter registra); o botão é o envio dele.
  const enviar = () => nos(tela.arvore).find((n) => n.type === 'form').props.onSubmit({ preventDefault: nada })
  assert.equal(botao(tela, 'Marcar pago').props.tipo, 'submit')
  await enviar()
  tela.renderizar()
  assert.deepEqual(alertas(tela), ['Pagamento não registrado: Falha ao gravar'])
  assert.equal(doTipo(tela, 'CampoSelecao')[0].props.value, 'pix')
  assert.equal(botao(tela, 'Marcar pago').props.disabled, false)
  devolver = undefined
  await enviar()
  tela.renderizar()
  assert.deepEqual(idas, [[50, 'pix'], [50, 'pix']])
  assert.deepEqual(alertas(tela), [])
  tela.encerrar()
})
await confere('D  "Desfazer pagamento" espera, trava o segundo toque e só fecha no sucesso', async () => {
  comoApi()
  const pago = pedidoComLink({
    estado: 'pago', statusApi: 'aguardando', totalPagoApi: 50,
    pagamentosApi: [{ valor: 50, coberto: 50, em: AGORA - 1000, meio: 'dinheiro', pagamentoId: 'pg1' }],
    cobranca: { id: 'cb', meio: 'maquininha', manual: true, valor: 50, link: null, criadaEm: AGORA - 60000, expiraEm: null, pagaEm: AGORA - 1000, valorPago: 50 },
  })
  const espera = pendente()
  const idas = []
  const tela = new Tela(BarraProximoPasso, propsDaBarra(pago, { aoDesfazerPagamento: (motivo) => { idas.push(motivo); return idas.length === 1 ? espera.promise : Promise.resolve(undefined) } }))
  const menu = doTipo(tela, 'MenuDoPedido')[0]
  assert.equal(menu.props.podeDesfazerPagamento, true)
  menu.props.aoEscolher('desfazer')
  tela.renderizar()
  const campo = () => doTipo(tela, 'CampoArea').find((n) => n.props.rotulo === 'Motivo')
  campo().props.onChange({ target: { value: 'Lançamento errado' } })
  tela.renderizar()
  const enviar = () => nos(tela.arvore).find((n) => n.type === 'form').props.onSubmit({ preventDefault: nada })
  assert.equal(botao(tela, 'Desfazer pagamento').props.tipo, 'submit')
  const ida = enviar()
  enviar()
  tela.renderizar()
  assert.deepEqual(idas, ['Lançamento errado'])
  assert.equal(botao(tela, 'Desfazendo…').props.disabled, true)
  espera.resolver({ erro: 'Pagamento não desfeito: Falha ao gravar' }); await ida; tela.renderizar()
  assert.deepEqual(alertas(tela), ['Pagamento não desfeito: Falha ao gravar'])
  assert.equal(campo().props.value, 'Lançamento errado', 'o motivo digitado se perdeu')
  await enviar()
  tela.renderizar()
  assert.equal(campo(), undefined, 'a confirmação continua aberta depois do sucesso')
  tela.encerrar()
})
await confere('F  link vencido: no modo API o texto substitui "Reenviar cobrança"; na demonstração o botão fica', () => {
  const vencido = pedidoComLink({ cobranca: { ...pedidoComLink().cobranca, expiraEm: AGORA - 1000 } })
  comoApi()
  const api = new Tela(BarraProximoPasso, propsDaBarra(vencido, { aoReenviarPix: nada }))
  assert.equal(botao(api, 'Reenviar cobrança'), undefined)
  assert.ok(texto(api.arvore).includes('Cobrança venceu'))
  assert.ok(texto(api.arvore).includes(cobrancaDom.avisoDeLinkVencidoApi(vencido.cobranca, vencido.statusApi)))
  assert.ok(botao(api, 'Marcar pago'), '#1683: com o link vencido a baixa à mão continua na barra')
  api.encerrar()
  comoDemonstracao()
  const demo = new Tela(BarraProximoPasso, propsDaBarra({ ...vencido, pedidoId: null, statusApi: undefined }, { aoReenviarPix: nada }))
  assert.ok(botao(demo, 'Reenviar cobrança'))
  assert.ok(!texto(demo.arvore).includes('manda um link novo'))
  demo.encerrar()
})

if (falhas.length > 0) {
  console.error(`\n${passou} ok, ${falhas.length} falha(s)`)
  process.exit(1)
}
console.log(`\n${passou} ok, 0 falha(s)`)
