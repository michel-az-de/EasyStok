// #2495 (S1, I6): Chromium, componentes reais e HTTP controlado. A ordem escolhida vai para a API, volta à
// página 1, marca aria-sort no cabeçalho Nome e continua igual ao reabrir a tela.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2495/cadastros-ordem')
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
function Cena(){const [rascunhos,definir]=useState({});const [montada,montar]=useState(true);const gravacoes=useGravacoesDosAjustes();return <ContextoAcessoModulos.Provider value={{acoes:{},permite:()=>true}}><ContextoEstado.Provider value={{fonteApi:true,conversas:[],selecionada:null}}><ContextoAcoes.Provider value={{}}><ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir:(chave,valor)=>definir(atual=>({...atual,[chave]:valor})),...gravacoes}}><button onClick={()=>montar(m=>!m)}>Remontar</button>{montada && <PainelCadastros tela="clientes"/>}</ContextoRascunhosDosAjustes.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoAcessoModulos.Provider>}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 1600 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'operador' }, expiraEm: Date.now() + 600000 })))
const pessoas = Array.from({ length: 45 }, (_, i) => ({ id: `C${i}`, revisao: 1, nome: `Pessoa ${String(i).padStart(2, '0')}`, telefone: '', email: '', ativo: true, orderCount: i % 5 }))
const consultas = [], erros = []
await contexto.route('**/api/**', async route => {
  const req = route.request(), url = new URL(req.url()), path = url.pathname
  if (!path.startsWith('/api/')) return route.continue()
  assert.equal(path, '/api/clientes', path)
  consultas.push(Object.fromEntries(url.searchParams))
  const page = Number(url.searchParams.get('page') ?? 1)
  return route.fulfill({ json: { data: pessoas.slice((page - 1) * 20, page * 20), meta: { total: pessoas.length } } })
})
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(10000)
pagina.on('pageerror', e => erros.push(e.message))
const ultima = () => consultas.at(-1)
const ariaSortDoNome = () => pagina.getByRole('columnheader', { name: 'Nome', exact: true }).getAttribute('aria-sort')
const esperarAriaSort = (valor) => pagina.waitForFunction(v => [...document.querySelectorAll('th')].find(th => th.textContent.includes('Nome'))?.getAttribute('aria-sort') === v, valor)
const endereco = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2495/cadastros-ordem/index.html`
try {
  await pagina.goto(endereco)
  await pagina.getByRole('row').filter({ hasText: 'Pessoa 00' }).waitFor()
  assert.equal(ultima().sort, undefined, 'A primeira abertura mantém a ordem padrão da API')
  assert.equal(await ariaSortDoNome(), 'ascending', 'Clientes abre por nome A a Z')

  // Página 2, depois troca a ordem: volta à página 1 com sort e order.
  await pagina.getByRole('button', { name: 'Próxima', exact: true }).click()
  await pagina.getByText('Página 2').waitFor()
  const porPedidos = pagina.waitForRequest(r => new URL(r.url()).searchParams.get('sort') === 'ordercount')
  await pagina.getByRole('combobox', { name: 'Ordem' }).click()
  await pagina.getByRole('option', { name: 'Mais pedidos', exact: true }).click()
  await porPedidos
  await pagina.getByText('Página 1').waitFor()
  assert.equal(ultima().order, 'desc'); assert.equal(ultima().page, '1')
  await esperarAriaSort('none')

  // Cabeçalho Nome: A a Z (padrão de clientes, sem sort), depois Z a A.
  await pagina.getByRole('button', { name: 'Nome', exact: true }).click()
  await esperarAriaSort('ascending')
  assert.equal(ultima().sort, undefined, 'Nome A a Z é a padrão de clientes: sem sort')
  const porNome = pagina.waitForRequest(r => new URL(r.url()).searchParams.get('sort') === 'nome')
  await pagina.getByRole('button', { name: 'Nome', exact: true }).click()
  await porNome
  await esperarAriaSort('descending')
  assert.equal(ultima().order, 'desc')
  await pagina.screenshot({ path: resolve(pasta, 'nome-za-1280.png'), fullPage: true })

  // Sair e voltar: a ordem continua.
  await pagina.getByRole('button', { name: 'Remontar', exact: true }).click()
  const reaberta = pagina.waitForRequest(r => new URL(r.url()).pathname === '/api/clientes')
  await pagina.getByRole('button', { name: 'Remontar', exact: true }).click()
  await reaberta
  await pagina.getByRole('row').filter({ hasText: 'Pessoa' }).first().waitFor()
  assert.equal(ultima().sort, 'nome'); assert.equal(ultima().order, 'desc')
  assert.equal(await ariaSortDoNome(), 'descending')
  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ httpControlado: true, consultas }, null, 2))
  console.log('#2495 S1 navegador: ordem vai para a API, volta à página 1, aria-sort no Nome e memória ao reabrir.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await browser.close() }
