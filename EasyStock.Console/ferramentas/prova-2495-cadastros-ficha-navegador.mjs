// #2495 (S6, I4): Chromium, componentes reais e HTTP controlado. No editor de um cliente existente, a ficha
// mostra o resumo e os 5 pedidos mais recentes, adiciona e remove endereço estruturado e telefone extra.
// CEP inválido não envia; remover pede confirmação; a ficha não marca o formulário como alterado.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2495/cadastros-ficha')
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
function Cena(){const [rascunhos,definir]=useState({});const gravacoes=useGravacoesDosAjustes();return <ContextoAcessoModulos.Provider value={{acoes:{editarCadastrosClientes:true,verCadastrosClientes:true,verPedidos:true},permite:()=>true}}><ContextoEstado.Provider value={{fonteApi:true,conversas:[],selecionada:null}}><ContextoAcoes.Provider value={{}}><ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir:(chave,valor)=>definir(atual=>({...atual,[chave]:valor})),...gravacoes}}><PainelCadastros tela={tela}/></ContextoRascunhosDosAjustes.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoAcessoModulos.Provider>}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 1400 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'operador' }, expiraEm: Date.now() + 600000 })))
const cliente = { id: 'A', revisao: 1, nome: 'Ana Souza', telefone: '+5511999998888', email: '', documento: '', tipoPessoa: 'fisica', ativo: true, orderCount: 7, lastOrderAt: '2026-11-10T15:30:00Z' }
let enderecos = []
let telefones = [{ id: 'T1', clienteId: 'A', numero: '+5511955554444', whatsapp: true, principal: false }]
const pedidos = Array.from({ length: 5 }, (_, i) => ({ id: `P${i + 1}`, numeroDoDia: 10 + i, dataNumero: `2026-11-0${i + 1}`, criadoEm: `2026-11-0${i + 1}T12:00:00Z`, status: 'Entregue', total: 50 + i, clienteNome: 'Ana Souza' }))
const escritas = [], consultasDePedidos = [], erros = []
let proximo = 1, falharDetalhe = false
await contexto.route('**/api/**', async route => {
  const req = route.request(), url = new URL(req.url()), path = url.pathname
  if (!path.startsWith('/api/')) return route.continue()
  const [, , recurso, id, sub, subId] = path.split('/')
  if (recurso === 'pedidos') {
    if (id) return route.fulfill({ json: { data: { pedido: pedidos.find(p => p.id === id), itens: [], eventos: [], pagamentos: [] } } })
    consultasDePedidos.push(Object.fromEntries(url.searchParams))
    return route.fulfill({ json: { data: pedidos, meta: { totalCount: 7 } } })
  }
  assert.equal(recurso, 'clientes', path)
  if (req.method() === 'GET') {
    if (!id) return route.fulfill({ json: { data: [cliente], meta: { total: 1 } } })
    if (falharDetalhe) return route.fulfill({ status: 503, json: { error: { message: 'Fora do ar.' } } })
    // #2734: o editor do cliente lê também o dossiê (notas, tags, bloqueio e conversas).
    if (sub === 'dossie') return route.fulfill({ json: { data: { cliente, enderecos, tags: [], notas: [], ultimosPedidos: [], totalPedidos: 7, bloqueado: false, conversasRecentes: [], totalGasto: 0 } } })
    return route.fulfill({ json: { data: { cliente, enderecos, telefones, documentos: [], alteracoes: [] } } })
  }
  escritas.push({ metodo: req.method(), path, empresaId: url.searchParams.get('empresaId'), corpo: req.postDataJSON?.() ?? null })
  if (req.method() === 'POST' && sub === 'enderecos') { const novo = { id: `E${proximo++}`, clienteId: 'A', ...req.postDataJSON() }; enderecos = [...enderecos, novo]; return route.fulfill({ status: 201, json: { data: { id: novo.id } } }) }
  if (req.method() === 'POST' && sub === 'telefones') { const novo = { id: `T${++proximo}`, clienteId: 'A', ...req.postDataJSON() }; telefones = [...telefones, novo]; return route.fulfill({ status: 201, json: { data: { id: novo.id } } }) }
  if (req.method() === 'DELETE' && sub === 'enderecos') { enderecos = enderecos.filter(e => e.id !== subId); return route.fulfill({ status: 204, body: '' }) }
  if (req.method() === 'DELETE' && sub === 'telefones') { telefones = telefones.filter(t => t.id !== subId); return route.fulfill({ status: 204, body: '' }) }
  return route.fulfill({ status: 500, json: { error: { message: `Inesperado: ${req.method()} ${path}` } } })
})
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(10000)
pagina.on('pageerror', e => erros.push(e.message))
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2495/cadastros-ficha/index.html`
try {
  await pagina.goto(base)
  await pagina.getByRole('row').filter({ hasText: 'Ana Souza' }).waitFor()

  // Cliente novo não tem ficha.
  await pagina.getByRole('button', { name: 'Novo cliente', exact: true }).click()
  let editor = pagina.getByRole('dialog', { name: 'Novo cliente', exact: true }); await editor.waitFor()
  assert.equal(await editor.getByRole('region', { name: 'Pedidos' }).count(), 0, 'Cliente novo não mostra ficha')
  await editor.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await editor.waitFor({ state: 'detached' })

  // Resumo e os 5 pedidos mais recentes do cliente.
  await pagina.getByRole('button', { name: 'Abrir ficha de Ana Souza', exact: true }).click()
  editor = pagina.getByRole('dialog', { name: 'Ficha do cliente', exact: true }); await editor.waitFor()
  // #2849: a ficha abre em leitura. O resumo mostra os números com as palavras do Atendimento e o contato;
  // o formulário e o Salvar só aparecem depois de "Editar dados", com o foco no primeiro campo.
  const resumo = editor.getByRole('region', { name: 'Resumo de Ana Souza', exact: true })
  await resumo.getByLabel('Números do cliente').getByText('compras válidas', { exact: true }).waitFor()
  await resumo.getByLabel('Dados de contato').getByText('Telefone', { exact: true }).waitFor()
  assert.equal(await editor.getByRole('textbox', { name: 'Nome', exact: true }).count(), 0, 'formulário fechado na leitura')
  assert.equal(await editor.getByRole('button', { name: 'Salvar cadastro', exact: true }).count(), 0, 'sem Salvar na leitura')
  await pagina.screenshot({ path: resolve(pasta, 'ficha-leitura-1280.png'), fullPage: true })
  await editor.getByRole('button', { name: 'Editar dados', exact: true }).click()
  assert.equal(await editor.getByRole('textbox', { name: 'Nome', exact: true }).evaluate(el => el === document.activeElement), true, 'foco no primeiro campo')
  assert.equal(await editor.getByLabel('Dados de contato').count(), 0, 'o contato em leitura sai quando o formulário abre')
  const secaoPedidos = editor.getByRole('region', { name: 'Pedidos' })
  await secaoPedidos.getByText('7 pedidos').waitFor()
  await secaoPedidos.getByText('Último pedido em 10/11/2026, 12:30').waitFor()
  const ultimos = secaoPedidos.getByRole('list', { name: 'Últimos pedidos' }).getByRole('button')
  await ultimos.first().waitFor()
  assert.equal(await ultimos.count(), 5)
  assert.equal(consultasDePedidos.at(-1).clienteId, 'A')
  assert.equal(consultasDePedidos.at(-1).pageSize, '5')
  assert.equal(consultasDePedidos.at(-1).sort, 'criadoem')
  assert.equal(consultasDePedidos.at(-1).order, 'desc')
  await pagina.screenshot({ path: resolve(pasta, 'ficha-1280.png'), fullPage: true })

  // Abrir um pedido mostra o detalhe por cima; fechar volta ao editor.
  await ultimos.first().click()
  const detalhe = pagina.getByRole('dialog', { name: /^Pedido / }); await detalhe.waitFor(); await detalhe.getByText('Feito em').first().waitFor()
  await detalhe.getByRole('button', { name: 'Fechar', exact: true }).last().click()
  await detalhe.waitFor({ state: 'detached' })
  await editor.waitFor()

  // Endereço: CEP inválido não envia; válido envia bairro e CEP, e o primeiro vai como padrão.
  await editor.getByRole('tab', { name: /^Endereços/ }).click() // #3249: cada parte da ficha é uma aba
  const secaoEnderecos = editor.getByRole('region', { name: 'Endereços' })
  await secaoEnderecos.getByText('Nenhum endereço cadastrado.').waitFor()
  await secaoEnderecos.getByRole('button', { name: 'Adicionar endereço', exact: true }).click()
  let modal = pagina.getByRole('dialog', { name: 'Novo endereço', exact: true }); await modal.waitFor()
  await modal.getByRole('textbox', { name: 'Rua ou avenida', exact: true }).fill('Rua das Flores')
  await modal.getByRole('textbox', { name: 'Número', exact: true }).fill('10')
  await modal.getByRole('textbox', { name: 'CEP', exact: true }).fill('0123')
  await modal.getByRole('textbox', { name: 'CEP', exact: true }).press('Enter')
  await modal.getByText('CEP precisa de 8 dígitos').waitFor()
  assert.equal(await modal.getByRole('textbox', { name: 'CEP', exact: true }).getAttribute('aria-invalid'), 'true')
  assert.equal(await modal.getByRole('textbox', { name: 'CEP', exact: true }).evaluate(el => el === document.activeElement), true, 'Foco vai ao campo com erro')
  assert.equal(escritas.length, 0, 'CEP inválido não envia')
  await modal.getByRole('textbox', { name: 'CEP', exact: true }).fill('01234-567')
  await modal.getByRole('textbox', { name: 'Bairro', exact: true }).fill('Centro')
  await modal.getByRole('textbox', { name: 'Cidade', exact: true }).fill('São Paulo')
  await modal.getByRole('textbox', { name: 'UF', exact: true }).fill('sp')
  await pagina.screenshot({ path: resolve(pasta, 'novo-endereco-1280.png'), fullPage: true })
  await modal.getByRole('button', { name: 'Adicionar endereço', exact: true }).click()
  await modal.waitFor({ state: 'detached' })
  assert.deepEqual(escritas.at(-1), { metodo: 'POST', path: '/api/clientes/A/enderecos', empresaId: null, corpo: {
    empresaId: 'empresa', logradouro: 'Rua das Flores', numero: '10', complemento: null, bairro: 'Centro', cidade: 'São Paulo', estado: 'SP', cep: '01234567', referencia: null, padrao: true,
  } })
  await secaoEnderecos.getByText('Rua das Flores, 10').waitFor()
  await secaoEnderecos.getByText('Centro · São Paulo/SP · CEP 01234-567').waitFor()
  await editor.getByRole('status').filter({ hasText: 'Endereço adicionado.' }).waitFor()

  // Telefone extra: com DDD; WhatsApp sim ou não.
  // #2734: a seção passou a listar também o telefone principal, então o nome é só "Telefones".
  await editor.getByRole('tab', { name: /^Telefones/ }).click() // #3249: cada parte da ficha é uma aba
  const secaoTelefones = editor.getByRole('region', { name: 'Telefones', exact: true })
  await secaoTelefones.getByText('(11) 95555-4444').waitFor()
  await secaoTelefones.getByRole('button', { name: 'Adicionar telefone', exact: true }).click()
  modal = pagina.getByRole('dialog', { name: 'Novo telefone', exact: true }); await modal.waitFor()
  assert.equal(await editor.getByRole('status').filter({ hasText: 'Endereço adicionado.' }).count(), 0, 'Abrir outra ação limpa o aviso anterior')
  await modal.getByRole('textbox', { name: 'Telefone', exact: true }).fill('(11) 3333-2222')
  await modal.getByRole('button', { name: 'Adicionar telefone', exact: true }).click()
  await modal.waitFor({ state: 'detached' })
  assert.deepEqual(escritas.at(-1), { metodo: 'POST', path: '/api/clientes/A/telefones', empresaId: null, corpo: { empresaId: 'empresa', numero: '(11) 3333-2222', whatsapp: false } })
  await secaoTelefones.getByText('(11) 3333-2222').waitFor()

  // Remover pede confirmação e envia DELETE; Cancelar não envia.
  await editor.getByRole('tab', { name: /^Endereços/ }).click() // #3249: cada parte da ficha é uma aba
  const escritasAntes = escritas.length
  await secaoEnderecos.getByRole('button', { name: 'Remover endereço Rua das Flores, 10', exact: true }).click()
  modal = pagina.getByRole('dialog', { name: 'Remover endereço?', exact: true }); await modal.waitFor()
  await modal.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await modal.waitFor({ state: 'detached' })
  assert.equal(escritas.length, escritasAntes, 'Cancelar não envia')
  await secaoEnderecos.getByRole('button', { name: 'Remover endereço Rua das Flores, 10', exact: true }).click()
  modal = pagina.getByRole('dialog', { name: 'Remover endereço?', exact: true }); await modal.waitFor()
  await modal.getByRole('button', { name: 'Remover', exact: true }).click()
  await modal.waitFor({ state: 'detached' })
  assert.deepEqual(escritas.at(-1), { metodo: 'DELETE', path: '/api/clientes/A/enderecos/E1', empresaId: 'empresa', corpo: null })
  await secaoEnderecos.getByText('Nenhum endereço cadastrado.').waitFor()

  falharDetalhe = true
  await editor.getByRole('tab', { name: /^Telefones/ }).click() // #3249: cada parte da ficha é uma aba
  await secaoTelefones.getByRole('button', { name: 'Remover telefone (11) 95555-4444', exact: true }).click()
  modal = pagina.getByRole('dialog', { name: 'Remover telefone?', exact: true }); await modal.waitFor()
  await modal.getByRole('button', { name: 'Remover', exact: true }).click()
  await modal.waitFor({ state: 'detached' })
  assert.deepEqual(escritas.at(-1), { metodo: 'DELETE', path: '/api/clientes/A/telefones/T1', empresaId: 'empresa', corpo: null })
  await editor.getByRole('status').filter({ hasText: 'Telefone removido.' }).waitFor()
  assert.equal(await secaoTelefones.getByText('(11) 95555-4444').count(), 0, 'Gravou: some da lista mesmo se a recarga falhar')
  assert.equal(await editor.getByRole('alert').count(), 0, 'Recarga que falha não vira erro de gravação')
  falharDetalhe = false

  // A ficha grava sozinha: o formulário do cliente segue sem alteração para salvar.
  assert.equal(await editor.getByRole('button', { name: 'Salvar cadastro', exact: true }).isDisabled(), true, 'A ficha não marca o formulário como alterado')
  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ httpControlado: true, escritas, consultasDePedidos }, null, 2))
  console.log('#2495 S6 navegador: ficha com resumo e 5 pedidos; endereço com bairro e CEP; telefone extra; remover com confirmação.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await browser.close() }
