// #2496: Chromium, a ModalAssistente real e HTTP controlado (não usa a API real). Contato da equipe: o veredito diz
// que o Assistente não responde e por quê, as 10 etapas aparecem na ordem do robô com o estado, o prompt abre só
// para ler, e desligar a chave pergunta antes, grava só `ativo` e checa de novo. Fotos em 1280 e 375 px.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2496/checagem-assistente')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {ModalAssistente} from '../../../src/features/atendimento/ModalAssistente';
import '../../../src/estilos/base.css';
document.body.style.cssText='margin:0;background:var(--fundo);font-family:var(--f-corpo);color:var(--tinta)';
const conversa={id:'c5',nome:'Felipe Azevedo',contato:{nome:'Felipe Azevedo'},numeroComanda:'5',mensagens:[]};
createRoot(document.getElementById('root')).render(<ContextoAcessoModulos.Provider value={{acoes:{editarAtendimento:true},permite:()=>true}}><ModalAssistente conversa={conversa} aoFechar={()=>{}}/></ContextoAcessoModulos.Provider>);
`)

const etapa = (codigo, titulo, estado, detalhe) => ({ codigo, titulo, estado, detalhe })
const EQUIPE = 'Recebe os avisos da equipe: o Assistente só responde depois que alguém devolver esta conversa a ele.'
let ativo = true
const checagem = () => ({
  ativo,
  responde: false,
  bloqueio: ativo ? EQUIPE : 'Desligado: toda conversa vai para a equipe.',
  entradasPendentes: 3,
  etapas: [
    etapa('situacao', 'Quem atende', 'Passa', 'O Assistente atende esta conversa.'),
    etapa('vinculo', 'Cadastro da conversa', 'Passa', 'Sem revisão de cadastro pendente.'),
    etapa('janela', 'Janela de 24 h', 'Passa', 'O cliente escreveu nas últimas 24 h.'),
    ativo ? etapa('chave', 'Assistente na loja', 'Passa', 'Ligado: o Assistente responde sozinho.')
      : etapa('chave', 'Assistente na loja', 'Barra', 'Desligado: toda conversa vai para a equipe.'),
    etapa('modelo', 'Modelo de IA', 'Passa', 'Disponível no servidor.'),
    etapa('canal', 'Canal', 'Passa', 'O Assistente atende no WhatsApp.'),
    etapa('equipe', 'Contato da equipe', 'Barra', EQUIPE),
    etapa('bloqueado', 'Cliente bloqueado', 'Passa', 'Cadastro sem bloqueio.'),
    etapa('pausado', 'Encaminhado à responsável', 'Passa', 'Não foi encaminhado.'),
    etapa('limite', 'Respostas na última hora', 'NaoSeAplica', 'Sem limite configurado.'),
  ],
  prompt: 'Você é o assistente de atendimento da Casa.\nTom: acolhedor.\n\n## Dossiê\nCliente: Felipe Azevedo',
  ferramentas: [{ nome: 'consultar_pedido', descricao: 'Consulta o pedido' }, { nome: 'enviar_cardapio', descricao: 'Envia o cardápio' }],
})

const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 900 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'admin' }, expiraEm: Date.now() + 600000 })))
const pedidos = [], erros = []
// Só o /api da raiz: um glob **/api/** também pegaria os módulos de src/infra/api/ e a página ficaria em branco.
await contexto.route(url => url.pathname.startsWith('/api/'), async route => {
  const req = route.request(), path = new URL(req.url()).pathname
  pedidos.push({ metodo: req.method(), path, corpo: req.postDataJSON?.() ?? null })
  if (req.method() === 'GET' && path === '/api/atendimento/conversas/c5/assistente') return route.fulfill({ json: { data: checagem() } })
  if (req.method() === 'PUT' && path === '/api/atendimento/configuracao') { ativo = req.postDataJSON().ativo; return route.fulfill({ json: { data: { ativo } } }) }
  return route.fulfill({ status: 404, json: { error: { message: 'fora da prova: ' + path } } })
})
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(10000)
pagina.on('pageerror', e => erros.push(e.message))
const perguntas = []
pagina.on('dialog', d => { perguntas.push(d.message()); d.accept() })
const endereco = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2496/checagem-assistente/index.html`
try {
  await pagina.goto(endereco)
  const modal = pagina.getByRole('dialog', { name: 'Assistente nesta conversa', exact: true })
  await modal.waitFor()
  await modal.getByText('O Assistente não responde esta conversa.', { exact: true }).waitFor()
  assert.equal(await modal.locator('[data-veredito]').getAttribute('data-veredito'), 'perigo')
  await modal.locator('[data-veredito]').getByText(EQUIPE, { exact: true }).waitFor()

  const estados = await modal.locator('li[data-etapa]').evaluateAll(lis => lis.map(li => `${li.dataset.etapa}:${li.dataset.estado}`))
  assert.deepEqual(estados, ['situacao:passa', 'vinculo:passa', 'janela:passa', 'chave:passa', 'modelo:passa', 'canal:passa',
    'equipe:barra', 'bloqueado:passa', 'pausado:passa', 'limite:fora'], 'as 10 etapas na ordem do robô, com o estado')
  await modal.getByText('8 passam, 1 barram, 1 não se aplicam', { exact: true }).waitFor()
  // A cor nunca vai sozinha: cada etapa leva o rótulo do estado.
  assert.equal(await modal.locator('li[data-etapa="equipe"]').getByText('Barra', { exact: true }).count(), 1)

  const chave = modal.getByRole('switch', { name: 'Assistente ligado na loja' })
  assert.equal(await chave.isChecked(), true)
  await pagina.screenshot({ path: resolve(pasta, 'contato-da-equipe-1280.png'), fullPage: true })

  await modal.getByText(/Instruções que o Assistente recebe nesta conversa \(\d+ caracteres\)/).click()
  await modal.getByText('Tom: acolhedor.', { exact: false }).waitFor()
  await modal.getByText(/Ferramentas que ele pode usar \(2\)/).click()
  await modal.locator('code', { hasText: 'consultar_pedido' }).waitFor()
  await pagina.screenshot({ path: resolve(pasta, 'prompt-aberto-1280.png'), fullPage: true })

  // Desligar a loja toda pergunta antes, grava só `ativo` e checa de novo.
  const antes = pedidos.length
  await chave.click()
  await modal.getByRole('switch', { name: 'Assistente desligado na loja' }).waitFor()
  assert.equal(perguntas.length, 1, 'pergunta antes de desligar a loja toda')
  const novos = pedidos.slice(antes)
  assert.deepEqual(novos.map(p => `${p.metodo} ${p.path}`), ['PUT /api/atendimento/configuracao', 'GET /api/atendimento/conversas/c5/assistente'])
  assert.deepEqual(novos[0].corpo, { ativo: false }, 'só a chave vai no corpo')
  assert.equal(await modal.locator('li[data-etapa="chave"]').getAttribute('data-estado'), 'barra')

  // Ligar de novo não pergunta.
  await modal.getByRole('switch', { name: 'Assistente desligado na loja' }).click()
  await modal.getByRole('switch', { name: 'Assistente ligado na loja' }).waitFor()
  assert.equal(perguntas.length, 1, 'ligar não pergunta')

  await pagina.setViewportSize({ width: 375, height: 812 })
  await pagina.screenshot({ path: resolve(pasta, 'contato-da-equipe-375.png'), fullPage: true })
  const larguraDoDocumento = await pagina.evaluate(() => document.documentElement.scrollWidth)
  assert.ok(larguraDoDocumento <= 375, `sem rolagem lateral em 375 px (${larguraDoDocumento})`)
  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ httpControlado: true, estados, perguntas, pedidos }, null, 2))
  console.log('#2496 navegador: veredito, 10 etapas em ordem, prompt só leitura, chave com pergunta e PUT só de ativo.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await contexto.close(); await browser.close() }
