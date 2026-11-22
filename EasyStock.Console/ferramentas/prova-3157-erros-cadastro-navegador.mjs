// #3157 e #3192: Chromium, componentes reais e HTTP controlado. No Novo cliente, e-mail "abc@" e documento "123" ficam marcados
// no campo (texto, aria-invalid, aria-describedby) com o motivo no Salvar desligado, e o erro some ao corrigir; CPF válido passa;
// telefone de cliente existente avisa com o nome e o Salvar segue possível. Medido a 1440 e a 390.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/3157/erros-cadastro')
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
const acoes={abrirConversaDoCliente:async()=>{},abrirJanelaDoPedido:()=>{}};
function Cena(){const [rascunhos,definir]=useState({});const gravacoes=useGravacoesDosAjustes();return <ContextoAcessoModulos.Provider value={{acoes:{editarCadastrosClientes:true,verCadastrosClientes:true,atenderConversas:true},permite:()=>true}}><ContextoEstado.Provider value={{fonteApi:true,conversas:[],selecionada:null}}><ContextoAcoes.Provider value={acoes}><ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir:(chave,valor)=>definir(atual=>({...atual,[chave]:valor})),...gravacoes}}><PainelCadastros tela="clientes"/></ContextoRascunhosDosAjustes.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoAcessoModulos.Provider>}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const M = '5b2c8d1e-6a47-4d09-9b6e-1c2d3e4f5a6b'
const clientes = new Map([
  [M, { id: M, revisao: 1, nome: 'Mariana Lopes Teste', telefone: '+5511987654321', email: '', documento: '', tipoPessoa: 'fisica', ativo: true }],
])
const digitos = (v) => String(v ?? '').replace(/\D/g, '')
const escritas = [], erros = []
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1440, height: 1200 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'operador' }, expiraEm: Date.now() + 600000 })))
await contexto.route('**/api/**', async route => {
  const req = route.request(), url = new URL(req.url()), path = url.pathname
  if (!path.startsWith('/api/')) return route.continue()
  const [, , recurso, id] = path.split('/')
  if (recurso === 'pedidos') return route.fulfill({ json: { data: [], meta: { totalCount: 0 } } })
  assert.equal(recurso, 'clientes', path)
  if (req.method() === 'GET') {
    if (id) return route.fulfill({ json: { data: { cliente: clientes.get(id), enderecos: [], telefones: [], documentos: [], alteracoes: [] } } })
    const termo = url.searchParams.get('search') ?? ''
    // Como a API: "contém" em nome, telefone e documento.
    const achados = [...clientes.values()].filter(c => !termo || c.nome.includes(termo) || digitos(c.telefone).includes(termo) || digitos(c.documento).includes(termo))
    return route.fulfill({ json: { data: achados, meta: { total: achados.length } } })
  }
  escritas.push({ metodo: req.method(), path, corpo: req.postDataJSON?.() ?? null })
  return route.fulfill({ status: 500, json: { error: { message: 'Não grava nesta prova' } } })
})
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(10000)
pagina.on('pageerror', e => erros.push(e.message))
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/3157/erros-cadastro/index.html`
const semRolagemLateral = async (largura) => assert.ok(await pagina.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), `Sem rolagem lateral a ${largura}`)
const novoCliente = async () => {
  await pagina.getByRole('button', { name: 'Novo cliente', exact: true }).click()
  const editor = pagina.getByRole('dialog', { name: 'Novo cliente', exact: true }); await editor.waitFor()
  return editor
}
// Erro no campo: texto visível, aria-invalid e aria-describedby apontando para esse texto.
const erroNoCampo = async (editor, campo, texto) => {
  await editor.getByRole('alert').filter({ hasText: texto }).waitFor()
  assert.equal(await campo.getAttribute('aria-invalid'), 'true', 'O campo fica marcado')
  const ids = (await campo.getAttribute('aria-describedby') ?? '').split(' ').filter(Boolean)
  const lidos = await Promise.all(ids.map(id => pagina.locator(`[id="${id}"]`).textContent()))
  assert.ok(lidos.some(t => texto.test(t ?? '')), `aria-describedby leva ao texto do erro: ${lidos}`)
}
const salvar = (editor) => editor.getByRole('button', { name: 'Salvar cadastro', exact: true })
try {
  for (const largura of [1440, 390]) {
    await pagina.setViewportSize({ width: largura, height: largura === 390 ? 844 : 1200 })
    await pagina.goto(base)
    await pagina.getByText('Mariana Lopes Teste').first().waitFor()
    const editor = await novoCliente()
    await editor.getByRole('textbox', { name: 'Nome', exact: true }).fill('Teste Campos')
    assert.equal(await salvar(editor).isEnabled(), true, 'Com o nome, o Salvar liga')

    // 1. E-mail "abc@": texto no campo e motivo no Salvar; corrigir tira os dois.
    const email = editor.getByRole('textbox', { name: 'E-mail', exact: true })
    await email.fill('abc@'); await email.blur()
    await erroNoCampo(editor, email, /e-mail/i)
    assert.equal(await salvar(editor).isDisabled(), true, 'E-mail inválido desliga o Salvar')
    await editor.getByText('Corrija o e-mail para salvar.', { exact: true }).waitFor()
    await pagina.screenshot({ path: resolve(pasta, `email-${largura}.png`), fullPage: true })
    await email.fill('ana@exemplo.com'); await email.blur()
    assert.equal(await email.getAttribute('aria-invalid'), null, 'Corrigir tira o aria-invalid')
    assert.equal(await editor.getByRole('alert').filter({ hasText: /e-mail/i }).count(), 0, 'Corrigir tira o texto')
    assert.equal(await salvar(editor).isEnabled(), true, 'Corrigido, o Salvar volta')

    // 2. Documento "123": erro no campo e Salvar desligado; CPF válido passa; vazio é permitido.
    const documento = editor.getByRole('textbox', { name: 'CPF ou documento', exact: true })
    await documento.fill('123'); await documento.blur()
    await erroNoCampo(editor, documento, /CPF ou CNPJ/)
    assert.equal(await salvar(editor).isDisabled(), true, 'Documento inválido desliga o Salvar')
    await editor.getByText('Corrija o documento para salvar.', { exact: true }).waitFor()
    await pagina.screenshot({ path: resolve(pasta, `documento-${largura}.png`), fullPage: true })
    await documento.fill('529.982.247-26'); await documento.blur()
    await erroNoCampo(editor, documento, /CPF ou CNPJ/)
    await documento.fill('529.982.247-25'); await documento.blur()
    assert.equal(await documento.getAttribute('aria-invalid'), null, 'CPF válido passa')
    assert.equal(await salvar(editor).isEnabled(), true, 'CPF válido liga o Salvar')
    await documento.fill(''); await documento.blur()
    assert.equal(await documento.getAttribute('aria-invalid'), null, 'Vazio é permitido')
    assert.deepEqual(escritas, [], 'Nada inválido chega à API')

    // 3. Telefone de quem já existe: aviso com o nome, no campo; o Salvar segue possível.
    const telefone = editor.getByRole('textbox', { name: 'Telefone', exact: true })
    await telefone.fill('(11) 98765-4321'); await telefone.blur()
    await editor.getByRole('status').filter({ hasText: 'Já existe Mariana Lopes Teste com este telefone' }).waitFor()
    await editor.getByRole('button', { name: 'Abrir cadastro de Mariana Lopes Teste' }).waitFor()
    assert.equal(await telefone.getAttribute('aria-invalid'), null, 'Duplicado avisa, não marca erro')
    assert.equal(await salvar(editor).isEnabled(), true, 'Duplicado não bloqueia o Salvar')
    await pagina.screenshot({ path: resolve(pasta, `duplicado-${largura}.png`), fullPage: true })
    await semRolagemLateral(largura)
    if (largura === 1440) {
      await salvar(editor).click()
      for (let i = 0; i < 50 && !escritas.length; i++) await pagina.waitForTimeout(100)
      assert.equal(escritas.length, 1, 'Salvar com telefone repetido chega à API')
      escritas.length = 0
    }
  }
  assert.deepEqual(erros, [])
  console.log('#3157 e #3192 navegador: e-mail e documento com erro no campo e motivo no Salvar, somem ao corrigir; CPF válido passa; telefone repetido avisa com o nome sem bloquear; 1440 e 390.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await browser.close() }
