// Prova da #2783: um editor só para a mensagem automática e lista de modelos aprovados no Programar mensagem.
// Estrutura (o mesmo componente nas duas telas) e navegador (Chromium, componentes reais com contextos de teste):
//  1. Respostas e Automações usam o mesmo EditorDeAutomatica (a aba não tem mais editor próprio).
//  2. O editor único mostra o motivo que o servidor recusou ao salvar e mantém o texto.
//  3. Em Respostas, a chave desconhecida segue marcada como erro na prévia (#3036) e o editor traz selos e prévia.
//  4. Programar mensagem lista os modelos aprovados, não tem nome livre e programa o modelo escolhido.
//
// Roda com o Vite no ar: CONSOLE_TEST_URL=http://localhost:5735 node ferramentas/prova-2783-editor-automatica-unico.mjs
// Contra o master (para ver o Red): PROVA_SRC=<pasta src do master>; a cena de navegador usa o src do worktree.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'

const raiz = process.env.PROVA_SRC ?? resolve(import.meta.dirname, '../src')
const fonte = (p) => readFileSync(resolve(raiz, p), 'utf8')

// 1. Estrutura: um componente, duas telas; Programar usa o seletor compartilhado e não pede nome livre.
const aba = fonte('features/gestao/respostas/AbaRespostas.jsx')
const modal = fonte('features/automacoes/ModalAutomacoes.jsx')
assert.match(aba, /<EditorDeAutomatica/, 'Respostas usa o editor único de automática')
assert.match(modal, /<EditorDeAutomatica/, 'Automações usa o editor único de automática')
assert.doesNotMatch(aba, /<CampoArea[^>]*Texto de \$\{regra\.nome\}/, 'a aba não mantém um editor próprio da automática')
const programar = fonte('features/atendimento/ModalProgramarMensagem.jsx')
assert.match(programar, /<SeletorDeModelo/, 'Programar mensagem usa o seletor de modelo aprovado')
assert.doesNotMatch(programar, /Nome do modelo na Meta/, 'Programar mensagem não pede o nome do modelo digitado')
assert.match(fonte('features/atendimento/ModalEnviarModelo.jsx'), /<SeletorDeModelo/, 'Enviar modelo usa o mesmo seletor')

// 2 a 4: navegador, com a cena montada pelo Vite.
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2783/editor')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoEstado,ContextoAcoes,ContextoCatalogo} from '../../../src/aplicacao/contextos';
import {EditorDeAutomatica} from '../../../src/componentes/EditorDeAutomatica';
import {AbaRespostas} from '../../../src/features/gestao/respostas/AbaRespostas';
import {ModalProgramarMensagem} from '../../../src/features/atendimento/ModalProgramarMensagem';
import {ModalAutomacoes} from '../../../src/features/automacoes/ModalAutomacoes';
import {analisarAutomatica} from '../../../src/dominio/automacao';
import {FUNCIONAMENTO_PADRAO} from '../../../src/dominio/funcionamento';
import '../../../src/estilos/base.css';
document.body.style.cssText='margin:0;padding:16px;background:var(--fundo);font-family:var(--f-corpo);color:var(--tinta)';
const MODELOS=[{nome:'lembrete_pedido',idioma:'pt_BR',categoria:'UTILITY',quantidadeParametros:1,corpo:'Oi, seu pedido {{1}} sai hoje.'},{nome:'promo_cor',idioma:'pt_BR',categoria:'MARKETING',quantidadeParametros:0,corpo:'Chegou cor nova.'}];
window.__programadas=[]; window.__salvas=[];
const regra={id:'r1',nome:'Primeira resposta',descricao:'Primeira mensagem.',ativa:true,texto:'Oi, {nome}!',gatilho:'primeiro-contato'};
const estado={regras:[regra],fonteApi:false,cargaDasRegras:{estado:'pronto'},agora:Date.parse('2026-11-04T12:00:00Z'),funcionamento:FUNCIONAMENTO_PADRAO,aberta:true,lojaAberta:null,permissaoNotificacao:'default',som:{},audioBloqueado:false,selecionada:null};
const acoes={alternarRegra(){},editarRegra:async(id,c)=>{window.__salvas.push([id,c.texto])},incluirRespostaPronta(){},editarRespostaPronta(){},alternarArquivamentoRespostaPronta(){},
  listarModelos:async()=>MODELOS,listarProgramadas:async()=>[],cancelarProgramada:async()=>({}),
  programarMensagem:async(id,d)=>{window.__programadas.push(d);return {id:'p1',situacao:'Agendada',agendadaPara:d.agendadaPara,modelo:d.modelo}}};
const acoesFalha={...acoes,listarModelos:async()=>{throw new Error('falhou')}};
const conversa={id:'c1',nome:'Maria Souza'};
const canal={nome:'WhatsApp',aceitaModelo:true};
const cena=new URLSearchParams(location.search).get('c');
function Cena(){return <ContextoEstado.Provider value={estado}><ContextoAcoes.Provider value={acoes}><ContextoCatalogo.Provider value={{respostasProntas:[],janelas:[],canais:[]}}>
  {cena==='editor'&&<section id="editor"><EditorDeAutomatica regra={regra} analisar={(texto)=>analisarAutomatica({regra,texto,fonteApi:true,editavel:true,valores:{nome:'Maria'}})} editavel avisoApi='A variável {xyz} não existe. Use {nome}, {pedido} ou {faixa}.' aoSalvar={async()=>false}/></section>}
  {cena==='salvou'&&<section id="salvou"><EditorDeAutomatica regra={regra} analisar={(texto)=>analisarAutomatica({regra,texto,fonteApi:false,editavel:true,valores:{nome:'Maria'}})} editavel aoSalvar={async()=>true}/></section>}
  {cena==='falha'&&<ContextoAcoes.Provider value={acoesFalha}><section id="falha"><ModalProgramarMensagem conversa={conversa} canal={canal} rascunho="" aoAgendar={()=>{}} aoFechar={()=>{}}/></section></ContextoAcoes.Provider>}
  {cena==='aba'&&<section id="aba"><AbaRespostas comAutomaticas/></section>}
  {cena==='automacoes'&&<section id="automacoes"><ModalAutomacoes regras={[regra]} aoAlternar={()=>{}} aoFechar={()=>{}}/></section>}
  {cena==='programar'&&<section id="programar"><ModalProgramarMensagem conversa={conversa} canal={canal} rascunho="" aoAgendar={()=>{}} aoFechar={()=>{}}/></section>}
</ContextoCatalogo.Provider></ContextoAcoes.Provider></ContextoEstado.Provider>}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const browser = await chromium.launch({ headless: true })
const pagina = await (await browser.newContext({ viewport: { width: 1280, height: 1400 }, locale: 'pt-BR', reducedMotion: 'reduce' })).newPage()
pagina.setDefaultTimeout(8000)
const erros = []
pagina.on('pageerror', (e) => erros.push(e.message))
try {
  const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2783/editor/index.html`
  await pagina.goto(`${base}?c=editor`, { timeout: 60000 })
  // 2. Erro do servidor no editor único: o motivo aparece e o texto digitado fica.
  const editor = pagina.locator('#editor')
  const area = editor.getByRole('textbox', { name: 'Texto de Primeira resposta' })
  await area.fill('Oi, {nome}, de novo!')
  await editor.getByRole('button', { name: 'Salvar', exact: true }).click()
  await editor.getByRole('alert').filter({ hasText: 'A variável {xyz} não existe' }).waitFor()
  assert.equal(await area.inputValue(), 'Oi, {nome}, de novo!', 'a edição é mantida depois da recusa')

  // Salvar com espaço no fim: o servidor grava sem ele, então o rascunho não pode ficar "não salvo".
  await pagina.goto(`${base}?c=salvou`)
  const salvou = pagina.locator('#salvou')
  await salvou.getByRole('textbox', { name: 'Texto de Primeira resposta' }).fill('Oi, {nome}!  \n')
  await salvou.getByText('Alterações não salvas').waitFor()
  await salvou.getByRole('button', { name: 'Salvar', exact: true }).click()
  await salvou.getByText('Salvo', { exact: true }).waitFor()
  assert.equal(await salvou.getByText('Alterações não salvas').count(), 0, 'salvo com espaço no fim não fica como não salvo')

  // 3. Respostas: o editor traz selos e prévia, e a chave desconhecida vira erro marcado.
  await pagina.goto(`${base}?c=aba`)
  const abaEl = pagina.locator('#aba')
  await abaEl.getByRole('button', { name: 'Editar texto' }).click()
  const editorAba = abaEl.locator('[data-editor-automatica]')
  await editorAba.waitFor()
  await editorAba.getByRole('list', { name: 'Variáveis que o texto aceita' }).waitFor()
  await editorAba.getByRole('textbox').fill('Oi, {nome} {xyz}')
  await editorAba.getByRole('group').filter({ hasText: 'Oi, Maria' }).waitFor()
  assert.equal(await editorAba.locator('mark').count(), 1, 'a chave desconhecida fica marcada na prévia')
  await editorAba.getByRole('textbox').fill('Oi, {nome}, tudo bem?')
  await editorAba.getByRole('button', { name: 'Salvar', exact: true }).click()
  await pagina.waitForFunction(() => window.__salvas.length === 1)
  assert.deepEqual(await pagina.evaluate(() => window.__salvas[0]), ['r1', 'Oi, {nome}, tudo bem?'])

  // #3056: Automações montada de verdade (a prova lia só o fonte e deixou passar o import do Pilula).
  await pagina.goto(`${base}?c=automacoes`)
  await pagina.locator('#automacoes').getByText('Aberta agora', { exact: true }).waitFor()
  await pagina.locator('#automacoes [data-editor-automatica]').first().waitFor()
  assert.deepEqual(erros, [], 'Automações abre sem erro de página')

  // Falha ao carregar a lista: o erro real aparece e a frase de "nenhuma mensagem padrão" não.
  await pagina.goto(`${base}?c=falha`)
  const falha = pagina.getByRole('dialog', { name: 'Programar mensagem' })
  await falha.getByRole('checkbox', { name: /Usar mensagem padrão/ }).check()
  await falha.getByRole('alert').first().waitFor()
  assert.equal(await falha.locator('[data-vazio]').count(), 0, 'com erro ao carregar, não diz que a conta não tem mensagem padrão')

  // 4. Programar mensagem: lista dos aprovados, sem nome livre, e o modelo escolhido vai para a API.
  await pagina.goto(`${base}?c=programar`)
  const prog = pagina.getByRole('dialog', { name: 'Programar mensagem' })
  await prog.getByRole('checkbox', { name: /Usar mensagem padrão/ }).check()
  assert.equal(await prog.getByRole('textbox', { name: /Nome do modelo/ }).count(), 0, 'sem campo de nome livre')
  const seletor = prog.getByRole('combobox', { name: /mensagem padrão/i })
  await seletor.waitFor()
  assert.deepEqual(await seletor.locator('option').allInnerTexts(), ['Selecione um modelo', 'lembrete_pedido · pt_BR', 'promo_cor · pt_BR'])
  await seletor.selectOption({ label: 'lembrete_pedido · pt_BR' })
  await prog.getByRole('textbox', { name: /1º campo/ }).fill('123')
  await prog.getByText('Oi, seu pedido 123 sai hoje.').waitFor()
  await prog.getByRole('button', { name: 'Programar', exact: true }).click()
  await pagina.waitForFunction(() => window.__programadas.length === 1)
  const enviado = await pagina.evaluate(() => window.__programadas[0])
  assert.deepEqual(enviado.modelo, { nome: 'lembrete_pedido', idioma: 'pt_BR', parametros: ['123'] })
  assert.deepEqual(erros, [], 'sem erro de página')
  console.log('OK prova-2783: editor único, erro do servidor, prévia e seletor de modelo')
} finally {
  await browser.close()
}
