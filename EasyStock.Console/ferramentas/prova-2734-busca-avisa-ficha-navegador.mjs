// #2734: App real + HTTP controlado. O cadastro aberto pela Buscar na Casa fica fora do Atendimento; a nota fixada
// e o cadastro salvo ali precisam chegar à Ficha da conversa dessa pessoa, que está aberta por trás.
// Não chama API, LLM nem portas de envio reais.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2734/busca-avisa-ficha')
mkdirSync(pasta, { recursive: true })
const ID = '3f2b8c1e-5a47-4d09-9b6e-1c2d3e4f5a6b'
const CONVERSA = 'conversa-da-pessoa'
const DOSSIE_DA_CONVERSA = `/api/atendimento/conversas/${CONVERSA}/dossie`
const resumo = { id: CONVERSA, contatoNome: 'João Açúcar', contatoIdExterno: '5511999991234', clienteId: ID, vinculoRevisao: 2, canal: 'WhatsApp', situacao: 'Assumida', assumidaPorUsuarioId: 'dona', ultimaMensagemEm: new Date().toISOString(), naoLidas: 0 }
const pessoa = { id: ID, nome: 'João Açúcar', telefone: '11999991234', email: '', documento: '', tipoPessoa: 'fisica', revisao: 45, ativo: true, observacoes: '' }
let notas = []
const dossie = () => ({
  cliente: { id: ID, nome: pessoa.nome, telefone: pessoa.telefone, email: null, observacoes: pessoa.observacoes || null, motivoBloqueio: null, fotoUrl: null },
  enderecos: [], tags: [], notas, ultimosPedidos: [], itemFavorito: null, ultimaCompraEm: null, totalPedidos: 0, bloqueado: false,
  conversasRecentes: [], pedidoEmAndamento: null, contatoInformado: null, totalGasto: 0,
})
// Cada leitura do dossiê da conversa guarda quantas gravações o servidor já tinha recebido quando ela chegou.
const escritas = [], leiturasDaFicha = [], erros = []
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja da busca' }, usuario: { id: 'dona', nome: 'Dona de teste' } })))
await contexto.route('**/api/**', async route => {
  const req = route.request(), url = new URL(req.url()), path = url.pathname
  if (!path.startsWith('/api/')) return route.continue()
  const ok = data => route.fulfill({ json: { data } })
  if (req.method() === 'POST' && path === `/api/clientes/${ID}/notas`) {
    const nota = { id: `nota-${notas.length + 1}`, texto: req.postDataJSON().texto, autor: 'Dona de teste', pedidoId: null, mensagemId: null, criadoEm: new Date().toISOString() }
    notas = [nota, ...notas]; escritas.push('nota')
    return route.fulfill({ status: 201, json: { data: nota } })
  }
  if (req.method() === 'PATCH' && path === `/api/clientes/${ID}`) {
    Object.assign(pessoa, { observacoes: req.postDataJSON().observacoes, revisao: pessoa.revisao + 1 }); escritas.push('cadastro')
    return ok(pessoa)
  }
  // A tela pede a sugestão ao abrir a conversa: aqui volta sem texto, e nenhuma IA é chamada.
  if (req.method() === 'POST' && path.endsWith('/sugestao')) return ok(null)
  assert.ok(req.method() === 'GET' || path.endsWith('/marcar-lida'), `Escrita inesperada: ${req.method()} ${path}`)
  if (path === '/api/auth/me/modulos') return ok({ modulos: ['atendimento', 'cadastros', 'cardapio'].map(id => ({ id, liberado: true })), acoes: { verCadastrosProdutos: true, editarCadastrosProdutos: true, verPedidos: true } })
  if (path === '/api/clientes/buscar') return ok([pessoa])
  if (path === `/api/clientes/${ID}/dossie`) return ok(dossie())
  if (path === `/api/clientes/${ID}/notas/pagina`) return ok({ itens: notas, temMais: false, proximaPagina: null })
  if (path === `/api/clientes/${ID}`) return ok({ cliente: pessoa, enderecos: [], telefones: [], documentos: [], alteracoes: [] })
  if (path === '/api/atendimento/conversas') return ok([resumo])
  if (path === DOSSIE_DA_CONVERSA) { leiturasDaFicha.push(escritas.length); return ok(dossie()) }
  if (path.endsWith('/mensagens')) return ok([{ id: 'mensagem', direcao: 'Entrada', texto: 'Mensagem da conversa certa.', enviadaEm: new Date().toISOString(), tipoConteudo: 'Texto' }])
  if (path.endsWith('/pedido')) return ok(null)
  if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
  if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: true, dias: [] })
  if (path === '/api/atendimento/comanda/cardapio') return ok({ itens: [], porcoes: {} })
  return ok([])
})
const page = await contexto.newPage(); page.setDefaultTimeout(12000); page.on('pageerror', e => erros.push(e.message))
const ate = async (condicao, descricao, prazo = 5000) => {
  const fim = Date.now() + prazo
  while (Date.now() < fim) { if (condicao()) return; await page.waitForTimeout(50) }
  assert.fail(`não aconteceu em ${prazo} ms: ${descricao}`)
}
try {
  // A conversa da pessoa está aberta no Atendimento: a Ficha já leu o dossiê uma vez.
  await page.goto(`${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/#/m/atendimento/balcao`)
  await page.getByRole('button', { name: /^Todas/ }).click()
  await page.getByText('João Açúcar', { exact: true }).first().click()
  await page.getByLabel('Mensagens da conversa').getByText('Mensagem da conversa certa.', { exact: true }).waitFor()
  await ate(() => leiturasDaFicha.length >= 1, 'a Ficha ler o dossiê ao abrir a conversa')
  // Sem gravação, ninguém relê a Ficha sozinho: a releitura que a prova espera é a do aviso, não uma periódica.
  await page.waitForTimeout(1500)
  const antes = leiturasDaFicha.length

  // A mesma pessoa abre pela Buscar na Casa, por cima do Atendimento.
  await page.keyboard.press('Control+k')
  const dialogo = page.getByRole('dialog', { name: 'Buscar na Casa', exact: true })
  await dialogo.getByRole('combobox', { name: 'Buscar na Casa', exact: true }).fill('joao')
  await page.getByRole('option').filter({ hasText: 'Pessoas' }).click()
  const editor = page.getByRole('dialog', { name: 'Ficha do cliente', exact: true }); await editor.waitFor(); await editor.getByRole('button', { name: 'Editar dados', exact: true }).click()
  assert.equal(leiturasDaFicha.length, antes, 'abrir o cadastro não relê a Ficha')

  // Nota fixada no cadastro: a Ficha da conversa relê o dossiê depois da gravação.
  const secaoNotas = editor.getByRole('region', { name: 'Notas internas', exact: true })
  await secaoNotas.getByRole('button', { name: 'Adicionar nota', exact: true }).click()
  const modal = page.getByRole('dialog', { name: 'Nota interna', exact: true }); await modal.waitFor()
  await modal.getByRole('textbox', { name: 'Texto da nota', exact: true }).fill('Só aceita entrega depois das 19h.')
  await modal.getByRole('button', { name: 'Fixar nota', exact: true }).click()
  await modal.waitFor({ state: 'detached' })
  await secaoNotas.getByText('Só aceita entrega depois das 19h.').waitFor()
  await ate(() => leiturasDaFicha.some(gravadas => gravadas >= 1), 'a Ficha reler o dossiê depois da nota fixada pela Buscar na Casa')

  // Cadastro salvo pela Buscar na Casa: a Ficha relê de novo e mostra a observação.
  await editor.getByRole('textbox', { name: 'Observações', exact: true }).fill('Portão verde, casa dos fundos.')
  await editor.getByRole('button', { name: 'Salvar cadastro', exact: true }).click()
  await editor.waitFor({ state: 'detached' })
  assert.deepEqual(escritas, ['nota', 'cadastro'])
  await ate(() => leiturasDaFicha.some(gravadas => gravadas >= 2), 'a Ficha reler o dossiê depois do cadastro salvo pela Buscar na Casa')

  // Fechada a busca, a Ficha do Atendimento mostra o que foi gravado, sem recarregar a página.
  await dialogo.getByRole('button', { name: /Fechar/ }).first().click()
  await dialogo.waitFor({ state: 'detached' })
  const naFicha = page.getByRole('region', { name: 'Notas internas do cliente', exact: true }).first()
  await naFicha.getByText('Só aceita entrega depois das 19h.', { exact: true }).first().waitFor()
  await naFicha.getByText('Portão verde, casa dos fundos.', { exact: true }).first().waitFor()
  await page.screenshot({ path: resolve(pasta, 'ficha-depois-1440.png') })

  assert.deepEqual(erros, [])
  console.log('#2734 app real: nota e cadastro gravados pela Buscar na Casa chegam à Ficha da conversa aberta.')
} catch (erro) { await page.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }).catch(() => {}); console.error(erros.join('\n')); throw erro }
finally { await browser.close() }
