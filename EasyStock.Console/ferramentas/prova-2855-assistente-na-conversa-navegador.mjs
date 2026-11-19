// #2855: Chromium, a ModalAssistente real com o painel entregue pelo App (o mesmo contexto do App.jsx) e HTTP
// controlado. O modal da conversa abre em "Esta conversa" (veredito, chave e só as etapas que barram) e, nas abas
// ao lado, mostra o uso contra a cota em uma linha, o Fluxo e o histórico. Sem o contexto, fica como era.
// O Fluxo é um DESENHO (canvas do React Flow), não uma lista: nós em linha, arestas com seta, rótulo "barra" para a
// Equipe, Horizontal e Vertical, arrastar, grupos que abrem e a chave no próprio nó. Fotos em 1280 e 375 px, sem
// rolagem lateral da página.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2855/assistente-na-conversa')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {ContextoPainelDoAssistente} from '../../../src/aplicacao/painelDoAssistente';
import {ModalAssistente} from '../../../src/features/atendimento/ModalAssistente';
import {PainelAssistente} from '../../../src/features/gestao/atendimento/PainelAssistente';
import '../../../src/estilos/base.css';
document.body.style.cssText='margin:0;background:var(--fundo);font-family:var(--f-corpo);color:var(--tinta)';
const conversa={id:'c5',nome:'Felipe Azevedo',contato:{nome:'Felipe Azevedo'},numeroComanda:'5',mensagens:[]};
const comPainel = new URLSearchParams(location.search).get('painel') !== 'nao';
const modal = <ModalAssistente conversa={conversa} aoFechar={()=>{}}/>;
createRoot(document.getElementById('root')).render(<ContextoAcessoModulos.Provider value={{acoes:{editarAtendimento:true},permite:()=>true}}>
  {comPainel ? <ContextoPainelDoAssistente.Provider value={props => <PainelAssistente {...props}/>}>{modal}</ContextoPainelDoAssistente.Provider> : modal}
</ContextoAcessoModulos.Provider>);
`)

const etapa = (codigo, titulo, estado, detalhe) => ({ codigo, titulo, estado, detalhe })
const checagem = {
  ativo: true, responde: false, bloqueio: 'A cota do mês acabou.', entradasPendentes: 0,
  etapas: [etapa('situacao', 'Quem atende', 'Passa', 'O Assistente atende esta conversa.'), etapa('cota', 'Cota do mês', 'Barra', 'A cota do mês acabou.')],
  prompt: 'Você é o assistente de atendimento da Casa.', ferramentas: [{ nome: 'consultar_pedido', descricao: 'Consulta o pedido', ligada: true }],
}
const PORTOES = [['situacao', 'Quem atende'], ['chave', 'Assistente na loja'], ['modelo', 'Modelo de IA'], ['equipe', 'Contato da equipe'], ['bloqueado', 'Cliente bloqueado'],
  ['pausado', 'Encaminhado à responsável'], ['limite', 'Respostas na última hora'], ['janela', 'Janela de 24 horas'], ['horario', 'Horário de atendimento'], ['fila', 'Mensagens na fila'], ['cota', 'Cota do mês']]
  .map(([codigo, titulo]) => ({ codigo, titulo, checa: `Checa ${titulo.toLowerCase()}.`, escala: codigo !== 'situacao' && codigo !== 'janela', quemMuda: null }))
const uso = { ano: 2026, mes: 11, turnos: 120, tokensEntrada: 700_000, tokensSaida: 100_000, tokensCacheLido: 50_000,
  totalTokens: 850_000, custoEstimado: 0, turnosSemPreco: 120, cotaTokensMes: 1_000_000, percentual: 85, alerta: true }
// As 22 ferramentas de produção; escalar_para_dona é a travada.
const NOMES = ['consultar_pedido', 'consultar_cardapio', 'consultar_estoque', 'consultar_horario', 'consultar_entrega', 'calcular_frete', 'criar_pedido', 'alterar_pedido',
  'cancelar_pedido', 'enviar_link_pagamento', 'consultar_pagamento', 'enviar_cardapio', 'enviar_foto', 'registrar_observacao', 'registrar_alergia', 'agendar_retirada',
  'consultar_cliente', 'atualizar_cliente', 'aplicar_cupom', 'avisar_atraso', 'pedir_avaliacao', 'escalar_para_dona']
let desligadas = []
const ferramentas = () => NOMES.map(nome => ({ nome, descricao: 'Faz ' + nome.replaceAll('_', ' ') + '.', ligada: !desligadas.includes(nome), travada: nome === 'escalar_para_dona' }))
const fluxo = () => ({ ativo: true, modeloDisponivel: true, portoes: PORTOES, roteiroPersonalizado: true,
  roteiro: [{ nome: 'Acolher', instrucao: 'Cumprimente pelo nome.', ativo: true }, { nome: 'Entender o pedido', instrucao: 'Pergunte o que a pessoa quer.', ativo: true },
    { nome: 'Fechar', instrucao: 'Mande o resumo e o link.', ativo: false }],
  ferramentas: ferramentas(),
  prompt: 'Você é o assistente virtual da loja.', observacoesLoja: '' })
const historico = { itens: [{ id: 't1', conversaId: 'c5', criadoEm: '2026-11-18T15:00:00Z', modelo: 'm', tokensEntrada: 4000, tokensSaida: 300,
  tokensCacheLido: 0, custoEstimado: null, ferramentas: [], passos: 1, desfecho: 'Respondeu', duracaoMs: 2400,
  perguntaCliente: 'Meu pedido sai que horas?', resposta: 'Sai às 18h.' }], total: 1 }

const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 900 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'admin' }, expiraEm: Date.now() + 600000 })))
const pedidos = [], erros = []
// Só o /api da raiz: um glob **/api/** também pegaria os módulos de src/infra/api/ e a página ficaria em branco.
await contexto.route(url => url.pathname.startsWith('/api/'), async route => {
  const req = route.request(), path = new URL(req.url()).pathname
  const fora = () => route.fulfill({ status: 404, json: { error: { message: 'fora da prova: ' + path } } })
  if (req.method() === 'PUT' && path === '/api/atendimento/configuracao') {
    const corpo = req.postDataJSON()
    pedidos.push({ metodo: 'PUT', path, corpo })
    if (corpo.ferramentasDesligadas) desligadas = corpo.ferramentasDesligadas
    return route.fulfill({ json: { data: {} } })
  }
  pedidos.push({ metodo: req.method(), path })
  if (req.method() !== 'GET') return fora()
  if (path === '/api/atendimento/conversas/c5/assistente') return route.fulfill({ json: { data: checagem } })
  if (path === '/api/atendimento/assistente/uso') return route.fulfill({ json: { data: uso } })
  if (path === '/api/atendimento/assistente/fluxo') return route.fulfill({ json: { data: fluxo() } })
  if (path === '/api/atendimento/assistente/historico') return route.fulfill({ json: { data: historico } })
  return fora()
})
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(10000)
pagina.on('pageerror', e => erros.push(e.message))
const perguntas = []
pagina.on('dialog', d => { perguntas.push(d.message()); d.accept() })
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2855/assistente-na-conversa/index.html`
try {
  await pagina.goto(base)
  const modal = pagina.getByRole('dialog', { name: 'Assistente nesta conversa' })
  await modal.waitFor()
  await modal.getByRole('tab', { name: 'Esta conversa' }).waitFor()
  const abas = (await modal.getByRole('tab').allInnerTexts()).map(t => t.trim())
  assert.deepEqual(abas, ['Esta conversa', 'Uso e cota', 'Fluxo', 'Histórico'], 'abas do painel dentro do modal')
  assert.equal(await modal.getByRole('tab', { name: 'Esta conversa' }).getAttribute('aria-selected'), 'true', 'abre na conversa')

  // Esta conversa: veredito, chave da loja e só as etapas que barram; o resto fica recolhido.
  await modal.getByText('O Assistente não responde esta conversa.').waitFor()
  assert.equal(await modal.getByRole('heading', { name: 'Painel do Assistente' }).count(), 0, 'sem o cabeçalho do painel dentro do modal')
  assert.equal(await modal.getByRole('button', { name: 'Atualizar' }).count(), 0, 'sem o Atualizar do painel dentro do modal')
  assert.equal(await modal.locator('[data-etapa="cota"]').first().isVisible(), true, 'a etapa que barra aparece')
  assert.equal(await modal.locator('[data-etapa="situacao"]').isVisible(), false, 'as que passam ficam recolhidas')
  assert.equal(await modal.getByText(/Instruções que o Assistente recebe/).count(), 0, 'instruções ficam no Fluxo')
  assert.equal(await modal.getByText(/Ferramentas que ele pode usar/).count(), 0, 'ferramentas ficam no Fluxo')
  await pagina.screenshot({ path: resolve(pasta, 'conversa-1280.png') })

  // Uso e cota: barra, título e uma linha.
  await modal.getByRole('tab', { name: 'Uso e cota' }).click()
  await modal.getByText('850 mil de 1 mi tokens (85%)', { exact: true }).waitFor()
  assert.equal(await modal.getByRole('meter').getAttribute('aria-valuenow'), '85')
  await modal.getByLabel('Cota mensal (mil tokens)').waitFor()
  assert.equal(await modal.locator('dl').count(), 0, 'sem os cartões de números')
  await pagina.screenshot({ path: resolve(pasta, 'uso-1280.png') })

  // "Checar de novo" também recarrega o painel (uso, fluxo e a checagem).
  const usosAntes = pedidos.filter(p => p.path === '/api/atendimento/assistente/uso').length
  const checagensAntes = pedidos.filter(p => p.path === '/api/atendimento/conversas/c5/assistente').length
  await pagina.getByRole('button', { name: 'Checar de novo' }).click()
  await pagina.waitForFunction(() => true)
  await (async () => { for (let i = 0; i < 50; i++) { if (pedidos.filter(p => p.path === '/api/atendimento/assistente/uso').length > usosAntes && pedidos.filter(p => p.path === '/api/atendimento/conversas/c5/assistente').length > checagensAntes) return; await pagina.waitForTimeout(100) } assert.fail('Checar de novo não recarregou o painel') })()

  // Fluxo: um DESENHO. Horizontal em 1280: os quatro nós principais na mesma linha, da esquerda para a direita.
  await modal.getByRole('tab', { name: 'Fluxo' }).click()
  await modal.locator('[data-no="saida"]').waitFor()
  await modal.locator('[data-no="equipe"]').waitFor()
  await pagina.waitForTimeout(400)
  const caixa = async (id) => modal.locator(`[data-no="${id}"]`).evaluate(e => { const r = e.getBoundingClientRect(); return { x: r.x, y: r.y, w: r.width, h: r.height } })
  const principais = ['entrada', 'grupo:portoes', 'assistente', 'saida']
  const horizontais = []
  for (const id of principais) horizontais.push(await caixa(id))
  horizontais.forEach((c, i) => {
    assert.ok(Math.abs(c.y - horizontais[0].y) < 2, `nó ${principais[i]} na mesma linha (y ${c.y})`)
    if (i > 0) assert.ok(c.x > horizontais[i - 1].x + horizontais[i - 1].w - 1, `${principais[i]} à direita do anterior`)
  })
  assert.ok(await modal.locator('.react-flow__edge').count() >= 5, 'as ligações são arestas do canvas')
  assert.ok(await modal.locator('.react-flow__edge-text').filter({ hasText: 'barra' }).count() >= 1, 'a aresta para a Equipe diz "barra"')
  assert.equal(await modal.locator('.react-flow__minimap').count(), 0, 'sem minimapa por cima dos nós')
  assert.ok(await modal.locator('.react-flow__controls').isVisible(), 'controles de zoom')
  await pagina.screenshot({ path: resolve(pasta, 'fluxo-horizontal-1280.png') })

  // Vertical: mesmo x, y crescente.
  await modal.getByRole('button', { name: 'Vertical' }).click()
  await pagina.waitForTimeout(600)
  const verticais = []
  for (const id of principais) verticais.push(await caixa(id))
  verticais.forEach((c, i) => {
    assert.ok(Math.abs(c.x - verticais[0].x) < 2, `nó ${principais[i]} na mesma coluna (x ${c.x})`)
    if (i > 0) assert.ok(c.y > verticais[i - 1].y + verticais[i - 1].h - 1, `${principais[i]} abaixo do anterior`)
  })
  await pagina.screenshot({ path: resolve(pasta, 'fluxo-vertical-1280.png') })
  await modal.getByRole('button', { name: 'Horizontal' }).click()
  await pagina.waitForTimeout(600)

  // Arrastar um nó muda a posição.
  await modal.getByRole('button', { name: 'Enquadrar o desenho na tela' }).click()
  await pagina.waitForTimeout(300)
  const antes = await caixa('assistente')
  await pagina.mouse.move(antes.x + 30, antes.y + 8)
  await pagina.mouse.down()
  await pagina.mouse.move(antes.x + 60, antes.y + 40, { steps: 5 })
  await pagina.mouse.move(antes.x + 90, antes.y + 80, { steps: 5 })
  await pagina.mouse.up()
  const depois = await caixa('assistente')
  assert.ok(Math.abs(depois.x - antes.x) > 20 || Math.abs(depois.y - antes.y) > 20, `arrastar muda a posição (${antes.x},${antes.y}) -> (${depois.x},${depois.y})`)

  // Verificações abrem como 11 nós encadeados, na ordem do turno.
  await modal.locator('[data-no="grupo:portoes"]').click()
  await modal.locator('[data-no="portao:cota"]').waitFor()
  const portoes = await modal.locator('[data-no^="portao:"]').evaluateAll(b => b.map(x => x.dataset.no.slice(7)))
  assert.deepEqual(portoes, PORTOES.map(p => p.codigo))
  await modal.locator('[data-no="grupo:portoes"]').click()
  await modal.locator('[data-no="portao:cota"]').waitFor({ state: 'detached' })

  // Ferramentas abrem com um nó por ferramenta e a chave no nó grava só a lista de desligadas.
  await modal.locator('[data-no="grupo:ferramentas"]').click()
  await modal.locator('[data-no="ferramenta:escalar_para_dona"]').waitFor()
  assert.equal(await modal.locator('[data-no^="ferramenta:"]').count(), 22)
  assert.equal(await modal.locator('[data-no="ferramenta:escalar_para_dona"]').getByRole('switch').count(), 0, 'a saída para a equipe não tem chave')
  await pagina.waitForTimeout(400)
  await pagina.screenshot({ path: resolve(pasta, 'fluxo-ferramentas-1280.png') })
  const marca = pedidos.length
  await modal.locator('[data-no="ferramenta:consultar_pedido"]').getByRole('switch').click()
  await modal.getByText('21 de 22 ligadas').first().waitFor()
  assert.equal(perguntas.length, 1, 'pergunta antes de desligar')
  assert.deepEqual(pedidos.slice(marca).filter(p => p.metodo === 'PUT').map(p => p.corpo), [{ ferramentasDesligadas: ['consultar_pedido'] }])
  await modal.locator('[data-no="ferramenta:consultar_cardapio"]').click()
  await modal.getByRole('switch', { name: 'Ligada na loja' }).waitFor()

  await modal.getByRole('tab', { name: 'Histórico' }).click()
  await modal.getByText('Meu pedido sai que horas?', { exact: false }).waitFor()

  // Celular: vertical por padrão, a página sem rolagem lateral (o canvas tem a própria área).
  await pagina.setViewportSize({ width: 375, height: 812 })
  await pagina.goto(base)
  await modal.getByRole('tab', { name: 'Fluxo' }).click()
  await modal.locator('[data-no="saida"]').waitFor()
  await modal.locator('[data-no="grupo:roteiro"]').click()
  await modal.locator('[data-no="roteiro:1"]').waitFor()
  await pagina.waitForTimeout(500)
  await pagina.screenshot({ path: resolve(pasta, 'fluxo-375.png') })
  const a = await caixa('assistente'), s = await caixa('saida')
  assert.ok(Math.abs(a.x - s.x) < 2 && s.y > a.y, 'vertical por padrão no celular')
  await modal.getByRole('tab', { name: 'Uso e cota' }).click()
  await modal.getByText('850 mil de 1 mi tokens (85%)', { exact: true }).waitFor()
  const largura = await pagina.evaluate(() => document.documentElement.scrollWidth)
  assert.ok(largura <= 375, `sem rolagem lateral em 375 px (${largura})`)

  // Sem o painel entregue pelo App (cena isolada), o modal fica como antes: sem abas e com o caminho da Gestão.
  await pagina.setViewportSize({ width: 1280, height: 900 })
  await pagina.goto(base + '?painel=nao')
  await modal.getByText('Etapas, na ordem do robô', { exact: false }).waitFor()
  await modal.getByText(/Instruções que o Assistente recebe/).waitFor()
  assert.equal(await modal.getByRole('tab').count(), 0)
  await modal.getByText(/ficam em Gestão › Atendimento/).waitFor()

  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ httpControlado: true, abas, portoes, pedidos }, null, 2))
  console.log('#2855 navegador: o modal da conversa mostra Esta conversa (só as etapas que barram), Uso e cota em uma linha e o Fluxo como canvas (horizontal e vertical, arrastar, grupos, chave no nó); sem o App, fica como antes.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await contexto.close(); await browser.close() }
