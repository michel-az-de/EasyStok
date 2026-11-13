// Componente real com contexto controlado: correção da identidade enquanto o diálogo está aberto.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2395/reconferencia')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width, initial-scale=1"><div id="root"></div><script type="module" src="./browser.jsx"></script></html>')
writeFileSync(resolve(pasta, 'browser.jsx'), `
import React,{useState,useRef} from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoAcoes} from '../../../src/aplicacao/contextos';
import {criarAcoesLocais} from '../../../src/aplicacao/criarAcoes';
import {casosRascunhoDaComanda} from '../../../src/aplicacao/casos/rascunhoDaComanda';
import {ConferenciaPessoaDaComanda} from '../../../src/features/ficha-cliente/ConferenciaPessoaDaComanda';
import '../../../src/estilos/base.css';
function Teste(){const [estado,definir]=useState({conversas:[{id:'conversa',clienteId:'b',vinculoRevisao:5,nome:'Pessoa B',rascunho:'Mensagem preservada',pedido:{itens:[{sku:'lasanha',qtd:2}],janela:'amanha',meio:'dinheiro-entrega',identidadeProposta:{clienteId:'a',vinculoRevisao:4}}}]});
const ref=useRef(estado);ref.current=estado;const acoes=criarAcoesLocais({estadoRef:ref,agoraRef:{current:Date.now()},despachar:a=>definir(e=>casosRascunhoDaComanda[a.tipo](e,a))});
window.mudarPessoa=()=>definir(e=>({...e,conversas:[{...e.conversas[0],vinculoRevisao:7}]}));
window.retirarPessoa=()=>definir(e=>({...e,conversas:[{...e.conversas[0],clienteId:null,vinculoRevisao:8}]}));
window.vincularPessoa=()=>definir(e=>({...e,conversas:[{...e.conversas[0],clienteId:'c',nome:'Pessoa C',vinculoRevisao:9}]}));
return <ContextoAcoes.Provider value={acoes}><ConferenciaPessoaDaComanda conversa={estado.conversas[0]} aoAbrirCadastro={()=>window.cadastroAberto=true}/><output data-testid="estado">{JSON.stringify(estado.conversas[0])}</output></ContextoAcoes.Provider>}
createRoot(document.getElementById('root')).render(<Teste/>);
`)
const browser = await chromium.launch({ headless: true })
const page = await browser.newPage({ viewport: { width: 390, height: 844 }, locale: 'pt-BR', reducedMotion: 'reduce' })
const erros = []; page.on('pageerror', e => erros.push(e.message))
try {
  await page.goto(`${process.env.CONSOLE_TEST_URL ?? 'http://127.0.0.1:5209'}/.build/2395/reconferencia/index.html`)
  await page.getByRole('button', { name: 'Conferir pessoa da comanda', exact: true }).click()
  await page.getByRole('dialog').getByText('Pessoa B', { exact: true }).waitFor()
  await page.evaluate(() => window.mudarPessoa())
  await page.getByRole('alert').waitFor()
  assert.equal(await page.getByRole('button', { name: 'Confirmar pessoa e manter itens' }).isEnabled(), false)
  await page.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await page.getByRole('button', { name: 'Conferir pessoa da comanda', exact: true }).click()
  await page.getByRole('button', { name: 'Confirmar pessoa e manter itens' }).click()
  let estado = JSON.parse(await page.getByTestId('estado').textContent())
  assert.deepEqual(estado.pedido.identidadeProposta, { clienteId: 'b', vinculoRevisao: 7 })
  assert.deepEqual(estado.pedido.itens, [{ sku: 'lasanha', qtd: 2 }]); assert.equal(estado.pedido.janela, 'amanha'); assert.equal(estado.rascunho, 'Mensagem preservada')
  await page.evaluate(() => window.retirarPessoa())
  await page.getByRole('button', { name: 'Conferir cadastro', exact: true }).click()
  assert.ok(await page.evaluate(() => window.cadastroAberto))
  await page.evaluate(() => window.vincularPessoa())
  await page.getByRole('button', { name: 'Conferir pessoa da comanda', exact: true }).click()
  await page.getByRole('dialog').getByText('Pessoa C', { exact: true }).waitFor()
  await page.getByRole('dialog').evaluate(async el => { await Promise.all(el.getAnimations({ subtree: true }).map(a => a.finished)) })
  const confirmar = page.getByRole('button', { name: 'Confirmar pessoa e manter itens' })
  const posicao = await confirmar.boundingBox()
  assert.ok(posicao && posicao.y + posicao.height <= 844, 'Confirmação visível no viewport de 390px')
  await page.screenshot({ path: resolve(pasta, 'reconferencia-390.png') })
  await page.getByRole('button', { name: 'Confirmar pessoa e manter itens' }).click()
  estado = JSON.parse(await page.getByTestId('estado').textContent())
  assert.deepEqual(estado.pedido.identidadeProposta, { clienteId: 'c', vinculoRevisao: 9 })
  assert.equal(estado.pedido.itens[0].qtd, 2); assert.equal(estado.pedido.meio, 'dinheiro-entrega')
  assert.deepEqual(erros, [])
  console.log('Reconferência 390px: ABA durante diálogo bloqueado; cancelar/reabrir confirma snapshot exibido; sem cadastro abre vínculo; itens, entrega, meio e mensagem preservados.')
} finally { await browser.close() }
