// #2857: o modo Expandir do Fluxo, no Chromium, com o ModalAssistente real e HTTP controlado. Confere em 1280 e 375 px:
// o dialog com nome acessível e o desenho em quase toda a viewport; o ajuste do nó escolhido dentro da viewport (coluna à
// direita em 1280, gaveta em 375); Esc e Recolher fecham só o Expandir e devolvem o foco ao botão; e, no tema escuro, o
// contraste do botão de zoom do React Flow contra o ícone (cores computadas, mínimo 3:1).
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2857/expandir')
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
createRoot(document.getElementById('root')).render(<ContextoAcessoModulos.Provider value={{acoes:{editarAtendimento:true},permite:()=>true}}>
  <ContextoPainelDoAssistente.Provider value={props => <PainelAssistente {...props}/>}><ModalAssistente conversa={conversa} aoFechar={()=>{}}/></ContextoPainelDoAssistente.Provider>
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
const erros = []
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2857/expandir/index.html`

async function abrir(largura, altura, esquema = 'light') {
  const contexto = await browser.newContext({ viewport: { width: largura, height: altura }, locale: 'pt-BR', reducedMotion: 'reduce', colorScheme: esquema })
  await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'admin' }, expiraEm: Date.now() + 600000 })))
  await contexto.route(url => url.pathname.startsWith('/api/'), async route => {
    const req = route.request(), path = new URL(req.url()).pathname
    if (req.method() !== 'GET') return route.fulfill({ json: { data: {} } })
    if (path === '/api/atendimento/conversas/c5/assistente') return route.fulfill({ json: { data: checagem } })
    if (path === '/api/atendimento/assistente/uso') return route.fulfill({ json: { data: uso } })
    if (path === '/api/atendimento/assistente/fluxo') return route.fulfill({ json: { data: fluxo() } })
    if (path === '/api/atendimento/assistente/historico') return route.fulfill({ json: { data: historico } })
    return route.fulfill({ status: 404, json: { error: { message: 'fora da prova: ' + path } } })
  })
  const pagina = await contexto.newPage()
  pagina.setDefaultTimeout(10000)
  pagina.on('pageerror', e => erros.push(e.message))
  await pagina.goto(base)
  const modal = pagina.getByRole('dialog', { name: 'Assistente nesta conversa' })
  await modal.waitFor()
  await modal.getByRole('tab', { name: 'Fluxo' }).click()
  await modal.locator('[data-no="saida"]').waitFor()
  await pagina.waitForTimeout(400)
  return { contexto, pagina, modal }
}

const caixa = (loc) => loc.evaluate(e => { const r = e.getBoundingClientRect(); return { x: r.x, y: r.y, w: r.width, h: r.height } })
const dentro = (c, v, rotulo) => assert.ok(c.x >= -1 && c.y >= -1 && c.x + c.w <= v.width + 1 && c.y + c.h <= v.height + 1, `${rotulo} dentro da viewport: ${JSON.stringify(c)} em ${v.width}x${v.height}`)

// Luminância relativa (WCAG) de uma cor computada: rgb()/rgba() ou color(srgb r g b).
function luminancia(cor) {
  let m = cor.match(/^rgba?\(([^)]+)\)/), canais
  if (m) canais = m[1].split(/[ ,/]+/).filter(Boolean).slice(0, 3).map(Number).map(v => v / 255)
  else if ((m = cor.match(/^color\(srgb ([^)]+)\)/))) canais = m[1].split(/[ /]+/).filter(Boolean).slice(0, 3).map(Number)
  else throw new Error('formato de cor não tratado: ' + cor)
  const [r, g, b] = canais.map(v => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4))
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}
const contraste = (a, b) => { const [x, y] = [luminancia(a), luminancia(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05) }

async function expandir(pagina, modal) {
  const botao = modal.getByRole('button', { name: 'Expandir' })
  await botao.click()
  const dialogo = pagina.getByRole('dialog', { name: 'Fluxo do atendimento' })
  await dialogo.waitFor()
  await dialogo.locator('[data-no="saida"]').waitFor()
  await pagina.waitForTimeout(500)
  return { botao, dialogo }
}

async function cenario(largura, altura) {
  const { contexto, pagina, modal } = await abrir(largura, altura)
  const v = { width: largura, height: altura }
  const { botao, dialogo } = await expandir(pagina, modal)

  // O desenho ocupa quase a viewport inteira.
  const desenho = await caixa(dialogo.locator('.react-flow').first())
  const fracao = (desenho.w * desenho.h) / (largura * altura)
  assert.ok(fracao >= 0.85, `${largura}px: o desenho ocupa ${(fracao * 100).toFixed(0)}% da viewport (mínimo 85%)`)

  // Selecionar um nó deixa o ajuste visível dentro da viewport.
  await dialogo.locator('[data-no="equipe"]').click({ position: { x: 12, y: 8 } })
  const ajuste = dialogo.getByRole('complementary', { name: 'Ajuste do item escolhido' })
  await ajuste.waitFor()
  await pagina.waitForTimeout(300)
  assert.equal(await ajuste.isVisible(), true, `${largura}px: ajuste visível`)
  const aj = await caixa(ajuste)
  dentro(aj, v, `${largura}px: ajuste`)
  if (largura >= 768) {
    const des = await caixa(dialogo.locator('.react-flow').first())
    assert.ok(aj.x >= des.x + des.w - 2, `${largura}px: ajuste na coluna à direita do desenho (ajuste x=${aj.x}, desenho até ${des.x + des.w})`)
  } else {
    assert.ok(aj.y > altura * 0.3 && Math.abs(aj.y + aj.h - altura) <= 2, `${largura}px: ajuste é uma gaveta colada na base: ${JSON.stringify(aj)}`)
  }
  const fechar = dialogo.getByRole('button', { name: 'Fechar ajuste' })
  assert.equal(await fechar.isVisible(), true, `${largura}px: Fechar ajuste visível`)
  dentro(await caixa(fechar), v, `${largura}px: Fechar ajuste`)

  // Esc fecha só o Expandir: a conversa continua e o foco volta ao botão.
  await pagina.keyboard.press('Escape')
  await dialogo.waitFor({ state: 'detached' })
  assert.equal(await modal.isVisible(), true, `${largura}px: a janela da conversa continua aberta depois do Esc`)
  assert.equal(await botao.evaluate(e => e === document.activeElement), true, `${largura}px: foco volta ao botão Expandir depois do Esc`)

  // Recolher também fecha.
  const segundo = await expandir(pagina, modal)
  await segundo.dialogo.getByRole('button', { name: 'Recolher' }).click()
  await segundo.dialogo.waitFor({ state: 'detached' })
  assert.equal(await modal.isVisible(), true, `${largura}px: a conversa continua depois do Recolher`)
  assert.equal(await botao.evaluate(e => e === document.activeElement), true, `${largura}px: foco volta ao Expandir depois do Recolher`)
  await contexto.close()
}

async function escuro(largura, altura) {
  const { contexto, pagina, modal } = await abrir(largura, altura, 'dark')
  await pagina.evaluate(() => { document.documentElement.dataset.theme = 'dark' })
  const { dialogo } = await expandir(pagina, modal)
  const botaoZoom = dialogo.locator('.react-flow__controls-button').first()
  await botaoZoom.waitFor()
  const { fundo, icone, texto } = await botaoZoom.evaluate(e => ({ fundo: getComputedStyle(e).backgroundColor, icone: getComputedStyle(e.querySelector('svg')).fill, texto: getComputedStyle(e).color }))
  const corIcone = icone && icone !== 'none' ? icone : texto
  const c = contraste(fundo, corIcone)
  assert.ok(luminancia(fundo) < 0.5, `${largura}px escuro: fundo do botão de zoom escuro (${fundo})`)
  assert.ok(c >= 3, `${largura}px escuro: contraste do botão de zoom ${c.toFixed(2)}:1 (fundo ${fundo}, ícone ${corIcone}); mínimo 3:1`)
  await contexto.close()
  return c
}

try {
  await cenario(1280, 900)
  await cenario(375, 812)
  const c = await escuro(1280, 900)
  assert.deepEqual(erros, [], 'sem erro de página')
  console.log(`#2857 Expandir: dialog, ocupação >= 85%, ajuste na viewport, Esc e Recolher com foco de volta, contraste escuro ${c.toFixed(2)}:1`)
} catch (e) {
  if (erros.length) console.error(erros.join('\n'))
  throw e
} finally {
  await browser.close()
}
