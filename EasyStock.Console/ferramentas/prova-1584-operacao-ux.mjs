/* eslint-disable no-console */
// Prova da #1584 (banca de navegação e UX de 2026-10-11, operação):
//   A. Hoje/Amanhã acompanha a virada do dia: a Cozinha volta sozinha para Hoje, e Entregas
//      tira hoje e amanhã do relógio que anda (o chip "Hoje" não abre mais ontem);
//   B. trocar Entrada/Saída no lançamento do caixa tira a categoria sugerida do outro tipo;
//   C. "Alterar agendamento" mora fora da lista: o modal fica quando o pedido muda de janela;
//   D. o erro da cozinha aparece dentro da comanda, e "Reimprimir na fila" dá retorno;
//   E. perda de lote, desfazer perda e remover bloqueio só executam depois da confirmação em
//      linha; "Desfazer" some para quem não tem a ação de Gerente na produção.
// As telas rodam compiladas, com `useState` e as fontes de dados controlados; pixel, foco e
// gesto exigem navegador. Cada catraca foi provada por mutação (desfazer a correção reprova).
//
//   node ferramentas/prova-1584-operacao-ux.mjs

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { transformWithOxc } from 'vite'

const fontes = new Map()
for (const caminho of [
  'features/cozinha/TelaCozinhaApi', 'features/cozinha/ComandaKds', 'features/entregas/EntregasDoDia',
  'features/entregas/CadastroEntregaApi', 'features/gestao/caixa/AbaCaixaApi', 'features/gestao/producao/AbaPerdasApi',
]) {
  const url = new URL(`../src/${caminho}.jsx`, import.meta.url)
  fontes.set(url.href, (await transformWithOxc(readFileSync(url, 'utf8'), url.pathname,
    { jsx: { runtime: 'automatic' } })).code)
}

// Estado de cada tela montada: os valores ficam no hospedeiro, na ordem das chamadas.
let host
const hooks = {
  useState(valor) {
    const atual = host, i = atual.i++
    if (!(i in atual.valores)) atual.valores[i] = typeof valor === 'function' ? valor() : valor
    return [atual.valores[i], (novo) => { atual.valores[i] = typeof novo === 'function' ? novo(atual.valores[i]) : novo }]
  },
  useRef(valor) { return host.valores[host.i++] ??= { current: valor } },
  useId() { return host.valores[host.i++] ??= `prova-${host.i}` },
  useEffect() { host.i++ },
}
const g = globalThis.__prova1584 = { hooks, agora: 0, cozinha: { api: null, args: null }, perdas: null, cadastro: null, caixa: null, acoes: null, acesso: { acoes: {} } }
globalThis.requestAnimationFrame = (callback) => callback()

const inline = (source) => ({ format: 'module', shortCircuit: true, source })
const DE_FORA = 'globalThis.__prova1584'
// Componente que não está sob prova vira o próprio nome: a árvore mostra quem foi desenhado.
const nomesExportados = (fonte) => [...fonte.matchAll(/^export\s+(?:async\s+)?(?:function|const)\s+([A-Za-z_$][\w$]*)/gm)].map((m) => m[1])
registerHooks({
  resolve(spec, ctx, next) {
    if (spec === 'react') return { url: `data:text/javascript,export const {useState,useRef,useEffect,useId}=${DE_FORA}.hooks`, shortCircuit: true }
    if (spec === 'react/jsx-runtime') return { url: 'data:text/javascript,export const jsx=(type,props)=>({type,props});export const jsxs=jsx;export const Fragment="fragment"', shortCircuit: true }
    if (spec === '@dnd-kit/core') return { url: 'data:text/javascript,' + encodeURIComponent(`
      export const DndContext='dnd',DragOverlay='overlay',KeyboardSensor='keyboard',MouseSensor='mouse',TouchSensor='touch';
      export const useSensor=(sensor,options)=>({sensor,options}),useSensors=(...s)=>s;
      export const useDroppable=()=>({setNodeRef(){},isOver:false});
      export const pointerWithin=()=>[],rectIntersection=()=>[];`), shortCircuit: true }
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      for (const ext of ['.js', '.jsx']) { try { return next(spec + ext, ctx) } catch { /* próxima extensão */ } }
    }
    return next(spec, ctx)
  },
  load(url, ctx, next) {
    if (fontes.has(url)) return inline(fontes.get(url))
    if (url.endsWith('/infra/fonteDados.js')) return inline("export const FONTE_API = true\nexport const API_BASE = ''\n")
    if (url.endsWith('/hooks/useRelogio.js')) return inline(`export const useRelogio=()=>${DE_FORA}.agora`)
    if (url.endsWith('/aplicacao/useCozinhaApi.js')) return inline(`export const useCozinhaApi=(a)=>{${DE_FORA}.cozinha.args=a;return ${DE_FORA}.cozinha.api}`)
    if (url.endsWith('/aplicacao/usePerdasApi.js')) return inline(`export const usePerdasApi=()=>${DE_FORA}.perdas`)
    if (url.endsWith('/aplicacao/useCadastroEntregaApi.js')) return inline(`export const useCadastroEntregaApi=()=>${DE_FORA}.cadastro`)
    // #2070: a pendência dos formulários de entrega vai ao contexto da visita, que esta prova não monta.
    if (url.endsWith('/aplicacao/rascunhosDosAjustes.js')) return inline('export const usePendenciaDoAjuste=()=>{}')
    if (url.endsWith('/aplicacao/useCaixaDoDiaApi.js')) return inline(`export const useCaixaDoDiaApi=()=>${DE_FORA}.caixa`)
    if (url.endsWith('/aplicacao/acessoModulos.jsx')) return inline(`export const useAcessoModulos=()=>({permite:()=>true,...${DE_FORA}.acesso})`)
    if (url.endsWith('/aplicacao/contextos.js')) return inline(`export const useAcoes=()=>${DE_FORA}.acoes;export const useAtendimento=()=>({agora:${DE_FORA}.agora})`)
    if (url.endsWith('.css')) return inline('export default new Proxy({},{get:(_,p)=>p})')
    if (url.endsWith('.json')) return inline('export default ' + readFileSync(new URL(url), 'utf8'))
    if (url.endsWith('.jsx')) return inline(nomesExportados(readFileSync(new URL(url), 'utf8')).map((n) => `export const ${n}='${n}'`).join('\n'))
    return next(url, ctx)
  },
})

const kds = await import('../src/dominio/kds.js')
const caixa = await import('../src/dominio/caixa.js')
const { TelaCozinhaApi } = await import('../src/features/cozinha/TelaCozinhaApi.jsx')
const { ComandaKds } = await import('../src/features/cozinha/ComandaKds.jsx')
const { EntregasDoDia } = await import('../src/features/entregas/EntregasDoDia.jsx')
const { CadastroEntregaApi } = await import('../src/features/entregas/CadastroEntregaApi.jsx')
const { AbaCaixaApi } = await import('../src/features/gestao/caixa/AbaCaixaApi.jsx')
const { AbaPerdasApi } = await import('../src/features/gestao/producao/AbaPerdasApi.jsx')

function montar(componente, props = {}) {
  const meu = { i: 0, valores: [] }
  return { props, renderizar() { host = meu; meu.i = 0; return componente(this.props) } }
}
const filhos = (el) => [el.props?.children, el.props?.rodape, el.props?.acao].flat(Infinity)
const andar = (el, fn) => (!el || typeof el !== 'object' ? []
  : [...(fn(el) ? [el] : []), ...filhos(el).flatMap((c) => andar(c, fn))])
const texto = (el) => (el == null || typeof el === 'boolean' ? '' : typeof el !== 'object' ? String(el)
  : Array.isArray(el) ? el.map(texto).join('') : texto(el.props?.children))
const doTipo = (arvore, tipo) => andar(arvore, (x) => x.type === tipo || (typeof x.type === 'function' && x.type.name === tipo))
const comRotulo = (arvore, tipo, rotulo) => doTipo(arvore, tipo).find((x) => texto(x) === rotulo)
const botao = (arvore, rotulo) => comRotulo(arvore, 'Botao', rotulo)
const chip = (arvore, rotulo) => comRotulo(arvore, 'Chip', rotulo)

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

// O relógio da prova fica longe do dia em que ela roda: tela que voltar a ler `Date.now()`
// por conta própria não acerta estas datas por acaso.
const VESPERA = Date.parse('2026-03-15T02:50:00Z') // 23:50 de 14/03 na loja; em UTC já é dia 15
const VIRADA = Date.parse('2026-03-15T03:00:20Z') // 00:00:20 de 15/03 na loja

// --- A. Virada do dia ---------------------------------------------------------------------
await confere('fila da cozinha: sem escolher Amanhã, é hoje e a API não recebe data', () => {
  assert.deepEqual(kds.diaDaFilaDaCozinha(null, VESPERA), { amanha: false, data: null })
})
await confere('fila da cozinha: Amanhã vale no dia em que foi escolhido e manda o dia seguinte da loja', () => {
  assert.deepEqual(kds.diaDaFilaDaCozinha('2026-03-14', VESPERA), { amanha: true, data: '2026-03-15' })
  assert.deepEqual(kds.diaDaFilaDaCozinha('2026-03-14', Date.parse('2026-03-14T12:00:00Z')), { amanha: true, data: '2026-03-15' })
})
await confere('fila da cozinha: virou o dia da loja, volta para Hoje (não vira depois de amanhã)', () => {
  assert.deepEqual(kds.diaDaFilaDaCozinha('2026-03-14', VIRADA), { amanha: false, data: null })
  assert.deepEqual(kds.diaDaFilaDaCozinha('2026-03-15', VIRADA), { amanha: true, data: '2026-03-16' })
})
await confere('fila da cozinha: o dia é o da loja em qualquer fuso do aparelho', () => {
  const original = process.env.TZ
  const fusos = ['UTC', 'America/Sao_Paulo', 'Asia/Tokyo']
  const emCadaFuso = (fn) => fusos.map((fuso) => { process.env.TZ = fuso; return fn() })
  const doAparelho = emCadaFuso(() => new Date(2026, 2, 14, 23, 50).toISOString())
  assert.equal(new Set(doAparelho).size, fusos.length, `fuso do aparelho não mudou: ${doAparelho.join(' ')}`)
  assert.deepEqual(emCadaFuso(() => kds.diaDaFilaDaCozinha('2026-03-14', VESPERA)), fusos.map(() => ({ amanha: true, data: '2026-03-15' })))
  assert.deepEqual(emCadaFuso(() => kds.diaDaFilaDaCozinha('2026-03-14', VIRADA)), fusos.map(() => ({ amanha: false, data: null })))
  if (original === undefined) delete process.env.TZ
  else process.env.TZ = original
})

const linhas = { casa: { rotulo: 'Preparar em casa' }, servir: { rotulo: 'Para servir' } }
const pedidoKds = { id: 'p1', numeroCurto: '0011', clienteNome: 'Ana', status: 'aguardando', statusRotulo: 'Aguardando', itens: [], atrasado: false, janela: null }
const novaCozinha = (extra = {}) => {
  g.cozinha.api = {
    pedidos: [], erro: null, aoVivo: true, movendo: new Set(), novos: new Set(), mudaram: new Set(), saindo: [], impressoes: [],
    avancar: () => Promise.resolve(), recarregar() {}, reimprimir: () => Promise.resolve(), confirmarImpressao() {}, avisar() {}, limparErro() {},
    ...extra,
  }
  return montar(TelaCozinhaApi, { linhas })
}

await confere('Cozinha: com a tela aberta em Amanhã, a virada devolve a fila de hoje sozinha', () => {
  g.agora = VESPERA
  const t = novaCozinha()
  let el = t.renderizar()
  assert.equal(g.cozinha.args.data, null)
  assert.equal(chip(el, 'Hoje').props.ativo, true)
  chip(el, 'Amanhã').props.onClick()
  el = t.renderizar()
  assert.equal(g.cozinha.args.data, '2026-03-15')
  assert.equal(chip(el, 'Amanhã').props.ativo, true)
  assert.match(texto(el), /Nenhum pedido pago para amanhã ainda\./)

  g.agora = VIRADA
  el = t.renderizar()
  assert.equal(g.cozinha.args.data, null, 'a fila continuou pedindo outro dia depois da virada')
  assert.equal(chip(el, 'Hoje').props.ativo, true)
  assert.equal(chip(el, 'Amanhã').props.ativo, false)
  assert.match(texto(el), /Cozinha em dia hoje\./)

  chip(el, 'Amanhã').props.onClick()
  el = t.renderizar()
  assert.equal(g.cozinha.args.data, '2026-03-16')
  chip(el, 'Hoje').props.onClick()
  t.renderizar()
  assert.equal(g.cozinha.args.data, null)
})

const pedidoDoDia = { id: 'e1', numero: '0042', cliente: 'Bia', apto: null, rotulo: 'Agendado', etapa: 'agendado', status: 'aguardando', endereco: 'Rua A, 10', pago: true, entregador: null, falta: 'Falta preparar', podePorNaViagem: false }
const grupo = (chave, faixa, pedidos) => ({ chave, faixa, label: faixa, capacidade: 4, bloqueio: null, quemLeva: [], pedidos })
const roteiroCom = (grupos) => ({ data: '2026-03-14', bloqueioDoDia: null, grupos })
const novasEntregas = (extra = {}) => {
  const mudancas = []
  const props = {
    dia: '2026-03-14', mudarDia: (d) => mudancas.push(d), roteiro: roteiroCom([grupo('almoco', '12h às 14h', [pedidoDoDia])]),
    montando: [], ocupado: false, acoes: { recarregar() {}, incluirParada() {} }, aoAbrirCadastro() {}, aoVerViagens() {}, ...extra,
  }
  return { t: montar(EntregasDoDia, props), mudancas }
}
const escolhaDoDia = (t) => { const e = doTipo(t.renderizar(), 'EscolhaDoDia')[0]; return e.type(e.props) }

await confere('Entregas: hoje e amanhã saem do relógio que anda; depois da virada "Hoje" não abre ontem', () => {
  g.agora = VESPERA
  const { t, mudancas } = novasEntregas()
  let el = escolhaDoDia(t)
  assert.equal(chip(el, 'Hoje').props.ativo, true)
  assert.equal(chip(el, 'Amanhã').props.ativo, false)
  chip(el, 'Hoje').props.onClick(); chip(el, 'Amanhã').props.onClick()
  assert.deepEqual(mudancas.splice(0), ['2026-03-14', '2026-03-15'])

  g.agora = VIRADA // a tela segue montada, mostrando o roteiro de ontem
  el = escolhaDoDia(t)
  assert.equal(chip(el, 'Hoje').props.ativo, false, 'o roteiro de ontem ficou marcado como Hoje')
  chip(el, 'Hoje').props.onClick(); chip(el, 'Amanhã').props.onClick()
  assert.deepEqual(mudancas.splice(0), ['2026-03-15', '2026-03-16'])

  t.props.dia = '2026-03-15' // a recarga seguinte leva o roteiro para o dia novo
  el = escolhaDoDia(t)
  assert.equal(chip(el, 'Hoje').props.ativo, true)
  assert.equal(chip(el, 'Amanhã').props.ativo, false, 'o dia novo ficou marcado como Amanhã')
})

// --- B. Categoria do caixa ----------------------------------------------------------------
const { ENTRADA, SAIDA } = caixa.TIPOS_MOVIMENTO
await confere('caixa: a categoria sugerida de um tipo sai ao trocar para o outro', () => {
  assert.ok(caixa.CATEGORIAS_SAIDA_SUGERIDAS.length > 0 && caixa.CATEGORIAS_ENTRADA_SUGERIDAS.length > 0)
  for (const sugerida of caixa.CATEGORIAS_SAIDA_SUGERIDAS) assert.equal(caixa.categoriaAoTrocarTipo(sugerida, ENTRADA), '', sugerida)
  for (const sugerida of caixa.CATEGORIAS_ENTRADA_SUGERIDAS) assert.equal(caixa.categoriaAoTrocarTipo(sugerida, SAIDA), '', sugerida)
})
await confere('caixa: a sugerida do próprio tipo e a categoria digitada ficam', () => {
  assert.equal(caixa.categoriaAoTrocarTipo('Sangria', SAIDA), 'Sangria')
  assert.equal(caixa.categoriaAoTrocarTipo('Suprimento', ENTRADA), 'Suprimento')
  assert.equal(caixa.categoriaAoTrocarTipo('Gás', ENTRADA), 'Gás')
  assert.equal(caixa.categoriaAoTrocarTipo('Gás', SAIDA), 'Gás')
  assert.equal(caixa.categoriaAoTrocarTipo('', ENTRADA), '')
})

await confere('Lançamento: Sangria e depois Entrada não grava entrada rotulada Sangria', async () => {
  const lancados = []
  g.agora = VESPERA
  g.acesso = { acoes: { gerenciarCaixa: true } }
  g.acoes = { lancarMovimentoCaixa: async (_agora, corpo) => { lancados.push(corpo) }, fecharCaixa() {}, abrirCaixa() {}, estornarMovimentoCaixa() {} }
  g.caixa = {
    estado: 'ok', erro: null, fechamentos: [], recarregar: async () => {},
    dia: { aberto: true, fechado: false, esquecidoAberto: false, saldoInicial: 100, movimentos: [], linhasExtras: [], totalEntradasExtras: 0, totalSaidasExtras: 0, totalPagamentosPedidos: 0, saldoEsperado: 100 },
  }
  const aba = montar(AbaCaixaApi)
  botao(aba.renderizar(), 'Lançamento').props.onClick()
  // #2078: o formulário passou a morar junto do seu modal (`ModalLancamento`), para segurar o Esc.
  const elForm = doTipo(aba.renderizar(), 'ModalLancamento')[0]
  const form = montar(elForm.type, elForm.props)
  const categoria = (el) => doTipo(el, 'CampoTexto').find((c) => c.props.rotulo === 'Categoria')
  const enviar = (el) => doTipo(el, 'Botao').find((b) => b.props.tipo === 'submit')

  let el = form.renderizar()
  assert.equal(chip(el, 'Saída').props.ativo, true)
  chip(el, 'Sangria').props.onClick()
  el = form.renderizar()
  assert.equal(categoria(el).props.value, 'Sangria')
  chip(el, 'Entrada').props.onClick()
  el = form.renderizar()
  assert.equal(categoria(el).props.value, '', 'a categoria da saída ficou na entrada')
  assert.equal(chip(el, 'Sangria'), undefined)

  doTipo(el, 'CampoDinheiro')[0].props.aoMudar(5000)
  el = form.renderizar()
  assert.equal(enviar(el).props.disabled, true, 'entrada sem categoria não pode ser lançada')
  chip(el, 'Suprimento').props.onClick()
  el = form.renderizar()
  await doTipo(el, 'form')[0].props.onSubmit({ preventDefault() {} })
  assert.deepEqual(lancados, [{ tipoMovimento: ENTRADA, categoria: 'Suprimento', valor: 50, meio: 'dinheiro', descricao: '' }])

  // A categoria digitada é da dona: acompanha a troca nos dois sentidos.
  categoria(form.renderizar()).props.onChange({ target: { value: 'Gás' } })
  chip(form.renderizar(), 'Saída').props.onClick()
  assert.equal(categoria(form.renderizar()).props.value, 'Gás')
  chip(form.renderizar(), 'Entrada').props.onClick()
  assert.equal(categoria(form.renderizar()).props.value, 'Gás')
})

// --- C. Alterar agendamento fora da lista -------------------------------------------------
await confere('Entregas: o modal de reagendamento sobrevive ao pedido mudar de janela, sair do dia e à recarga', () => {
  g.agora = VESPERA
  const recarregar = () => {}
  const { t } = novasEntregas({ acoes: { recarregar, incluirParada() {} } })
  const modal = () => doTipo(t.renderizar(), 'ModalAlterarAgendamentoApi')[0]
  const itemDaLista = () => {
    const janela = doTipo(t.renderizar(), 'JanelaDoDia')[0]
    const item = doTipo(janela.type(janela.props), 'PedidoDoDia')[0]
    return item.type(item.props)
  }
  assert.equal(modal(), undefined)
  botao(itemDaLista(), 'Alterar agendamento').props.onClick()
  assert.equal(doTipo(itemDaLista(), 'ModalAlterarAgendamentoApi').length, 0, 'o modal continua dentro do item da lista')
  assert.deepEqual({ pedidoId: modal().props.pedidoId, nome: modal().props.nome }, { pedidoId: 'e1', nome: 'Bia' })
  assert.equal(modal().props.aoAlterado, recarregar)

  t.props.roteiro = roteiroCom([grupo('almoco', '12h às 14h', []), grupo('jantar', '18h às 20h', [pedidoDoDia])])
  assert.equal(modal()?.props.pedidoId, 'e1', 'o modal sumiu quando o pedido mudou de janela')
  t.props.roteiro = roteiroCom([grupo('almoco', '12h às 14h', [])])
  assert.equal(modal()?.props.pedidoId, 'e1', 'o modal sumiu quando o pedido saiu do dia')
  t.props.roteiro = null
  assert.equal(modal()?.props.nome, 'Bia', 'o modal sumiu durante a recarga do dia')

  modal().props.aoFechar()
  assert.equal(modal(), undefined)
})

// --- D. Comanda da cozinha ----------------------------------------------------------------
const alertas = (el) => andar(el, (x) => x.props?.role === 'alert').map(texto)
const confirmacoes = (el) => doTipo(el, 'output').map(texto)

await confere('Cozinha: o erro da tela vai para dentro da comanda aberta', () => {
  g.agora = VESPERA
  const t = novaCozinha({ pedidos: [pedidoKds] })
  doTipo(t.renderizar(), 'CartaoKds')[0].props.aoAbrir('p1')
  let comanda = doTipo(t.renderizar(), 'ComandaKds')[0]
  assert.equal(comanda.props.erro, null)
  assert.equal(comanda.props.aoLimparErro, g.cozinha.api.limparErro)
  g.cozinha.api.erro = 'O pedido não mudou: HTTP 409'
  comanda = doTipo(t.renderizar(), 'ComandaKds')[0]
  assert.equal(comanda.props.erro, 'O pedido não mudou: HTTP 409')
})

const novaComanda = (extra = {}) => montar(ComandaKds, {
  pedido: pedidoKds, agora: VESPERA, linhas, movendo: false, erro: null,
  aoAvancar() {}, aoImprimir() {}, aoReimprimir: () => Promise.resolve(), aoLimparErro() {}, aoFechar() {}, ...extra,
})

await confere('Comanda: o erro é desenhado dentro do modal, como alerta', () => {
  const c = novaComanda({ erro: 'O pedido não mudou: HTTP 409' })
  const el = c.renderizar()
  assert.equal(el.type, 'Modal')
  assert.equal(alertas(el).length, 1)
  assert.match(alertas(el)[0], /O pedido não mudou: HTTP 409/)
  c.props.erro = null
  assert.deepEqual(alertas(c.renderizar()), [])
})

await confere('Comanda: "Reimprimir na fila" limpa o erro antigo, trava o botão e confirma no fim', async () => {
  const ordem = []
  let soltar
  const c = novaComanda({
    aoLimparErro: () => ordem.push('limpou'),
    aoReimprimir: (id) => { ordem.push(`reimprimiu ${id}`); return new Promise((ok) => { soltar = ok }) },
  })
  let el = c.renderizar()
  assert.deepEqual(confirmacoes(el), [])
  const pronto = botao(el, 'Reimprimir na fila').props.onClick()
  el = c.renderizar()
  assert.deepEqual(ordem, ['limpou', 'reimprimiu p1'])
  assert.equal(botao(el, 'Reenviando…').props.disabled, true)
  assert.deepEqual(confirmacoes(el), [], 'confirmou antes de a fila responder')
  soltar()
  await pronto
  el = c.renderizar()
  assert.equal(botao(el, 'Reimprimir na fila').props.disabled, false)
  assert.equal(confirmacoes(el).length, 1)
  assert.match(confirmacoes(el)[0], /Canhoto de volta na fila de impressão\./)
})

await confere('Comanda: reimpressão que falha mostra o erro e não confirma', async () => {
  // A ação da cozinha guarda a própria falha no erro da tela e resolve mesmo assim.
  const c = novaComanda()
  c.props.aoReimprimir = async () => { c.props.erro = 'Não voltou para a fila de impressão: HTTP 500' }
  await botao(c.renderizar(), 'Reimprimir na fila').props.onClick()
  const el = c.renderizar()
  assert.deepEqual(confirmacoes(el), [])
  assert.match(alertas(el)[0], /Não voltou para a fila de impressão: HTTP 500/)
})

// --- E. Confirmação em linha --------------------------------------------------------------
const lote = { itemEstoqueId: 'l9', produtoId: 'pr1', nome: 'Molho branco', lote: 'LOT-V', quantidade: 3, diasVencido: 2, valor: 9 }
const perda = { id: 'm1', nome: 'Lasanha', data: '2026-03-14T15:00:00Z', quantidade: 2, descricao: 'Vencido', motivo: 'Vencido', lote: 'L1', valor: 12, desfeita: false }
const novasPerdas = ({ resultado = true, acoes = { editarProducao: true } } = {}) => {
  const chamadas = []
  g.acesso = { acoes }
  g.perdas = {
    estado: 'ok', erro: null, aviso: null, feito: null, opcoes: [], vencidos: [lote], periodo: { de: null, ate: null },
    resumo: { de: '2026-03-01', ate: '2026-03-14', valor: 12, porMotivo: [], lancamentos: [perda] },
    setPeriodo() {}, recarregar() {}, fecharAviso() {}, lancar: async () => true,
    lancarVencido: async (v) => { chamadas.push(['vencido', v.itemEstoqueId]); return resultado },
    desfazer: async (l) => { chamadas.push(['desfazer', l.id]); return resultado },
  }
  return { t: montar(AbaPerdasApi), chamadas }
}

await confere('Perdas: "Registrar perda do lote" pede confirmação; "Manter lote" não lança nada', async () => {
  const { t, chamadas } = novasPerdas()
  botao(t.renderizar(), 'Registrar perda do lote').props.onClick()
  let el = t.renderizar()
  assert.deepEqual(chamadas, [], 'um toque baixou o lote')
  assert.equal(botao(el, 'Registrar perda do lote'), undefined)
  assert.match(texto(el), /Lançar o lote inteiro como perda por vencimento\?/)
  botao(el, 'Manter lote').props.onClick()
  el = t.renderizar()
  assert.deepEqual(chamadas, [])
  botao(el, 'Registrar perda do lote').props.onClick()
  await botao(t.renderizar(), 'Registrar perda').props.onClick()
  assert.deepEqual(chamadas, [['vencido', 'l9']])
  assert.ok(botao(t.renderizar(), 'Registrar perda do lote'), 'a confirmação não fechou depois de lançar')
})

await confere('Perdas: "Desfazer" pede confirmação; se a API recusa, a confirmação fica aberta', async () => {
  const { t, chamadas } = novasPerdas({ resultado: false })
  botao(t.renderizar(), 'Desfazer').props.onClick()
  let el = t.renderizar()
  assert.deepEqual(chamadas, [], 'um toque desfez a perda')
  assert.match(texto(el), /Desfazer esta perda\? A quantidade volta para o estoque\./)
  await botao(el, 'Desfazer perda').props.onClick()
  el = t.renderizar()
  assert.deepEqual(chamadas, [['desfazer', 'm1']])
  assert.ok(botao(el, 'Desfazer perda'), 'a confirmação fechou mesmo com a recusa')
  botao(el, 'Manter perda').props.onClick()
  assert.ok(botao(t.renderizar(), 'Desfazer'))
})

await confere('Perdas: sem a ação de Gerente na produção, "Desfazer" não aparece; a baixa do lote continua', () => {
  for (const acoes of [{}, { editarProducao: false }, { editarCardapio: true }]) {
    const el = novasPerdas({ acoes }).t.renderizar()
    assert.equal(botao(el, 'Desfazer'), undefined, JSON.stringify(acoes))
    assert.ok(botao(el, 'Registrar perda do lote'))
  }
})

await confere('Bloqueios: "Remover" pede confirmação; só "Remover bloqueio" chama a API', async () => {
  const removidos = []
  let resultado = false
  g.cadastro = {
    janelas: [], zonas: [], erro: null, enviando: false, recarregar() {}, limparErro() {},
    bloqueios: [{ id: 'b1', data: '2026-03-20', motivo: 'Feriado', janelaEspecificaId: null }],
    acoes: { criarZona() {}, alternarZona() {}, criarBloqueio() {}, removerBloqueio: async (id) => { removidos.push(id); return resultado } },
  }
  const t = montar(CadastroEntregaApi)
  botao(t.renderizar(), 'Bloqueios 1').props.onClick()
  botao(t.renderizar(), 'Remover').props.onClick()
  let el = t.renderizar()
  assert.deepEqual(removidos, [], 'um toque removeu o bloqueio')
  assert.equal(botao(el, 'Remover'), undefined)
  assert.match(texto(el), /Remover este bloqueio\?/)
  botao(el, 'Manter bloqueio').props.onClick()
  el = t.renderizar()
  assert.deepEqual(removidos, [])

  botao(el, 'Remover').props.onClick()
  await botao(t.renderizar(), 'Remover bloqueio').props.onClick()
  assert.deepEqual(removidos, ['b1'])
  assert.ok(botao(t.renderizar(), 'Remover bloqueio'), 'a confirmação fechou mesmo com a recusa da API')
  resultado = true
  await botao(t.renderizar(), 'Remover bloqueio').props.onClick()
  assert.deepEqual(removidos, ['b1', 'b1'])
  assert.ok(botao(t.renderizar(), 'Remover'), 'a confirmação não fechou depois de remover')
})

console.log(`\n${passou} verificações passaram, ${falhas.length} falharam.`)
if (falhas.length) process.exit(1)
