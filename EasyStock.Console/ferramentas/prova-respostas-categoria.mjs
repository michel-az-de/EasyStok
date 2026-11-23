/* eslint-disable no-console */
// Prova visual da #3208: respostas rápidas agrupadas por categoria e editáveis no chat.
//   a) seletor aberto: cabeçalhos de categoria em ordem, "Sem categoria" por último;
//   b) busca pelo nome da categoria filtra para aquele grupo;
//   c) setas e Enter atravessam os grupos e inserem o texto no campo da mensagem;
//   d) "Nova resposta" abre o formulário DENTRO do painel (sem role=dialog); salvar volta à lista, no grupo certo;
//   e) "Editar resposta X" troca a categoria e o item muda de grupo;
//   f) Gestão > Respostas: selo de categoria na lista e a busca casa a categoria;
//   g) a 390 px nada fica cortado nem coberto (document.elementFromPoint no centro dos botões);
//   h) erro do servidor no formulário do painel, em role="alert";
//   i) nenhum item, selo ou cabeçalho destaca por borda colorida só à esquerda;
//   k/l) rodapé inteiro e em uma linha; Salvar/Cancelar visíveis no painel SEM rolar (elementFromPoint do próprio centro);
//   m) linha do item compacta; n) formulário sujo não se perde por Esc nem por clique fora (limpo fecha);
//   o) na árvore de acessibilidade o listbox só tem group/option e os "Editar resposta X" ficam fora dele; F2 edita o
//      item ativo (no campo de busca e, aberto por "/", no campo de texto); p) operador sem permissão não vê Nova/Editar;
//   q/r) formulário sujo também não se perde pelo botão Respostas nem pela Nota interna (trocar de conversa remonta o Composer: sem preservação).
// FASE=antes: a mesma tela no código do master (CODIGO_ANTES=<pasta EasyStock.Console>), sem grupos.
// Componentes reais com CSS real, em Chromium, ações de mentira; sem servidor.
//
//   node ferramentas/prova-respostas-categoria.mjs
//   FASE=antes CODIGO_ANTES=C:/rep/EasyStok/EasyStock.Console node ferramentas/prova-respostas-categoria.mjs
//   Os PNG vão para EVIDENCIAS_3208 (padrão: .build/prova-respostas-categoria/<fase>).
import assert from 'node:assert/strict'
import { mkdirSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { resolve } from 'node:path'
import { build } from 'vite'
import react from '@vitejs/plugin-react'

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const fase = process.env.FASE === 'antes' ? 'antes' : 'depois'
const aqui = fileURLToPath(new URL('..', import.meta.url)).replaceAll('\\', '/').replace(/\/$/, '')
const raiz = fase === 'antes' ? String(process.env.CODIGO_ANTES ?? '').replaceAll('\\', '/').replace(/\/$/, '') : aqui
if (!raiz) throw new Error('FASE=antes pede CODIGO_ANTES=<pasta EasyStock.Console do código de antes>')
const pasta = resolve(aqui, '.build/prova-respostas-categoria')
const evidencias = resolve(process.env.EVIDENCIAS_3208 ?? resolve(pasta, fase))
mkdirSync(pasta, { recursive: true })
mkdirSync(evidencias, { recursive: true })
const entrada = resolve(pasta, `entrada-${fase}.jsx`)

writeFileSync(entrada, `
import React, { useMemo, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { ContextoAcessoModulos } from '${raiz}/src/aplicacao/acessoModulos.jsx'
import { ContextoAcoes, ContextoCatalogo, ContextoEstado } from '${raiz}/src/aplicacao/contextos.js'
import { PainelAtendimento } from '${raiz}/src/features/atendimento/PainelAtendimento.jsx'
import { AbaRespostas } from '${raiz}/src/features/gestao/respostas/AbaRespostas.jsx'
import { CANAIS } from '${raiz}/src/infra/catalogo.js'
import moldura from '${raiz}/src/app/moldura.module.css'
import '${raiz}/src/estilos/base.css'

const q = new URLSearchParams(location.search)
const agora = Date.now()
const noop = () => {}
const R = (id, titulo, atalho, texto, categoria) => ({ id, titulo, atalho, texto, categoria: categoria ?? null, arquivada: false })
const INICIAIS = q.get('sem') === 'categorias'
  ? [R('r1', 'Chave Pix', '/pix', 'Nossa chave Pix é 11.222.333/0001-44. Envie o comprovante por aqui.'),
     R('r2', 'Bom dia', '/bomdia', 'Bom dia! Como posso ajudar?')]
  : [
    R('r1', 'Chave Pix', '/pix', 'Nossa chave Pix é 11.222.333/0001-44. Envie o comprovante por aqui.', 'Pagamento'),
    R('r2', 'Parcelamento', '/parcelas', 'Parcelamos em até 3 vezes no cartão, sem juros.', 'Pagamento'),
    R('r3', 'Taxa de frete', '/frete', 'O frete para o seu bairro custa R$ 8,00.', 'Logística'),
    R('r4', 'Prazo de envio', '/prazo', 'Enviamos em até 2 dias úteis depois da confirmação.', 'Logística'),
    R('r5', 'Obrigado', '/obrigado', 'Obrigado pela preferência! Volte sempre.'),
    R('r6', 'Bom dia', '/bomdia', 'Bom dia! Como posso ajudar?'),
  ]

function useRespostas() {
  const [lista, setLista] = useState(INICIAIS)
  const [aviso, setAviso] = useState(null)
  const acoes = useMemo(() => ({
    incluirRespostaPronta: async (dados) => {
      if (q.get('recusar') === '1') { setAviso('O servidor recusou: já existe uma resposta com esse atalho.'); return false }
      setLista((l) => [...l, { id: 'n' + l.length, arquivada: false, ...dados }])
      return true
    },
    editarRespostaPronta: async (id, dados) => { setLista((l) => l.map((r) => (r.id === id ? { ...r, ...dados } : r))); return true },
    alternarArquivamentoRespostaPronta: noop,
  }), [])
  return { lista, aviso, acoes }
}

function CenaConversa() {
  const { lista, aviso, acoes: acoesResp } = useRespostas()
  const conversa = {
    id: 'c1', nome: 'Renan Okamoto', canal: 'WhatsApp', estado: 'Aberto', conta: 'cliente', clienteId: 'cli1',
    cliente: { tags: [], notas: [], pedidos: 2 }, pedido: null, passagem: null,
    mensagens: [{ id: 'm0', dir: 'in', texto: 'Oi, como faço para pagar?', em: new Date(agora - 60000).toISOString() }],
    ultimaEm: new Date(agora - 60000).toISOString(),
  }
  const acoes = useMemo(() => ({
    descarregarRascunho: noop, enviarMidia: noop, definirRascunho: noop, devolverAutomatico: noop, selecionar: noop,
    abrirEncerramento: noop, reabrir: noop, listarAtendentes: async () => [], enviar: noop, ...acoesResp,
  }), [acoesResp])
  const estado = { selecionada: conversa, visiveis: [conversa], agora, automaticoPausado: { c1: false }, rascunho: '', escritaDeFora: 0, fonteApi: true, sincronizacao: aviso ? { aviso } : {} }
  const cat = { janelas: [], canais: CANAIS, respostasProntas: lista, cardapio: [], linhas: [], adicionais: [] }
  const acesso = { permite: () => true, acoes: { reabrirConversa: true, editarRespostas: q.get('semedicao') !== '1' }, aoSair: null }
  return (
    <ContextoAcessoModulos value={acesso}><ContextoEstado value={estado}><ContextoAcoes value={acoes}><ContextoCatalogo value={cat}>
      <div style={{ display: 'flex', height: '100dvh' }}>
        <section className={moldura.coluna + ' ' + moldura.centro}>
          <PainelAtendimento aoAbrirNota={noop} aoAbrirGaleria={noop} aoAbrirBiblioteca={noop} />
        </section>
      </div>
    </ContextoCatalogo></ContextoAcoes></ContextoEstado></ContextoAcessoModulos>
  )
}

function CenaGestao() {
  const { lista, acoes } = useRespostas()
  const estado = { fonteApi: true, selecionada: null, visiveis: [], agora }
  const cat = { janelas: [], canais: CANAIS, respostasProntas: lista, cardapio: [], linhas: [], adicionais: [] }
  const acesso = { permite: () => true, acoes: { editarRespostas: true }, aoSair: null }
  return (
    <ContextoAcessoModulos value={acesso}><ContextoEstado value={estado}><ContextoAcoes value={acoes}><ContextoCatalogo value={cat}>
      <main style={{ padding: 16, height: '100dvh', overflow: 'auto', boxSizing: 'border-box' }}><div style={{ maxWidth: 960, margin: '0 auto' }}><AbaRespostas comAutomaticas={false} /></div></main>
    </ContextoCatalogo></ContextoAcoes></ContextoEstado></ContextoAcessoModulos>
  )
}

createRoot(document.getElementById('root')).render(q.get('cena') === 'gestao' ? <CenaGestao /> : <CenaConversa />)
`)

process.env.VITE_FONTE_DADOS = 'api'
const compilado = await build({
  root: raiz, configFile: false, logLevel: 'silent', cacheDir: resolve(pasta, '.cache-vite'),
  define: { 'process.env.NODE_ENV': '"production"' },
  resolve: { dedupe: ['react', 'react-dom'] },
  plugins: [react()],
  build: { write: false, minify: false, cssCodeSplit: false, lib: { entry: entrada, formats: ['iife'], name: 'Prova' } },
})
const saidas = (Array.isArray(compilado) ? compilado[0] : compilado).output
const codigo = saidas.find((a) => a.type === 'chunk').code
const estilo = saidas.filter((a) => a.type === 'asset' && a.fileName.endsWith('.css')).map((a) => String(a.source)).join('\n')
const html = `<!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${estilo}</style><div id="root"></div><script>${codigo.replaceAll('</script', '<\\/script')}</script></html>`

const ORIGEM = 'http://prova.local'
const TELAS = { desktop: { width: 1440, height: 900 }, celular: { width: 390, height: 844 } }
const T = { timeout: 8000 }
const navegador = await chromium.launch({ headless: true })
const erros = []
const resultado = []
let ultima = null

async function abrir(tela, consulta = '') {
  const ctx = await navegador.newContext({ viewport: TELAS[tela], reducedMotion: 'reduce', serviceWorkers: 'block' })
  await ctx.addInitScript(() => {
    sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa' }, usuario: { id: 'u1' } }))
  })
  await ctx.route(ORIGEM + '/**', (rota) => {
    const url = new URL(rota.request().url())
    if (!url.pathname.startsWith('/api/')) return rota.fulfill({ contentType: 'text/html', body: html })
    return rota.fulfill({ json: { success: true, data: [] } })
  })
  const pagina = await ctx.newPage()
  ultima = pagina
  pagina.on('pageerror', (e) => erros.push('pageerror: ' + e.message))
  await pagina.goto(`${ORIGEM}/?${consulta}`)
  await (consulta.includes('cena=gestao') ? pagina.getByRole('heading', { name: 'Respostas prontas' }) : pagina.getByLabel('Mensagem para o cliente', { exact: true })).waitFor(T)
  return pagina
}
const foto = (p, nome) => p.screenshot({ path: resolve(evidencias, `${nome}.png`) })
const painel = (p) => p.getByRole('group', { name: 'Respostas rápidas' })
const abrirPainel = async (p) => { await p.getByRole('button', { name: 'Respostas', exact: true }).click(); await painel(p).waitFor(T) }
const cabecalhos = (p) => p.locator('[role="group"][aria-labelledby]').evaluateAll((gs) => gs.map((g) => document.getElementById(g.getAttribute('aria-labelledby'))?.textContent))
const titulos = (p) => p.locator('[role="option"] > span:first-child').allTextContents()
const grupoDe = (p, titulo) => p.getByRole('option').filter({ hasText: titulo }).first().evaluate((o) => {
  // Grupo ancestral (estrutura antiga) ou o que assume a opção por aria-owns.
  const g = o.closest('[role="group"][aria-labelledby]') ?? document.querySelector(`[role="group"][aria-owns~="${o.id}"]`)
  return g ? document.getElementById(g.getAttribute('aria-labelledby'))?.textContent : null
})
// Borda colorida só à esquerda: largura na esquerda e nenhuma nas outras três, ou ::before vertical.
const bordaSoEsquerda = (p, seletor) => p.locator(seletor).evaluateAll((els) => els.flatMap((e) => {
  const c = getComputedStyle(e)
  const b = getComputedStyle(e, '::before')
  const so = parseFloat(c.borderLeftWidth) > 0 && c.borderLeftStyle !== 'none' && !parseFloat(c.borderTopWidth) && !parseFloat(c.borderRightWidth) && !parseFloat(c.borderBottomWidth)
  const barra = b.content !== 'none' && parseFloat(b.width) <= 6 && parseFloat(b.height) > 10 && b.backgroundColor !== 'rgba(0, 0, 0, 0)'
  return so || barra ? [(e.textContent || '').slice(0, 30) + (so ? ' [border-left]' : ' [::before]')] : []
}))
// Centro do botão: o elemento que recebe o clique é o próprio botão (ou um filho), e está dentro da janela.
async function livre(p, nome, loc) {
  await loc.scrollIntoViewIfNeeded()
  const r = await loc.evaluate((b) => {
    const x = b.getBoundingClientRect()
    const cx = x.left + x.width / 2, cy = x.top + x.height / 2
    const topo = document.elementFromPoint(cx, cy)
    return { dentro: x.left >= 0 && x.right <= innerWidth && x.top >= 0 && x.bottom <= innerHeight, livre: !!topo && b.contains(topo), coberto: topo ? (topo.tagName + '.' + String(topo.className).slice(0, 40)) : null }
  })
  assert.ok(r.dentro, `${nome}: fora da janela`)
  assert.ok(r.livre, `${nome}: coberto por ${r.coberto}`)
}
// Sem rolar: o botão está dentro do retângulo visível do painel e da janela, e é o elementFromPoint do próprio centro.
async function livreNoPainel(nome, loc, caixa) {
  const c = await caixa.evaluate((g) => { const x = g.getBoundingClientRect(); return { top: x.top, bottom: x.bottom, left: x.left, right: x.right } })
  const r = await loc.evaluate((b) => {
    const x = b.getBoundingClientRect()
    const topo = document.elementFromPoint(x.left + x.width / 2, x.top + x.height / 2)
    return { top: x.top, bottom: x.bottom, left: x.left, right: x.right, livre: !!topo && b.contains(topo), coberto: topo ? (topo.tagName + '.' + String(topo.className).slice(0, 40)) : null, jan: [innerWidth, innerHeight] }
  })
  assert.ok(r.top >= c.top - 0.5 && r.bottom <= c.bottom + 0.5 && r.left >= c.left - 0.5 && r.right <= c.right + 0.5, `${nome}: fora do painel (${JSON.stringify({ botao: r, painel: c })})`)
  assert.ok(r.bottom <= r.jan[1] && r.right <= r.jan[0] && r.top >= 0 && r.left >= 0, `${nome}: fora da janela`)
  assert.ok(r.livre, `${nome}: coberto por ${r.coberto}`)
}
async function semEstouro(p, nome, loc) {
  const r = await loc.evaluate((e) => ({ sw: e.scrollWidth, cw: e.clientWidth, dir: e.getBoundingClientRect().right, jan: innerWidth }))
  assert.ok(r.sw <= r.cw + 1, `${nome}: conteúdo estoura o painel (${r.sw} > ${r.cw})`)
  assert.ok(r.dir <= r.jan + 1, `${nome}: painel passa da janela (${r.dir} > ${r.jan})`)
}

async function cenario(nome, corpo) {
  try { await corpo(); resultado.push([nome, 'ok']); console.log('ok    ' + nome) } catch (e) {
    resultado.push([nome, 'FALHA']); console.log('FALHA ' + nome + '\n      ' + String(e.message).split('\n').slice(0, 4).join('\n      '))
    if (ultima) await ultima.screenshot({ path: resolve(evidencias, `falha-${nome.replace(/\W+/g, '-')}.png`) }).catch(() => {})
  }
}

try {
  if (fase === 'antes') {
    for (const tela of Object.keys(TELAS)) {
      await cenario(`h-antes-${tela}`, async () => {
        const p = await abrir(tela)
        await abrirPainel(p)
        await p.getByRole('option').first().waitFor(T)
        assert.equal(await p.locator('[role="listbox"] [role="group"]').count(), 0, 'master não agrupa')
        assert.equal(await p.getByRole('button', { name: 'Nova resposta' }).count(), 0)
        await foto(p, `antes-seletor-${tela}`)
        const g = await abrir(tela, 'cena=gestao')
        await foto(g, `antes-gestao-${tela}`)
      })
    }
  } else {
    for (const tela of Object.keys(TELAS)) {
      const cel = tela === 'celular'
      await cenario(`a-grupos-em-ordem-${tela}`, async () => {
        const p = await abrir(tela)
        await abrirPainel(p)
        await p.getByRole('option').first().waitFor(T)
        assert.deepEqual(await cabecalhos(p), ['Logística', 'Pagamento', 'Sem categoria'])
        assert.deepEqual(await titulos(p), ['Prazo de envio', 'Taxa de frete', 'Chave Pix', 'Parcelamento', 'Bom dia', 'Obrigado'])
        assert.equal(await p.getByRole('dialog').count(), 0)
        await p.getByRole('combobox', { name: 'Buscar resposta pronta' }).waitFor(T)
        assert.equal(await p.getByRole('combobox', { name: 'Buscar resposta pronta' }).getAttribute('placeholder'), 'Buscar por título, atalho, texto ou categoria')
        await foto(p, `depois-a-seletor-${tela}`)
      })
      await cenario(`b-busca-por-categoria-${tela}`, async () => {
        const p = await abrir(tela)
        await abrirPainel(p)
        await p.getByRole('combobox', { name: 'Buscar resposta pronta' }).fill('logistica')
        assert.deepEqual(await cabecalhos(p), ['Logística'])
        assert.deepEqual(await titulos(p), ['Prazo de envio', 'Taxa de frete'])
        await foto(p, `depois-b-busca-${tela}`)
      })
      await cenario(`c-setas-e-enter-${tela}`, async () => {
        const p = await abrir(tela)
        await abrirPainel(p)
        const busca = p.getByRole('combobox', { name: 'Buscar resposta pronta' })
        await busca.press('ArrowDown'); await busca.press('ArrowDown') // Prazo -> Taxa -> Chave Pix (cruza o grupo)
        assert.equal(await p.locator('[role="option"][aria-selected="true"]').innerText().then((t) => t.split('\n')[0]), 'Chave Pix')
        await foto(p, `depois-c-destaque-${tela}`)
        await busca.press('Enter')
        assert.match(await p.getByLabel('Mensagem para o cliente', { exact: true }).inputValue(), /chave Pix é 11\.222/)
        assert.equal(await painel(p).count(), 0, 'o painel fecha ao inserir')
      })
      await cenario(`d-nova-resposta-no-painel-${tela}`, async () => {
        const p = await abrir(tela)
        await abrirPainel(p)
        const nova = p.getByRole('button', { name: 'Nova resposta' })
        if (cel) await livre(p, 'Nova resposta', nova)
        await nova.click()
        assert.equal(await p.getByRole('dialog').count(), 0, 'sem modal')
        const f = painel(p)
        await f.getByLabel('Título').fill('Retirada na loja')
        await f.getByRole('textbox', { name: 'Texto' }).fill('Pode retirar na loja de segunda a sábado, das 9h às 18h.')
        await f.getByLabel('Categoria').fill('Logística')
        assert.equal(await f.locator('form').count(), 1, 'formulário dentro do painel')
        await foto(p, `depois-d-formulario-${tela}`)
        if (cel) {
          await livreNoPainel('Salvar', f.getByRole('button', { name: 'Salvar' }), f)
          await livreNoPainel('Cancelar', f.getByRole('button', { name: 'Cancelar' }), f)
          await semEstouro(p, 'painel', f)
          assert.equal(await f.getByRole('button').evaluateAll((bs) => bs.filter((b) => /primar/i.test(b.className) || b.matches('[type="submit"]')).length), 1, 'um só botão primário')
          await foto(p, `depois-g-formulario-celular`)
        }
        await f.getByRole('button', { name: 'Salvar' }).click()
        await p.getByRole('option').first().waitFor(T)
        assert.equal(await grupoDe(p, 'Retirada na loja'), 'Logística')
        assert.deepEqual(await cabecalhos(p), ['Logística', 'Pagamento', 'Sem categoria'])
        await foto(p, `depois-d-salvou-${tela}`)
      })
      await cenario(`e-editar-troca-de-grupo-${tela}`, async () => {
        const p = await abrir(tela)
        await abrirPainel(p)
        assert.equal(await grupoDe(p, 'Taxa de frete'), 'Logística')
        await p.getByRole('button', { name: 'Editar resposta Taxa de frete' }).click()
        assert.equal(await p.getByRole('dialog').count(), 0)
        const f = painel(p)
        assert.equal(await f.getByLabel('Título').inputValue(), 'Taxa de frete')
        await f.getByLabel('Categoria').fill('Pagamento')
        await foto(p, `depois-e-editando-${tela}`)
        await f.getByRole('button', { name: 'Salvar' }).click()
        await p.getByRole('option').first().waitFor(T)
        assert.equal(await grupoDe(p, 'Taxa de frete'), 'Pagamento')
        assert.deepEqual(await cabecalhos(p), ['Logística', 'Pagamento', 'Sem categoria'])
        await foto(p, `depois-e-trocou-${tela}`)
      })
      await cenario(`h-erro-do-servidor-${tela}`, async () => {
        const p = await abrir(tela, 'recusar=1')
        await abrirPainel(p)
        await p.getByRole('button', { name: 'Nova resposta' }).click()
        const f = painel(p)
        await f.getByLabel('Título').fill('Duplicada')
        await f.getByRole('textbox', { name: 'Texto' }).fill('Texto qualquer.')
        await f.getByRole('button', { name: 'Salvar' }).click()
        await f.getByRole('alert').filter({ hasText: 'servidor recusou' }).waitFor(T)
        assert.equal(await f.locator('form').count(), 1, 'o formulário segue aberto')
        await foto(p, `depois-h-erro-${tela}`)
        if (cel) { await livreNoPainel('Salvar', f.getByRole('button', { name: 'Salvar' }), f); await semEstouro(p, 'painel', f) }
      })
      await cenario(`f-gestao-selo-e-busca-${tela}`, async () => {
        const p = await abrir(tela, 'cena=gestao')
        const main = p.locator('main')
        for (const c of ['Pagamento', 'Logística']) assert.ok(await main.locator('span').filter({ hasText: new RegExp(`^${c}$`) }).count() >= 1, `selo ${c}`)
        await foto(p, `depois-f-gestao-${tela}`)
        await p.getByLabel('Buscar resposta pronta').fill('logistica')
        await p.getByText('Taxa de frete').first().waitFor(T)
        assert.equal(await main.getByText('Chave Pix').count(), 0, 'busca por categoria esconde as outras')
        assert.equal(await main.getByText('Prazo de envio').count(), 1)
        await foto(p, `depois-f-gestao-busca-${tela}`)
        await p.getByLabel('Buscar resposta pronta').fill('')
        await main.getByRole('button', { name: 'Nova resposta' }).click()
        const form = main.locator('form').first()
        await form.getByLabel('Categoria').waitFor(T)
        await foto(p, `depois-f-gestao-form-${tela}`)
        if (cel) { await livre(p, 'Salvar', form.getByRole('button', { name: 'Salvar' })); await livre(p, 'Cancelar', form.getByRole('button', { name: 'Cancelar' })); await semEstouro(p, 'formulário', form) }
      })
      await cenario(`i-sem-borda-so-a-esquerda-${tela}`, async () => {
        const p = await abrir(tela)
        await abrirPainel(p)
        const achados = [
          ...await bordaSoEsquerda(p, '[role="option"], [role="group"][aria-label="Respostas rápidas"] li, [role="group"][aria-label="Respostas rápidas"] p'),
          ...await bordaSoEsquerda(p, '[role="group"][aria-label="Respostas rápidas"], [role="group"][aria-label="Respostas rápidas"] > div'),
        ]
        const g = await abrir(tela, 'cena=gestao')
        achados.push(...await bordaSoEsquerda(g, 'main li, main span'))
        assert.deepEqual(achados, [], 'borda colorida só à esquerda')
      })
      await cenario(`k-rodape-sem-corte-${tela}`, async () => {
        const p = await abrir(tela)
        await abrirPainel(p)
        const medidas = await painel(p).evaluate((g) => {
          const caixa = g.getBoundingClientRect()
          return ['Nova resposta', 'Gerenciar respostas'].map((nome) => {
            const b = [...g.querySelectorAll('button')].find((x) => x.textContent.trim().replace(/\s+/g, ' ') === nome)
            if (!b) return { nome, ausente: true }
            const r = b.getBoundingClientRect()
            // Linhas do TEXTO do botão (o botão tem altura mínima de toque, então altura/linha-height engana).
            const tops = new Set(), andar = document.createTreeWalker(b, NodeFilter.SHOW_TEXT)
            for (let n = andar.nextNode(); n; n = andar.nextNode()) { if (!n.textContent.trim()) continue; const rg = document.createRange(); rg.selectNodeContents(n); for (const q of rg.getClientRects()) tops.add(Math.round(q.top)) }
            const linhas = tops.size
            return { nome, linhas, dbg: [Math.round(r.bottom), Math.round(caixa.bottom), Math.round(r.height)], cortado: r.bottom > caixa.bottom + 0.5 || r.right > caixa.right + 0.5, rolagem: b.scrollWidth > b.clientWidth + 1 }
          })
        })
        for (const m of medidas) {
          assert.ok(!m.ausente, `${m.nome}: botão não achado`)
          assert.ok(m.linhas <= 1 && !m.cortado && !m.rolagem, `${m.nome}: rodapé quebra ou fica cortado (${JSON.stringify(m)})`)
        }
      })
      await cenario(`l-formulario-cabe-no-painel-${tela}`, async () => {
        const p = await abrir(tela)
        await abrirPainel(p)
        await p.getByRole('button', { name: 'Nova resposta' }).click()
        const f = painel(p)
        await f.getByRole('button', { name: 'Salvar' }).waitFor(T)
        const r = await f.evaluate((g) => {
          const c = g.getBoundingClientRect(), b = [...g.querySelectorAll('button')].find((x) => x.textContent.trim() === 'Salvar').getBoundingClientRect()
          return { painelBaixo: Math.round(c.bottom), salvarBaixo: Math.round(b.bottom), rolagem: g.scrollHeight > g.clientHeight + 1, sh: g.scrollHeight, ch: g.clientHeight, sel: Math.round(g.firstElementChild.getBoundingClientRect().height), alt: Math.round(c.height), fora: [...g.querySelectorAll('*')].filter((e) => e.getBoundingClientRect().bottom > c.bottom + 1 && getComputedStyle(e).position === 'absolute').map((e) => e.tagName + '.' + String(e.className).slice(0, 25) + ':' + Math.round(e.getBoundingClientRect().bottom)).slice(0, 4) }
        })
        await foto(p, `depois-l-formulario-${tela}`)
        assert.ok(r.salvarBaixo <= r.painelBaixo && !r.rolagem, `Salvar fica abaixo da área visível do painel sem rolar (${JSON.stringify(r)})`)
      })
      await cenario(`m-linha-compacta-${tela}`, async () => {
        const p = await abrir(tela)
        await abrirPainel(p)
        const alturas = await p.getByRole('option').evaluateAll((os) => os.map((o) => Math.round(o.parentElement.getBoundingClientRect().height)))
        assert.ok(alturas.length === 6 && Math.max(...alturas) <= 60, `linha do item alta demais: ${JSON.stringify(alturas)}`)
      })
      await cenario(`n-formulario-sujo-nao-se-perde-${tela}`, async () => {
        const p = await abrir(tela)
        await abrirPainel(p)
        await p.getByRole('button', { name: 'Nova resposta' }).click()
        const f = painel(p)
        await f.getByLabel('Título').fill('Rascunho importante')
        const pergunta = f.getByText('Descartar o que foi digitado?')
        await p.keyboard.press('Escape')
        assert.equal(await painel(p).count(), 1, 'Esc fechou o painel com formulário sujo')
        assert.equal(await f.getByLabel('Título').inputValue(), 'Rascunho importante', 'Esc apagou o que foi digitado')
        await pergunta.waitFor(T) // Esc dá retorno: a mesma pergunta do Cancelar
        await f.getByRole('button', { name: 'Continuar editando' }).click()
        assert.equal(await pergunta.count(), 0)
        await p.mouse.click(5, 5)
        assert.equal(await painel(p).count(), 1, 'clique fora fechou o painel com formulário sujo')
        assert.equal(await f.getByLabel('Título').inputValue(), 'Rascunho importante', 'clique fora apagou o que foi digitado')
        await pergunta.waitFor(T) // clique fora também pergunta
        await f.getByRole('button', { name: 'Continuar editando' }).click()
        await f.getByRole('button', { name: 'Cancelar' }).click()
        await f.getByRole('button', { name: 'Descartar' }).click()
        await p.getByRole('option').first().waitFor(T)
        await p.getByRole('button', { name: 'Nova resposta' }).click()
        await f.getByLabel('Título').waitFor(T)
        await p.keyboard.press('Escape')
        assert.equal(await painel(p).count(), 0, 'formulário limpo deve fechar por Esc')
        // Edição: sujo = diferente do valor inicial (voltar ao original deixa limpo).
        await abrirPainel(p)
        await p.getByRole('button', { name: 'Editar resposta Taxa de frete' }).click()
        await f.getByLabel('Título').waitFor(T)
        await f.getByLabel('Título').fill('Taxa de frete 2')
        await p.keyboard.press('Escape')
        assert.equal(await painel(p).count(), 1, 'Esc fechou a edição suja')
        assert.equal(await f.getByLabel('Título').inputValue(), 'Taxa de frete 2')
        await pergunta.waitFor(T)
        await f.getByRole('button', { name: 'Continuar editando' }).click()
        await f.getByLabel('Título').fill('Taxa de frete')
        await p.keyboard.press('Escape')
        assert.equal(await painel(p).count(), 0, 'edição de volta ao original deve fechar por Esc')
      })
      await cenario(`o-editar-fora-do-listbox-e-por-teclado-${tela}`, async () => {
        const p = await abrir(tela)
        await abrirPainel(p)
        // Árvore de acessibilidade (não a do DOM): o listbox só tem group/option, e os Editar existem fora dele.
        const lista = await p.getByRole('listbox', { name: 'Respostas prontas' }).ariaSnapshot()
        const papeis = [...lista.matchAll(/^\s*- ([a-z]+)/gm)].map((m) => m[1])
        assert.ok(papeis.includes('group') && papeis.includes('option'), `listbox sem group/option na árvore:\n${lista}`)
        assert.deepEqual([...new Set(papeis)].filter((r) => !['listbox', 'group', 'option', 'text', 'paragraph'].includes(r)), [], `filho proibido no listbox:\n${lista}`)
        assert.doesNotMatch(lista, /Editar resposta/, 'Editar dentro do listbox na árvore')
        const tudo = await painel(p).ariaSnapshot()
        assert.equal([...tudo.matchAll(/button "Editar resposta [^"]+"/g)].length, 6, `cada item tem o seu Editar, com nome acessível:\n${tudo}`)
        // Com o mouse a linha segue inteira: o Editar fica na altura da opção, à direita dela.
        const caixa = await p.getByRole('option').filter({ hasText: 'Taxa de frete' }).boundingBox()
        const botao = await p.getByRole('button', { name: 'Editar resposta Taxa de frete' }).boundingBox()
        assert.ok(Math.abs((botao.y + botao.height / 2) - (caixa.y + caixa.height / 2)) < 8 && botao.x >= caixa.x + caixa.width - 1, `Editar fora da linha da opção: ${JSON.stringify({ caixa, botao })}`)
        const busca = p.getByRole('combobox', { name: 'Buscar resposta pronta' })
        assert.equal(await busca.getAttribute('aria-keyshortcuts'), 'F2')
        await busca.press('ArrowDown') // Prazo -> Taxa de frete
        await escutarF2NaJanela(p)
        await busca.press('F2')
        assert.equal(await painel(p).getByLabel('Título').inputValue(), 'Taxa de frete', 'F2 não abriu a edição do item ativo')
        assert.equal(await p.evaluate(() => window.__f2NaJanela), 0, 'F2 do seletor vazou para a janela: na demonstração abriria Simulações junto')
      })
      await cenario(`o2-f2-pela-barra-${tela}`, async () => {
        const p = await abrir(tela)
        const campo = p.getByLabel('Mensagem para o cliente', { exact: true })
        await campo.fill('/')
        await painel(p).waitFor(T)
        assert.equal(await p.getByRole('combobox', { name: 'Buscar resposta pronta' }).count(), 0, 'pela barra não há campo de busca')
        assert.equal(await p.getByRole('listbox', { name: 'Respostas prontas' }).getAttribute('aria-keyshortcuts'), 'F2')
        await campo.press('ArrowDown') // Prazo -> Taxa de frete
        await escutarF2NaJanela(p)
        await campo.press('F2')
        assert.equal(await painel(p).getByLabel('Título').inputValue(), 'Taxa de frete', 'F2 pela barra não abriu a edição')
        assert.equal(await p.evaluate(() => window.__f2NaJanela), 0, 'F2 pela barra vazou para a janela: na demonstração abriria Simulações junto')
      })
      // A Moldura escuta F2 na janela (Simular, na demonstração): o F2 do seletor não pode chegar lá.
      async function escutarF2NaJanela(p) {
        await p.evaluate(() => { window.__f2NaJanela = 0; window.addEventListener('keydown', (e) => { if (e.key === 'F2') window.__f2NaJanela += 1 }) })
      }
      const sujar = async (p) => {
        await abrirPainel(p)
        await p.getByRole('button', { name: 'Nova resposta' }).click()
        await painel(p).getByLabel('Título').fill('Rascunho importante')
        return painel(p)
      }
      await cenario(`q-botao-respostas-nao-apaga-formulario-sujo-${tela}`, async () => {
        const p = await abrir(tela)
        const f = await sujar(p)
        await p.getByRole('button', { name: 'Respostas', exact: true }).click()
        assert.equal(await painel(p).count(), 1, 'o botão Respostas fechou o painel com formulário sujo')
        assert.equal(await f.getByLabel('Título').inputValue(), 'Rascunho importante')
        await f.getByText('Descartar o que foi digitado?').waitFor(T)
      })
      await cenario(`r-nota-interna-nao-apaga-formulario-sujo-${tela}`, async () => {
        const p = await abrir(tela)
        const f = await sujar(p)
        await p.getByRole('button', { name: 'Nota interna' }).click()
        assert.equal(await painel(p).count(), 1, 'a Nota interna fechou o painel com formulário sujo')
        assert.equal(await f.getByLabel('Título').inputValue(), 'Rascunho importante')
        await f.getByText('Descartar o que foi digitado?').waitFor(T)
      })
      await cenario(`p-sem-permissao-nao-edita-${tela}`, async () => {
        const p = await abrir(tela, 'semedicao=1')
        await abrirPainel(p)
        assert.equal(await p.getByRole('button', { name: 'Nova resposta' }).count(), 0)
        assert.equal(await p.getByRole('button', { name: /^Editar resposta / }).count(), 0)
        const busca = p.getByRole('combobox', { name: 'Buscar resposta pronta' })
        assert.equal(await busca.getAttribute('aria-keyshortcuts'), null)
        await busca.press('F2')
        assert.equal(await painel(p).locator('form').count(), 0, 'F2 abriu o formulário sem permissão')
        await p.getByRole('button', { name: 'Ver respostas' }).waitFor(T)
        await foto(p, `depois-p-sem-permissao-${tela}`)
      })
      await cenario(`j-sem-categoria-nenhuma-${tela}`, async () => {
        const p = await abrir(tela, 'sem=categorias')
        await abrirPainel(p)
        await p.getByRole('option').first().waitFor(T)
        assert.deepEqual(await cabecalhos(p), [], 'sem categoria, sem cabeçalho')
        assert.deepEqual(await titulos(p), ['Bom dia', 'Chave Pix'])
        await foto(p, `depois-j-sem-categorias-${tela}`)
      })
    }
  }
  assert.deepEqual(erros, [], 'erros de página')
} finally {
  await navegador.close()
}
const falhas = resultado.filter(([, s]) => s !== 'ok')
console.log(`\n${resultado.length - falhas.length}/${resultado.length} cenários ok`)
if (falhas.length || erros.length) { console.log(erros.join('\n')); process.exit(1) }
