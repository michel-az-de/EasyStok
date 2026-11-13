/* eslint-disable no-console */
// Prova da #2185: um menu ⋮ só na tela de Atendimento, com as ações que o estado da conversa oferece.
//   1. cada estado mostra no menu exatamente o que `acoesDaConversa` diz, e a barra só o Assumir;
//   2. não sobra "Mais ações", nem <details> de menu, nem segundo menu na conversa;
//   3. o menu fecha ao escolher, no Esc (foco volta ao gatilho) e ao clicar fora, e anda com as setas;
//   4. conversa de colega: Assumir pede confirmação nomeando quem está com ela; Cancelar não assume;
//   5. Assumir com toque duplo chama o servidor uma vez;
//   6. o lugar do Assistente vem antes do menu em toda conversa (não troca de lado);
//   7. trocar de conversa fecha o modal aberto pelo menu;
//   8. "Anexar" abre o seletor de arquivos mesmo com o menu fechando no clique;
//   9. o gatilho tem o mesmo tamanho no topo e no composer: 40 px no mouse, 48 px no toque;
//  10. item desligado explica o motivo por escrito; chave de ligar mostra o estado.
//
// Componentes reais (PainelAtendimento, BarraModo, Composer, MenuDeAcoes) com CSS real, em Chromium.
//   PLAYWRIGHT_MODULE=<pasta do playwright> node ferramentas/prova-2185-menu-de-acoes.mjs
import assert from 'node:assert/strict'
import { mkdirSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { resolve } from 'node:path'
import { build } from 'vite'
import react from '@vitejs/plugin-react'

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const raiz = fileURLToPath(new URL('..', import.meta.url)).replaceAll('\\', '/').replace(/\/$/, '')
const pasta = resolve(raiz, '.build/prova-2185')
mkdirSync(pasta, { recursive: true })

writeFileSync(resolve(pasta, 'entrada.jsx'), `
import React, { useState } from 'react'
import { createRoot } from 'react-dom/client'
import { ContextoAcoes, ContextoCatalogo, ContextoEstado } from '/src/aplicacao/contextos.js'
import { PainelAtendimento } from '/src/features/atendimento/PainelAtendimento.jsx'
import { CANAIS } from '/src/infra/catalogo.js'
import { PAUSA_POR_ASSUMIR, PAUSA_POR_OUTRO } from '/src/dominio/automatico.js'
import moldura from '/src/app/moldura.module.css'
import '/src/estilos/base.css'

const agora = Date.now()
const noop = () => {}
const chamadas = []
window.chamadas = chamadas
const msg = [{ id: 'm0', dir: 'in', texto: 'Oi, tem lasanha hoje?', em: new Date(agora - 600000).toISOString() }]
const FILA = { motivo: 'Restrição alimentar: alergia a camarão', em: null, assumida: false }
const CENAS = {
  robo: { conversa: { canal: 'WhatsApp' }, pausado: false },
  meu: { conversa: { canal: 'WhatsApp', clienteId: 'cl1' }, pausado: PAUSA_POR_ASSUMIR },
  outro: { conversa: { canal: 'WhatsApp', responsavel: 'Ana Lima' }, pausado: PAUSA_POR_OUTRO },
  fila: { conversa: { canal: 'WhatsApp', passagem: FILA }, pausado: true },
  site: { conversa: { canal: 'Chat do site', passagem: { ...FILA, origem: 'chat-site', motivo: 'Visitante do site aguardando atendimento da equipe.' } }, pausado: true },
  concluida: { conversa: { canal: 'WhatsApp', estado: 'Encerrado' }, pausado: false },
  bloqueada: { conversa: { canal: 'WhatsApp', clienteId: 'cl1', bloqueio: { motivo: 'Teste', em: new Date(agora).toISOString() } }, pausado: true },
}
const montar = (cena) => ({
  id: cena, nome: 'Renan Okamoto', estado: 'Aberto', conta: 'cliente', cliente: { tags: [], notas: [], pedidos: 0 },
  pedido: null, passagem: null, mensagens: msg, ultimaEm: new Date(agora - 60000).toISOString(), ...CENAS[cena].conversa,
})

function Cena() {
  const [cena, setCena] = useState(new URLSearchParams(location.search).get('cena') ?? 'robo')
  window.trocar = setCena
  const conversa = montar(cena)
  const acoes = {
    descarregarRascunho: noop, enviarMidia: noop, definirRascunho: noop, selecionar: noop, reabrir: noop,
    listarAtendentes: async () => [], enviar: noop, transferirConversa: async () => ({ ok: true }),
    abrirEncerramento: (id) => chamadas.push('concluir:' + id),
    devolverAutomatico: (id) => chamadas.push('devolver:' + id),
    assumirAtendimento: async (id) => { chamadas.push('assumir:' + id); await new Promise((r) => setTimeout(r, window.atrasoAssumir ?? 60)); return true },
  }
  const estado = {
    selecionada: conversa, visiveis: [conversa], agora, automaticoPausado: { [conversa.id]: CENAS[cena].pausado },
    rascunho: '', escritaDeFora: 0, fonteApi: true,
  }
  const catalogo = { janelas: [], canais: CANAIS, respostasProntas: [], cardapio: [], linhas: [], adicionais: [] }
  return (
    <ContextoEstado value={estado}><ContextoAcoes value={acoes}><ContextoCatalogo value={catalogo}>
      <div style={{ display: 'flex', height: '100dvh' }}>
        <section className={moldura.coluna + ' ' + moldura.centro}>
          <PainelAtendimento aoAbrirNota={noop} aoAbrirGaleria={noop} aoAbrirBiblioteca={noop} />
        </section>
      </div>
    </ContextoCatalogo></ContextoAcoes></ContextoEstado>
  )
}
createRoot(document.getElementById('root')).render(<Cena />)
`)

process.env.VITE_FONTE_DADOS = 'api'
const compilado = await build({
  root: raiz, configFile: false, logLevel: 'silent',
  define: { 'process.env.NODE_ENV': '"production"' },
  plugins: [react()],
  build: { write: false, minify: false, cssCodeSplit: false, lib: { entry: '.build/prova-2185/entrada.jsx', formats: ['iife'], name: 'Prova' } },
})
const saidas = (Array.isArray(compilado) ? compilado[0] : compilado).output
const codigo = saidas.find((a) => a.type === 'chunk').code
const estilo = String(saidas.find((a) => a.type === 'asset' && a.fileName.endsWith('.css'))?.source ?? '')
const pagina = `<!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${estilo}</style><div id="root"></div><script>${codigo.replaceAll('</script', '<\\/script')}</script></html>`

const ORIGEM = 'http://prova.local'
const navegador = await chromium.launch({ headless: true })
const erros = []
let passou = 0
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok    ' + descricao) } catch (erro) {
    erros.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 4).join(' ')}`)
  }
}

async function abrir(cena = 'robo', { width = 1280, height = 720, toque = false } = {}) {
  const ctx = await navegador.newContext({ viewport: { width, height }, reducedMotion: 'reduce', serviceWorkers: 'block', hasTouch: toque, isMobile: toque })
  await ctx.addInitScript(() => {
    sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa' }, usuario: { id: 'u1' } }))
  })
  await ctx.route(ORIGEM + '/**', (rota) => {
    const url = new URL(rota.request().url())
    if (!url.pathname.startsWith('/api/')) return rota.fulfill({ contentType: 'text/html', body: pagina })
    return rota.fulfill({ json: { success: true, data: [] } })
  })
  const p = await ctx.newPage()
  p.on('pageerror', (e) => erros.push('pageerror: ' + e.message))
  await p.goto(`${ORIGEM}/?cena=${cena}`)
  await p.getByRole('region', { name: 'Quem responde esta conversa' }).waitFor()
  return { ctx, p }
}
const GATILHO = 'Mais opções da conversa'
const gatilho = (p) => p.getByRole('button', { name: GATILHO, exact: true })
const itensDoMenu = async (p) => {
  if (!await gatilho(p).count()) return []
  await gatilho(p).click()
  await p.getByRole('menu', { name: GATILHO }).waitFor()
  const itens = await p.getByRole('menu', { name: GATILHO }).getByRole('menuitem').allInnerTexts()
  await p.keyboard.press('Escape')
  return itens.map((t) => t.trim())
}
const chamadas = (p) => p.evaluate(() => window.chamadas.slice())

const TODOS = ['Enviar cardápio', 'Passar conversa', 'Concluir conversa']
const COM_DEVOLVER = ['Enviar cardápio', 'Passar conversa', 'Devolver ao Assistente', 'Concluir conversa']
const MATRIZ = [
  ['robo', TODOS, true],
  ['meu', COM_DEVOLVER, false],
  ['outro', TODOS, true],
  ['fila', COM_DEVOLVER, true],
  ['site', TODOS, true],
  ['concluida', [], false],
  ['bloqueada', ['Concluir conversa'], false],
]

try {
  for (const [cena, esperado, assumir] of MATRIZ) {
    await confere(`1. ${cena}: menu ${JSON.stringify(esperado)}, Assumir ${assumir ? 'à vista' : 'ausente'}`, async () => {
      const { ctx, p } = await abrir(cena)
      assert.deepEqual(await itensDoMenu(p), esperado)
      assert.equal(await p.getByRole('button', { name: 'Assumir conversa', exact: true }).count(), assumir ? 1 : 0) // #2437: "Assumir conversa" desde a #2232
      await ctx.close()
    })
  }

  await confere('1. o motivo da passagem fica à vista, e não só no title', async () => {
    const { ctx, p } = await abrir('fila')
    assert.equal(await p.getByText('Restrição alimentar: alergia a camarão', { exact: true }).isVisible(), true)
    await ctx.close()
  })

  await confere('1. bloqueado aparece uma vez entre o cabeçalho e a barra', async () => {
    const { ctx, p } = await abrir('bloqueada')
    const textos = await p.locator('[data-cabecalho-conversa], [data-controle-conversa] > div > span').allInnerTexts()
    assert.equal(textos.join(' ').match(/Bloqueado/g)?.length, 1, textos.join(' | '))
    await ctx.close()
  })

  await confere('2. não sobra "Mais ações" nem <details> de menu na tela', async () => {
    for (const cena of ['robo', 'meu', 'fila']) {
      const { ctx, p } = await abrir(cena)
      assert.equal(await p.getByText('Mais ações').count(), 0, cena)
      assert.equal(await p.locator('[data-cabecalho-conversa] details, [data-controle-conversa] details, [data-area-envio] details').count(), 0, cena)
      await ctx.close()
    }
  })

  await confere('3. fecha ao escolher e faz a ação', async () => {
    const { ctx, p } = await abrir('meu')
    await gatilho(p).click()
    await p.getByRole('menuitem', { name: 'Concluir conversa' }).click()
    assert.equal(await p.getByRole('menu').count(), 0)
    assert.deepEqual(await chamadas(p), ['concluir:meu'])
    await ctx.close()
  })
  await confere('3. Esc fecha e devolve o foco ao gatilho', async () => {
    const { ctx, p } = await abrir('meu')
    await gatilho(p).click()
    await p.keyboard.press('Escape')
    assert.equal(await p.getByRole('menu').count(), 0)
    assert.equal(await gatilho(p).evaluate((el) => document.activeElement === el), true)
    await ctx.close()
  })
  await confere('3. clicar fora fecha', async () => {
    const { ctx, p } = await abrir('meu')
    await gatilho(p).click()
    await p.locator('[role="log"]').click({ position: { x: 20, y: 20 } })
    assert.equal(await p.getByRole('menu').count(), 0)
    await ctx.close()
  })
  await confere('3. setas andam entre os itens e Home/End vão às pontas', async () => {
    const { ctx, p } = await abrir('meu')
    await gatilho(p).press('Enter')
    const foco = () => p.evaluate(() => document.activeElement.innerText.trim())
    assert.equal(await foco(), 'Enviar cardápio')
    await p.keyboard.press('ArrowDown')
    assert.equal(await foco(), 'Passar conversa')
    await p.keyboard.press('End')
    assert.equal(await foco(), 'Concluir conversa')
    await p.keyboard.press('ArrowDown')
    assert.equal(await foco(), 'Enviar cardápio')
    await ctx.close()
  })

  await confere('4. conversa de colega: Assumir confirma, nomeia o colega e Cancelar não assume', async () => {
    const { ctx, p } = await abrir('outro')
    assert.match(await p.getByRole('region', { name: 'Quem responde esta conversa' }).innerText(), /Com Ana Lima/)
    await p.getByRole('button', { name: 'Assumir conversa', exact: true }).click()
    const janela = p.getByRole('dialog', { name: 'Assumir esta conversa?' })
    await janela.waitFor()
    assert.match(await janela.innerText(), /Ela está com Ana Lima/)
    await janela.getByRole('button', { name: 'Cancelar', exact: true }).click()
    assert.deepEqual(await chamadas(p), [])
    await p.getByRole('button', { name: 'Assumir conversa', exact: true }).click()
    await p.getByRole('dialog').getByRole('button', { name: 'Assumir conversa', exact: true }).click()
    assert.deepEqual(await chamadas(p), ['assumir:outro'])
    await ctx.close()
  })

  await confere('5. Assumir com toque duplo chama o servidor uma vez', async () => {
    const { ctx, p } = await abrir('robo')
    await p.getByRole('button', { name: 'Assumir conversa', exact: true }).dblclick({ force: true })
    await p.waitForTimeout(150)
    assert.deepEqual(await chamadas(p), ['assumir:robo'])
    await ctx.close()
  })

  await confere('5. Assumir em andamento numa conversa não trava o Assumir de outra', async () => {
    const { ctx, p } = await abrir('robo')
    await p.evaluate(() => { window.atrasoAssumir = 800 })
    await p.getByRole('button', { name: 'Assumir conversa', exact: true }).click()
    await p.evaluate(() => window.trocar('fila'))
    await p.getByText('Passou para você').waitFor()
    const assumir = p.getByRole('button', { name: 'Assumir conversa', exact: true })
    assert.equal(await assumir.isDisabled(), false)
    await assumir.click()
    assert.deepEqual(await chamadas(p), ['assumir:robo', 'assumir:fila'])
    await ctx.close()
  })
  await confere('6. o lugar do Assistente vem antes do menu em toda conversa', async () => {
    const { ctx, p } = await abrir('robo')
    for (const cena of ['robo', 'meu', 'fila', 'outro', 'robo']) {
      await p.evaluate((c) => window.trocar(c), cena)
      await p.waitForTimeout(60)
      const antes = await p.evaluate(() => {
        const ancora = document.querySelector('[data-ancora-assistente]')
        const menu = [...document.querySelectorAll('button')].find((b) => b.getAttribute('aria-label') === 'Mais opções da conversa')
        return Boolean(ancora.compareDocumentPosition(menu) & Node.DOCUMENT_POSITION_FOLLOWING)
      })
      assert.equal(antes, true, cena)
    }
    await ctx.close()
  })

  await confere('7. trocar de conversa fecha o modal aberto pelo menu', async () => {
    const { ctx, p } = await abrir('meu')
    await gatilho(p).click()
    await p.getByRole('menuitem', { name: 'Devolver ao Assistente' }).click()
    await p.getByRole('dialog', { name: 'Devolver ao Assistente?' }).waitFor()
    await p.evaluate(() => window.trocar('fila'))
    await p.waitForTimeout(60)
    assert.equal(await p.getByRole('dialog').count(), 0)
    await ctx.close()
  })
  await confere('7. devolver confirma antes e chama o servidor uma vez', async () => {
    const { ctx, p } = await abrir('meu')
    await gatilho(p).click()
    await p.getByRole('menuitem', { name: 'Devolver ao Assistente' }).click()
    assert.deepEqual(await chamadas(p), [])
    await p.getByRole('dialog').getByRole('button', { name: 'Devolver ao Assistente', exact: true }).click()
    assert.deepEqual(await chamadas(p), ['devolver:meu'])
    await ctx.close()
  })

  await confere('8. "Anexar" abre o seletor de arquivos, mesmo o menu fechando no clique', async () => {
    const { ctx, p } = await abrir('meu')
    await p.getByRole('button', { name: 'Mais ferramentas', exact: true }).click()
    const seletor = p.waitForEvent('filechooser', { timeout: 3000 })
    await p.getByRole('menuitem', { name: 'Anexar' }).click()
    await seletor
    assert.equal(await p.getByRole('menu').count(), 0)
    await ctx.close()
  })

  for (const [descricao, opcoes, alvo] of [['mouse', {}, 40], ['toque', { width: 390, height: 844, toque: true }, 48]]) {
    await confere(`9. ${descricao}: gatilhos do topo e do composer medem ${alvo} px`, async () => {
      const { ctx, p } = await abrir('meu', opcoes)
      for (const nome of [GATILHO, 'Mais ferramentas']) {
        const caixa = await p.getByRole('button', { name: nome, exact: true }).boundingBox()
        assert.deepEqual([Math.round(caixa.width), Math.round(caixa.height)], [alvo, alvo], nome)
      }
      await ctx.close()
    })
  }

  await confere('10. item desligado explica o motivo por escrito, ligado ao item', async () => {
    const { ctx, p } = await abrir('robo')
    await p.getByRole('button', { name: 'Mais ferramentas', exact: true }).click()
    // A prova roda sem permissão de editar: o item se chama "Programadas" e só consulta.
    const programar = p.getByRole('menuitem', { name: 'Programadas' })
    assert.equal(await programar.isDisabled(), true)
    const id = await programar.getAttribute('aria-describedby')
    assert.equal(await p.locator(`[id="${id}"]`).innerText(), 'Cadastre o cliente primeiro.')
    await ctx.close()
  })
  await confere('10. item ligado diz o que aceita, ligado ao item por aria-describedby', async () => {
    const { ctx, p } = await abrir('meu')
    await p.getByRole('button', { name: 'Mais ferramentas', exact: true }).click()
    const anexar = p.getByRole('menuitem', { name: 'Anexar' })
    assert.equal(await anexar.isDisabled(), false)
    const id = await anexar.getAttribute('aria-describedby')
    assert.equal(await p.locator(`[id="${id}"]`).innerText(), 'Foto do computador (imagem, até 6 MB)') // #2437: modo API aceita só imagem, até 6 MB (#2284)
    await ctx.close()
  })
  await confere('11. conversa sem ação no menu não mostra o gatilho', async () => {
    const { ctx, p } = await abrir('concluida')
    assert.equal(await gatilho(p).count(), 0)
    await ctx.close()
  })
  await confere('11. menu aberto numa conversa que fica sem ações não reabre sozinho ao voltar', async () => {
    const { ctx, p } = await abrir('meu')
    await gatilho(p).click()
    await p.getByRole('menu', { name: GATILHO }).waitFor()
    await p.evaluate(() => window.trocar('concluida'))
    await gatilho(p).waitFor({ state: 'detached' })
    await p.evaluate(() => window.trocar('meu'))
    await gatilho(p).waitFor()
    assert.equal(await p.getByRole('menu', { name: GATILHO }).count(), 0)
    await ctx.close()
  })
  await confere('10. chave de ligar mostra o estado no menu', async () => {
    const { ctx, p } = await abrir('meu')
    await p.getByRole('button', { name: 'Mais ferramentas', exact: true }).click()
    assert.equal(await p.getByRole('menuitemcheckbox', { name: 'Sugestões do Assistente' }).getAttribute('aria-checked'), 'true')
    await ctx.close()
  })
} finally {
  await navegador.close()
}

const semPagina = erros.filter((e) => e.startsWith('pageerror'))
console.log(`\nprova-2185: ${passou} conferências ok, ${erros.length} falha(s)${semPagina.length ? ` (${semPagina.join('; ')})` : ''}`)
if (erros.length) process.exit(1)
