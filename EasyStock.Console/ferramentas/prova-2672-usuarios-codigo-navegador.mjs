// Prova da #2672 (homologação #1769, Configurações C1 e C3), com HTTP controlado e sem API real:
//   - C3: "Gerar código de acesso" numa linha de baixo abria o painel do código no topo da lista, fora da tela, e o
//     foco ia para o select da linha. O código só aparece uma vez: o foco tem de chegar nele, e ele na tela;
//   - C1: os botões de cada linha nomeiam a pessoa, como o select já faz ("Perfil de X").
//
//   node ferramentas/rodar-provas.mjs 2672
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5790'
const empresaId = '11111111-1111-4111-8111-111111111111'
const PERFIS = [
  { perfilId: 'p-admin', nome: 'Admin', nivel: 'Admin' },
  { perfilId: 'p-gerente', nome: 'Gerente', nivel: 'Gerente' },
  { perfilId: 'p-operador', nome: 'Operador', nivel: 'Operador' },
]
const pessoa = (n, extra = {}) => ({
  usuarioId: `u-${String(n).padStart(2, '0')}`, nome: `Pessoa ${String(n).padStart(2, '0')}`, email: `pessoa${n}@teste.demo`,
  ativo: true, ultimoAcessoEm: null, criadoEm: '2026-11-15T12:00:00Z', nivel: 'Operador',
  convite: { estado: 'pendente', aceitoEm: null, via: null }, perfilId: 'p-operador', perfil: 'Operador',
  // #3174: os botões da linha vêm de `acoes`, que a API devolve por pessoa.
  acoes: { trocarPerfil: true, reenviarConvite: true, desativar: true }, ...extra,
})
// Doze pessoas: a última fica bem abaixo da dobra numa janela de 700 px.
const USUARIOS = [
  ...Array.from({ length: 11 }, (_, i) => pessoa(i + 1)),
  pessoa(12, { ativo: false, convite: { estado: 'nenhum', aceitoEm: null, via: null }, acoes: { reativar: true } }),
]

const browser = await chromium.launch({ headless: true })
const falhas = []
let passou = 0
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok    ' + descricao) }
  catch (erro) { falhas.push(descricao); console.log(`FALHA ${descricao}\n      ${erro.message.split('\n').slice(0, 3).join(' ')}`) }
}

async function abrir({ falhaNoCodigo = false } = {}) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 700 }, reducedMotion: 'reduce' })
  await context.grantPermissions(['local-network-access'], { origin: origem })
  await context.addInitScript(({ empresaId }) => sessionStorage.setItem('easystok.sessao', JSON.stringify({
    token: 'teste-ui-controlado', expiraEm: Date.now() + 3600000,
    empresa: { id: empresaId, nome: 'Empresa de teste' }, usuario: { id: 'u-dona', nome: 'Dona de teste', nivel: 'Admin' },
  })), { empresaId })
  let emitidos = 0
  await context.route('**/api/**', async (route) => {
    const caminho = new URL(route.request().url()).pathname
    if (!caminho.startsWith('/api/')) return route.continue()
    const ok = (data) => route.fulfill({ json: { data } })
    if (caminho === '/api/auth/me/modulos') return ok({ modulos: ['configuracoes'].map((id) => ({ id, liberado: true })), acoes: {} })
    if (caminho === '/api/usuarios/perfis') return ok(PERFIS)
    if (caminho === '/api/usuarios') return ok(USUARIOS)
    if (/^\/api\/usuarios\/[^/]+\/chave-acesso$/.test(caminho)) {
      if (falhaNoCodigo) return route.fulfill({ status: 500, json: { error: { message: 'Falha controlada ao gerar.' } } })
      emitidos += 1
      return ok({ chave: `CHAVE-CONTROLADA-${emitidos}`, expiraEm: '2026-12-15T12:00:00Z' })
    }
    if (caminho === '/api/notificacoes/badge') return ok({ count: 0 })
    return ok([])
  })
  const page = await context.newPage()
  page.setDefaultTimeout(8000)
  await page.goto(origem + '/#/m/configuracoes/usuarios', { waitUntil: 'domcontentloaded' })
  await page.getByRole('heading', { name: 'Quem tem acesso' }).waitFor()
  return { context, page }
}

// O campo está na janela e não está coberto por nada (cabeçalho fixo, aviso).
const visivelNaTela = (page, localizador) => localizador.evaluate((el) => {
  const r = el.getBoundingClientRect()
  const x = r.left + r.width / 2
  const y = r.top + r.height / 2
  return r.top >= 0 && r.bottom <= window.innerHeight && document.elementFromPoint(x, y) === el
})

try {
  const { context, page } = await abrir()
  try {
    await confere('C1: os botões da linha nomeiam a pessoa', async () => {
      for (const nome of ['Reenviar convite para Pessoa 11', 'Gerar código de acesso de Pessoa 11', 'Desativar Pessoa 11', 'Reativar Pessoa 12']) {
        assert.equal(await page.getByRole('button', { name: nome, exact: true }).count(), 1, `sem botão "${nome}"`)
      }
    })

    const linha11 = page.locator('li[data-usuario="u-11"]')
    await confere('C3: gerar numa linha de baixo leva o foco ao código, e o código entra na tela', async () => {
      await linha11.getByRole('button', { name: /^Gerar código de acesso/ }).click()
      const campo = page.getByRole('textbox', { name: 'Código de acesso de Pessoa 11' })
      await campo.waitFor()
      await page.waitForTimeout(400)
      assert.equal(await campo.evaluate((el) => el === document.activeElement), true, 'o foco não está no código')
      assert.equal(await visivelNaTela(page, campo), true, 'o código ficou fora da tela')
      assert.equal(await campo.inputValue(), 'CHAVE-CONTROLADA-1')
    })

    await confere('C3: gerar de novo, com o painel aberto, leva o foco ao código novo', async () => {
      await page.locator('li[data-usuario="u-10"]').getByRole('button', { name: /^Gerar código de acesso/ }).click()
      const campo = page.getByRole('textbox', { name: 'Código de acesso de Pessoa 10' })
      await campo.waitFor()
      await page.waitForTimeout(400)
      assert.equal(await campo.inputValue(), 'CHAVE-CONTROLADA-2')
      assert.equal(await campo.evaluate((el) => el === document.activeElement), true, 'o foco não está no código novo')
      assert.equal(await visivelNaTela(page, campo), true, 'o código novo ficou fora da tela')
    })
  } finally { await context.close() }

  const falha = await abrir({ falhaNoCodigo: true })
  try {
    await confere('C3: falha ao gerar devolve o foco à linha', async () => {
      const linha = falha.page.locator('li[data-usuario="u-11"]')
      await linha.getByRole('button', { name: /^Gerar código de acesso/ }).click()
      await falha.page.getByRole('alert').waitFor()
      await falha.page.waitForTimeout(400)
      assert.equal(await linha.evaluate((li) => li.contains(document.activeElement)), true, 'o foco saiu da linha')
      assert.equal(await falha.page.getByRole('textbox', { name: /^Código de acesso de/ }).count(), 0)
    })
  } finally { await falha.context.close() }
} finally {
  await browser.close()
}

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
