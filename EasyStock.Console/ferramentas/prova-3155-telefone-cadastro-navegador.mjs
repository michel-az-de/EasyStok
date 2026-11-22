// #3155: Chromium, componentes reais e HTTP controlado. No Cadastro de clientes, telefone que não é número ("12") fica
// marcado no campo e o Salvar não grava; fixo e número de fora entram com o aviso de que não recebem Novo pedido; o
// telefone gravado que não mudou não trava a edição de outro campo. A Ficha marca o principal que o Novo pedido recusa,
// e a recusa do Novo pedido recebe o foco. Medido a 1440 e a 390.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/3155/telefone-cadastro')
mkdirSync(pasta, { recursive: true })
const RECUSA = 'Este número não é de celular. Corrija o telefone na Ficha.'
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React,{useState} from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoEstado,ContextoAcoes} from '../../../src/aplicacao/contextos';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {ContextoRascunhosDosAjustes,useGravacoesDosAjustes} from '../../../src/aplicacao/rascunhosDosAjustes';
import {PainelCadastros} from '../../../src/features/gestao/cadastros/PainelCadastros';
import '../../../src/estilos/base.css';
document.body.style.cssText='margin:0;padding:16px;box-sizing:border-box;background:var(--fundo);font-family:var(--f-corpo);color:var(--tinta)';
const acoes={abrirConversaDoCliente:async()=>{throw new Error(${JSON.stringify(RECUSA)})},abrirJanelaDoPedido:()=>{}};
function Cena(){const [rascunhos,definir]=useState({});const gravacoes=useGravacoesDosAjustes();return <ContextoAcessoModulos.Provider value={{acoes:{editarCadastrosClientes:true,verCadastrosClientes:true,atenderConversas:true},permite:()=>true}}><ContextoEstado.Provider value={{fonteApi:true,conversas:[],selecionada:null}}><ContextoAcoes.Provider value={acoes}><ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir:(chave,valor)=>definir(atual=>({...atual,[chave]:valor})),...gravacoes}}><PainelCadastros tela="clientes"/></ContextoRascunhosDosAjustes.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoAcessoModulos.Provider>}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const F = '3f2b8c1e-5a47-4d09-9b6e-1c2d3e4f5a6b', L = '4a3c9d2f-6b58-4e1a-8c7f-2d3e4f5a6b7c'
const clientes = new Map([
  [F, { id: F, revisao: 1, nome: 'Fixo Silva', telefone: '+551133334444', email: '', documento: '', tipoPessoa: 'fisica', ativo: true }],
  // Gravado antes da regra: a API guarda o telefone cru quando não casa (AtualizarClienteUseCase).
  [L, { id: L, revisao: 1, nome: 'Legado Souza', telefone: '12', email: '', documento: '', tipoPessoa: 'fisica', ativo: true }],
])
const dossie = (c) => ({ cliente: { id: c.id, nome: c.nome, telefone: c.telefone, email: null, observacoes: null, motivoBloqueio: null, fotoUrl: null }, enderecos: [], tags: [], notas: [], ultimosPedidos: [], itemFavorito: null, ultimaCompraEm: null, totalPedidos: 0, bloqueado: false, conversasRecentes: [], pedidoEmAndamento: null, contatoInformado: null, totalGasto: 0 })
const escritas = [], erros = []
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1440, height: 1200 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'operador' }, expiraEm: Date.now() + 600000 })))
await contexto.route('**/api/**', async route => {
  const req = route.request(), url = new URL(req.url()), path = url.pathname
  if (!path.startsWith('/api/')) return route.continue()
  const [, , recurso, id, sub] = path.split('/')
  if (recurso === 'pedidos') return route.fulfill({ json: { data: [], meta: { totalCount: 0 } } })
  assert.equal(recurso, 'clientes', path)
  if (req.method() === 'GET') {
    if (!id) return route.fulfill({ json: { data: [...clientes.values()], meta: { total: clientes.size } } })
    const c = clientes.get(id)
    if (sub === 'dossie') return route.fulfill({ json: { data: dossie(c) } })
    return route.fulfill({ json: { data: { cliente: c, enderecos: [], telefones: [{ id: `T${id}`, numero: c.telefone, principal: true, whatsapp: false }], documentos: [], alteracoes: [] } } })
  }
  escritas.push({ metodo: req.method(), path, corpo: req.postDataJSON?.() ?? null })
  if (['PUT', 'PATCH'].includes(req.method()) && id && !sub) {
    const c = clientes.get(id); Object.assign(c, { nome: req.postDataJSON().nome ?? c.nome, revisao: c.revisao + 1 })
    return route.fulfill({ json: { data: c } })
  }
  return route.fulfill({ status: 500, json: { error: { message: `Inesperado: ${req.method()} ${path}` } } })
})
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(10000)
pagina.on('pageerror', e => erros.push(e.message))
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/3155/telefone-cadastro/index.html`
const AVISO = 'Este número não recebe Novo pedido'
const semRolagemLateral = async (largura) => assert.ok(await pagina.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), `Sem rolagem lateral a ${largura}`)
const novoCliente = async () => {
  await pagina.getByRole('button', { name: 'Novo cliente', exact: true }).click()
  const editor = pagina.getByRole('dialog', { name: 'Novo cliente', exact: true }); await editor.waitFor()
  return editor
}
try {
  for (const largura of [1440, 390]) {
    await pagina.setViewportSize({ width: largura, height: largura === 390 ? 844 : 1200 })
    await pagina.goto(base)
    await pagina.getByRole('row').filter({ hasText: 'Fixo Silva' }).or(pagina.getByText('Fixo Silva').first()).first().waitFor()

    // 1. "12" é marcado no campo ao sair dele; Salvar não grava e devolve o foco ao telefone.
    const editor = await novoCliente()
    await editor.getByRole('textbox', { name: 'Nome', exact: true }).fill('Teste Doze')
    const telefone = editor.getByRole('textbox', { name: 'Telefone', exact: true })
    await telefone.fill('12'); await telefone.blur()
    await editor.getByText('Telefone incompleto', { exact: true }).waitFor()
    assert.equal(await telefone.getAttribute('aria-invalid'), 'true', 'O campo fica marcado')
    await editor.getByRole('textbox', { name: 'Nome', exact: true }).focus()
    await editor.getByRole('button', { name: 'Salvar cadastro', exact: true }).click()
    await pagina.waitForFunction(() => document.activeElement?.getAttribute('aria-invalid') === 'true')
    assert.equal(await telefone.evaluate(el => el === document.activeElement), true, 'O foco volta ao telefone recusado')
    assert.deepEqual(escritas, [], '"12" não chega à API')
    await pagina.screenshot({ path: resolve(pasta, `erro-${largura}.png`), fullPage: true })

    // 2. Fixo e número de outro país entram, com o aviso de que não recebem Novo pedido; celular não avisa.
    await telefone.fill('(11) 3333-4444')
    assert.equal(await telefone.getAttribute('aria-invalid'), null, 'Mudar o campo tira o erro')
    await editor.getByText(AVISO).waitFor()
    await telefone.fill('+44 20 7946 0958'); await telefone.blur()
    await editor.getByText(AVISO).waitFor()
    assert.equal(await editor.getByText('Telefone incompleto').count(), 0)
    await telefone.fill('(11) 98765-4321'); await telefone.blur()
    assert.equal(await editor.getByText(AVISO).count(), 0, 'Celular não avisa')
    await telefone.fill('(11) 3333-4444')
    await pagina.screenshot({ path: resolve(pasta, `aviso-${largura}.png`), fullPage: true })
    await semRolagemLateral(largura)
    await editor.getByRole('button', { name: 'Cancelar', exact: true }).click()
    const descartar = pagina.getByRole('button', { name: /Descartar/ })
    if (await descartar.count()) await descartar.first().click()
    await editor.waitFor({ state: 'detached' })
  }

  // 3. O telefone gravado antes da regra não trava a edição de outro campo.
  await pagina.setViewportSize({ width: 1440, height: 1200 })
  await pagina.getByRole('button', { name: 'Abrir ficha de Legado Souza', exact: true }).click()
  let ficha = pagina.getByRole('dialog', { name: 'Ficha do cliente', exact: true }); await ficha.waitFor()
  await ficha.getByRole('button', { name: 'Editar dados', exact: true }).click()
  await ficha.getByRole('textbox', { name: 'Nome', exact: true }).fill('Legado Souza Lima')
  await ficha.getByRole('button', { name: 'Salvar cadastro', exact: true }).click()
  for (let i = 0; i < 50 && !escritas.length; i++) await pagina.waitForTimeout(100)
  assert.equal(escritas.length, 1, 'Mudar só o nome grava')
  assert.equal(escritas[0].corpo?.nome, 'Legado Souza Lima')
  await ficha.waitFor({ state: 'detached' }).catch(() => {})
  await pagina.keyboard.press('Escape').catch(() => {})

  // 4. A Ficha marca o principal fixo com selo e motivo; contorno ou selo, nunca borda colorida de um lado só.
  await pagina.getByRole('button', { name: 'Abrir ficha de Fixo Silva', exact: true }).click()
  ficha = pagina.getByRole('dialog', { name: 'Ficha do cliente', exact: true }); await ficha.waitFor()
  const selo = ficha.getByText('Sem Novo pedido', { exact: true }); await selo.waitFor()
  await ficha.getByLabel('Telefones').getByText(AVISO).waitFor()
  const bordas = await selo.evaluate(el => { const s = getComputedStyle(el); return [s.borderLeftWidth, s.borderRightWidth, s.borderTopWidth, s.borderBottomWidth] })
  assert.equal(new Set(bordas).size, 1, `Selo sem borda de um lado só: ${bordas}`)
  await pagina.screenshot({ path: resolve(pasta, 'ficha-1440.png'), fullPage: true })
  await pagina.keyboard.press('Escape')
  await ficha.waitFor({ state: 'detached' })

  // 5. A recusa do Novo pedido recebe o foco (o botão desligado no "Abrindo…" o perdia).
  await pagina.getByRole('button', { name: 'Novo pedido de Fixo Silva', exact: true }).click()
  const recusa = pagina.getByRole('alert').filter({ hasText: RECUSA }); await recusa.waitFor()
  await pagina.waitForFunction(() => document.activeElement?.getAttribute('role') === 'alert')
  assert.equal(await recusa.evaluate(el => el === document.activeElement), true, 'O foco fica na recusa')
  await pagina.screenshot({ path: resolve(pasta, 'recusa-1440.png'), fullPage: true })

  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ httpControlado: true, escritas: escritas.length }, null, 2))
  console.log('#3155 navegador: "12" marcado e não gravado; fixo e de fora avisam sem bloquear; gravado antigo não trava; Ficha marca o principal; recusa do Novo pedido com foco; 1440 e 390.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await browser.close() }
