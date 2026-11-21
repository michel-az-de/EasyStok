/* eslint-disable no-console */
// #2734 (D2): Combobox não abre no foco, avisos com role=status, CampoMascarado obrigatório, CampoSelecao com erro,
// Formas de pagamento vazias e concordância, Categorias com descarte e erro no campo. Chromium e HTTP controlado.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2734/d2')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React,{useState} from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoEstado,ContextoAcoes} from '../../../src/aplicacao/contextos';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {ContextoRascunhosDosAjustes,useGravacoesDosAjustes} from '../../../src/aplicacao/rascunhosDosAjustes';
import {PainelCadastros} from '../../../src/features/gestao/cadastros/PainelCadastros';
import {Combobox} from '../../../src/componentes/Combobox';
import {CampoMascarado} from '../../../src/componentes/CampoMascarado';
import {CampoTexto,CampoSelecao} from '../../../src/componentes/Campo';
import '../../../src/estilos/base.css';
document.body.style.cssText='margin:0;padding:16px;box-sizing:border-box;background:var(--fundo);font-family:var(--f-corpo);color:var(--tinta)';
const tela=location.hash.slice(1);
function Componentes(){const [v,d]=useState('b');return <div>
<Combobox rotulo="Sabor" valor={v} aoMudar={d} opcoes={[{valor:'a',rotulo:'Abacaxi'},{valor:'b',rotulo:'Brigadeiro'},{valor:'c',rotulo:'Coco'}]}/>
<CampoMascarado tipo="telefone" rotulo="Telefone" obrigatorio valor="" aoMudarDigitos={()=>{}}/>
<CampoSelecao rotulo="Tamanho" erro="Escolha o tamanho." opcoes={[{valor:'p',rotulo:'Pequeno'}]}/>
<CampoTexto rotulo="Apelido" aviso="Use até 5 caracteres." value="abcdef" onChange={()=>{}}/>
<CampoTexto rotulo="Nome" erro="Informe o nome." value="" onChange={()=>{}}/>
<ComLimite/>
</div>}
function ComLimite(){const [s,d]=useState('abcd');return <CampoTexto rotulo="Sigla" limite={3} value={s} onChange={e=>d(e.target.value)}/>}
function Cena(){const [rascunhos,definir]=useState({});const gravacoes=useGravacoesDosAjustes();
if(tela==='componentes')return <Componentes/>
return <ContextoAcessoModulos.Provider value={{acoes:{verCadastrosProdutos:true,editarCadastrosProdutos:true,verCadastrosFinanceiros:true,gerenciarCadastrosFinanceiros:true},permite:()=>true}}><ContextoEstado.Provider value={{fonteApi:true,conversas:[],selecionada:null}}><ContextoAcoes.Provider value={{}}><ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir:(chave,valor)=>definir(atual=>({...atual,[chave]:valor})),...gravacoes}}><PainelCadastros tela={tela}/></ContextoRascunhosDosAjustes.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoAcessoModulos.Provider>}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 1000 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'operador' }, expiraEm: Date.now() + 600000 })))
let metodos = []
const escritas = [], erros = []
await contexto.route('**/api/**', async route => {
  const req = route.request(), path = new URL(req.url()).pathname
  if (!path.startsWith('/api/')) return route.continue()
  if (path === '/api/cadastros/metodos-pagamento') return route.fulfill({ json: { data: metodos } })
  if (path.startsWith('/api/cadastros/metodos-pagamento/')) { escritas.push(path); return route.fulfill({ json: { data: { ...metodos[0], ativo: true } } }) }
  if (path === '/api/categorias') return route.fulfill({ json: { data: [
    { id: 'R1', nome: 'Bolos', categoriaPaiId: null, empresaId: 'empresa', descricao: null, subCategorias: [] },
    { id: 'R2', nome: 'Doces', categoriaPaiId: null, empresaId: 'empresa', descricao: null, subCategorias: [] },
  ] } })
  erros.push('404 ' + path); return route.fulfill({ status: 404, json: { error: { code: 'NOT_FOUND', message: path } } })
})
const pagina = await contexto.newPage()
pagina.setDefaultTimeout(10000)
pagina.on('pageerror', e => erros.push(e.message))
pagina.on('console', m => { if (m.type() === 'error') erros.push(m.text()) })
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/2734/d2/index.html`
const ir = async (tela) => { await pagina.goto(`${base}#${tela}`); await pagina.reload() }
try {
  // Componentes compartilhados.
  await ir('componentes')
  const combo = pagina.getByRole('combobox', { name: 'Sabor' })
  await combo.waitFor()
  await pagina.keyboard.press('Tab')
  assert.equal(await combo.evaluate(e => document.activeElement === e), true, 'o Tab chega ao combobox')
  assert.equal(await combo.getAttribute('aria-expanded'), 'false', 'foco sozinho não abre a lista')
  assert.equal(await combo.inputValue(), 'Brigadeiro', 'o valor escolhido continua legível no foco')
  await pagina.keyboard.press('ArrowDown')
  assert.equal(await combo.getAttribute('aria-expanded'), 'true', 'seta para baixo abre')
  await pagina.keyboard.press('Escape')
  assert.equal(await combo.getAttribute('aria-expanded'), 'false')
  await combo.click()
  assert.equal(await combo.getAttribute('aria-expanded'), 'true', 'clique abre')
  await pagina.keyboard.press('Escape')
  await pagina.keyboard.type('co')
  assert.equal(await combo.getAttribute('aria-expanded'), 'true', 'digitar abre')
  assert.deepEqual(await pagina.getByRole('listbox', { name: 'Sabor' }).getByRole('option').allTextContents(), ['Coco'])
  await pagina.keyboard.press('Escape')

  const tel = pagina.getByRole('textbox', { name: 'Telefone' })
  assert.equal(await tel.getAttribute('aria-required'), 'true')
  assert.equal(await pagina.locator('label', { hasText: 'Telefone' }).locator('span[aria-hidden="true"]').textContent(), ' *')
  const tam = pagina.getByRole('combobox', { name: 'Tamanho' })
  assert.equal(await tam.getAttribute('aria-invalid'), 'true')
  assert.equal(await tam.getAttribute('aria-describedby'), await pagina.getByText('Escolha o tamanho.').getAttribute('id'))
  assert.equal(await pagina.getByText('Escolha o tamanho.').getAttribute('role'), 'alert')
  assert.equal(await pagina.getByText('Use até 5 caracteres.').getAttribute('role'), 'status', 'aviso de limite não reanuncia como alert')
  assert.equal(await pagina.getByText('Informe o nome.').getAttribute('role'), 'alert', 'erro de validação continua alert')
  assert.equal(await pagina.getByRole('textbox', { name: 'Apelido' }).getAttribute('aria-invalid'), null, 'aviso não marca o campo como inválido')
  // #2792 (revisão): a contagem muda a cada tecla, então a frase visível não é região viva; o anúncio estável
  // "Passou do limite" fica numa região própria e só muda ao cruzar o limite.
  const sigla = pagina.getByRole('textbox', { name: 'Sigla' })
  const contagem = pagina.getByText('Use até 3 caracteres. Agora tem 4.', { exact: true })
  assert.equal(await contagem.getAttribute('role'), null, 'a contagem visível não reanuncia a cada tecla')
  assert.equal(await sigla.getAttribute('aria-invalid'), 'true')
  assert.equal(await sigla.getAttribute('aria-describedby'), await contagem.getAttribute('id'))
  const anuncio = pagina.getByRole('status').filter({ hasText: 'Passou do limite de 3 caracteres.' })
  assert.equal(await anuncio.count(), 1, 'um anúncio estável ao passar do limite')
  await anuncio.evaluate(e => { e.dataset.prova = 'anuncio-sigla' })
  await sigla.press('End'); await sigla.press('e')
  await pagina.getByText('Use até 3 caracteres. Agora tem 5.', { exact: true }).waitFor()
  assert.equal(await anuncio.count(), 1, 'o anúncio não muda com a contagem')
  await sigla.fill('abc')
  assert.equal(await pagina.getByText(/Use até 3 caracteres/).count(), 0)
  assert.equal(await anuncio.count(), 0, 'dentro do limite, nada a anunciar')
  // A região viva continua no DOM, vazia: só assim o próximo estouro é anunciado.
  const regiao = pagina.locator('[data-prova="anuncio-sigla"]')
  assert.deepEqual(await regiao.evaluate(e => [e.getAttribute('role'), e.textContent]), ['status', ''], 'a mesma região fica vazia dentro do limite')
  await sigla.fill('abcd')
  assert.equal(await regiao.textContent(), 'Passou do limite de 3 caracteres.', 'o mesmo nó anuncia de novo')

  // Formas de pagamento: vazio e concordância.
  metodos = []
  await ir('pagamentos')
  await pagina.getByText('Nenhuma forma de pagamento cadastrada').waitFor()
  assert.equal(await pagina.locator('ul li').count(), 0, 'sem lista vazia')
  metodos = [{ codigo: 'pix', nome: 'Pix', ativo: false }]
  await pagina.getByRole('button', { name: 'Atualizar', exact: true }).click()
  await pagina.getByRole('button', { name: 'Ativar Pix', exact: true }).click()
  await pagina.getByRole('dialog').getByRole('button', { name: 'Ativar Pix', exact: true }).click()
  await pagina.getByRole('status').filter({ hasText: 'Pix: ativo.' }).waitFor()
  assert.equal(escritas.length, 1)

  // Categorias: descarte e erro no campo.
  await ir('categorias')
  await pagina.getByRole('button', { name: 'Nova categoria', exact: true }).first().click()
  let d = pagina.getByRole('dialog', { name: 'Nova categoria', exact: true })
  await d.getByRole('textbox', { name: 'Nome' }).fill('Tortas')
  await pagina.keyboard.press('Escape')
  await pagina.getByText('Descartar o que foi digitado?').waitFor()
  await pagina.getByRole('button', { name: 'Continuar editando', exact: true }).click()
  assert.equal(await d.getByRole('textbox', { name: 'Nome' }).inputValue(), 'Tortas', 'o digitado sobrevive ao Esc')
  await d.getByRole('button', { name: 'Cancelar', exact: true }).click()
  await pagina.getByText('Descartar o que foi digitado?').waitFor()
  await pagina.getByRole('button', { name: 'Descartar', exact: true }).click()
  await d.waitFor({ state: 'detached' })
  await pagina.getByRole('button', { name: 'Nova categoria', exact: true }).first().click()
  await pagina.keyboard.press('Escape')
  await d.waitFor({ state: 'detached' })

  await pagina.getByRole('button', { name: 'Mais opções de Bolos', exact: true }).click()
  await pagina.getByRole('menuitem', { name: 'Mover para outra categoria', exact: true }).click()
  d = pagina.getByRole('dialog', { name: 'Mover Bolos', exact: true })
  await d.getByRole('button', { name: 'Mover', exact: true }).click()
  const campo = d.getByRole('combobox', { name: 'Mover para', exact: true })
  assert.equal(await campo.getAttribute('aria-invalid'), 'true', 'o erro marca o próprio campo')
  const alerta = d.getByRole('alert').filter({ hasText: 'Escolha para onde mover.' })
  assert.equal(await alerta.count(), 1)
  assert.equal(await campo.getAttribute('aria-describedby'), await alerta.getAttribute('id'))
  assert.deepEqual(erros, [])
  console.log('#2734 D2: combobox, avisos, campos e telas de Cadastros conferidos no navegador.')
} catch (erro) { await pagina.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); console.error(erros.join('\n')); throw erro }
finally { await browser.close() }
