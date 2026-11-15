// #2495 (S5, B6): Chromium, componentes reais e HTTP controlado. Ao sair de Telefone ou Documento no editor de
// cliente, a tela avisa quem já tem os mesmos dígitos, com botão para abrir esse cadastro. A busca da API é por
// "contém": número parecido não avisa, o próprio cliente não conta e Salvar segue liberado.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2495/cadastros-duplicado')
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
const tela = new URLSearchParams(location.search).get('tela') ?? 'clientes';
function Cena(){const [rascunhos,definir]=useState({});const gravacoes=useGravacoesDosAjustes();return <ContextoAcessoModulos.Provider value={{acoes:{editarCadastrosClientes:true,verCadastrosClientes:true},permite:()=>true}}><ContextoEstado.Provider value={{fonteApi:true,conversas:[],selecionada:null}}><ContextoAcoes.Provider value={{}}><ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir:(chave,valor)=>definir(atual=>({...atual,[chave]:valor})),...gravacoes}}><PainelCadastros tela={tela}/></ContextoRascunhosDosAjustes.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoAcessoModulos.Provider>}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 1400 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'operador' }, expiraEm: Date.now() + 600000 })))
const clientes = new Map([
  ['A', { id: 'A', revisao: 1, nome: 'Ana Souza', telefone: '+5511999998888', email: '', documento: '', tipoPessoa: 'fisica', ativo: true }],
  ['B', { id: 'B', revisao: 1, nome: 'Bia Lima', telefone: '+5511977776666', email: '', documento: '12345678900', tipoPessoa: 'fisica', ativo: true }],
  ['C', { id: 'C', revisao: 1, nome: 'Caio Ramal', telefone: '+5511999998888777', email: '', documento: '', tipoPessoa: 'fisica', ativo: true }],
])
const buscas = [], escritas = [], erros = []
const digitos = (v) => String(v ?? '').replace(/\D/g, '')
await contexto.route('**/api/**', async route => {
  const req = route.request(), url = new URL(req.url()), path = url.pathname
  if (!path.startsWith('/api/')) return route.continue()
  const [, , recurso, id] = path.split('/')
  assert.equal(recurso, 'clientes', path)
  if (req.method() !== 'GET') { escritas.push({ metodo: req.method(), path }); return route.fulfill({ status: 500, json: { error: { message: 'Não grava nesta prova' } } }) }
  if (id) return route.fulfill({ json: { data: { cliente: clientes.get(id), alteracoes: [] } } })
  const termo = url.searchParams.get('search') ?? ''
  if (url.searchParams.get('pageSize') === '10') buscas.push(termo)
  // Como a API: "contém" em nome, telefone e documento.
  const achados = [...clientes.values()].filter(c => !termo || c.nome.includes(termo) || digitos(c.telefone).includes(termo) || digitos(c.documento).includes(termo))
  return route.fulfill({ json: { data: achados, meta: { total: achados.length } } })
})
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(10000)
pagina.on('pageerror', e => erros.push(e.message))
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2495/cadastros-duplicado/index.html`
const aviso = (editor) => editor.getByRole('status').filter({ hasText: 'Já existe' })
const esperarBuscas = async (n) => {
  for (let i = 0; i < 50 && buscas.length < n; i++) await pagina.waitForTimeout(100)
  assert.equal(buscas.length, n, `Esperava ${n} buscas de duplicado, houve ${buscas.length}`)
  await pagina.waitForTimeout(200)
}
try {
  await pagina.goto(base)
  await pagina.getByRole('row').filter({ hasText: 'Ana Souza' }).waitFor()

  // Novo cliente com o telefone da Ana: ao sair do campo, aviso com o nome e o botão para abrir.
  await pagina.getByRole('button', { name: 'Novo cliente', exact: true }).click()
  let editor = pagina.getByRole('dialog', { name: 'Novo cliente', exact: true }); await editor.waitFor()
  await editor.getByRole('textbox', { name: 'Nome', exact: true }).fill('Ana S.')
  const telefone = editor.getByRole('textbox', { name: 'Telefone', exact: true })
  await telefone.fill('(11) 99999-8888'); await telefone.blur()
  await aviso(editor).filter({ hasText: 'Já existe Ana Souza com este telefone.' }).waitFor()
  assert.equal(buscas.at(-1), '11999998888', 'Busca só com os dígitos, sem o 55')
  assert.equal(await aviso(editor).filter({ hasText: 'Caio' }).count(), 0, 'Quem só contém o número não conta')
  assert.equal(await editor.getByRole('button', { name: 'Salvar cadastro', exact: true }).isDisabled(), false, 'Salvar segue liberado')
  await pagina.screenshot({ path: resolve(pasta, 'aviso-telefone-1280.png'), fullPage: true })

  // Mudar o telefone tira o aviso; número parecido (contido no da Ana) não avisa.
  await telefone.fill('(11) 9999-9888')
  assert.equal(await aviso(editor).count(), 0, 'Mudar o campo tira o aviso')
  await telefone.blur(); await esperarBuscas(2)
  assert.equal(await aviso(editor).count(), 0, 'Número parecido não avisa')

  // Documento repetido também avisa.
  const documento = editor.getByRole('textbox', { name: 'CPF ou documento', exact: true })
  await documento.fill('123.456.789-00'); await documento.blur()
  await aviso(editor).filter({ hasText: 'Já existe Bia Lima com este documento.' }).waitFor()

  // Abrir o outro cadastro com o formulário alterado pede confirmação; Continuar aqui mantém tudo.
  await editor.getByRole('button', { name: 'Abrir cadastro de Bia Lima', exact: true }).click()
  let confirmar = pagina.getByRole('dialog', { name: 'Abrir o cadastro de Bia Lima?', exact: true }); await confirmar.waitFor()
  await confirmar.getByText('O que você digitou aqui não será salvo.').waitFor()
  await pagina.screenshot({ path: resolve(pasta, 'abrir-outro-1280.png'), fullPage: true })
  await confirmar.getByRole('button', { name: 'Continuar aqui', exact: true }).click()
  await confirmar.waitFor({ state: 'detached' })
  assert.equal(await editor.getByRole('textbox', { name: 'Nome', exact: true }).inputValue(), 'Ana S.')
  await editor.getByRole('button', { name: 'Abrir cadastro de Bia Lima', exact: true }).click()
  confirmar = pagina.getByRole('dialog', { name: 'Abrir o cadastro de Bia Lima?', exact: true }); await confirmar.waitFor()
  await confirmar.getByRole('button', { name: 'Abrir cadastro', exact: true }).click()
  editor = pagina.getByRole('dialog', { name: 'Editar cliente', exact: true }); await editor.waitFor()
  assert.equal(await editor.getByRole('textbox', { name: 'Nome', exact: true }).inputValue(), 'Bia Lima')

  // O próprio cliente não conta.
  const antes = buscas.length
  const documentoBia = editor.getByRole('textbox', { name: 'CPF ou documento', exact: true })
  await documentoBia.focus(); await documentoBia.blur(); await esperarBuscas(antes + 1)
  assert.equal(await aviso(editor).count(), 0, 'O próprio cliente não conta')
  assert.deepEqual(escritas, [], 'Nada foi gravado')
  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ httpControlado: true, buscas }, null, 2))
  console.log('#2495 S5 navegador: telefone e documento repetidos avisam com nome e botão; parecido e o próprio não avisam; salvar segue liberado.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await browser.close() }
