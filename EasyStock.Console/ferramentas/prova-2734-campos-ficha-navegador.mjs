// #2734 (D1, A01 A02 A06 A09 A11 A16 A29 A31): campos da Ficha do Atendimento em Chromium, componentes reais e
// ações falsas. Telefone colado com DDI, foco no blur com erro, limite do nome sem maxLength, nome acessível do
// botão de edição, Cancelar edição, tag duplicada e Esc nas tags.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2734/campos-ficha')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width, initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React,{useState} from 'react';import {createRoot} from 'react-dom/client';
import {ContextoEstado,ContextoCatalogo,ContextoAcoes} from '../../../src/aplicacao/contextos';
import {ContextoRascunhosDosAjustes,useGravacoesDosAjustes} from '../../../src/aplicacao/rascunhosDosAjustes';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {BlocoCliente} from '../../../src/features/ficha-cliente/BlocoCliente';
import {CartaoDoCliente} from '../../../src/features/ficha-cliente/CartaoDoCliente';
import {CartaoCliente} from '../../../src/features/assistente/Cartoes';
import {CANAIS} from '../../../src/infra/catalogo';import '../../../src/estilos/base.css';
const cena = new URLSearchParams(location.search).get('cena'); const lead = cena === 'lead'; const gravadas = cena === 'gravadas';
const conversa={id:'conversa',clienteId:lead?null:'a',vinculoRevisao:1,nome:'Pessoa A',canal:'WhatsApp',estado:'Aberto',conta:lead?'lead':'cliente',cliente:{tags:gravadas?['cliente_antiga','sem_gluten']:['Lactose','Sem glúten'],notas:[],pedidos:0,pontos:0,telefone:lead?null:'(11) 99999-9999',endereco:lead?null:'Rua A, 10, Centro, 01001-000'},pedido:null,mensagens:[]};
window.__chamadas=[];
const registra=(nome,n)=>(...a)=>{window.__chamadas.push([nome,...a.slice(0,n)])};
const acoes={selecionar:()=>{},editarDadoCliente:registra('editarDadoCliente',3),adicionarTag:registra('adicionarTag',2),removerTag:registra('removerTag',2),editarTag:registra('editarTag',3),salvarCadastroRapido:registra('salvarCadastroRapido',2)};
function Cena(){const [rascunhos,guardar]=useState({});const gravacoes=useGravacoesDosAjustes();
const estado={fonteApi:false,selecionada:conversa,conversas:[conversa],historico:[],agora:Date.now(),automaticoPausado:{},fidelidade:{recompensas:[],sorteios:[],resgatesPorCadastro:{},config:{}}};
return <ContextoAcessoModulos.Provider value={{acoes:{},permite:()=>true}}><ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir:(c,v)=>guardar(e=>({...e,[c]:v})),...gravacoes}}><ContextoEstado.Provider value={estado}><ContextoAcoes.Provider value={acoes}><ContextoCatalogo.Provider value={{canais:CANAIS,motivosBloqueio:[],prefixosCepAtendidos:[],restricoes:gravadas?['Lactose','Glúten']:[]}}>
{gravadas&&<><CartaoDoCliente conversa={conversa} aoAbrir={()=>{}} mostrarEntrega={false}/><CartaoCliente dado={{tags:conversa.cliente.tags,pedidos:0,total:0,ticketMedio:0,chegou:null}} aoDispensar={()=>{}}/></>}
<BlocoCliente conversa={conversa} integrado/>
</ContextoCatalogo.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoRascunhosDosAjustes.Provider></ContextoAcessoModulos.Provider>}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 390, height: 1200 }, locale: 'pt-BR', reducedMotion: 'reduce' })
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://127.0.0.1:5209'}/.build/2734/campos-ficha/index.html`
const erros = []
async function abrir(cena) {
  const pagina = await contexto.newPage(); pagina.setDefaultTimeout(8000)
  pagina.on('pageerror', e => erros.push(e.message))
  await pagina.goto(`${base}?cena=${cena}`)
  return pagina
}
const chamadas = (pagina) => pagina.evaluate(() => window.__chamadas)
try {
  // A16: o nome acessível do botão contém o texto visível (WCAG 2.5.3).
  let p = await abrir('ficha')
  await p.getByRole('button', { name: '(11) 99999-9999, editar Celular (com DDD)', exact: true }).waitFor()
  await p.getByRole('button', { name: 'Pessoa A, editar Nome do cliente', exact: true }).waitFor()

  // A01: colar com DDI grava o número certo.
  await p.getByRole('button', { name: '(11) 99999-9999, editar Celular (com DDD)', exact: true }).click()
  const tel = p.getByRole('textbox', { name: 'Celular (com DDD)', exact: true })
  await tel.fill('+55 11 98225-4398')
  assert.equal(await tel.inputValue(), '(11) 98225-4398')
  // A31: o X da edição se chama Cancelar edição.
  await p.getByRole('button', { name: 'Cancelar edição', exact: true }).waitFor()
  await p.getByRole('button', { name: 'Salvar', exact: true }).click()
  assert.deepEqual((await chamadas(p)).filter(c => c[0] === 'editarDadoCliente'), [['editarDadoCliente', 'conversa', 'telefone', '(11) 98225-4398']])

  // Número de outro país: a conversa grava pelo E.164 do Brasil e o servidor devolve 400 (visto na tela real).
  // A tela não corta o número nem o troca por um do Brasil: mostra como foi digitado, recusa e diz onde ele entra.
  await p.getByRole('button', { name: '(11) 99999-9999, editar Celular (com DDD)', exact: true }).click()
  await tel.fill('+44 7911 123456')
  assert.equal(await tel.inputValue(), '+44 7911 123456')
  await tel.press('Enter')
  await p.getByRole('alert').filter({ hasText: 'Aqui só entra telefone do Brasil, com DDD. O de outro país entra em Cadastros.' }).waitFor()
  assert.equal((await chamadas(p)).filter(c => c[0] === 'editarDadoCliente').length, 1, 'número de outro país não vai ao servidor')

  // A01: mais de 11 dígitos é recusado com mensagem que diz a saída; não grava.
  await tel.fill('119822543981')
  await tel.press('Enter')
  await p.getByRole('alert').filter({ hasText: 'Telefone com mais de 11 dígitos. Use só o DDD e o número.' }).waitFor()
  assert.equal((await chamadas(p)).filter(c => c[0] === 'editarDadoCliente').length, 1, 'recusado não grava')

  // A06: blur com erro mostra a mensagem e NÃO devolve o foco ao campo.
  await p.getByRole('button', { name: 'Cancelar edição', exact: true }).click()
  await p.getByRole('button', { name: 'Pessoa A, editar Nome do cliente', exact: true }).click()
  const nome = p.getByRole('textbox', { name: 'Nome do cliente', exact: true })
  await nome.fill('')
  await nome.evaluate(el => el.blur())
  await p.getByRole('alert').filter({ hasText: 'Escreva o nome' }).waitFor()
  assert.equal(await nome.evaluate(el => el === document.activeElement), false, 'o blur com erro não prende o foco')
  // Enter com erro continua devolvendo o foco ao campo.
  await nome.focus(); await nome.press('Enter')
  assert.equal(await nome.evaluate(el => el === document.activeElement), true, 'o Enter com erro mantém o foco')

  // A09: sem maxLength; o limite avisa e trava o Salvar. A11: o limite é o do servidor (150).
  assert.equal(await nome.getAttribute('maxlength'), null)
  await nome.fill('A'.repeat(151))
  assert.equal((await nome.inputValue()).length, 151, 'o texto não é cortado')
  await p.getByRole('alert').filter({ hasText: 'Use até 150 caracteres' }).waitFor()
  await p.getByRole('button', { name: 'Salvar', exact: true }).click()
  assert.deepEqual((await chamadas(p)).filter(c => c[2] === 'nome'), [], 'acima do limite não grava')
  await nome.fill('B'.repeat(100))
  await p.getByRole('button', { name: 'Salvar', exact: true }).click()
  assert.deepEqual((await chamadas(p)).filter(c => c[2] === 'nome'), [['editarDadoCliente', 'conversa', 'nome', 'B'.repeat(100)]], '100 caracteres cabem (servidor aceita 150)')

  // A29: Esc cancela de verdade; tag duplicada é recusada com mensagem.
  const tags = () => p.getByRole('list', { name: 'Tags do cliente', exact: true })
  await tags().getByRole('button', { name: 'Sem glúten', exact: true }).click()
  const edicaoTag = tags().getByRole('textbox')
  await edicaoTag.fill('Vegana')
  await edicaoTag.press('Escape')
  await p.waitForTimeout(300)
  assert.deepEqual((await chamadas(p)).filter(c => c[0] === 'editarTag'), [], 'Esc não salva a tag')
  await tags().getByRole('button', { name: 'Sem glúten', exact: true }).click()
  await tags().getByRole('textbox').fill('lactose')
  await tags().getByRole('textbox').press('Enter')
  await p.getByRole('alert').filter({ hasText: 'Esta tag já existe.' }).waitFor()
  assert.deepEqual((await chamadas(p)).filter(c => c[0] === 'editarTag'), [], 'tag duplicada não grava')
  // O campo da tag tem nome e o motivo da recusa fica ligado a ele (leitor de tela lê o erro no próprio campo).
  const motivoNoCampo = async (campo) => { const alvo = await campo.getAttribute('aria-describedby'); return alvo ? p.locator(`[id="${alvo}"]`).textContent() : null }
  const editarTag = tags().getByRole('textbox', { name: 'Editar tag Sem glúten', exact: true })
  assert.equal(await editarTag.getAttribute('aria-invalid'), 'true')
  assert.equal(await motivoNoCampo(editarTag), 'Esta tag já existe.')
  await tags().getByRole('textbox').press('Escape')
  await tags().getByRole('button', { name: 'Tag', exact: true }).click()
  await tags().getByRole('textbox').fill('LACTOSE')
  await tags().getByRole('textbox').press('Enter')
  await p.getByRole('alert').filter({ hasText: 'Esta tag já existe.' }).waitFor()
  assert.deepEqual((await chamadas(p)).filter(c => c[0] === 'adicionarTag'), [], 'tag repetida não entra')
  const novaTag = tags().getByRole('textbox', { name: 'Nova tag', exact: true })
  assert.equal(await novaTag.getAttribute('aria-invalid'), 'true')
  assert.equal(await motivoNoCampo(novaTag), 'Esta tag já existe.')
  await tags().getByRole('textbox').fill('Vegana')
  assert.equal(await novaTag.getAttribute('aria-invalid'), null, 'ao corrigir, o campo deixa de estar recusado')
  await tags().getByRole('textbox').press('Enter')
  assert.deepEqual((await chamadas(p)).filter(c => c[0] === 'adicionarTag'), [['adicionarTag', 'conversa', 'Vegana']])
  await p.close()

  // Tag gravada pelo EasyStok ("sem_gluten", "cliente_antiga"): rótulo com espaço em todo lugar, e a restrição
  // segue restrição (alerta e primeiro lugar) no cartão do Resumo, na Ficha e no cartão do Assistente.
  p = await abrir('gravadas')
  // O cartão é o resumo e a Ficha é a lista que se edita: cada uma tem o seu nome, e o alerta tem nome nas duas.
  const noCartao = p.locator('section[aria-label^="Pedido de "] ul[aria-label="Resumo das tags do cliente"] li')
  await noCartao.first().waitFor()
  assert.deepEqual(await noCartao.allInnerTexts(), ['sem gluten', 'cliente antiga'])
  assert.deepEqual(await noCartao.evaluateAll(lis => lis.map(li => li.querySelectorAll('svg').length)), [1, 0], 'no cartão, só a restrição leva o alerta')
  assert.equal(await p.locator('ul[aria-label="Resumo das tags do cliente"]').getByRole('img', { name: 'Restrição alimentar', exact: true }).count(), 1, 'no cartão, o alerta tem nome')
  assert.equal(await p.locator('[data-tags-cliente] ul[aria-label="Tags do cliente"]').getByRole('img', { name: 'Restrição alimentar', exact: true }).count(), 1, 'na Ficha, o alerta tem nome')
  const naFicha = p.locator('[data-tags-cliente] ul[aria-label="Tags do cliente"] li')
  assert.deepEqual(await naFicha.evaluateAll(lis => lis.slice(0, 2).map(li => ({ texto: li.querySelector('button')?.textContent.trim(), alerta: li.querySelectorAll(':scope > span > svg').length }))),
    [{ texto: 'sem gluten', alerta: 1 }, { texto: 'cliente antiga', alerta: 0 }])
  const perfil = p.locator('article').filter({ has: p.getByRole('heading', { name: 'Perfil do cliente' }) })
  assert.match(await perfil.innerText(), /cliente antiga/)
  assert.doesNotMatch(await p.locator('body').innerText(), /cliente_antiga|sem_gluten/, 'a chave crua não aparece em lugar nenhum')
  await p.close()

  // A02: no cadastro rápido, o telefone colado com DDI vira o número certo; mais de 11 dígitos é recusado.
  p = await abrir('lead')
  const rapido = p.getByRole('textbox', { name: 'Celular (com DDD)', exact: true })
  await rapido.fill('+55 11 98225-4398')
  assert.equal(await rapido.inputValue(), '(11) 98225-4398')
  // DDD 55: um dígito a mais não vira outro número. O campo mostra o que foi digitado e o salvar recusa.
  await rapido.fill('(55) 99999-8888')
  assert.equal(await rapido.inputValue(), '(55) 99999-8888')
  await rapido.press('End'); await rapido.pressSequentially('7')
  assert.equal(await rapido.inputValue(), '(55) 99999-88887', 'o dígito a mais não troca o DDD')
  await rapido.fill('119822543981')
  await p.getByRole('button', { name: 'Salvar cadastro', exact: true }).click()
  await p.getByRole('alert').filter({ hasText: 'Telefone com mais de 11 dígitos. Use só o DDD e o número.' }).waitFor()
  assert.deepEqual((await chamadas(p)).filter(c => c[0] === 'salvarCadastroRapido'), [], 'recusado não grava')
  // Número de outro país: o cadastro rápido também grava pela conversa (só Brasil); recusa e diz onde ele entra.
  await rapido.fill('+351 912 345 678')
  assert.equal(await rapido.inputValue(), '+351 912 345 678')
  await p.getByRole('button', { name: 'Salvar cadastro', exact: true }).click()
  await p.getByRole('alert').filter({ hasText: 'Aqui só entra telefone do Brasil, com DDD. O de outro país entra em Cadastros.' }).waitFor()
  assert.deepEqual((await chamadas(p)).filter(c => c[0] === 'salvarCadastroRapido'), [], 'número de outro país não vai ao servidor')
  // O do Brasil, colado com o DDI, grava.
  await rapido.fill('+55 11 98225-4398')
  await p.getByRole('button', { name: 'Salvar cadastro', exact: true }).click()
  await p.waitForFunction(() => window.__chamadas.some(c => c[0] === 'salvarCadastroRapido'))
  assert.deepEqual((await chamadas(p)).filter(c => c[0] === 'salvarCadastroRapido').map(c => String(c[2].telefone).replace(/\D/g, '')), ['11982254398'])
  await p.close()
  assert.deepEqual(erros, [])
  console.log('ok #2734 D1: telefone com DDI, foco no blur, limite 150 sem maxLength, nome acessível, Cancelar edição e tags')
} finally { await browser.close() }
