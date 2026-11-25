// #3339 (R1D-09): duplo clique em Enviar manda uma mensagem e não pede o microfone.
// a 2 cliques seguidos no Enviar (dblclick) com texto no campo: 1 balão no fio;
// b nenhum pedido de microfone (getUserMedia) em nenhum dos cliques.
// Antes: o 1º clique envia e o botão vira "Gravar áudio" no mesmo lugar; o 2º clique o aciona e pede o microfone.
// Demonstração local em 1440x900, tema claro. PLAYWRIGHT_MODULE aponta para o runtime externo; sem ele usa o do node_modules.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { resolve } from 'node:path'

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pastaConsole = resolve(import.meta.dirname, '..')
let servidor = null
if (!process.env.CONSOLE_DEMO_URL) {
  delete process.env.VITE_FONTE_DADOS
  const { createServer } = await import('vite')
  servidor = await createServer({ root: pastaConsole, logLevel: 'silent', server: { host: '127.0.0.1', port: 0, strictPort: false } })
  await servidor.listen()
}
const origem = process.env.CONSOLE_DEMO_URL ?? servidor.resolvedUrls.local[0].replace(/\/$/, '')

const falhas = []
let passou = 0
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok ' + descricao) } catch (erro) {
    falhas.push(descricao); console.log(`FALHA ${descricao}\n ${String(erro.message).split('\n').slice(0, 4).join(' ')}`)
  }
}

const navegador = await chromium.launch({ headless: true })
const TEXTO = 'Teste dbl 3339'

// Abre a 3ª conversa da demonstração (atendida pelo Assistente), assume e digita o texto.
async function abrirComTexto() {
  const contexto = await navegador.newContext({ viewport: { width: 1440, height: 900 }, colorScheme: 'light', reducedMotion: 'reduce' })
  // Conta os pedidos de microfone sem abrir microfone nenhum: a prova só precisa saber se o gesto chegou até ali.
  await contexto.addInitScript(() => {
    window.__pedidosDeMicrofone = 0
    if (navigator.mediaDevices) {
      navigator.mediaDevices.getUserMedia = () => {
        window.__pedidosDeMicrofone += 1
        return Promise.reject(new DOMException('prova sem microfone', 'NotAllowedError'))
      }
    }
  })
  const pagina = await contexto.newPage()
  await pagina.goto(`${origem}/#/m/atendimento/balcao`, { waitUntil: 'networkidle', timeout: 120_000 })
  await pagina.waitForSelector('[class*=contextoLinha]', { timeout: 30_000 })
  await pagina.locator('[class*=contextoLinha]').nth(2).click()
  await pagina.getByRole('button', { name: 'Assumir conversa', exact: true }).click()
  await pagina.waitForTimeout(400)
  const campo = pagina.getByRole('textbox', { name: 'Mensagem para o cliente' })
  await campo.click()
  await campo.fill(TEXTO)
  return { contexto, pagina }
}

// --- a e b. duplo clique em Enviar ---------------------------------------------------------------------------------------
{
  const { contexto, pagina } = await abrirComTexto()
  await pagina.getByRole('button', { name: 'Enviar mensagem', exact: true }).dblclick()
  await pagina.waitForTimeout(600)
  const balões = await pagina.getByText(TEXTO, { exact: true }).count()
  const pedidos = await pagina.evaluate(() => window.__pedidosDeMicrofone)
  await confere('a. dois cliques seguidos em Enviar mandam 1 mensagem', () => {
    assert.equal(balões, 1, `o fio tem ${balões} balão(ões) com o texto`)
  })
  await confere('b. o duplo clique em Enviar não pede o microfone', () => {
    assert.equal(pedidos, 0, `o microfone foi pedido ${pedidos} vez(es)`)
  })
  await contexto.close()
}

await navegador.close()
await servidor?.close()
console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) { console.log(falhas.map((f) => ' - ' + f).join('\n')); process.exit(1) }
