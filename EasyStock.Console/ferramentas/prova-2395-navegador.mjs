// Componentes reais em Chromium, HTTP controlado para atrasos/falhas. Não substitui prova com PostgreSQL/API.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_TEST_URL ?? 'http://127.0.0.1:5209'
const pasta = resolve(import.meta.dirname, '../.build/2395')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width, initial-scale=1"><div id="root"></div><script type="module" src="./browser.jsx"></script></html>')
writeFileSync(resolve(pasta, 'browser.jsx'), `
import React, { useState } from 'react'; import { createRoot } from 'react-dom/client';
import { ContextoEstado, ContextoAcoes } from '../../src/aplicacao/contextos';
import { ContextoAcessoModulos } from '../../src/aplicacao/acessoModulos';
import { ContextoRascunhosDosAjustes, useGravacoesDosAjustes } from '../../src/aplicacao/rascunhosDosAjustes';
import { PainelCadastros } from '../../src/features/gestao/cadastros/PainelCadastros';
import { Combobox } from '../../src/componentes/Combobox';
import '../../src/estilos/base.css';
document.body.style.cssText = 'margin:0;padding:16px;box-sizing:border-box;background:var(--fundo);font-family:var(--f-corpo);color:var(--tinta)';
function Teste() { const [rascunhos, definir] = useState({}); const gravacoes = useGravacoesDosAjustes(); const [valor, escolher] = useState('0'); const [selecionada, selecionar] = useState(null);
return <ContextoAcessoModulos.Provider value={{ acoes: {}, permite: () => true }}><ContextoEstado.Provider value={{ fonteApi:true, conversas:[], selecionada, sessao:{empresa:{id:'empresa'},usuario:{id:'operador'}} }}><ContextoAcoes.Provider value={{ selecionar:()=>{},carregarClienteDaConversa:()=>{} }}><ContextoRascunhosDosAjustes.Provider value={{ rascunhos, definir:(chave,valor)=>definir(atual=>({...atual,[chave]:valor})),...gravacoes }}>
<button onClick={()=>setTimeout(()=>selecionar({id:'contexto-restaurado',nome:'Contato restaurado',clienteId:null,mensagens:[]}),1000)}>Restaurar contexto após abrir editor</button>
<PainelCadastros tela="clientes" /><div style={{maxWidth:400,marginTop:24}}><Combobox rotulo="Opções para testar teclado" valor={valor} aoMudar={escolher} opcoes={Array.from({length:40},(_,i)=>({valor:String(i),rotulo:'Opção '+i}))} /></div>
</ContextoRascunhosDosAjustes.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoAcessoModulos.Provider> }
createRoot(document.getElementById('root')).render(<Teste/>);
`)
const browser = await chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'pt-BR', reducedMotion: 'reduce' })
const page = await context.newPage()
page.setDefaultTimeout(10000)
const erros = [], resultados = []
page.on('pageerror', e => erros.push(e.message))
const cliente = { id: 'cliente', nome: 'Ana Fixture', telefone: '11999887766', orderCount: 0, ativo: true, tipoPessoa: 'fisica' }
let detalhePendente, resolverDetalhe
await context.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'operador' }, expiraEm: Date.now() + 600000 })))
await context.route('**/api/clientes?*', route => route.fulfill({ json: { data: [cliente], meta: { total: 1, page: 1, limit: 20 } } }))
await context.route('**/api/clientes/cliente?*', async route => { detalhePendente = true; await new Promise(resolve => { resolverDetalhe = resolve }); await route.fulfill({ json: { data: { cliente, alteracoes: [] } } }).catch(() => {}) })
try {
  await page.goto(`${origem}/.build/2395/index.html`)
  await page.getByText('Ana Fixture', { exact: true }).waitFor()
  await page.setViewportSize({ width: 390, height: 844 })
  await page.getByRole('button', { name: 'Restaurar contexto após abrir editor', exact: true }).click()
  await page.getByRole('button', { name: 'Leads', exact: true }).click()
  await page.getByPlaceholder('Nome, telefone ou documento', { exact: true }).fill('Ana')
  await page.waitForTimeout(1200)
  assert.equal(await page.getByRole('button', { name: 'Leads', exact: true }).getAttribute('aria-pressed'), 'true', 'Restauração tardia não substitui filtro escolhido')
  assert.equal(await page.getByPlaceholder('Nome, telefone ou documento', { exact: true }).inputValue(), 'Ana')
  resultados.push('390px: Leads e busca escolhidos antes de restaurar contexto continuam ativos')
  await page.reload(); await page.setViewportSize({ width: 1440, height: 900 })
  await page.getByText('Ana Fixture', { exact: true }).waitFor()
  await page.getByRole('button', { name: 'Editar Ana Fixture' }).click()
  while (!detalhePendente) await new Promise(resolve => setTimeout(resolve, 10))
  await page.getByRole('button', { name: 'Novo cliente', exact: true }).click()
  await page.getByRole('textbox', { name: 'Nome', exact: true }).fill('Novo preservado')
  resolverDetalhe()
  await page.waitForTimeout(100)
  assert.equal(await page.getByRole('textbox', { name: 'Nome', exact: true }).inputValue(), 'Novo preservado')
  resultados.push('GET atrasado do Editar não substitui Novo já digitado')
  await page.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await page.getByRole('button', { name: 'Continuar editando' }).click()
  assert.equal(await page.getByRole('textbox', { name: 'Nome', exact: true }).inputValue(), 'Novo preservado')
  resultados.push('Cancelar descarte preserva entrada e editor')
  await page.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await page.getByRole('button', { name: 'Descartar', exact: true }).click()
  const combo = page.getByRole('combobox', { name: 'Opções para testar teclado' })
  await combo.focus(); await combo.press('End')
  const geometria = await combo.evaluate(el => { const opcao = document.getElementById(el.getAttribute('aria-activedescendant')), lista = document.getElementById(el.getAttribute('aria-controls')); return { opcao: opcao.getBoundingClientRect().bottom, lista: lista.getBoundingClientRect().bottom, scroll: lista.scrollTop } })
  assert.ok(geometria.scroll > 0 && geometria.opcao <= geometria.lista + 1)
  await combo.press('Enter'); assert.equal(await combo.inputValue(), 'Opção 39')
  await combo.focus(); await combo.press('ArrowDown'); await combo.press('Escape'); assert.equal(await combo.getAttribute('aria-expanded'), 'false')
  resultados.push('Combobox setas/End/Enter/Escape e opção ativa visível')
  await page.getByRole('button', { name: 'Restaurar contexto após abrir editor', exact: true }).click()
  await page.getByRole('button', { name: 'Novo cliente', exact: true }).click()
  await page.getByRole('textbox', { name: 'Nome', exact: true }).fill('Nome digitado antes da restauração')
  await page.waitForTimeout(1200)
  assert.equal(await page.getByRole('textbox', { name: 'Nome', exact: true }).inputValue(), 'Nome digitado antes da restauração')
  assert.equal(await page.getByRole('button', { name: 'Consultar com IA', exact: true }).count(), 0, 'Nova identidade não introduz IA no editor de outro contexto')
  await page.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await page.getByRole('button', { name: 'Continuar editando', exact: true }).click()
  assert.equal(await page.getByRole('textbox', { name: 'Nome', exact: true }).inputValue(), 'Nome digitado antes da restauração')
  await page.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await page.getByRole('button', { name: 'Descartar', exact: true }).click()
  await page.getByText('Contato restaurado', { exact: true }).waitFor()
  resultados.push('Restauração assíncrona da conversa preserva editor, campos e contexto de IA até fechar')
  for (const largura of [1440, 390]) {
    await page.setViewportSize({ width: largura, height: largura === 390 ? 844 : 900 })
    const tamanho = await page.evaluate(() => ({ conteudo: document.documentElement.scrollWidth, tela: innerWidth }))
    assert.ok(tamanho.conteudo <= tamanho.tela, `Sem overflow em ${largura}: ${JSON.stringify(tamanho)}`)
    await page.getByRole('button', { name: 'Cadastrar pessoa da conversa', exact: true }).click()
    const altura = await page.getByRole('combobox', { name: 'Tipo de pessoa' }).evaluate(e => e.getBoundingClientRect().height)
    assert.ok(altura >= (largura === 390 ? 48 : 40))
    await page.screenshot({ path: resolve(pasta, `cadastro-${largura}.png`), fullPage: true })
    await page.getByRole('button', { name: 'Cancelar', exact: true }).click()
    resultados.push(`Editor e tabela sem overflow em ${largura}px; alvo combobox ${altura}px`)
  }
  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ escopo: 'Chromium com HTTP controlado; não homologação de API/tenant/provedor', resultados }, null, 2))
  console.log(resultados.map(r => `ok ${r}`).join('\n'))
} catch (e) {
  await page.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true })
  throw e
} finally { resolverDetalhe?.(); await context.close(); await browser.close() }
