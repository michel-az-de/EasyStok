// Componentes reais e retratos HTTP/PostgreSQL; ações controladas, sem enviar mensagens ou dinheiro.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5752'
const pasta = resolve(import.meta.dirname, '../.build/pedido-final')
const evidencias = resolve(process.env.EVIDENCIAS_UX ?? pasta)
mkdirSync(evidencias, { recursive: true })
for (const nome of ['cancelado-sinal', 'cancelado-segundo-link']) JSON.parse(readFileSync(join(pasta, nome + '.json'), 'utf8'))
writeFileSync(join(pasta, 'index.html'), '<!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Homologação de pedidos encerrados</title><div id="root"></div><script type="module" src="/.build/pedido-final/index.jsx"></script></html>')
writeFileSync(join(pasta, 'index.jsx'), `
import React from 'react'; import { createRoot } from 'react-dom/client'; import { DndContext } from '@dnd-kit/core';
import { ContextoEstado, ContextoAcoes, ContextoCatalogo } from '/src/aplicacao/contextos.js';
import { ContextoAcessoModulos } from '/src/aplicacao/acessoModulos.jsx';
import { pedidoDaApi } from '/src/infra/api/comandaApi.js';
import { BlocoPedido } from '/src/features/ficha-cliente/BlocoPedido.jsx';
import { BlocoCobranca } from '/src/features/ficha-cliente/BlocoCobranca.jsx';
import { BarraProximoPasso } from '/src/features/ficha-cliente/BarraProximoPasso.jsx';
import '/src/estilos/base.css';
const q = new URLSearchParams(location.search); const nome = q.get('caso');
const retrato = await (await fetch('/.build/pedido-final/' + nome + '.json')).json();
if(q.has('ativo')) retrato.status = nome === 'cancelado-sinal' ? 'aguardando' : 'aguardando_pagamento';
const pedido = pedidoDaApi(retrato); window.retratoDaProva = structuredClone(pedido); window.acoesDaProva = [];
const acao = (nome) => () => { window.acoesDaProva.push({nome}); return Promise.resolve({}); };
const props = {pedido, editavel: pedido.estado !== 'cancelado', nomeCliente:'Cliente da homologação', conversaId:'conversa-controlada', numeroComanda:pedido.numero, agora:Date.now(), aoGerarPedido:acao('gerar'), aoReenviar:acao('reenviar'), aoGerarPix:acao('gerar'), aoReenviarPix:acao('reenviar'), aoConfirmarPagamento:acao('receber'), aoCobrarDiferenca:acao('cobrar-diferenca'), aoEncerrarAtendimento:acao('encerrar')};
createRoot(document.getElementById('root')).render(<ContextoAcessoModulos value={{permite:()=>true,acoes:{editarAtendimento:true}}}><ContextoEstado value={{fonteApi:true,fidelidade:{cupons:[]},conversas:[]}}><ContextoCatalogo value={{cardapio:[],linhas:{},entregadores:[]}}><ContextoAcoes value={{marcarCanhotoImpresso:acao('imprimir'), confirmarPagamento:acao('receber'), carregarJanelasComanda:async()=>({janelas:[]})}}><DndContext><main style={{maxWidth:680,margin:'24px auto',padding:16,display:'grid',gap:16}}><BlocoPedido {...props}/><BlocoCobranca {...props}/><BarraProximoPasso {...props}/></main></DndContext></ContextoAcoes></ContextoCatalogo></ContextoEstado></ContextoAcessoModulos>);
`)
const falhas = []; const browser = await chromium.launch({ headless: true })
try {
  for (const width of [1440, 390]) for (const nome of ['cancelado-sinal', 'cancelado-segundo-link']) for (const ativo of [false, true]) {
    const id = nome + (ativo ? '-ativo' : '') + '-' + width
    const contexto = await browser.newContext({ viewport: { width, height: 960 }, reducedMotion: 'reduce' })
    const page = await contexto.newPage(); const erros = []; page.on('pageerror', erro => erros.push(erro.message))
    try {
      await page.goto(origem + '/.build/pedido-final/index.html?caso=' + nome + (ativo ? '&ativo=1' : ''))
      await page.locator('main').waitFor(); const texto = await page.locator('main').innerText()
      await page.screenshot({ path: join(evidencias, id + '.png'), fullPage: true })
      assert.deepEqual(erros, [])
      if (ativo) {
        if (nome === 'cancelado-sinal') assert.match(texto, /Item novo depois do pagamento[\s\S]*32,50/)
        else assert.match(texto, /Segundo link vencido: o EasyStok cancela o pedido sozinho/)
      } else {
        assert.doesNotMatch(texto, /pedido continua valendo|Item novo depois do pagamento|a cobrar|clique em Marcar pago/i)
        assert.equal(await page.getByRole('button', { name: /Marcar pago|Cobrar diferença|Reenviar cobrança|Registrar sinal/ }).count(), 0)
        await page.getByRole('button', { name: 'Encerrar atendimento', exact: true }).click()
        assert.deepEqual(await page.evaluate(() => window.acoesDaProva), [{nome:'encerrar'}])
        const retrato = await page.evaluate(() => window.retratoDaProva)
        assert.equal(retrato.estado, 'cancelado')
        assert.equal(retrato.totalPagoApi, nome === 'cancelado-sinal' ? 32.5 : 0)
      }
      console.log('ok ' + id)
    } catch (erro) { falhas.push(id + ': ' + erro.message.split('\n')[0]) }
    finally { await contexto.close() }
  }
} finally { await browser.close() }
assert.deepEqual(falhas, [], falhas.join('\n'))
console.log('OK: 8 retratos reais/controlados, estados finais e negativos ativos, desktop/mobile; nenhuma ação financeira executada.')
