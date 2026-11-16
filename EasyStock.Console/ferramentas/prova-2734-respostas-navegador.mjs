// #2734 (B2, A05 A10 A17 A18 T17 T18): Chromium e componente real da aba Respostas. Cancelar com texto pede
// confirmação; Título passa de 120 (limite do servidor) sem maxLength e trava o salvar; arquivar e restaurar
// desabilitam durante a gravação e anunciam o resultado; salvar devolve o foco ao Editar do item.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2734/respostas')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React,{useMemo,useState} from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoEstado,ContextoAcoes,ContextoCatalogo} from '../../../src/aplicacao/contextos';
import {AbaRespostas} from '../../../src/features/gestao/respostas/AbaRespostas';
import '../../../src/estilos/base.css';
document.body.style.cssText='margin:0;padding:16px;box-sizing:border-box;background:var(--fundo);font-family:var(--f-corpo);color:var(--tinta)';
window.__chamadas=[];
const espera=(ms)=>new Promise(r=>setTimeout(r,ms));
function Cena(){
  const [lista,setLista]=useState([{id:'R1',titulo:'Saudação',atalho:'/oi',texto:'Olá, {nome}!',arquivada:false},{id:'R2',titulo:'Pix',atalho:'/pix',texto:'Chave Pix da Casa.',arquivada:false}]);
  const acoes=useMemo(()=>({
    incluirRespostaPronta:async(d)=>{window.__chamadas.push(['incluir',d]);await espera(150);setLista(l=>[...l,{id:'N'+l.length,arquivada:false,...d}])},
    editarRespostaPronta:async(id,d)=>{window.__chamadas.push(['editar',id,d]);await espera(150);setLista(l=>l.map(r=>r.id===id?{...r,...d}:r))},
    alternarArquivamentoRespostaPronta:async(id)=>{window.__chamadas.push(['alternar',id]);await espera(400);setLista(l=>l.map(r=>r.id===id?{...r,arquivada:!r.arquivada}:r))},
  }),[]);
  return <ContextoEstado.Provider value={{fonteApi:false,regras:[]}}><ContextoAcoes.Provider value={acoes}><ContextoCatalogo.Provider value={{respostasProntas:lista}}><AbaRespostas comAutomaticas={false}/></ContextoCatalogo.Provider></ContextoAcoes.Provider></ContextoEstado.Provider>
}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1000, height: 900 }, locale: 'pt-BR', reducedMotion: 'reduce' })
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(8000)
const erros = []
pagina.on('pageerror', e => erros.push(e.message))
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2734/respostas/index.html`
const focoAtual = () => pagina.evaluate(() => document.activeElement?.getAttribute('aria-label') ?? document.activeElement?.textContent?.trim())
try {
  await pagina.goto(base)
  await pagina.getByText('Saudação', { exact: true }).waitFor()

  // A05: Cancelar com texto digitado pergunta; sem texto fecha direto.
  await pagina.getByRole('button', { name: 'Nova resposta', exact: true }).click()
  await pagina.getByRole('button', { name: 'Cancelar', exact: true }).click()
  assert.equal(await pagina.getByRole('textbox', { name: /^Título/ }).count(), 0, 'Sem alteração, Cancelar fecha direto')
  await pagina.getByRole('button', { name: 'Nova resposta', exact: true }).click()
  await pagina.getByRole('textbox', { name: /^Título/ }).fill('Horário')
  await pagina.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await pagina.getByRole('alert').filter({ hasText: 'Descartar o que foi digitado?' }).waitFor()
  assert.equal(await pagina.getByRole('textbox', { name: /^Título/ }).inputValue(), 'Horário', 'A pergunta não apaga o texto')
  await pagina.getByRole('button', { name: 'Continuar editando', exact: true }).click()
  assert.equal(await pagina.getByRole('alert').filter({ hasText: 'Descartar o que foi digitado?' }).count(), 0)
  assert.equal(await pagina.getByRole('textbox', { name: /^Título/ }).inputValue(), 'Horário')
  await pagina.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await pagina.getByRole('button', { name: 'Descartar', exact: true }).click()
  assert.equal(await pagina.getByRole('textbox', { name: /^Título/ }).count(), 0, 'Descartar fecha o formulário')
  assert.equal(await focoAtual(), 'Nova resposta', 'O foco volta ao botão que abriu o formulário')

  // A10/T17: sem maxLength; 121 caracteres mostram o erro e travam o salvar; 120 liberam.
  await pagina.getByRole('button', { name: 'Nova resposta', exact: true }).click()
  const titulo = pagina.getByRole('textbox', { name: /^Título/ })
  assert.equal(await titulo.getAttribute('maxlength'), null, 'Sem maxLength no campo')
  // #2792: o motivo do Salvar diz só o que falta, não pede o que já está preenchido.
  await pagina.getByText('Preencha o título e o texto.', { exact: true }).waitFor()
  await titulo.fill('Horário')
  await pagina.getByText('Preencha o texto.', { exact: true }).waitFor()
  await titulo.fill('')
  await pagina.getByRole('textbox', { name: /^Texto/ }).fill('Corpo')
  await pagina.getByText('Preencha o título.', { exact: true }).waitFor()
  await titulo.fill('a'.repeat(121))
  await pagina.getByText('Use até 120 caracteres. Agora tem 121.', { exact: true }).waitFor()
  assert.equal(await pagina.getByRole('button', { name: 'Salvar', exact: true }).isDisabled(), true)
  await titulo.fill('a'.repeat(120))
  assert.equal(await pagina.getByText(/Use até 120 caracteres/).count(), 0)
  assert.equal(await pagina.getByRole('button', { name: 'Salvar', exact: true }).isDisabled(), false)
  // #2792: Atalho (40) e Texto (4096) avisam e travam o salvar como o Título.
  const atalho = pagina.getByRole('textbox', { name: /^Atalho/ })
  const texto = pagina.getByRole('textbox', { name: /^Texto/ })
  assert.equal(await atalho.getAttribute('maxlength'), null, 'Sem maxLength no Atalho')
  assert.equal(await texto.getAttribute('maxlength'), null, 'Sem maxLength no Texto')
  await atalho.fill('/' + 'a'.repeat(40))
  await pagina.getByText('Use até 40 caracteres. Agora tem 41.', { exact: true }).waitFor()
  assert.equal(await pagina.getByRole('button', { name: 'Salvar', exact: true }).isDisabled(), true, 'Atalho 41 trava o salvar')
  await atalho.fill('/' + 'a'.repeat(39))
  assert.equal(await pagina.getByText(/Use até 40 caracteres/).count(), 0)
  assert.equal(await pagina.getByRole('button', { name: 'Salvar', exact: true }).isDisabled(), false)
  await atalho.fill('')
  await texto.fill('a'.repeat(4097))
  await pagina.getByText('Use até 4096 caracteres. Agora tem 4097.', { exact: true }).waitFor()
  assert.equal(await pagina.getByRole('button', { name: 'Salvar', exact: true }).isDisabled(), true, 'Texto 4097 trava o salvar')
  await texto.fill('a'.repeat(4096))
  assert.equal(await pagina.getByText(/Use até 4096 caracteres/).count(), 0)
  assert.equal(await pagina.getByRole('button', { name: 'Salvar', exact: true }).isDisabled(), false)
  await texto.fill('Corpo')
  // #2792: título longo sem atalho: o atalho sugerido já cabe em 40, com palavras inteiras, e o Salvar fica livre.
  await titulo.fill('palavra '.repeat(8).trim())
  assert.equal(await atalho.getAttribute('placeholder'), '/palavra-palavra-palavra-palavra-palavra', 'atalho sugerido cabe em 40')
  assert.equal(await pagina.getByText(/Use até 40 caracteres/).count(), 0)
  assert.equal(await pagina.getByRole('button', { name: 'Salvar', exact: true }).isDisabled(), false, 'título longo não trava o salvar')
  await pagina.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await pagina.getByRole('button', { name: 'Descartar', exact: true }).click()

  // A18: salvar uma edição devolve o foco ao Editar do item salvo.
  await pagina.getByRole('button', { name: 'Editar resposta Pix', exact: true }).click()
  await pagina.getByRole('textbox', { name: /^Texto/ }).fill('Chave Pix nova.')
  await pagina.getByRole('button', { name: 'Salvar', exact: true }).click()
  await pagina.getByText('Chave Pix nova.', { exact: true }).waitFor()
  await pagina.waitForFunction(() => document.activeElement?.getAttribute('aria-label') === 'Editar resposta Pix')

  // A17/T18: duplo clique grava uma vez, desabilita durante a gravação e anuncia o resultado.
  const arquivar = pagina.getByRole('button', { name: 'Arquivar resposta Pix', exact: true })
  await arquivar.dblclick()
  await pagina.getByRole('status').filter({ hasText: 'Resposta Pix arquivada.' }).waitFor()
  assert.equal((await pagina.evaluate(() => window.__chamadas)).filter(c => c[0] === 'alternar').length, 1, 'Duplo clique grava uma vez')
  // A linha arquivada sai da lista: o foco vai para um controle que continua na tela, não se perde.
  await pagina.waitForFunction(() => document.activeElement?.textContent?.trim() === 'Nova resposta', undefined, { timeout: 4000 })
  await pagina.getByLabel('Mostrar arquivadas').check()
  const restaurar = pagina.getByRole('button', { name: 'Restaurar resposta Pix', exact: true })
  await restaurar.click()
  assert.equal(await restaurar.isDisabled(), true, 'Desabilitado durante a gravação')
  await pagina.getByRole('status').filter({ hasText: 'Resposta Pix restaurada.' }).waitFor()
  assert.equal(await pagina.getByRole('button', { name: 'Arquivar resposta Pix', exact: true }).isDisabled(), false)
  // A linha restaurada continua na tela: o foco volta ao botão dela.
  await pagina.waitForFunction(() => document.activeElement?.getAttribute('aria-label') === 'Arquivar resposta Pix', undefined, { timeout: 4000 })
  assert.deepEqual(erros, [])
  console.log('#2734 B2 respostas: descarte confirmado, limite de 120 sem maxLength, arquivar anunciado e sem duplo envio, foco devolvido.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await browser.close() }
