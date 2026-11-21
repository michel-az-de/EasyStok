/* eslint-disable no-console */
// Prova de navegador da #2998: o bloco Cobrança real, no modo API, com um pedido cujo link do Mercado Pago venceu.
// Com GET /api/pix-direto dizendo "disponível", "Alterar forma de pagamento" oferece "Pix na chave da loja" ao
// lado de "Receber na entrega"; escolher o Pix manda `{ forma: 'pix_direto' }`. A recusa da API aparece com a
// frase dela. Sem o Pix direto disponível, a troca é a de antes (só na entrega, sem chips). HTTP controlado
// pela rota do Playwright: nenhum pedido real é tocado.
//
//   npm run provas -- 2998   (sobe o Vite no modo API e passa CONSOLE_URL)
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve, join } from 'node:path'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5790'
const fixture = resolve(import.meta.dirname, '../.build/pix-direto-troca')
const pasta = process.env.EVIDENCIAS_UX ?? '../.build/pix-direto-troca-browser'
mkdirSync(fixture, { recursive: true })
mkdirSync(pasta, { recursive: true })

const PEDIDO_ID = '33333333-3333-3333-3333-333333333333'
writeFileSync(join(fixture, 'index.html'), '<html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="/.build/pix-direto-troca/index.jsx"></script></html>')
writeFileSync(join(fixture, 'index.jsx'), `
import React, { useMemo, useRef, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { ContextoEstado, ContextoAcoes, ContextoCatalogo } from '/src/aplicacao/contextos.js'
import { ContextoAcessoModulos } from '/src/aplicacao/acessoModulos.jsx'
import { criarAcoesComandaApi } from '/src/aplicacao/api/comanda.js'
import { gravarSessao } from '/src/infra/api/sessao.js'
import { BlocoCobranca } from '/src/features/ficha-cliente/BlocoCobranca.jsx'
import '/src/estilos/base.css'
gravarSessao({ token: 'teste-controlado', expiraEm: Date.now() + 60000, empresa: { id: 'empresa-controlada' } })
const inicial = {
  pedidoId: '${PEDIDO_ID}', numero: '1', estado: 'aguardando', statusApi: 'aguardando_pagamento', meio: 'cartao-link',
  sinalNaReserva: false, totalApi: 100, totalPagoApi: 0, itens: [], pagamentos: [], pagamentosApi: [],
  cobranca: { id: 'cb', valor: 100, meio: 'cartao-link', link: 'https://example.test/pagar', tentativa: 2,
    criadaEm: Date.now() - 3600000, expiraEm: Date.now() - 1800000 },
}
function Tela() {
  const [pedido, setPedido] = useState(inicial)
  const [aviso, setAviso] = useState(null)
  const estadoRef = useRef()
  estadoRef.current = { conversas: [{ id: 'conversa-controlada', pedido }] }
  const acoes = useMemo(() => criarAcoesComandaApi({}, { estadoRef, despachar: (acao) => {
    if (acao.mensagem) setAviso(acao.mensagem)
  } }), [])
  return (
    <ContextoAcessoModulos value={{ permite: () => true, acoes: { editarAtendimento: true } }}>
      <ContextoEstado value={{ fonteApi: true, fidelidade: { cupons: [] }, conversas: [], agora: Date.now() }}>
        <ContextoCatalogo value={{ cardapio: [], entregadores: [] }}>
          <ContextoAcoes value={acoes}>
            <main style={{ maxWidth: 420, margin: '24px auto', padding: 16 }}>
              {aviso && <p role="alert">{aviso}</p>}
              <BlocoCobranca pedido={pedido} conversaId="conversa-controlada" nomeCliente="Cliente de teste" agora={Date.now()} editavel
                aoAlterarMeio={(meio) => acoes.alterarMeioPagamento('conversa-controlada', meio)} />
            </main>
          </ContextoAcoes>
        </ContextoCatalogo>
      </ContextoEstado>
    </ContextoAcessoModulos>
  )
}
createRoot(document.getElementById('root')).render(<Tela />)
`)

const LINK_PENDENTE = 'Há uma cobrança online pendente. Confira o pagamento antes de receber manualmente.'
const dto = {
  pedidoId: PEDIDO_ID, status: 'aguardando_pagamento', total: 100, totalPago: 0, frete: 0, itens: [], pagamentos: [],
  cobranca: { cobrancaId: 'cb', status: 'Pendente', provedor: 'mercadopago', valor: 100, linkPagamento: 'https://example.test/pagar', tentativa: 2 },
}

async function abrir(browser, width, { disponivel, respostaDaTroca }) {
  const context = await browser.newContext({ viewport: { width, height: 900 }, reducedMotion: 'reduce' })
  await context.grantPermissions(['local-network-access'], { origin: origem })
  const page = await context.newPage()
  page.setDefaultTimeout(10000)
  const chamadas = []
  const erros = []
  page.on('pageerror', (e) => erros.push(e.message))
  await page.route((url) => url.pathname.startsWith('/api/'), async (route) => {
    const req = route.request()
    chamadas.push({ metodo: req.method(), url: req.url(), corpo: req.postData() })
    if (req.url().endsWith('/api/pix-direto')) return route.fulfill({ json: { data: { disponivel } } })
    if (req.url().endsWith('/cobranca/forma')) return route.fulfill(respostaDaTroca)
    return route.fulfill({ json: { data: dto } })
  })
  await page.goto(origem + '/.build/pix-direto-troca/index.html')
  return { context, page, chamadas, erros }
}

const indice = []
const browser = await chromium.launch({ headless: true })
try {
  for (const width of [1440, 390]) {
    // 1. Pix direto disponível: o modal oferece as duas formas; escolher o Pix diz o que acontece.
    {
      const { context, page, chamadas, erros } = await abrir(browser, width, {
        disponivel: true, respostaDaTroca: { status: 400, json: { error: { code: 'VALIDATION_ERROR', message: LINK_PENDENTE } } },
      })
      await page.getByRole('button', { name: 'Alterar forma de pagamento' }).click()
      const pix = page.getByRole('radio', { name: 'Pix na chave da loja' })
      await pix.waitFor()
      await page.getByRole('radio', { name: 'Receber na entrega' }).waitFor()
      assert.equal(await page.getByRole('button', { name: 'Confirmar troca' }).isEnabled(), false, 'confirma sem escolher')
      await pix.click()
      await page.getByText(/Cliente de teste recebe o Pix na chave da loja/).waitFor()
      const arquivo = `oferece-pix-${width}.png`
      await page.screenshot({ path: join(pasta, arquivo), fullPage: true })
      indice.push({ caso: 'oferece-pix', width, arquivo })
      // 2. Confirmar manda pix_direto; a recusa da API aparece com a frase dela.
      await page.getByRole('button', { name: 'Confirmar troca' }).click()
      await page.getByRole('alert').filter({ hasText: `Forma de pagamento: ${LINK_PENDENTE}` }).waitFor()
      const troca = chamadas.filter((c) => c.metodo === 'POST')
      assert.deepEqual(troca.map((c) => JSON.parse(c.corpo)), [{ forma: 'pix_direto' }])
      const recusa = `recusa-${width}.png`
      await page.screenshot({ path: join(pasta, recusa), fullPage: true })
      indice.push({ caso: 'recusa', width, arquivo: recusa })
      assert.deepEqual(erros, [])
      await context.close()
      console.log(`ok oferece Pix e mostra a recusa (${width})`)
    }
    // 3. Sem Pix direto: a troca de antes, sem chips.
    {
      const { context, page, chamadas, erros } = await abrir(browser, width, { disponivel: false, respostaDaTroca: { json: { data: {} } } })
      await page.getByRole('button', { name: 'Alterar forma de pagamento' }).click()
      await page.getByText(/o pedido passa a receber na entrega/).waitFor()
      assert.equal(await page.getByRole('radio').count(), 0)
      assert.equal(await page.getByRole('button', { name: 'Confirmar troca' }).isEnabled(), true)
      const arquivo = `sem-pix-${width}.png`
      await page.screenshot({ path: join(pasta, arquivo), fullPage: true })
      indice.push({ caso: 'sem-pix', width, arquivo })
      assert.equal(chamadas.filter((c) => c.metodo === 'POST').length, 0)
      assert.deepEqual(erros, [])
      await context.close()
      console.log(`ok sem Pix direto a troca é a de antes (${width})`)
    }
  }
} finally {
  await browser.close()
  writeFileSync(join(pasta, 'indice.json'), JSON.stringify(indice, null, 2))
}
console.log('OK: Pix direto no Alterar forma com link vencido, recusa visível e troca de antes sem a flag; HTTP controlado.')
