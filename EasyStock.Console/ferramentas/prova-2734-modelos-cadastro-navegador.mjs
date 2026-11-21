// #2734 (B2, A19 A20 A34 T9 T16): Chromium e componentes reais de Cadastros > Modelos. As abas são um tablist
// com o nome da tela de respostas prontas; o editor de modelo não mostra a bolha nativa e avisa o limite de 150
// enquanto digita, explicando o botão parado; o editor de etiqueta lista os campos que o desenho usa.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2734/modelos')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React,{useState} from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoEstado,ContextoAcoes} from '../../../src/aplicacao/contextos';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {ContextoRascunhosDosAjustes,useGravacoesDosAjustes} from '../../../src/aplicacao/rascunhosDosAjustes';
import {PainelCadastros} from '../../../src/features/gestao/cadastros/PainelCadastros';
import '../../../src/estilos/base.css';
document.body.style.cssText='margin:0;padding:16px;box-sizing:border-box;background:var(--fundo);font-family:var(--f-corpo);color:var(--tinta)';
function Cena(){const [rascunhos,definir]=useState({});const gravacoes=useGravacoesDosAjustes();return <ContextoAcessoModulos.Provider value={{acoes:{verModelosNotificacao:true,verCadastrosEtiquetas:true},permite:()=>true}}><ContextoEstado.Provider value={{fonteApi:true,conversas:[],selecionada:null}}><ContextoAcoes.Provider value={{}}><ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir:(chave,valor)=>definir(atual=>({...atual,[chave]:valor})),...gravacoes}}><PainelCadastros tela="modelos" respostas={<p>Conteúdo das respostas</p>} impressao={<p>Conteúdo da impressão</p>}/></ContextoRascunhosDosAjustes.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoAcessoModulos.Provider>}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 1000 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'operador' }, expiraEm: Date.now() + 600000 })))
const layout = { size: { w_mm: 60, h_mm: 40 }, elements: [{ id: 'a', type: 'text', content: '{produto} por {preco}' }, { id: 'b', type: 'text', content: 'Validade {validade}, {preco}' }, { id: 'c', type: 'barcode', content: '{codigo}' }] }
const escritas = [], erros = []
await contexto.route('**/api/**', async route => {
  const req = route.request(), path = new URL(req.url()).pathname
  if (!path.startsWith('/api/')) return route.continue()
  if (req.method() !== 'GET') { escritas.push(`${req.method()} ${path}`); return route.fulfill({ status: 500, json: { error: { message: 'Não deveria gravar.' } } }) }
  if (path === '/api/notificacoes/templates') return route.fulfill({ json: { data: [], meta: { totalCount: 0 } } })
  if (path === '/api/etiquetas/templates') return route.fulfill({ json: { data: [{ id: 'E1', origem: 'Empresa', nome: 'Gôndola', descricao: 'Preço na gôndola', isDefault: false, layoutJson: JSON.stringify(layout) }] } })
  return route.fulfill({ status: 500, json: { error: { message: `Inesperado: ${path}` } } })
})
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(8000)
pagina.on('pageerror', e => erros.push(e.message))
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2734/modelos/index.html`
try {
  await pagina.goto(base)
  // A34 e T9: tablist, com o nome da tela de respostas prontas. As abas se chamam pelo que escolhem ("Tipos de
  // modelo"), como antes de virarem tablist; "Painéis" é o nome da barra do Atendimento.
  const lista = pagina.getByRole('tablist', { name: 'Tipos de modelo', exact: true })
  await lista.waitFor()
  await pagina.getByRole('navigation', { name: 'Tipos de modelo', exact: true }).waitFor()
  assert.deepEqual(await lista.getByRole('tab').allInnerTexts(), ['Respostas prontas', 'E-mail e SMS', 'Etiquetas', 'Impressão'])
  assert.equal(await pagina.getByRole('tab', { name: 'Respostas prontas', exact: true }).getAttribute('aria-selected'), 'true')
  await pagina.getByText('Conteúdo das respostas', { exact: true }).waitFor()
  await pagina.getByRole('tab', { name: 'E-mail e SMS', exact: true }).click()
  assert.equal(await pagina.getByRole('tab', { name: 'E-mail e SMS', exact: true }).getAttribute('aria-selected'), 'true')

  // A19/A20: sem bolha nativa, erro no campo, contagem enquanto digita, botão parado com o porquê.
  await pagina.getByRole('button', { name: 'Novo modelo', exact: true }).click()
  const dialogo = pagina.getByRole('dialog', { name: 'Novo modelo', exact: true }); await dialogo.waitFor()
  assert.equal(await dialogo.locator('form').evaluate(f => f.noValidate), true, 'Formulário com noValidate')
  await dialogo.getByRole('button', { name: 'Salvar modelo', exact: true }).click()
  await dialogo.getByText('Informe o nome do modelo.', { exact: true }).waitFor()
  await dialogo.getByText('Informe a identificação do modelo.', { exact: true }).waitFor()
  await dialogo.getByText('Escreva a mensagem.', { exact: true }).waitFor()
  assert.deepEqual(escritas, [], 'Formulário inválido não envia')
  const nome = dialogo.getByRole('textbox', { name: /^Nome do modelo/ })
  await nome.fill('Aviso de pedido')
  await dialogo.getByText('15 de 150 caracteres', { exact: true }).waitFor()
  await nome.fill('n'.repeat(151))
  await dialogo.getByText('Use até 150 caracteres. Agora tem 151.', { exact: true }).waitFor()
  await dialogo.getByText('Use até 150 caracteres no nome do modelo.', { exact: true }).waitFor()
  assert.equal(await dialogo.getByRole('button', { name: 'Salvar modelo', exact: true }).isDisabled(), true)
  await nome.fill('n'.repeat(150))
  assert.equal(await dialogo.getByRole('button', { name: 'Salvar modelo', exact: true }).isDisabled(), false)
  await dialogo.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await pagina.getByRole('button', { name: 'Descartar', exact: true }).click()
  await dialogo.waitFor({ state: 'detached' })

  // T16: o editor de etiqueta lista os campos que o desenho usa, sem repetir.
  await pagina.getByRole('tab', { name: 'Etiquetas', exact: true }).click()
  await pagina.getByRole('button', { name: 'Criar a partir deste', exact: true }).click()
  const etiqueta = pagina.getByRole('dialog', { name: 'Criar modelo de etiqueta', exact: true }); await etiqueta.waitFor()
  await etiqueta.getByText('Campos deste modelo: {codigo}, {preco}, {produto}, {validade}.', { exact: true }).waitFor()
  assert.deepEqual(erros, [])
  console.log('#2734 B2 modelos: tablist, nome da tela, noValidate com erro no campo, limite à vista, campos da etiqueta.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await browser.close() }
