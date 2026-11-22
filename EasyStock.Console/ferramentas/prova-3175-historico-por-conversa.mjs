// #3175: Chromium, a ModalAssistente real com o painel entregue pelo App e HTTP controlado (não usa a API real).
// Dentro da conversa o histórico lê só os turnos dela (`conversaId` na query); "Todas as conversas" refaz a leitura
// sem `conversaId` e na página 1; sem turno na conversa, estado vazio próprio; na Gestão nada muda e não há o controle.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/3175/historico-por-conversa')
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
const gestao = new URLSearchParams(location.search).get('gestao') === '1';
const painel = props => <PainelAssistente {...props}/>;
createRoot(document.getElementById('root')).render(<ContextoAcessoModulos.Provider value={{acoes:{editarAtendimento:true},permite:()=>true}}>
  {gestao ? <PainelAssistente podeEditar/> : <ContextoPainelDoAssistente.Provider value={painel}><ModalAssistente conversa={conversa} aoFechar={()=>{}}/></ContextoPainelDoAssistente.Provider>}
</ContextoAcessoModulos.Provider>);
`)

const checagem = { ativo: true, responde: true, bloqueio: null, entradasPendentes: 0, etapas: [], prompt: 'Você é o assistente da Casa.', ferramentas: [] }
const uso = { ano: 2026, mes: 11, turnos: 1, tokensEntrada: 1, tokensSaida: 1, tokensCacheLido: 0, totalTokens: 2, custoEstimado: 0, turnosSemPreco: 1,
  cotaTokensMes: null, percentual: null, alerta: false, respostasSemIa: 0, tokensPoupadosEstimados: 0 }
const fluxo = { ativo: true, modeloDisponivel: true, portoes: [], roteiro: [], roteiroPersonalizado: false, ferramentas: [], prompt: 'x', observacoesLoja: '' }
const turno = (id, conversaId, pergunta) => ({ id, conversaId, criadoEm: '2026-11-18T15:00:00Z', modelo: 'm', tokensEntrada: 4000, tokensSaida: 300,
  tokensCacheLido: 0, custoEstimado: null, ferramentas: [], passos: 1, desfecho: 'Respondeu', duracaoMs: 2400, perguntaCliente: pergunta, resposta: 'Ok.' })
let conversaSemTurnos = false

const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 900 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'admin' }, expiraEm: Date.now() + 600000 })))
const urls = [], erros = []
await contexto.route(url => url.pathname.startsWith('/api/'), async route => {
  const req = route.request(), u = new URL(req.url()), path = u.pathname
  const fora = () => route.fulfill({ status: 404, json: { error: { message: 'fora da prova: ' + path } } })
  if (req.method() !== 'GET') return fora()
  if (path === '/api/atendimento/conversas/c5/assistente') return route.fulfill({ json: { data: checagem } })
  if (path === '/api/atendimento/assistente/uso') return route.fulfill({ json: { data: uso } })
  if (path === '/api/atendimento/assistente/fluxo') return route.fulfill({ json: { data: fluxo } })
  if (path === '/api/atendimento/assistente/historico') {
    urls.push(u.searchParams)
    const daConversa = u.searchParams.has('conversaId')
    if (daConversa && conversaSemTurnos) return route.fulfill({ json: { data: { itens: [], total: 0 } } })
    const pergunta = daConversa ? 'Pergunta desta conversa' : 'Pergunta de qualquer conversa'
    return route.fulfill({ json: { data: { itens: [turno('t1', daConversa ? 'c5' : 'c9', pergunta)], total: 25 } } })
  }
  return fora()
})
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(10000)
pagina.on('pageerror', e => erros.push(e.message))
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/3175/historico-por-conversa/index.html`
const ultima = () => urls[urls.length - 1]
const aguardarLeituras = async (n) => { const t = Date.now(); while (urls.length < n && Date.now() - t < 8000) await pagina.waitForTimeout(50) }
try {
  // (a) No modal, o histórico lê só a conversa aberta.
  await pagina.goto(base)
  const modal = pagina.getByRole('dialog', { name: 'Assistente nesta conversa' })
  await modal.getByRole('tab', { name: 'Histórico' }).click()
  await modal.getByText('Pergunta desta conversa').waitFor()
  assert.equal(urls.length, 1)
  assert.equal(ultima().get('conversaId'), 'c5', 'GET do histórico leva conversaId')
  assert.equal(ultima().get('pagina'), '1')

  // Segunda página continua na conversa.
  await modal.getByRole('button', { name: 'Mais antigos' }).click()
  await aguardarLeituras(2)
  assert.equal(ultima().get('pagina'), '2')
  assert.equal(ultima().get('conversaId'), 'c5')

  // (b) "Todas as conversas": refaz o GET sem conversaId e volta à página 1.
  const todas = modal.getByRole('button', { name: 'Todas as conversas' })
  assert.equal(await todas.getAttribute('aria-pressed'), 'false')
  await todas.click()
  await modal.getByText('Pergunta de qualquer conversa').waitFor()
  assert.equal(urls.length, 3)
  assert.equal(ultima().has('conversaId'), false, 'sem conversaId em Todas as conversas')
  assert.equal(ultima().get('pagina'), '1', 'trocar o filtro volta à página 1')
  assert.equal(await todas.getAttribute('aria-pressed'), 'true')

  // Voltar para a conversa.
  await todas.click()
  await modal.getByText('Pergunta desta conversa').waitFor()
  assert.equal(ultima().get('conversaId'), 'c5')
  assert.equal(ultima().get('pagina'), '1')

  // (c) Conversa sem turnos: estado vazio próprio; Todas as conversas continua à mão.
  conversaSemTurnos = true
  await pagina.goto(base)
  await modal.getByRole('tab', { name: 'Histórico' }).click()
  await modal.getByText('Nenhum turno nesta conversa.').waitFor()
  assert.equal(await modal.getByText('Nenhum turno do Assistente registrado ainda.').count(), 0)
  await modal.getByRole('button', { name: 'Todas as conversas' }).click()
  await modal.getByText('Pergunta de qualquer conversa').waitFor()
  await pagina.screenshot({ path: resolve(pasta, 'modal-1280.png') })

  // (d) Na Gestão: sem conversaId e sem o controle.
  urls.length = 0
  await pagina.goto(base + '?gestao=1')
  await pagina.getByRole('tab', { name: 'Histórico' }).click()
  await pagina.getByText('Pergunta de qualquer conversa').waitFor()
  assert.equal(urls.length, 1)
  assert.equal(ultima().has('conversaId'), false, 'a Gestão não manda conversaId')
  assert.equal(await pagina.getByRole('button', { name: 'Todas as conversas' }).count(), 0, 'sem o controle na Gestão')

  assert.deepEqual(erros, [])
  console.log('#3175 navegador: no modal o histórico lê só a conversa (conversaId), Todas as conversas refaz sem ele na página 1, vazio próprio e a Gestão não muda.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await contexto.close(); await browser.close() }
