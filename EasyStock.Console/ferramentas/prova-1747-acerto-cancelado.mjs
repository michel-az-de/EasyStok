// Componente real, DTO da prova HTTP #1739 e respostas controladas dos contratos de estornos.
// Nenhuma confirmação, solicitação de estorno ou movimentação financeira é enviada.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5762'
const laboratorio = resolve(import.meta.dirname, '../.build/acerto-cancelado')
const pasta = resolve(process.env.EVIDENCIAS_UX ?? resolve(import.meta.dirname, '../../.build/1747/depois'))
const antes = process.env.ANTES === '1'
mkdirSync(pasta, { recursive: true })
const retrato = JSON.parse(readFileSync(join(laboratorio, 'cancelado-sinal.json'), 'utf8'))
assert.equal(retrato.status, 'cancelado'); assert.equal(retrato.totalPago, 32.5)
writeFileSync(join(laboratorio, 'index.html'), '<!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Homologação do acesso ao acerto</title><div id="root"></div><script type="module" src="/.build/acerto-cancelado/index.jsx"></script></html>')
writeFileSync(join(laboratorio, 'index.jsx'), `
import React from 'react'; import { createRoot } from 'react-dom/client';
import { ContextoAcessoModulos } from '/src/aplicacao/acessoModulos.jsx';
import { pedidoDaApi } from '/src/infra/api/comandaApi.js';
import { BlocoDevolucoes } from '/src/features/ficha-cliente/BlocoDevolucoes.jsx';
import '/src/estilos/base.css';
const q = new URLSearchParams(location.search); const retrato = await (await fetch('/.build/acerto-cancelado/cancelado-sinal.json')).json();
if(q.get('caso') === 'ativo') retrato.status = 'aguardando';
const pedido = pedidoDaApi(retrato); const admin = q.get('perfil') === 'Admin';
window.pedidoDaProva = structuredClone(pedido);
createRoot(document.getElementById('root')).render(<ContextoAcessoModulos value={{acoes:{devolverPagamentoManual:admin,estornarPagamentoOnline:admin}}}><main style={{maxWidth:680,margin:'24px auto',padding:16}}><BlocoDevolucoes pedidoId={pedido.pedidoId} cancelado={pedido.estado === 'cancelado'}/></main></ContextoAcessoModulos>);
`)
const browser = await chromium.launch({ headless: true }), resultados = [], indice = []
const casos = antes ? ['manual'] : ['manual', 'online', 'sem', 'ativo', 'devolvido', 'reservado', 'erro', 'pendente']
try {
  for (const width of [1280, 390]) for (const perfil of ['Admin', 'Operador']) for (const caso of casos) {
    const id = `${caso}-${perfil}-${width}`, chamadas = [], erros = []
    const context = await browser.newContext({ viewport: { width, height: 844 }, reducedMotion: 'reduce' })
    await context.addInitScript(({ pedidoId, pagamentoId, caso }) => {
      sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova-controlada', expiraEm: Date.now() + 3600000, empresa: {id:'empresa-prova'}, usuario:{id:'usuario-prova'} }))
      if (caso === 'pendente') localStorage.setItem('easystok.devolucao:empresa-prova:usuario-prova:' + pedidoId,
        JSON.stringify({operacaoId:'operacao-pendente',pagamentoId,valor:10,metodo:'pix',motivo:'Prova controlada',referencia:'prova',valorJaDevolvido:true}))
    }, { pedidoId: retrato.pedidoId, pagamentoId: retrato.pagamentos[0].id, caso })
    await context.route('**/api/**', async route => {
      const req = route.request(), path = new URL(req.url()).pathname
      if (!path.startsWith('/api/')) return route.continue()
      chamadas.push({ path, metodo: req.method() })
      if (req.method() !== 'GET') return route.fulfill({ status: 409, json: { message: 'A prova não permite escrita financeira.' } })
      const online = caso === 'online' || caso === 'reservado'
      const pagamentos = caso === 'sem' ? [] : retrato.pagamentos.map(p => ({...p,manual:!online,devolvido:caso === 'devolvido' ? p.valor : 0,disponivel:online || caso === 'devolvido' ? 0 : p.valor}))
      if (path.endsWith('/estornos-manuais')) {
        if (caso === 'erro') return route.fulfill({ status: 503, json: { message: 'Consulta indisponível na prova.' } })
        return route.fulfill({ json: { data: { pagamentos, estornos: caso === 'devolvido' ? [{id:'devolucao-prova',pagamentoId:pagamentos[0].id,valor:32.5,metodo:'pix',registradoEm:'2026-10-13T16:10:00Z',motivo:'Devolução já registrada na prova',referencia:'prova'}] : [] } } })
      }
      if (path.endsWith('/estornos-online')) return route.fulfill({ json: { data: { pagamentos: retrato.pagamentos.map(p => ({...p,devolvido:0,reservado:caso === 'reservado' ? p.valor : 0,disponivel:caso === 'reservado' ? 0 : p.valor,legado:false})),estornos:[] } } })
      return route.fulfill({ json: { data: [] } })
    })
    const page = await context.newPage(); page.on('pageerror', e => erros.push(e.message))
    const fotografar = async (etapa) => {
      const arquivo = join(pasta, `${id}-${etapa}.png`)
      await page.screenshot({ path: arquivo, fullPage: true })
      indice.push({caso,perfil,width,etapa,arquivo,tipoProva:'Componente real, HTTP controlado'})
    }
    try {
      const resposta = page.waitForResponse(r => r.url().endsWith('/estornos-manuais'))
      await page.goto(`${origem}/.build/acerto-cancelado/index.html?caso=${caso}&perfil=${perfil}`)
      await resposta
      const acerto = !antes && !['sem','ativo','erro'].includes(caso)
      const gatilho = page.getByRole('button', {name: acerto ? /^Conferir acerto/ : /^Devoluções/})
      await gatilho.waitFor()
      if (acerto && caso !== 'devolvido') await page.getByText('Recebimento registrado', {exact:true}).waitFor()
      if (caso === 'devolvido') await page.getByText(/32,50 devolvidos/).waitFor()
      if (acerto && caso !== 'devolvido') assert.equal(await page.getByText('Recebimento registrado', {exact:true})
        .evaluate(e => e.scrollWidth <= e.clientWidth), true, 'Informação factual inteira no celular')
      if (caso !== 'erro') assert.equal(await gatilho.getAttribute('aria-expanded'), 'false')
      await fotografar('recolhido')
      if (caso !== 'erro') await gatilho.click()
      assert.equal(await gatilho.getAttribute('aria-expanded'), 'true')
      if (caso === 'erro') await page.getByRole('alert').waitFor()
      else {
        await page.getByText(/Recebido:/).first().waitFor()
        if (caso === 'pendente') await page.getByText(/Há uma confirmação.*sem resposta conclusiva/).waitFor()
        const manualPermitido = perfil === 'Admin' && ['manual','ativo'].includes(caso)
        assert.equal(await page.getByRole('button', {name:'Registrar devolução manual',exact:true}).count(), manualPermitido ? 1 : 0)
        const onlinePermitido = perfil === 'Admin' && caso === 'online'
        if (caso === 'online' || caso === 'reservado') await page.getByText(/Devolvido:.*Disponível:/).waitFor()
        assert.equal(await page.getByRole('button', {name:'Solicitar estorno pelo Mercado Pago',exact:true}).count(), onlinePermitido ? 1 : 0)
      }
      assert.equal(await page.locator('form').count(), 0, 'Abrir a seção não abre um formulário financeiro')
      assert.doesNotMatch(await page.locator('main').innerText(), /devolução devida|acerto pendente|a devolver automaticamente/i)
      assert.deepEqual(chamadas.filter(c => c.metodo !== 'GET'), [])
      assert.deepEqual(erros, [])
      await fotografar('aberto')
      resultados.push({id,ok:true,escritasFinanceiras:0})
      console.log('ok ' + id)
    } catch (e) {
      console.error(JSON.stringify({id,erros,chamadas,texto:(await page.locator('body').innerText()).slice(0,500)}))
      throw e
    } finally { await context.close() }
  }
} finally {
  await browser.close()
  writeFileSync(join(pasta,'indice.json'),JSON.stringify(indice,null,2))
  writeFileSync(join(pasta,'resultado.json'),JSON.stringify(resultados,null,2))
}
console.log(`OK: ${resultados.length} jornadas; acesso em um clique, permissões intactas e zero escritas financeiras.`)
