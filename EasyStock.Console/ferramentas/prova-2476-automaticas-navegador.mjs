// #2476 (U8, revisão da PR): Chromium e componentes reais, sem API. A automática com variável que o EasyStok não
// preenche não liga nem salva; a ligada ainda desliga; a chave travada aponta o motivo; o Operador lê o porquê.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2476/automaticas')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React,{useState} from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoEstado,ContextoAcoes,ContextoCatalogo} from '../../../src/aplicacao/contextos';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {AbaRespostas} from '../../../src/features/gestao/respostas/AbaRespostas';
import '../../../src/estilos/base.css';
const gerente = !location.search.includes('operador');
window.chamadas = [];
function Cena(){
  const [regras,setRegras]=useState([
    {id:'r1',nome:'Cardápio',descricao:'Ao receber a primeira mensagem.',texto:'Veja: {linkCardapio}',ativa:false},
    {id:'r2',nome:'Antiga',descricao:'Ao fechar o pedido.',texto:'Link {linkCardapio}',ativa:true},
    {id:'r3',nome:'Limpa',descricao:'Ao confirmar o pagamento.',texto:'Oi {nome}',ativa:false},
  ]);
  const acoes={
    alternarRegra:(id)=>{window.chamadas.push(['alternar',id]);setRegras(rs=>rs.map(r=>r.id===id?{...r,ativa:!r.ativa}:r))},
    editarRegra:(id,dados)=>{window.chamadas.push(['editar',id,dados.texto]);setRegras(rs=>rs.map(r=>r.id===id?{...r,...dados}:r));return Promise.resolve(true)},
    incluirRespostaPronta:()=>{},editarRespostaPronta:()=>{},alternarArquivamentoRespostaPronta:()=>{},
  };
  return <ContextoAcessoModulos.Provider value={{acoes:{editarAutomacoes:gerente,editarRespostas:gerente},permite:()=>true}}>
    <ContextoEstado.Provider value={{fonteApi:true,regras,cargaDasRegras:{estado:'pronto'},conversas:[],selecionada:null}}>
    <ContextoCatalogo.Provider value={{respostasProntas:[]}}><ContextoAcoes.Provider value={acoes}>
    <AbaRespostas comAutomaticas={!location.search.includes('modelos')}/></ContextoAcoes.Provider></ContextoCatalogo.Provider></ContextoEstado.Provider></ContextoAcessoModulos.Provider>
}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 2400 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.route('**/api/**', route => new URL(route.request().url()).pathname.startsWith('/api/') ? route.abort() : route.continue())
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(10000)
const erros = []
pagina.on('pageerror', e => erros.push(e.message)); pagina.on('console', m => m.type() === 'error' && erros.push(m.text()))
const endereco = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2476/automaticas/index.html`
const chave = (nome) => pagina.getByRole('checkbox', { name: new RegExp(`^${nome}, `) })
const linha = (nome) => pagina.getByRole('listitem').filter({ has: chave(nome) })
try {
  await pagina.goto(endereco)
  await chave('Cardápio').waitFor()
  // Desligada com {linkCardapio}: não liga, e a chave diz por quê (aria-describedby no alerta).
  assert.equal(await chave('Cardápio').isDisabled(), true, 'Com variável solta a automática não liga')
  const descrita = await chave('Cardápio').getAttribute('aria-describedby')
  assert.ok(descrita, 'A chave travada aponta o motivo')
  assert.match(await pagina.locator(`[id="${descrita}"]`).innerText(), /^Troque ou apague \{linkCardapio\}/)
  // Ligada com variável solta: ainda desliga.
  assert.equal(await chave('Antiga').isDisabled(), false, 'A ligada segue desligável')
  await chave('Antiga').click()
  assert.deepEqual(await pagina.evaluate(() => window.chamadas), [['alternar', 'r2']])
  assert.equal(await chave('Antiga').isDisabled(), true, 'Desligada, com variável solta, não religa')
  // Editar: variável solta trava o Salvar; com a variável da API, salva.
  const limpa = linha('Limpa')
  assert.equal(await chave('Limpa').isDisabled(), false)
  await limpa.getByRole('button', { name: 'Editar texto', exact: true }).click()
  const texto = limpa.getByRole('textbox', { name: 'Texto de Limpa', exact: true })
  await texto.fill('Abrimos {abre}')
  const salvar = limpa.getByRole('button', { name: 'Salvar', exact: true })
  assert.equal(await salvar.isDisabled(), true, 'Variável solta não salva')
  assert.ok(await salvar.getAttribute('aria-describedby'), 'O Salvar travado aponta o motivo')
  await limpa.getByRole('alert').filter({ hasText: 'Troque ou apague {abre}' }).waitFor()
  await pagina.screenshot({ path: resolve(pasta, 'trava-1280.png'), fullPage: true })
  await texto.fill('Oi {nome}, pedido {pedido}')
  assert.equal(await salvar.isDisabled(), false)
  await salvar.click()
  await texto.waitFor({ state: 'detached' })
  assert.deepEqual((await pagina.evaluate(() => window.chamadas)).at(-1), ['editar', 'r3', 'Oi {nome}, pedido {pedido}'])

  // Operador: não edita, então lê o porquê, sem a ordem de trocar o texto.
  await pagina.goto(`${endereco}?operador`)
  await chave('Cardápio').waitFor()
  await pagina.getByText('Tem {linkCardapio}, que o EasyStok não preenche: assim ela não liga.').first().waitFor()
  assert.equal(await pagina.getByText(/^Troque ou apague/).count(), 0, 'Operador não recebe ordem de editar')

  // Cadastros › Modelos (comAutomaticas false): só as respostas prontas e o caminho para as automáticas.
  await pagina.goto(`${endereco}?modelos`)
  const caminho = pagina.getByRole('link', { name: 'Respostas automáticas', exact: true })
  await caminho.waitFor()
  assert.match(await caminho.getAttribute('href'), /atendimento/, 'Aponta para as automáticas do Atendimento')
  assert.equal(await pagina.getByRole('heading', { name: 'Respostas automáticas' }).count(), 0, 'Sem a seção de automáticas em Modelos')
  assert.equal(await chave('Cardápio').count(), 0, 'Nenhuma chave de automática em Modelos')
  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ naoLiga: true, desligaLigada: true, salvarTravado: true, motivoNaChave: true, operador: true, modelosSemAutomaticas: true }, null, 2))
  console.log('#2476 automáticas: variável solta não liga nem salva; a ligada desliga; motivo ligado à chave; Operador lê o porquê.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await contexto.close(); await browser.close() }
