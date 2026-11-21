// #2853: Chromium, o PainelAssistente real da Gestão › Atendimento e HTTP controlado (não usa a API real).
// Uso: 85% da cota acende o alerta, custo sem preço não vira R$ 0 e salvar a cota manda só `cotaTokensMes`.
// Fluxo: 11 etapas fixas na ordem do turno, roteiro reordenado e salvo, ferramenta desligada com pergunta e a
// saída para a equipe sem chave. Histórico lista o turno. Fotos em 1280 e 375 px, sem rolagem lateral.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2853/painel-assistente')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {PainelAssistente} from '../../../src/features/gestao/atendimento/PainelAssistente';
import '../../../src/estilos/base.css';
for (const el of [document.documentElement, document.getElementById('root')]) el.style.cssText='height:auto;overflow:visible';
document.body.style.cssText='height:auto;overflow:visible;margin:0;padding:0 16px;background:var(--fundo);font-family:var(--f-corpo);color:var(--tinta)';
createRoot(document.getElementById('root')).render(<ContextoAcessoModulos.Provider value={{acoes:{editarAtendimento:true},permite:()=>true}}><PainelAssistente podeEditar={true}/></ContextoAcessoModulos.Provider>);
`)

const PORTOES = [
  ['situacao', 'Quem atende', false], ['vinculo', 'Cadastro da conversa', false], ['janela', 'Janela de 24 h', false],
  ['chave', 'Assistente na loja', true], ['modelo', 'Modelo de IA', true], ['canal', 'Canal', true],
  ['equipe', 'Contato da equipe', true], ['bloqueado', 'Cliente bloqueado', true], ['pausado', 'Encaminhado à responsável', true],
  ['limite', 'Respostas na última hora', true], ['cota', 'Cota do mês', true],
].map(([codigo, titulo, escala]) => ({ codigo, titulo, checa: `Checa ${titulo.toLowerCase()}.`, escala, quemMuda: null }))

let cota = 1_000_000
let desligadas = []
let roteiro = [
  { nome: 'Acolher', instrucao: 'Cumprimente pelo nome.', ativo: true },
  { nome: 'Entender o pedido', instrucao: 'Pergunte o que a pessoa quer.', ativo: true },
  { nome: 'Fechar', instrucao: 'Mande o resumo e o link.', ativo: true },
]
const FERRAMENTAS = [['consultar_pedido', 'Consulta o pedido'], ['enviar_link_pagamento', 'Envia o link'], ['escalar_para_dona', 'Passa à equipe']]
const uso = () => ({ ano: 2026, mes: 11, turnos: 120, tokensEntrada: 700_000, tokensSaida: 100_000, tokensCacheLido: 50_000,
  totalTokens: 850_000, custoEstimado: 0, turnosSemPreco: 120, cotaTokensMes: cota || null,
  percentual: cota ? Math.floor(850_000 * 100 / cota) : null, alerta: cota ? 850_000 * 100 / cota >= 80 : false,
  respostasSemIa: 20, tokensPoupadosEstimados: 170_000 })
const fluxo = () => ({ ativo: true, modeloDisponivel: true, portoes: PORTOES, roteiro, roteiroPersonalizado: true,
  ferramentas: FERRAMENTAS.map(([nome, descricao]) => ({ nome, descricao, ligada: !desligadas.includes(nome), travada: nome === 'escalar_para_dona' })),
  prompt: 'Você é o assistente virtual da loja.\nRegras (obrigatórias): ...', observacoesLoja: 'Domingo sem entrega.' })
const historico = { itens: [{ id: 't1', conversaId: 'c1', criadoEm: '2026-11-18T15:00:00Z', modelo: 'm', tokensEntrada: 4000, tokensSaida: 300,
  tokensCacheLido: 0, custoEstimado: null, ferramentas: [{ nome: 'consultar_pedido', ok: true }], passos: 2, desfecho: 'Respondeu',
  duracaoMs: 2400, perguntaCliente: 'Meu pedido sai que horas?', resposta: 'Sai às 18h.' }], total: 1 }

const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 900 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'admin' }, expiraEm: Date.now() + 600000 })))
const pedidos = [], erros = []
await contexto.route(url => url.pathname.startsWith('/api/'), async route => {
  const req = route.request(), path = new URL(req.url()).pathname
  const corpo = req.postDataJSON?.() ?? null
  pedidos.push({ metodo: req.method(), path, corpo })
  if (req.method() === 'GET' && path === '/api/atendimento/assistente/uso') return route.fulfill({ json: { data: uso() } })
  if (req.method() === 'GET' && path === '/api/atendimento/assistente/fluxo') return route.fulfill({ json: { data: fluxo() } })
  if (req.method() === 'GET' && path === '/api/atendimento/assistente/historico') return route.fulfill({ json: { data: historico } })
  if (req.method() === 'PUT' && path === '/api/atendimento/configuracao') {
    if ('cotaTokensMes' in corpo) cota = corpo.cotaTokensMes
    if ('roteiro' in corpo) roteiro = corpo.roteiro
    if ('ferramentasDesligadas' in corpo) desligadas = corpo.ferramentasDesligadas
    return route.fulfill({ json: { data: {} } })
  }
  return route.fulfill({ status: 404, json: { error: { message: 'fora da prova: ' + path } } })
})
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(10000)
pagina.on('pageerror', e => erros.push(e.message))
const perguntas = []
// #3011: a pergunta é o ModalConfirmar (o diálogo de cima), não mais o window.confirm: lê o texto e clica no verbo.
const confirmar = async (verbo) => {
  const dialogo = pagina.locator('dialog[open]').last()
  perguntas.push(await dialogo.locator('p').first().textContent())
  await dialogo.getByRole('button', { name: verbo, exact: true }).click()
}
const endereco = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2853/painel-assistente/index.html`
const putsDesde = (i) => pedidos.slice(i).filter(p => p.metodo === 'PUT').map(p => p.corpo)
try {
  await pagina.goto(endereco)
  const painel = pagina.getByRole('region', { name: 'Painel do Assistente' })
  await painel.waitFor()

  // Uso: 85% acende o alerta; sem preço o custo não vira R$ 0.
  await painel.getByText('850 mil de 1 mi tokens (85%)', { exact: true }).waitFor()
  assert.equal(await painel.locator('[data-tom]').getAttribute('data-tom'), 'aviso')
  await painel.getByText(/Passou de 80%/).waitFor()
  assert.equal(await painel.getByRole('meter').getAttribute('aria-valuenow'), '85')
  await painel.getByText(/custo sem preço configurado/).waitFor()
  // #2896 (S78): quantas respostas saíram sem IA e a estimativa do que pouparam.
  await painel.getByText('Respostas sem IA: 20 de 120 · cerca de 170 mil tokens poupados, pela média dos turnos com IA do mês', { exact: true }).waitFor()
  await pagina.screenshot({ path: resolve(pasta, 'uso-1280.png'), fullPage: true })

  let marca = pedidos.length
  await painel.getByLabel('Cota mensal (mil tokens)').fill('2000')
  await painel.getByRole('button', { name: 'Salvar cota' }).click()
  await painel.getByText('850 mil de 2 mi tokens (42%)', { exact: true }).waitFor()
  assert.deepEqual(putsDesde(marca), [{ cotaTokensMes: 2_000_000 }], 'só a cota vai no corpo')
  assert.equal(await painel.locator('[data-tom]').getAttribute('data-tom'), 'ok')

  // Fluxo: as 11 etapas fixas na ordem do turno, depois instruções, roteiro e ferramentas.
  await painel.getByRole('tab', { name: 'Fluxo' }).click()
  await painel.locator('[data-no="grupo:portoes"]').click()
  await painel.locator('[data-no="portao:cota"]').waitFor()
  const nos = await painel.locator('[data-no^="portao:"]').evaluateAll(b => b.map(x => x.dataset.no.slice(7)))
  assert.deepEqual(nos, PORTOES.map(p => p.codigo), 'as 11 etapas na ordem do turno')
  await painel.locator('[data-no="portao:cota"]').click()
  await painel.getByText(/Etapa fixa/).waitFor()
  await pagina.screenshot({ path: resolve(pasta, 'fluxo-1280.png'), fullPage: true })

  // Roteiro: a etapa 2 sobe e o roteiro salvo vai inteiro, na ordem nova.
  marca = pedidos.length
  await painel.locator('[data-no="grupo:roteiro"]').click()
  await painel.locator('[data-no="roteiro:1"]').click()
  await painel.getByRole('button', { name: 'Subir' }).click()
  await painel.getByText('Roteiro com mudanças não salvas.', { exact: true }).waitFor()
  await painel.getByRole('button', { name: 'Salvar roteiro' }).click()
  await painel.getByText('Salvo. O próximo turno do Assistente já usa.', { exact: true }).waitFor()
  assert.deepEqual(putsDesde(marca), [{ roteiro: [
    { nome: 'Entender o pedido', instrucao: 'Pergunte o que a pessoa quer.', ativo: true },
    { nome: 'Acolher', instrucao: 'Cumprimente pelo nome.', ativo: true },
    { nome: 'Fechar', instrucao: 'Mande o resumo e o link.', ativo: true },
  ] }])
  assert.equal(await painel.locator('[data-no="roteiro:0"]').innerText().then(t => t.startsWith('1. Entender o pedido')), true)

  // #3011: no Expandir, Esc com a pergunta de desligar aberta cancela só a pergunta: o Expandir segue aberto e nada é gravado.
  marca = pedidos.length
  await painel.getByRole('button', { name: 'Expandir' }).click()
  const expandido = pagina.getByRole('dialog', { name: 'Fluxo do atendimento' })
  await expandido.locator('[data-no="grupo:ferramentas"]').click()
  await expandido.locator('[data-no="ferramenta:consultar_pedido"]').click()
  await expandido.getByRole('switch', { name: 'Ligada na loja' }).click()
  await pagina.getByRole('button', { name: 'Desligar ferramenta', exact: true }).waitFor()
  await pagina.keyboard.press('Escape')
  await pagina.getByRole('button', { name: 'Desligar ferramenta', exact: true }).waitFor({ state: 'detached' })
  assert.equal(await expandido.isVisible(), true, 'Esc na pergunta não fecha o Expandir')
  assert.deepEqual(putsDesde(marca), [], 'Esc cancela sem gravar')
  await expandido.getByRole('button', { name: 'Recolher' }).click()
  await expandido.waitFor({ state: 'detached' })
  await painel.locator('[data-no="grupo:ferramentas"]').click() // o grupo aberto no Expandir segue aberto aqui: fecha para o passo seguinte abrir

  // Ferramenta: desligar pergunta antes e grava a lista; a saída para a equipe não tem chave.
  marca = pedidos.length
  await painel.locator('[data-no="grupo:ferramentas"]').click()
  await painel.locator('[data-no="ferramenta:consultar_pedido"]').click()
  await painel.getByRole('switch', { name: 'Ligada na loja' }).click()
  await confirmar('Desligar ferramenta')
  await painel.getByRole('switch', { name: 'Desligada na loja' }).waitFor()
  assert.equal(perguntas.length, 1, 'pergunta antes de desligar')
  assert.deepEqual(putsDesde(marca), [{ ferramentasDesligadas: ['consultar_pedido'] }])
  await painel.getByText('2 de 3 ligadas', { exact: true }).waitFor()
  await painel.locator('[data-no="ferramenta:escalar_para_dona"]').click()
  await painel.getByText('Sempre ligada: é o caminho do cliente até a equipe.', { exact: true }).waitFor()
  assert.equal(await painel.locator('[data-no="ferramenta:escalar_para_dona"]').getByRole('switch').count() + await painel.getByRole('switch', { name: /na loja/ }).count(), 0, 'saída para a equipe sem chave')
  await pagina.screenshot({ path: resolve(pasta, 'fluxo-ferramenta-1280.png'), fullPage: true })

  // Histórico.
  await painel.getByRole('tab', { name: 'Histórico' }).click()
  await painel.getByText('Meu pedido sai que horas?', { exact: false }).waitFor()
  await painel.getByText('Respondeu', { exact: true }).waitFor()

  await pagina.setViewportSize({ width: 375, height: 812 })
  await painel.getByRole('tab', { name: 'Fluxo' }).click()
  // Voltar do Histórico remonta o Fluxo com os grupos fechados: abre o roteiro de novo.
  await painel.locator('[data-no="grupo:roteiro"]').click()
  await painel.locator('[data-no="roteiro:2"]').waitFor()
  await painel.locator('[data-no="roteiro:2"]').click()
  await pagina.screenshot({ path: resolve(pasta, 'fluxo-375.png'), fullPage: true })
  const largura = await pagina.evaluate(() => document.documentElement.scrollWidth)
  assert.ok(largura <= 375, `sem rolagem lateral em 375 px (${largura})`)
  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ httpControlado: true, nos, perguntas, pedidos }, null, 2))
  console.log('#2853 navegador: alerta em 85%, custo sem preço honesto, cota/roteiro/ferramenta gravam só o campo, 11 etapas em ordem, histórico.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await contexto.close(); await browser.close() }
