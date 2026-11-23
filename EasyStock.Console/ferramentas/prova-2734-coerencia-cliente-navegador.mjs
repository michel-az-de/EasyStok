// #2734: Chromium, componentes reais e HTTP controlado. O editor de Cadastros mostra o mesmo relacionamento da
// Ficha do Atendimento: nota interna sem clique, tags, bloqueio, valor total e conversas recentes. O telefone
// principal aparece primeiro e não sai pela lista. O atalho `#/m/cadastros/clientes/<id>` abre o cadastro.
// Nota e tag gravam sozinhas (com `empresaId` na query) e não marcam o formulário como alterado.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2734/coerencia-cliente')
mkdirSync(pasta, { recursive: true })
const ID = '3f2b8c1e-5a47-4d09-9b6e-1c2d3e4f5a6b'
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React,{useState} from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoEstado,ContextoAcoes,ContextoCatalogo} from '../../../src/aplicacao/contextos';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {ContextoRascunhosDosAjustes,useGravacoesDosAjustes} from '../../../src/aplicacao/rascunhosDosAjustes';
import {PainelCadastros} from '../../../src/features/gestao/cadastros/PainelCadastros';
import '../../../src/estilos/base.css';
document.body.style.cssText='margin:0;padding:16px;box-sizing:border-box;background:var(--fundo);font-family:var(--f-corpo);color:var(--tinta)';
const conversas=[{id:'C1',clienteId:'${ID}',nome:'Marina Teste',canal:'Chat do site',cliente:{}}];
const acoes={selecionar:id=>{window.__selecionada=id},carregarClienteDaConversa:id=>{(window.__lidas??=[]).push(id)},relerClienteDaConversa:id=>{(window.__relidas??=[]).push(id)}};
function Cena(){const [rascunhos,definir]=useState({});const gravacoes=useGravacoesDosAjustes();return <ContextoAcessoModulos.Provider value={{acoes:{editarCadastrosClientes:true,verCadastrosClientes:true,verPedidos:true},permite:()=>true}}><ContextoEstado.Provider value={{fonteApi:true,conversas,selecionada:null}}><ContextoAcoes.Provider value={acoes}><ContextoCatalogo.Provider value={{restricoes:['Lactose','Glúten']}}><ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir:(chave,valor)=>definir(atual=>({...atual,[chave]:valor})),...gravacoes}}><PainelCadastros tela="clientes"/></ContextoRascunhosDosAjustes.Provider></ContextoCatalogo.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoAcessoModulos.Provider>}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 1400 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'operador' }, expiraEm: Date.now() + 600000 })))
const cliente = { id: ID, revisao: 1, nome: 'Marina Teste', telefone: '+5511999998888', email: '', documento: '', tipoPessoa: 'fisica', ativo: true, observacoes: 'Portão azul.', orderCount: 7, lastOrderAt: '2026-11-10T15:30:00Z' }
let telefones = [
  { id: 'T2', clienteId: ID, numero: '+5511955554444', whatsapp: false, principal: false },
  { id: 'T1', clienteId: ID, numero: '+5511999998888', whatsapp: true, principal: true },
]
let notas = [
  { id: 'N3', texto: 'Alergia a camarão. Prefere entrega depois das 18h.', autor: 'Operadora', pedidoId: null, mensagemId: null, criadoEm: '2026-11-12T21:05:00' },
  { id: 'N2', texto: 'Pede troco para 100.', autor: 'Operadora', pedidoId: null, mensagemId: null, criadoEm: '2026-11-11T15:00:00' },
  { id: 'N1', texto: 'Cliente desde a inauguração.', autor: 'Dona', pedidoId: null, mensagemId: null, criadoEm: '2026-11-01T12:00:00' },
]
// O servidor devolve as tags na ordem em que foram criadas: a restrição chega depois da tag comum.
let tags = ['mora_perto', 'sem_lactose']
const dossie = () => ({
  cliente: { id: ID, nome: cliente.nome, telefone: cliente.telefone, email: null, observacoes: cliente.observacoes, motivoBloqueio: 'Pedido não retirado duas vezes.', fotoUrl: null },
  enderecos: [], tags: tags.map(tag => ({ tag, origem: 'manual', criadoEm: '2026-11-10T12:00:00' })), notas, ultimosPedidos: [],
  itemFavorito: { nome: 'Feijoada', pedidos: 3 }, ultimaCompraEm: '2026-11-10T15:30:00', totalPedidos: 7, bloqueado: true,
  conversasRecentes: [
    { id: 'C1', canal: 'ChatSite', situacao: 'Automatica', iniciadaEm: '2026-11-12T20:00:00', ultimaMensagemEm: '2026-11-12T21:05:00' },
    { id: 'C0', canal: 'WhatsApp', situacao: 'Encerrada', iniciadaEm: '2026-11-01T12:00:00', ultimaMensagemEm: '2026-11-01T13:00:00' },
  ],
  pedidoEmAndamento: null, contatoInformado: null, totalGasto: 412.5,
})
// Segunda pessoa: só abre pelo atalho, para provar que nada do cadastro anterior passa para o seguinte.
const ID_B = '7a1c9d2e-0b34-4f56-8a7b-9c0d1e2f3a4b'
const clienteB = { id: ID_B, revisao: 1, nome: 'Bruno Outro', telefone: '+5511944443333', email: '', documento: '', tipoPessoa: 'fisica', ativo: true, observacoes: '', orderCount: 0, lastOrderAt: null }
const dossieB = () => ({
  cliente: { id: ID_B, nome: clienteB.nome, telefone: clienteB.telefone, email: null, observacoes: null, motivoBloqueio: null, fotoUrl: null },
  enderecos: [], tags: [{ tag: 'vizinho', origem: 'manual', criadoEm: '2026-11-10T12:00:00' }], notas: [], ultimosPedidos: [],
  itemFavorito: null, ultimaCompraEm: null, totalPedidos: 0, bloqueado: false, conversasRecentes: [],
  pedidoEmAndamento: null, contatoInformado: null, totalGasto: 0,
})
const escritas = [], erros = []
let falharDossie = false
await contexto.route('**/api/**', async route => {
  const req = route.request(), url = new URL(req.url()), path = url.pathname
  if (!path.startsWith('/api/')) return route.continue()
  const [, , recurso, id, sub, subId] = path.split('/')
  if (recurso === 'pedidos') return route.fulfill({ json: { data: [], meta: { totalCount: 0 } } })
  assert.equal(recurso, 'clientes', path)
  if (req.method() === 'GET') {
    if (!id) return route.fulfill({ json: { data: [cliente], meta: { total: 1 } } })
    if (sub === 'dossie') return falharDossie ? route.fulfill({ status: 503, json: { error: { message: 'Fora do ar.' } } }) : route.fulfill({ json: { data: id === ID_B ? dossieB() : dossie() } })
    if (id === ID_B) return route.fulfill({ json: { data: { cliente: clienteB, enderecos: [], telefones: [], documentos: [], alteracoes: [] } } })
    return route.fulfill({ json: { data: { cliente, enderecos: [], telefones, documentos: [], alteracoes: [] } } })
  }
  escritas.push({ metodo: req.method(), path, empresaId: url.searchParams.get('empresaId'), corpo: req.postDataJSON?.() ?? null })
  if (req.method() === 'POST' && sub === 'notas') {
    const nova = { id: `N${notas.length + 1}`, texto: req.postDataJSON().texto, autor: 'Operadora', pedidoId: null, mensagemId: null, criadoEm: '2026-11-14T15:00:00' }
    notas = [nova, ...notas]
    return route.fulfill({ status: 201, json: { data: nova } })
  }
  if (req.method() === 'POST' && sub === 'tags') {
    const tag = req.postDataJSON().tag.toLowerCase().replaceAll(' ', '_')
    if (tag === 'antiga') return route.fulfill({ status: 409, json: { error: { message: 'Tag duplicada.' } } })
    tags = [...tags, tag]
    return route.fulfill({ status: 201, json: { data: { tag, origem: 'manual', criadoEm: '2026-11-14T15:00:00' } } })
  }
  if (req.method() === 'DELETE' && sub === 'tags') { tags = tags.filter(t => t !== decodeURIComponent(subId)); return route.fulfill({ status: 204, body: '' }) }
  if (req.method() === 'DELETE' && sub === 'telefones') { telefones = telefones.filter(t => t.id !== subId); return route.fulfill({ status: 204, body: '' }) }
  if (req.method() === 'PATCH' && !sub) { Object.assign(cliente, { observacoes: req.postDataJSON().observacoes, revisao: cliente.revisao + 1 }); return route.fulfill({ json: { data: cliente } }) }
  return route.fulfill({ status: 500, json: { error: { message: `Inesperado: ${req.method()} ${path}` } } })
})
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(10000)
pagina.on('pageerror', e => erros.push(e.message))
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2734/coerencia-cliente/index.html`
try {
  // O atalho da Ficha do Atendimento abre o cadastro e sai do endereço.
  await pagina.goto(`${base}#/m/cadastros/clientes/${ID}`)
  let editor = pagina.getByRole('dialog', { name: 'Ficha do cliente', exact: true }); await editor.waitFor(); await editor.getByRole('button', { name: 'Editar dados', exact: true }).click()
  assert.equal(new URL(pagina.url()).hash, '#/m/cadastros/clientes', 'o atalho sai do endereço depois de abrir o cadastro')
  await pagina.locator('table > caption').waitFor({ state: 'attached' })
  assert.equal(await pagina.locator('table > caption').textContent(), 'Leads e clientes', 'a tabela tem nome')
  assert.equal(await pagina.locator('section[aria-labelledby]').first().evaluate(s => document.getElementById(s.getAttribute('aria-labelledby'))?.textContent), 'Leads e clientes', 'a região da lista usa o título que está na tela como nome')
  assert.equal(await pagina.locator('th[scope="col"]').count(), 4, 'os cabeçalhos dizem que são de coluna')

  // A nota interna aparece sem clique: as 2 mais recentes, com quem escreveu e quando.
  let secaoNotas = editor.getByRole('region', { name: 'Notas internas', exact: true })
  await secaoNotas.getByText('Alergia a camarão. Prefere entrega depois das 18h.').waitFor()
  await secaoNotas.getByText('Operadora · 12/11, 18:05').waitFor()
  assert.equal(await secaoNotas.getByRole('listitem').count(), 2, 'recolhida, a lista mostra as 2 notas mais recentes')
  assert.equal(await secaoNotas.getByText('Cliente desde a inauguração.').count(), 0)
  await secaoNotas.getByRole('button', { name: 'Ver mais 1 nota', exact: true }).click()
  assert.equal(await secaoNotas.getByRole('listitem').count(), 3, 'aberta, a lista mostra todas')
  await secaoNotas.getByText('Dona · 01/11, 09:00').waitFor()

  // Bloqueio, tags e números do relacionamento, com as palavras da Ficha.
  await editor.getByText('Pedido não retirado duas vezes.').waitFor()
  assert.equal(await editor.getByText('Cadastro bloqueado', { exact: true }).count(), 1)
  let secaoTags = editor.getByRole('region', { name: 'Tags', exact: true })
  await secaoTags.getByText('sem lactose', { exact: true }).waitFor()
  // A restrição vem primeiro e com o alerta, como na Ficha do Atendimento.
  assert.deepEqual(await secaoTags.getByRole('listitem').evaluateAll(itens => itens.map(li => ({
    texto: li.querySelector(':scope > span > span')?.textContent, alerta: li.querySelectorAll(':scope > span > svg').length,
  }))), [{ texto: 'sem lactose', alerta: 1 }, { texto: 'mora perto', alerta: 0 }])
  // O alerta não fica só no ícone: quem usa leitor de tela ouve que a tag é restrição.
  assert.equal(await secaoTags.getByRole('img', { name: 'Restrição alimentar', exact: true }).count(), 1, 'o alerta da restrição tem nome')
  const secaoPedidos = editor.getByRole('region', { name: 'Pedidos', exact: true })
  await secaoPedidos.getByText('Valor total R$ 412,50 · Costuma pedir Feijoada').waitFor()

  // Telefones: o principal primeiro, com selo e sem Remover.
  await editor.getByRole('tab', { name: /^Telefones/ }).click() // #3249: cada parte da ficha é uma aba
  const secaoTelefones = editor.getByRole('region', { name: 'Telefones', exact: true })
  const linhasTelefone = secaoTelefones.getByRole('listitem')
  assert.equal(await linhasTelefone.count(), 2)
  assert.match(await linhasTelefone.first().innerText(), /\(11\) 99999-8888[\s\S]*Principal/)
  assert.equal(await secaoTelefones.getByRole('button', { name: 'Remover telefone (11) 99999-8888', exact: true }).count(), 0, 'o telefone principal não sai pela lista')
  assert.equal(await secaoTelefones.getByRole('button', { name: 'Remover telefone (11) 95555-4444', exact: true }).count(), 1)

  // Conversas recentes: só a que está carregada no Atendimento abre daqui.
  await editor.getByRole('tab', { name: /^Conversas/ }).click() // #3249: cada parte da ficha é uma aba
  const secaoConversas = editor.getByRole('region', { name: 'Conversas recentes', exact: true })
  await secaoConversas.getByText('Chat do site · Aberto · 12/11, 18:05').waitFor()
  await secaoConversas.getByText('WhatsApp · Encerrado · 01/11, 10:00').waitFor()
  assert.equal(await secaoConversas.getByRole('button', { name: /^Abrir conversa/ }).count(), 1)
  // A conversa sem botão tem explicação: a pessoa não fica procurando como abrir.
  await secaoConversas.getByText('Só a conversa que está na lista do Atendimento abre daqui. As outras ficam como registro.', { exact: true }).waitFor()
  await pagina.screenshot({ path: resolve(pasta, 'relacionamento-1280.png'), fullPage: true })

  // A14: no Novo telefone o número ganha a máscara enquanto a pessoa digita; sair com texto pergunta antes.
  await editor.getByRole('tab', { name: /^Telefones/ }).click() // #3249: cada parte da ficha é uma aba
  await secaoTelefones.getByRole('button', { name: 'Adicionar telefone', exact: true }).click()
  let modal = pagina.getByRole('dialog', { name: 'Novo telefone', exact: true }); await modal.waitFor()
  const campoTelefone = modal.getByRole('textbox', { name: 'Telefone', exact: true })
  await campoTelefone.pressSequentially('11977776666')
  assert.equal(await campoTelefone.inputValue(), '(11) 97777-6666')
  await modal.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await modal.getByText('Descartar o que foi digitado?').waitFor()
  await modal.getByRole('button', { name: 'Continuar editando', exact: true }).click()
  assert.equal(await campoTelefone.inputValue(), '(11) 97777-6666', 'continuar editando preserva o que foi digitado')
  await modal.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await modal.getByRole('button', { name: 'Descartar', exact: true }).click()
  await modal.waitFor({ state: 'detached' })
  // No Novo endereço o CEP ganha o hífen e o número da casa fica como foi digitado (não vira telefone).
  await editor.getByRole('tab', { name: /^Endereços/ }).click() // #3249: cada parte da ficha é uma aba
  await editor.getByRole('button', { name: 'Adicionar endereço', exact: true }).click()
  modal = pagina.getByRole('dialog', { name: 'Novo endereço', exact: true }); await modal.waitFor()
  await modal.getByRole('textbox', { name: 'CEP', exact: true }).pressSequentially('01001000')
  assert.equal(await modal.getByRole('textbox', { name: 'CEP', exact: true }).inputValue(), '01001-000')
  await modal.getByRole('textbox', { name: 'Número', exact: true }).pressSequentially('11977')
  assert.equal(await modal.getByRole('textbox', { name: 'Número', exact: true }).inputValue(), '11977')
  await modal.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await modal.getByRole('button', { name: 'Descartar', exact: true }).click()
  await modal.waitFor({ state: 'detached' })
  assert.equal(escritas.length, 0, 'telefone e endereço descartados não enviam nada')

  // O aviso de gravação entra numa região que já estava na tela: o leitor de tela só anuncia o que muda dentro dela.
  assert.ok(await editor.locator('[role="status"]').evaluateAll(nos => nos.filter(no => no.textContent === '').length) >= 2, 'as regiões de aviso existem vazias antes da gravação')
  await editor.locator('[role="status"]').evaluateAll(nos => nos.forEach(no => { if (!no.textContent) no.dataset.vivaAntes = '1' }))

  // Nota nova: limite em mensagem (o texto colado não é cortado), descarte perguntado e gravação com empresaId.
  await secaoNotas.getByRole('button', { name: 'Adicionar nota', exact: true }).click()
  modal = pagina.getByRole('dialog', { name: 'Nota interna', exact: true }); await modal.waitFor()
  const campoNota = modal.getByRole('textbox', { name: 'Texto da nota', exact: true })
  await campoNota.fill('a'.repeat(501))
  assert.equal((await campoNota.inputValue()).length, 501, 'o campo não corta o que foi colado')
  await modal.getByText('Use até 500 caracteres. Agora tem 501.').first().waitFor()
  await modal.getByRole('button', { name: 'Fixar nota', exact: true }).click()
  assert.equal(await campoNota.getAttribute('aria-invalid'), 'true')
  assert.equal(escritas.length, 0, 'nota acima do limite não envia')
  await campoNota.fill('Não gosta de coentro.')
  await campoNota.press('Escape')
  await modal.getByText('Descartar o que foi digitado?').waitFor()
  await modal.getByRole('button', { name: 'Continuar editando', exact: true }).click()
  assert.equal(await campoNota.inputValue(), 'Não gosta de coentro.')
  await modal.getByRole('button', { name: 'Fixar nota', exact: true }).click()
  await modal.waitFor({ state: 'detached' })
  assert.deepEqual(escritas.at(-1), { metodo: 'POST', path: `/api/clientes/${ID}/notas`, empresaId: 'empresa', corpo: { texto: 'Não gosta de coentro.' } })
  await secaoNotas.getByText('Não gosta de coentro.').waitFor()
  await editor.getByRole('status').filter({ hasText: 'Nota adicionada.' }).waitFor()
  assert.equal(await editor.locator('[role="status"][data-viva-antes]').filter({ hasText: 'Nota adicionada.' }).count(), 1, 'o aviso entra na região que já existia')
  assert.deepEqual(await pagina.evaluate(() => window.__relidas), ['C1'], 'a nota gravada no cadastro chega à Ficha da conversa carregada')

  // Tag: repetida não envia; a que o servidor recusa mostra o motivo no campo; a nova entra na lista.
  await secaoTags.getByRole('button', { name: 'Adicionar tag', exact: true }).click()
  modal = pagina.getByRole('dialog', { name: 'Nova tag', exact: true }); await modal.waitFor()
  const campoTag = modal.getByRole('textbox', { name: 'Tag', exact: true })
  const escritasAntes = escritas.length
  await campoTag.fill('Sem Lactose')
  await modal.getByRole('button', { name: 'Adicionar tag', exact: true }).click()
  await modal.getByText('Esta tag já existe.').waitFor()
  assert.equal(escritas.length, escritasAntes, 'tag repetida não envia')
  await campoTag.fill('antiga')
  await modal.getByRole('button', { name: 'Adicionar tag', exact: true }).click()
  await modal.getByText('Esta tag já existe.').waitFor()
  assert.equal(await campoTag.getAttribute('aria-invalid'), 'true', 'a recusa do servidor aparece no campo')
  await campoTag.fill('vip')
  await modal.getByRole('button', { name: 'Adicionar tag', exact: true }).click()
  await modal.waitFor({ state: 'detached' })
  assert.deepEqual(escritas.at(-1), { metodo: 'POST', path: `/api/clientes/${ID}/tags`, empresaId: 'empresa', corpo: { tag: 'vip' } })
  await secaoTags.getByText('vip', { exact: true }).waitFor()
  await editor.getByRole('status').filter({ hasText: 'Tag adicionada.' }).waitFor()
  assert.deepEqual(await pagina.evaluate(() => window.__relidas), ['C1', 'C1'], 'a tag gravada no cadastro chega à Ficha; a recusada não relê nada')

  // Tirar tag pede confirmação.
  await secaoTags.getByRole('button', { name: 'Tirar tag sem lactose', exact: true }).click()
  modal = pagina.getByRole('dialog', { name: 'Tirar tag?', exact: true }); await modal.waitFor()
  await modal.getByRole('button', { name: 'Tirar tag', exact: true }).click()
  await modal.waitFor({ state: 'detached' })
  assert.deepEqual(escritas.at(-1), { metodo: 'DELETE', path: `/api/clientes/${ID}/tags/sem_lactose`, empresaId: 'empresa', corpo: null })
  await editor.getByRole('status').filter({ hasText: 'Tag tirada.' }).waitFor()
  assert.equal(await secaoTags.getByText('sem lactose', { exact: true }).count(), 0)
  assert.deepEqual(await pagina.evaluate(() => window.__relidas), ['C1', 'C1', 'C1'], 'a tag tirada no cadastro sai da Ficha')
  // O chip que tinha o foco saiu da tela: o foco vai para o controle da mesma seção, não para o X do cadastro.
  await pagina.waitForFunction(() => document.activeElement?.textContent?.trim() === 'Adicionar tag', undefined, { timeout: 4000 })

  // Telefone removido: a Ficha do Atendimento relê a pessoa e o foco fica na seção de telefones.
  await editor.getByRole('tab', { name: /^Telefones/ }).click() // #3249: cada parte da ficha é uma aba
  await secaoTelefones.getByRole('button', { name: 'Remover telefone (11) 95555-4444', exact: true }).click()
  modal = pagina.getByRole('dialog', { name: 'Remover telefone?', exact: true }); await modal.waitFor()
  await modal.getByRole('button', { name: 'Remover', exact: true }).click()
  await modal.waitFor({ state: 'detached' })
  assert.deepEqual(escritas.at(-1), { metodo: 'DELETE', path: `/api/clientes/${ID}/telefones/T2`, empresaId: 'empresa', corpo: null })
  await editor.getByRole('status').filter({ hasText: 'Telefone removido.' }).waitFor()
  assert.equal(await linhasTelefone.count(), 1, 'o telefone removido sai da lista')
  assert.deepEqual(await pagina.evaluate(() => window.__relidas), ['C1', 'C1', 'C1', 'C1'], 'o telefone removido no cadastro chega à Ficha da conversa carregada')
  await pagina.waitForFunction(() => document.activeElement?.textContent?.trim() === 'Adicionar telefone', undefined, { timeout: 4000 })

  // Nota, tag e telefone gravam sozinhos: o formulário segue sem alteração para salvar.
  assert.equal(await editor.getByRole('button', { name: 'Salvar cadastro', exact: true }).isDisabled(), true, 'o relacionamento não marca o formulário como alterado')

  // Abrir a conversa carregada fecha o cadastro e seleciona a conversa.
  await editor.getByRole('tab', { name: /^Conversas/ }).click() // #3249: cada parte da ficha é uma aba
  await secaoConversas.getByRole('button', { name: /^Abrir conversa/ }).click()
  await editor.waitFor({ state: 'detached' })
  assert.equal(await pagina.evaluate(() => window.__selecionada), 'C1')

  // Salvar o cadastro relê a conversa carregada desse cliente, mesmo sem ela estar selecionada.
  await pagina.evaluate(id => { window.location.hash = `#/m/cadastros/clientes/${id}` }, ID)
  editor = pagina.getByRole('dialog', { name: 'Ficha do cliente', exact: true }); await editor.waitFor(); await editor.getByRole('button', { name: 'Editar dados', exact: true }).click()
  await editor.getByRole('textbox', { name: 'Observações', exact: true }).fill('Portão verde.')
  await editor.getByRole('button', { name: 'Salvar cadastro', exact: true }).click()
  await editor.waitFor({ state: 'detached' })
  assert.equal(escritas.at(-1).metodo, 'PATCH')
  assert.deepEqual(await pagina.evaluate(() => window.__relidas), ['C1', 'C1', 'C1', 'C1', 'C1'], 'a Ficha da conversa carregada recebe o cadastro salvo')
  assert.equal(await pagina.evaluate(() => window.__lidas), undefined, 'quem gravou pede uma leitura nova, não a que já estava em voo')

  // A primeira leitura que falha diz o que houve e deixa tentar de novo; o resto do cadastro continua na tela.
  falharDossie = true
  await pagina.evaluate(id => { window.location.hash = `#/m/cadastros/clientes/${id}` }, ID)
  editor = pagina.getByRole('dialog', { name: 'Ficha do cliente', exact: true }); await editor.waitFor(); await editor.getByRole('button', { name: 'Editar dados', exact: true }).click()
  const falha = editor.getByRole('alert').filter({ hasText: 'Fora do ar.' }); await falha.waitFor()
  assert.equal(await editor.getByRole('textbox', { name: 'Nome', exact: true }).inputValue(), 'Marina Teste')
  falharDossie = false
  await falha.getByRole('button', { name: 'Tentar de novo', exact: true }).click()
  secaoNotas = editor.getByRole('region', { name: 'Notas internas', exact: true })
  await secaoNotas.getByText('Não gosta de coentro.').waitFor()
  secaoTags = editor.getByRole('region', { name: 'Tags', exact: true })
  await secaoTags.getByText('vip', { exact: true }).waitFor()
  assert.equal(await editor.getByRole('alert').count(), 0)

  // O atalho que chega com uma nota sendo escrita espera: a nota vai para a pessoa em que foi escrita.
  await secaoNotas.getByRole('button', { name: 'Adicionar nota', exact: true }).click()
  modal = pagina.getByRole('dialog', { name: 'Nota interna', exact: true }); await modal.waitFor()
  await modal.getByRole('textbox', { name: 'Texto da nota', exact: true }).fill('Só da Marina.')
  await pagina.evaluate(id => { window.location.hash = `#/m/cadastros/clientes/${id}` }, ID_B)
  await pagina.waitForTimeout(500)
  assert.equal(await editor.getByRole('textbox', { name: 'Nome', exact: true }).inputValue(), 'Marina Teste', 'com nota sendo escrita, o cadastro não troca de pessoa')
  assert.equal(await modal.getByRole('textbox', { name: 'Texto da nota', exact: true }).inputValue(), 'Só da Marina.', 'o texto da nota continua na tela')
  await modal.getByRole('button', { name: 'Fixar nota', exact: true }).click()
  await modal.waitFor({ state: 'detached' })
  assert.deepEqual(escritas.at(-1), { metodo: 'POST', path: `/api/clientes/${ID}/notas`, empresaId: 'empresa', corpo: { texto: 'Só da Marina.' } })
  // Gravada a nota, o atalho que esperava abre a outra pessoa, sem o aviso nem as notas da anterior.
  await editor.getByRole('region', { name: 'Tags', exact: true }).getByText('vizinho', { exact: true }).waitFor()
  assert.equal(await editor.getByRole('textbox', { name: 'Nome', exact: true }).inputValue(), 'Bruno Outro')
  assert.equal(new URL(pagina.url()).hash, '#/m/cadastros/clientes', 'o atalho sai do endereço depois de abrir a outra pessoa')
  assert.equal(await editor.getByRole('status').filter({ hasText: 'Nota adicionada.' }).count(), 0, 'o aviso da nota não passa para o cadastro seguinte')
  await editor.getByRole('region', { name: 'Notas internas', exact: true }).getByText('Nenhuma nota interna.').waitFor()
  assert.equal(await pagina.getByRole('dialog', { name: 'Nota interna', exact: true }).count(), 0)

  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ httpControlado: true, escritas }, null, 2))
  console.log('#2734 navegador: cadastro mostra nota, tag, bloqueio, valor total, telefone principal e conversas; atalho por endereço; nota e tag gravam sozinhas.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await browser.close() }
