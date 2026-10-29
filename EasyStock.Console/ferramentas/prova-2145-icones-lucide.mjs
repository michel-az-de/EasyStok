/* eslint-disable no-console */
// Prova da #2145 (Onda 1 da #2130, plano balcao-ux seção 3.2): o `Icone` virou fachada sobre o
// lucide-react e nenhum ícone das telas principais ficou vazio.
//   1. Hall: os ícones do Lucide aparecem e cada `svg` tem desenho (path, circle, rect, line...);
//   2. Atendimento com uma conversa aberta: idem, e o glifo próprio do canal (PROPRIOS) desenha;
//   3. Gestão: idem; e no celular o "Mais" do topo desenha o ⋮ (ellipsis-vertical);
//   4. em todas: traço 1,75, pontas arredondadas e `aria-hidden` no ícone sem rótulo.
//
// Console completo em demonstração (o `dist/` do `npm run build` sem VITE_FONTE_DADOS=api), aberto
// por file:// no Chromium. Nenhum servidor, nenhuma API.
//
//   npm run build
//   PLAYWRIGHT_MODULE=<pasta do playwright> node ferramentas/prova-2145-icones-lucide.mjs
// Com EVIDENCIAS_2145=<pasta> grava uma captura por tela.
import assert from 'node:assert/strict'
import { existsSync, mkdirSync } from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { resolve } from 'node:path'

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const raiz = fileURLToPath(new URL('..', import.meta.url))
const indice = resolve(raiz, 'dist/index.html')
assert.ok(existsSync(indice), 'rode npm run build antes: falta dist/index.html')
const evidencias = process.env.EVIDENCIAS_2145
if (evidencias) mkdirSync(evidencias, { recursive: true })

// Lê todos os `svg` do Lucide na página: classe, filhos com geometria e atributos de traço.
const lerIcones = (pagina) => pagina.evaluate(() => {
  const GEOMETRIA = 'path[d]:not([d=""]), circle, rect, line, polyline, polygon, ellipse'
  return [...document.querySelectorAll('svg.lucide')].map((svg) => ({
    classe: svg.getAttribute('class'),
    desenhos: svg.querySelectorAll(GEOMETRIA).length,
    traco: svg.getAttribute('stroke-width'),
    ponta: svg.getAttribute('stroke-linecap'),
    juncao: svg.getAttribute('stroke-linejoin'),
    rotulo: svg.getAttribute('aria-label'),
    oculto: svg.getAttribute('aria-hidden'),
    largura: svg.getBoundingClientRect().width,
  }))
})

const erros = []
let passou = 0
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok    ' + descricao) } catch (erro) {
    erros.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 4).join(' ')}`)
  }
}

const conferirTela = async (pagina, tela, { minimo }) => {
  const icones = await lerIcones(pagina)
  await confere(`${tela}: ao menos ${minimo} ícones do Lucide (achou ${icones.length})`, async () => {
    assert.ok(icones.length >= minimo, `só ${icones.length} svg.lucide`)
  })
  await confere(`${tela}: nenhum svg de ícone vazio`, async () => {
    const vazios = icones.filter((i) => i.desenhos === 0)
    assert.deepEqual(vazios.map((i) => i.classe), [])
  })
  await confere(`${tela}: nenhum svg vazio na página (ícone de qualquer origem)`, async () => {
    const vazios = await pagina.evaluate(() => [...document.querySelectorAll('svg')]
      .filter((svg) => svg.children.length === 0)
      .map((svg) => svg.outerHTML.slice(0, 120)))
    assert.deepEqual(vazios, [])
  })
  await confere(`${tela}: traço 1,75, pontas e junções arredondadas, aria-hidden sem rótulo`, async () => {
    for (const i of icones) {
      assert.equal(i.traco, '1.75', i.classe)
      assert.equal(i.ponta, 'round', i.classe)
      assert.equal(i.juncao, 'round', i.classe)
      if (!i.rotulo) assert.equal(i.oculto, 'true', `${i.classe} sem rótulo precisa de aria-hidden`)
    }
  })
  if (evidencias) await pagina.screenshot({ path: resolve(evidencias, `${tela.toLowerCase()}.png`) })
  return icones
}

const navegador = await chromium.launch({ headless: true })
try {
  const pagina = await navegador.newPage({ viewport: { width: 1440, height: 1000 } })
  pagina.setDefaultTimeout(8000)
  const falhasDePagina = []
  pagina.on('pageerror', (erro) => falhasDePagina.push(erro.message))
  await pagina.goto(pathToFileURL(indice).href)

  await pagina.getByText('Abrir Atendimento', { exact: true }).waitFor()
  await conferirTela(pagina, 'Hall', { minimo: 3 })

  await pagina.getByText('Abrir Atendimento', { exact: true }).click()
  await pagina.getByRole('button', { name: /Renan Okamoto/ }).click()
  await pagina.getByLabel('Mensagem para o cliente', { exact: true }).waitFor()
  const atendimento = await conferirTela(pagina, 'Atendimento', { minimo: 15 })
  await confere('Atendimento: o glifo próprio do canal (PROPRIOS) desenha', async () => {
    const canais = atendimento.filter((i) => /lucide-(whatsapp|instagram)\b/.test(i.classe))
    assert.ok(canais.length > 0, 'nenhum svg lucide-whatsapp/lucide-instagram na tela')
    assert.ok(canais.every((i) => i.desenhos > 0))
  })
  await confere('Atendimento: a chave de conceito desenha o ícone da tabela 3.2', async () => {
    // cardapio -> book-open, moto -> bike, nota -> sticky-note, agente/sparkles -> sparkles.
    for (const nome of ['book-open', 'bike', 'sticky-note', 'sparkles']) {
      assert.ok(atendimento.some((i) => i.classe.split(' ').includes('lucide-' + nome)), 'falta lucide-' + nome)
    }
  })

  await pagina.getByRole('button', { name: 'Gestão', exact: true }).first().click()
  await pagina.getByRole('dialog').first().waitFor()
  await conferirTela(pagina, 'Gestao', { minimo: 3 })

  // Celular: o menu "Mais" do topo usa a chave `ellipsis`, que vira o ⋮ da tabela 3.2.
  const celular = await navegador.newPage({ viewport: { width: 390, height: 844 } })
  celular.setDefaultTimeout(8000)
  celular.on('pageerror', (erro) => falhasDePagina.push(erro.message))
  await celular.goto(pathToFileURL(indice).href)
  await celular.getByText('Abrir Atendimento', { exact: true }).click()
  await celular.getByRole('button', { name: 'Mais', exact: true }).waitFor()
  const noCelular = await conferirTela(celular, 'Celular', { minimo: 3 })
  await confere('Celular: "Mais" desenha ellipsis-vertical (⋮), sem reticências horizontais', async () => {
    const classes = noCelular.map((i) => i.classe.split(' '))
    assert.ok(classes.some((c) => c.includes('lucide-ellipsis-vertical')), 'nenhum lucide-ellipsis-vertical')
    assert.ok(!classes.some((c) => c.includes('lucide-ellipsis')), 'ainda há lucide-ellipsis horizontal')
  })

  await confere('nenhum erro de página durante o roteiro', async () => assert.deepEqual(falhasDePagina, []))
} finally {
  await navegador.close()
}

console.log(`\n${passou} ok, ${erros.length} falha(s)`)
if (erros.length) process.exit(1)
