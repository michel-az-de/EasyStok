// App real; quota do navegador bloqueada somente para a visita. HTTP integralmente controlado.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2395/storage')
mkdirSync(pasta, { recursive: true })
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 900 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => {
  sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja de teste' }, usuario: { id: 'dona', nome: 'Dona de teste' } }))
  const gravar = Storage.prototype.setItem
  Storage.prototype.setItem = function(chave, valor) {
    if (chave.startsWith('easystok.visita:')) throw new DOMException('Quota de teste', 'QuotaExceededError')
    return gravar.call(this, chave, valor)
  }
})
const chamadas = [], erros = []
await contexto.route('**/api/**', route => {
  const path = new URL(route.request().url()).pathname
  if (!path.startsWith('/api/')) return route.continue()
  chamadas.push({ path, metodo: route.request().method() })
  const ok = data => route.fulfill({ json: { data } })
  if (path === '/api/auth/me/modulos') return ok({ modulos: ['atendimento', 'cardapio'].map(id => ({ id, liberado: true })), acoes: { editarAtendimento: true, editarCardapio: true } })
  if (path === '/api/atendimento/conversas') return ok([{ id: 'conversa', contatoNome: 'Cliente da quota', contatoIdExterno: '5511999000000', clienteId: 'cliente', vinculoRevisao: 0, canal: 'WhatsApp', situacao: 'Assumida', assumidaPorUsuarioId: 'dona', ultimaMensagemEm: new Date().toISOString(), naoLidas: 0 }])
  if (path === '/api/atendimento/comanda/cardapio') return ok({ itens: [{ id: 'prato', nome: 'Lasanha da quota', linha: 'paraServir', pesoExibicao: '600 g', precoCentavos: 3700, disponivel: true }], porcoes: {} })
  if (path.endsWith('/mensagens')) return ok([{ id: 'mensagem', direcao: 'Entrada', texto: 'Quero uma lasanha.', enviadaEm: new Date().toISOString(), tipoConteudo: 'Texto' }])
  if (path.endsWith('/pedido')) return ok(null)
  if (path.endsWith('/dossie')) return ok({ cliente: { id: 'cliente', nome: 'Cliente da quota', telefone: '+5511999000000' }, enderecos: [], totalPedidos: 0, notas: [], tags: [] })
  if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
  if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: true, dias: [] })
  return ok([])
})
const pagina = await contexto.newPage()
pagina.on('pageerror', erro => erros.push(erro.message))
try {
  await pagina.goto(`${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/#/m/atendimento/balcao`)
  await pagina.getByRole('button', { name: /^Todas/ }).click()
  await pagina.getByText('Cliente da quota', { exact: true }).first().click()
  await pagina.getByRole('button', { name: 'Adicionar itens', exact: true }).click()
  await pagina.getByRole('button', { name: /^Lasanha da quota/ }).click()
  const aviso = pagina.getByRole('alert').filter({ hasText: 'Não foi possível guardar as comandas neste navegador.' })
  await aviso.waitFor()
  assert.equal(await pagina.getByText('1 na comanda', { exact: true }).count(), 1)
  assert.equal(await pagina.evaluate(() => Object.keys(sessionStorage).filter(k => k.startsWith('easystok.visita:')).length), 0)
  await pagina.screenshot({ path: resolve(pasta, 'quota-bloqueada.png'), fullPage: true })
  await pagina.reload()
  await pagina.getByRole('button', { name: /^Todas/ }).click()
  await pagina.getByText('Cliente da quota', { exact: true }).first().click()
  await pagina.getByRole('button', { name: 'Adicionar itens', exact: true }).waitFor()
  assert.equal(await pagina.getByText('1 na comanda', { exact: true }).count(), 0, 'Sem armazenamento a proposta não foi restaurada após reload')
  assert.ok(chamadas.every(c => c.metodo === 'GET' || c.path.endsWith('/marcar-lida')), 'Nenhum pedido/cadastro foi gravado')
  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ appReal: true, httpControlado: true, quotaBloqueada: true, propostaPreservadaNaPagina: true, avisoVisivel: true, restauradaAposReload: false }, null, 2))
  console.log('Quota bloqueada: App preserva proposta na página, avisa limitação e não alega restauração após reload.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); throw erro }
finally { await contexto.close(); await browser.close() }
