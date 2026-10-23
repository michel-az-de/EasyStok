// Componente e handlers reais, com hooks, relógio e fronteiras de contexto controlados.
// Não prova pixels, foco nativo, leitores de tela ou o serviço remoto.
import assert from 'node:assert/strict'
import { readFileSync, writeFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { transformWithOxc } from 'vite'

const urlBarra = new URL('../src/features/ficha-cliente/BarraProximoPasso.jsx', import.meta.url)
const barraCompilada = (await transformWithOxc(readFileSync(urlBarra, 'utf8'), urlBarra.pathname, { jsx: { runtime: 'automatic' } })).code
let ativo, sequencia = 0
const tempos = new Map(), intervalos = new Map()
globalThis.setTimeout = (fn, ms) => { const id = ++sequencia; tempos.set(id, { fn, ms }); return id }
globalThis.clearTimeout = id => tempos.delete(id)
globalThis.setInterval = fn => { const id = ++sequencia; intervalos.set(id, fn); return id }
globalThis.clearInterval = id => intervalos.delete(id)
const mesmasDeps = (a, b) => a?.length === b?.length && a?.every((v, i) => Object.is(v, b[i]))
const hooks = {
  useState(inicial) {
    const host = ativo, i = host.indice++
    if (!(i in host.valores)) host.valores[i] = typeof inicial === 'function' ? inicial() : inicial
    return [host.valores[i], valor => {
      if (host.encerrado) return
      const novo = typeof valor === 'function' ? valor(host.valores[i]) : valor
      if (!Object.is(novo, host.valores[i])) { host.valores[i] = novo; host.sujo = true }
    }]
  },
  useRef(inicial) { const host = ativo, i = host.indice++; return host.valores[i] ??= { current: inicial } },
  useId() { const host = ativo, i = host.indice++; return host.valores[i] ??= `trilha-${++sequencia}` },
  useEffect(fn, deps) {
    const host = ativo, i = host.indice++, anterior = host.efeitos.get(i)
    if (!mesmasDeps(anterior?.deps, deps)) host.pendentes.push(() => {
      anterior?.limpar?.(); host.efeitos.set(i, { deps, limpar: fn() })
    })
  },
}
globalThis.__premiumComanda = { hooks, atendimento: { fonteApi: true, conversas: [] } }
const codigo = texto => 'data:text/javascript,' + encodeURIComponent(texto)
const fronteiras = new Map([
  ['/aplicacao/acessoModulos', 'export const useAcessoModulos=()=>({acoes:{cancelarPedidoPago:true}})'],
  ['/aplicacao/contextos', 'export const useAtendimento=()=>globalThis.__premiumComanda.atendimento; export const useCatalogo=()=>({entregadores:[],'
    + 'cardapio:[{sku:"massa",nome:"Massa fresca",preco:50}],meiosDePagamento:[{id:"pix",rotulo:"Link de pagamento (Pix ou cartão)"}]})'],
  ['/aplicacao/useAcaoDisponivel', 'export const useAcaoDisponivel=()=>()=>true'],
  ['/componentes/Botao', 'export function Botao(){}'],
  ['/componentes/Campo', 'export function CampoArea(){}'],
  ['/componentes/CampoMascarado', 'export function CampoMascarado(){}'],
  ['/componentes/EscolhaDeEntregador', 'export function EscolhaDeEntregador(){}'],
  ['/componentes/Icone', 'export function Icone(){}'],
  ['/features/ficha-cliente/BlocoCobranca', 'export function BaixaAMao(){}'],
  ['/features/ficha-cliente/MenuDoPedido', 'export function MenuDoPedido(){}'],
  ['/features/ficha-cliente/EscolhaDeMeio', 'export function EscolhaDeMeio(){}'],
])
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec === 'react') return { url: codigo('const h=globalThis.__premiumComanda.hooks; export const useState=h.useState,useRef=h.useRef,useId=h.useId,useEffect=h.useEffect'), shortCircuit: true }
    if (spec === 'react/jsx-runtime') return { url: codigo('export const Fragment="fragment"; export const jsx=(type,props,key)=>({type,props,key}); export const jsxs=jsx'), shortCircuit: true }
    if (spec.endsWith('.css')) return { url: codigo('export default new Proxy({},{get:(_,key)=>String(key)})'), shortCircuit: true }
    if (spec.startsWith('.')) {
      const url = new URL(spec, contexto.parentURL), caminho = url.pathname.replace(/\.(jsx?|mjs)$/, '')
      for (const [fim, fonte] of fronteiras) if (caminho.endsWith(fim)) return { url: codigo(fonte), shortCircuit: true }
      if (!/\.[a-z]+$/i.test(spec)) return proximo(spec + '.js', contexto)
    }
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (url === urlBarra.href) return { format: 'module', source: barraCompilada, shortCircuit: true }
    if (url.endsWith('.json')) return { format: 'module', source: `export default ${readFileSync(new URL(url), 'utf8')}`, shortCircuit: true }
    return proximo(url, contexto)
  },
})
const { BarraProximoPasso } = await import(urlBarra.href)
const { marcoDaComanda, houveAvancoConfirmado } = await import('../src/features/ficha-cliente/progressoDaComanda.js')
const { PASSOS } = await import('../src/dominio/esteira.js')
class Tela {
  constructor(props) { this.props = props; this.valores = []; this.efeitos = new Map(); this.renderizar() }
  renderizar(props = this.props) {
    this.props = props
    let voltas = 0
    do {
      assert.ok(++voltas < 12, 'render deve estabilizar')
      ativo = this; this.sujo = false; this.indice = 0; this.pendentes = []
      this.arvore = BarraProximoPasso(this.props)
      this.pendentes.forEach(fn => fn())
    } while (this.sujo)
    return this.arvore
  }
  encerrar() { this.encerrado = true; for (const efeito of this.efeitos.values()) efeito.limpar?.() }
}
const nos = arvore => Array.isArray(arvore) ? arvore.flatMap(nos) : !arvore || typeof arvore !== 'object' ? [] : [arvore, ...nos(arvore.props?.children)]
const texto = arvore => Array.isArray(arvore) ? arvore.map(texto).join('') : typeof arvore !== 'object' ? String(arvore ?? '') : texto(arvore?.props?.children)
const botao = (tela, rotulo) => nos(tela.arvore).find(n => (n.type === 'button' || n.type?.name === 'Botao') && texto(n).includes(rotulo))
const atualizou = tela => nos(tela.arvore).some(n => n.type === 'output' && texto(n).includes('Etapa atualizada'))
const props = (pedido = {}, extras = {}) => ({
  pedido: { estado: 'pago', pedidoId: 'pedido-a', numero: 17, itens: [{ sku: 'massa', qtd: 1 }], cobranca: null, totalApi: 100, totalPagoApi: 100, ...pedido },
  conversaId: 'conversa-a', nomeCliente: 'Cliente A', agora: 10_000, ...extras,
})
const pendente = () => { let resolver, rejeitar; const promise = new Promise((ok, erro) => { resolver = ok; rejeitar = erro }); return { promise, resolver, rejeitar } }
const resultados = []
const provar = async (nome, fn) => { await fn(); resultados.push({ nome, resultado: 'aprovado' }); console.log('ok ' + nome) }

await provar('abrir uma comanda em andamento não celebra uma ação que não ocorreu agora', () => {
  const tela = new Tela(props({ estado: 'entregue' }))
  assert.ok(!atualizou(tela)); assert.equal(tela.arvore.props['data-etapa-confirmada'], undefined)
  assert.ok(botao(tela, 'Encerrar atendimento')); tela.encerrar()
})
await provar('posição da jornada vem de cada estado real, sem pontos ou porcentagem inventados', () => {
  for (const [i, passo] of PASSOS.entries()) {
    const tela = new Tela(props({ estado: passo.id }))
    assert.ok(texto(tela.arvore).includes(`Etapa ${i + 1} de ${PASSOS.length}`))
    assert.ok(!atualizou(tela)); tela.encerrar()
  }
})
await provar('trilha recolhe e expande com alvo nativo e etapa atual identificada semanticamente', () => {
  const tela = new Tela(props()), abrir = botao(tela, 'Ver etapas')
  assert.equal(abrir.type, 'button'); assert.equal(abrir.props.type, 'button')
  assert.equal(abrir.props['aria-expanded'], false)
  const id = abrir.props['aria-controls']
  assert.equal(nos(tela.arvore).find(n => n.props.id === id).props.hidden, true)
  abrir.props.onClick(); tela.renderizar()
  assert.equal(botao(tela, 'Recolher').props['aria-expanded'], true)
  assert.equal(nos(tela.arvore).find(n => n.props.id === id).props.hidden, false)
  const atuais = nos(tela.arvore).filter(n => n.props['aria-current'] === 'step')
  assert.equal(atuais.length, 1); assert.ok(atuais[0].props['aria-label'].startsWith('Pago'))
  tela.encerrar()
})
await provar('ação pendente mostra espera sem avançar ou recompensar e ignora clique repetido', async () => {
  const espera = pendente(); let chamadas = 0
  const tela = new Tela(props({}, { aoAvancar: () => { chamadas++; return espera.promise } }))
  const clicado = botao(tela, 'Começar preparo'), ida = clicado.props.onClick()
  clicado.props.onClick(); tela.renderizar()
  assert.equal(chamadas, 1); assert.equal(botao(tela, 'Começar preparo').props.disabled, true)
  assert.ok(texto(tela.arvore).includes('Confirmando alteração…')); assert.ok(!atualizou(tela))
  assert.equal(tela.props.pedido.estado, 'pago')
  espera.resolver(undefined); await ida; tela.renderizar(); tela.encerrar()
})
await provar('falha HTTP conserva etapa e mostra erro, sem recompensa nem espera presa', async () => {
  const espera = pendente(), tela = new Tela(props({}, { aoAvancar: () => espera.promise }))
  const ida = botao(tela, 'Começar preparo').props.onClick()
  espera.resolver({ erro: 'Alteração recusada (503)' }); await ida; tela.renderizar()
  assert.ok(nos(tela.arvore).some(n => n.props.role === 'alert' && texto(n).includes('(503)')))
  assert.equal(tela.props.pedido.estado, 'pago'); assert.ok(!atualizou(tela))
  assert.equal(botao(tela, 'Começar preparo').props.disabled, false)
  assert.ok(!texto(tela.arvore).includes('Confirmando alteração…')); tela.encerrar()
})
await provar('Promise rejeitada vira erro contextual e libera o controle', async () => {
  const tela = new Tela(props({}, { aoAvancar: () => Promise.reject(new Error('Conexão interrompida')) }))
  await botao(tela, 'Começar preparo').props.onClick(); tela.renderizar()
  assert.ok(texto(tela.arvore).includes('Conexão interrompida')); assert.ok(!atualizou(tela))
  assert.equal(botao(tela, 'Começar preparo').props.disabled, false); tela.encerrar()
})
await provar('erro síncrono da ação também recebe feedback sem bloquear nova tentativa', () => {
  const tela = new Tela(props({}, { aoAvancar: () => { throw new Error('Não confirmado') } }))
  botao(tela, 'Começar preparo').props.onClick(); tela.renderizar()
  assert.ok(texto(tela.arvore).includes('Não confirmado'))
  assert.equal(botao(tela, 'Começar preparo').props.disabled, false); assert.ok(!atualizou(tela)); tela.encerrar()
})
await provar('Promise aceita sem pedido atualizado não é apresentada como etapa confirmada', async () => {
  const tela = new Tela(props({}, { aoAvancar: () => Promise.resolve(undefined) }))
  await botao(tela, 'Começar preparo').props.onClick(); tela.renderizar()
  assert.ok(!atualizou(tela)); assert.equal(tela.arvore.props['data-etapa-confirmada'], undefined)
  tela.encerrar()
})
await provar('pedido atualizado confirma avanço e o feedback termina, sem animar permanentemente', () => {
  const tela = new Tela(props())
  tela.renderizar({ ...tela.props, pedido: { ...tela.props.pedido, estado: 'preparo' } })
  assert.ok(atualizou(tela)); assert.equal(tela.arvore.props['data-etapa-confirmada'], '')
  const expira = [...tempos.values()].find(t => t.ms === 2400)
  assert.ok(expira); expira.fn(); tela.renderizar()
  assert.ok(!atualizou(tela)); assert.equal(tela.arvore.props['data-etapa-confirmada'], undefined)
  tela.encerrar()
})
await provar('voltar etapa ou cancelar não recebe confirmação de progresso positivo', () => {
  const tela = new Tela(props({ estado: 'preparo' }))
  tela.renderizar({ ...tela.props, pedido: { ...tela.props.pedido, estado: 'pago' } }); assert.ok(!atualizou(tela))
  tela.renderizar({ ...tela.props, pedido: { ...tela.props.pedido, estado: 'cancelado' } }); assert.ok(!atualizou(tela))
  assert.ok(!texto(tela.arvore).includes('Etapa 0')); tela.encerrar()
})
await provar('mudança de cliente ou de pedido nunca vira recompensa de outro contexto', () => {
  const antes = marcoDaComanda(props().pedido, 'cliente-a')
  assert.equal(houveAvancoConfirmado(antes, marcoDaComanda({ ...props().pedido, estado: 'preparo' }, 'cliente-b')), false)
  assert.equal(houveAvancoConfirmado(antes, marcoDaComanda({ ...props().pedido, pedidoId: 'pedido-b', estado: 'preparo' }, 'cliente-a')), false)
  const tela = new Tela(props())
  tela.renderizar(props({ estado: 'preparo' }, { conversaId: 'conversa-b' })); assert.ok(!atualizou(tela)); tela.encerrar()
})
await provar('resposta tardia do cliente anterior não altera o controle da nova ficha', async () => {
  const espera = pendente(), anterior = new Tela(props({}, { aoAvancar: () => espera.promise }))
  const ida = botao(anterior, 'Começar preparo').props.onClick(); anterior.encerrar()
  const atual = new Tela(props({ pedidoId: 'pedido-b' }, { conversaId: 'conversa-b' }))
  espera.resolver({ erro: 'Erro do pedido anterior' }); await ida; atual.renderizar()
  assert.ok(!texto(atual.arvore).includes('Erro do pedido anterior')); assert.ok(!atualizou(atual))
  assert.equal(botao(atual, 'Começar preparo').props.disabled, false)
  const fonteFicha = readFileSync(new URL('../src/features/ficha-cliente/PainelFicha.jsx', import.meta.url), 'utf8')
  assert.match(fonteFicha, /<BarraProximoPasso[\s\S]*?key=\{`\$\{selecionada\.id\}:/)
  assert.match(fonteFicha, /<BarraProximoPasso[\s\S]*?conversaId=\{selecionada\.id\}/)
  atual.encerrar()
})
await provar('resposta tardia de outro pedido na mesma conversa não aparece na comanda atual', async () => {
  const espera = pendente(); let chamadas = 0
  const tela = new Tela(props({}, { aoAvancar: () => { chamadas++; return espera.promise } }))
  const ida = botao(tela, 'Começar preparo').props.onClick()
  tela.renderizar({ ...tela.props, pedido: { ...tela.props.pedido, pedidoId: 'pedido-b', numero: 'PEDIDOB' } })
  assert.equal(botao(tela, 'Começar preparo').props.disabled, true)
  const textoPendente = texto(tela.arvore)
  botao(tela, 'Começar preparo').props.onClick(); assert.equal(chamadas, 1)
  espera.resolver({ erro: 'Erro exclusivo do pedido A' }); await ida; tela.renderizar()
  assert.ok(!texto(tela.arvore).includes('Erro exclusivo do pedido A'))
  assert.ok(!textoPendente.includes('Confirmando alteração…'))
  assert.ok(textoPendente.includes('Concluindo a ação iniciada nesta conversa…'))
  assert.ok(!texto(tela.arvore).includes('Concluindo a ação iniciada nesta conversa…'))
  assert.equal(botao(tela, 'Começar preparo').props.disabled, false)
  assert.ok(!atualizou(tela)); tela.encerrar()
})
await provar('rascunho que ganha pedidoId conserva a espera e não libera outro envio prematuro', async () => {
  const espera = pendente(); let chamadas = 0
  const tela = new Tela(props({ estado: 'aguardando', pedidoId: null, meio: 'pix' }, {
    aoGerarPix: () => { chamadas++; return espera.promise },
    aoAvancar: () => { chamadas++; return Promise.resolve(undefined) },
  }))
  // #1989 (#1765-3): o botão abre o resumo; quem gera é o "Confirmar e enviar".
  nos(tela.arvore).find(n => n.type?.name === 'Botao' && n.props.icone === 'dollar-sign').props.onClick(); tela.renderizar()
  assert.equal(chamadas, 0)
  const enviar = nos(tela.arvore).find(n => n.type?.name === 'Botao' && n.props['data-confirmar-cobranca'])
  const ida = enviar.props.onClick()
  tela.renderizar({ ...tela.props, pedido: { ...tela.props.pedido, pedidoId: 'pedido-criado', numero: 'CRIADO01', estado: 'pago' } })
  const iniciar = botao(tela, 'Começar preparo')
  assert.equal(iniciar.props.disabled, true); iniciar.props.onClick(); enviar.props.onClick()
  assert.equal(chamadas, 1); assert.ok(!atualizou(tela))
  espera.resolver(undefined); await ida; tela.renderizar()
  assert.equal(botao(tela, 'Começar preparo').props.disabled, false)
  assert.ok(!atualizou(tela)); tela.encerrar()
})
await provar('#1989-1 (#1765-3) forma já escolhida: um clique mostra itens, total e forma; só "Confirmar e enviar" gera', async () => {
  const chamadas = []
  const tela = new Tela(props({ estado: 'aguardando', pedidoId: null, meio: 'pix', itens: [{ sku: 'massa', qtd: 2 }] }, {
    aoGerarPix: (meio) => { chamadas.push(meio); return Promise.resolve() },
  }))
  const gerar = nos(tela.arvore).find(n => n.type?.name === 'Botao' && n.props.icone === 'dollar-sign')
  assert.equal(texto(gerar), 'Gerar cobrança e enviar')
  gerar.props.onClick(); tela.renderizar()
  assert.deepEqual(chamadas, [], 'um clique não cria o pedido nem manda o link')
  const resumo = texto(tela.arvore)
  assert.ok(resumo.includes('2× Massa fresca'), resumo)
  assert.ok(resumo.includes('Pedido de R$ 100,00'), resumo)
  assert.ok(resumo.includes('cobrança por Link de pagamento (Pix ou cartão)'), resumo)
  // Voltar fecha o resumo sem gerar.
  botao(tela, 'Voltar').props.onClick(); tela.renderizar()
  assert.equal(nos(tela.arvore).find(n => n.props?.['data-confirmar-cobranca']), undefined)
  assert.deepEqual(chamadas, [])
  gerar.props.onClick(); tela.renderizar()
  const confirmar = nos(tela.arvore).find(n => n.props?.['data-confirmar-cobranca'])
  assert.equal(texto(confirmar), 'Confirmar e enviar')
  await confirmar.props.onClick()
  assert.deepEqual(chamadas, ['pix'])
  tela.encerrar()
  // Revisão da PR #1997: a forma trocada na Cobrança com o resumo aberto vale no confirmar.
  const outra = new Tela(props({ estado: 'aguardando', pedidoId: null, meio: 'pix' }, {
    aoGerarPix: (meio) => { chamadas.push(meio); return Promise.resolve() },
  }))
  const tocarGerar = () => { nos(outra.arvore).find(n => n.type?.name === 'Botao' && n.props.icone === 'dollar-sign').props.onClick(); outra.renderizar() }
  const confirmarOutra = () => nos(outra.arvore).find(n => n.props?.['data-confirmar-cobranca']).props.onClick()
  tocarGerar()
  outra.renderizar({ ...outra.props, pedido: { ...outra.props.pedido, meio: 'maquininha' } })
  await confirmarOutra()
  assert.deepEqual(chamadas.slice(1), ['maquininha'], 'confirmou com a forma antiga')
  // Tocar de novo no botão não desfaz a forma escolhida no resumo.
  tocarGerar()
  nos(outra.arvore).find(n => n.type?.name === 'EscolhaDeMeio').props.aoEscolher('cartao-link'); outra.renderizar()
  tocarGerar()
  await confirmarOutra()
  assert.deepEqual(chamadas.slice(1), ['maquininha', 'cartao-link'], 'o segundo toque trocou a forma do resumo')
  outra.encerrar()
})
await provar('pedido na fila sem quitação mantém rótulo financeiro honesto', () => {
  const tela = new Tela(props({ totalPagoApi: 20, totalApi: 100 }))
  assert.ok(texto(tela.arvore).includes('Na fila de preparo'))
  assert.ok(texto(tela.arvore).includes('Falta:')); assert.ok(!atualizou(tela)); tela.encerrar()
})
await provar('pré-requisito e bloqueio continuam claros, sem punição ou avanço falso', () => {
  const bloqueada = new Tela(props({}, { bloqueado: true }))
  assert.equal(botao(bloqueada, 'Começar preparo').props.disabled, true)
  assert.ok(texto(bloqueada.arvore).includes('Cliente bloqueado')); assert.ok(!atualizou(bloqueada)); bloqueada.encerrar()
  const rascunho = new Tela(props({ estado: 'aguardando', pedidoId: null }, { prerequisito: 'Falta escolher a janela' }))
  assert.ok(texto(rascunho.arvore).includes('Falta escolher a janela'))
  assert.ok(nos(rascunho.arvore).some(n => n.type?.name === 'Botao' && n.props.disabled)); assert.ok(!atualizou(rascunho)); rascunho.encerrar()
})
await provar('confirmação aceita na demonstração conserva desfazer e recebe marca breve', () => {
  globalThis.__premiumComanda.atendimento = { fonteApi: false, conversas: [] }
  const tela = new Tela(props({ pedidoId: null }, { emDesfazer: true, aoDesfazer: () => {} }))
  assert.equal(tela.arvore.props['data-etapa-confirmada'], true); assert.ok(botao(tela, 'Desfazer'))
  tela.encerrar(); globalThis.__premiumComanda.atendimento = { fonteApi: true, conversas: [] }
})
await provar('movimento reduzido cancela a marca visual e o pulso infinito da trilha foi removido', () => {
  const css = readFileSync(new URL('../src/features/ficha-cliente/ficha.module.css', import.meta.url), 'utf8')
  assert.match(css, /\[data-etapa-confirmada\]\s*\{\s*animation:\s*confirmarEtapa 650ms[^}]* 1;/)
  assert.match(css, /prefers-reduced-motion:\s*reduce[\s\S]*?\[data-etapa-confirmada\]\s*\{\s*animation:\s*none/)
  assert.ok(!css.includes('pulsoAtualizacao'))
})

const resumo = { provas: resultados.length, aprovadas: resultados.length, resultados,
  limites: ['Handlers e JSX reais; contextos, hooks e relógio controlados.', 'Navegador deve conferir foco, teclado, pixels, movimento reduzido e identidade na composição completa.', 'Sem tráfego real, pontos, rankings, entrevistas ou mudança de negócio.'] }
if (process.env.PREMIUM_COMANDA_EVIDENCIA) writeFileSync(process.env.PREMIUM_COMANDA_EVIDENCIA, JSON.stringify(resumo, null, 2))
console.log(`${resultados.length} provas aprovadas`)
