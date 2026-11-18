// #2849: no Atendimento, o cliente já cadastrado abre a MESMA ficha de Cadastros e do Buscar na Casa.
// Chromium, HTTP controlado. O lead, e a leitura que falha, seguem com o cadastro rápido da conversa.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2849/ficha-atendimento')
mkdirSync(pasta, { recursive: true })
const ID = '5d1e7a90-3c2b-4f18-8e6a-2b9c0d1e2f3a'
writeFileSync(resolve(pasta, 'index.html'), '<!doctype html><html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React,{useState} from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoEstado,ContextoAcoes,ContextoCatalogo} from '../../../src/aplicacao/contextos';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {ContextoFichaUnicaDoCliente} from '../../../src/aplicacao/fichaUnicaDoCliente';
import {ContextoRascunhosDosAjustes,useGravacoesDosAjustes} from '../../../src/aplicacao/rascunhosDosAjustes';
import {FichaDoClienteNoAtendimento} from '../../../src/features/ficha-cliente/FichaDoClienteNoAtendimento';
import {EditorCadastro} from '../../../src/features/gestao/cadastros/EditorCadastro';
import {CANAIS} from '../../../src/infra/catalogo';import '../../../src/estilos/base.css';
const busca=new URLSearchParams(location.search);const cena=busca.get('cena');const lead=cena==='lead';
window.__fechou=0;
const conversa={id:'C1',clienteId:lead?null:'${ID}',cadastroId:lead?null:'${ID}',nome:'Marina Teste',canal:'WhatsApp',estado:'Aberto',conta:lead?'lead':'cliente',
cliente:{tags:[],notas:[],pedidos:7,pontos:0,telefone:'(11) 99999-8888',endereco:'Rua de teste, 100'},pedido:null,mensagens:[]};
const estado={fonteApi:true,conversas:[conversa],selecionada:conversa,historico:[],agora:Date.now(),automaticoPausado:{},fidelidade:{recompensas:[],sorteios:[],resgatesPorCadastro:{},config:{}}};
const acoes={salvarNota:async()=>false,editarDadoCliente:async()=>false,carregarClienteDaConversa:async()=>true,relerClienteDaConversa:async()=>true,selecionar:()=>{}};
const renderizar=props=><EditorCadastro {...props} restricoes={[]}/>;
function Cena(){const [aberto,mostrar]=useState(true);const [rascunhos,guardar]=useState({});const gravacoes=useGravacoesDosAjustes();
return <ContextoAcessoModulos.Provider value={{acoes:{},permite:()=>true}}><ContextoFichaUnicaDoCliente.Provider value={renderizar}>
<ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir:(c,v)=>guardar(e=>{const d={...e};if(v===null)delete d[c];else d[c]=v;return d}),...gravacoes}}>
<ContextoEstado.Provider value={estado}><ContextoAcoes.Provider value={acoes}><ContextoCatalogo.Provider value={{canais:CANAIS,motivosBloqueio:[],prefixosCepAtendidos:[],restricoes:[]}}>
{aberto&&<FichaDoClienteNoAtendimento conversa={conversa} editarAoAbrir={cena==='editar'} aoFechar={()=>{window.__fechou+=1;mostrar(false)}}/>}
</ContextoCatalogo.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoRascunhosDosAjustes.Provider></ContextoFichaUnicaDoCliente.Provider></ContextoAcessoModulos.Provider>}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 1100 }, locale: 'pt-BR', reducedMotion: 'reduce' })
await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', empresa: { id: 'empresa' }, usuario: { id: 'operador' }, expiraEm: Date.now() + 600000 })))
const cliente = { id: ID, revisao: 1, nome: 'Marina Teste', telefone: '+5511999998888', email: '', documento: '', tipoPessoa: 'fisica', ativo: true, observacoes: 'Portão azul.', orderCount: 7, lastOrderAt: '2026-11-10T15:30:00Z' }
const dossie = {
  cliente: { id: ID, nome: cliente.nome, telefone: cliente.telefone, email: null, observacoes: cliente.observacoes, motivoBloqueio: null, fotoUrl: null },
  enderecos: [], tags: [], notas: [], ultimosPedidos: [], itemFavorito: null, ultimaCompraEm: '2026-11-10T15:30:00', totalPedidos: 7, bloqueado: false,
  conversasRecentes: [], pedidoEmAndamento: null, contatoInformado: null, totalGasto: 412.5,
}
let falharCadastro = false
const lidas = [], escritas = []
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==', 'base64')
await contexto.route(/^https?:\/\/[^/]+\/api\//, route => {
  const req = route.request(), [, , recurso, id, sub] = new URL(req.url()).pathname.split('/')
  if (req.method() === 'GET' && recurso === 'clientes' && id === ID) {
    lidas.push(sub ?? 'cadastro')
    if (sub === 'foto') return route.fulfill({ contentType: 'image/png', body: PNG })
    if (sub === 'dossie') return route.fulfill({ json: { data: dossie } })
    if (!sub) return falharCadastro ? route.fulfill({ status: 503, json: { error: { message: 'Fora do ar.' } } }) : route.fulfill({ json: { data: { cliente, enderecos: [], telefones: [], documentos: [], alteracoes: [] } } })
  }
  if (req.method() === 'POST' && recurso === 'clientes' && id === ID && sub === 'foto') {
    escritas.push(`POST ${new URL(req.url()).pathname}`); dossie.cliente.fotoUrl = `/api/clientes/${ID}/foto?v=${'a'.repeat(32)}`
    return route.fulfill({ json: { data: { fotoUrl: dossie.cliente.fotoUrl } } })
  }
  return route.fulfill({ status: 404, json: { error: { message: 'Fora da prova.' } } })
})
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://127.0.0.1:5209'}/.build/2849/ficha-atendimento/index.html`
const erros = []
async function abrir(consulta = '') {
  const pagina = await contexto.newPage(); pagina.setDefaultTimeout(8000)
  pagina.on('pageerror', e => erros.push(e.message))
  await pagina.goto(`${base}?${consulta}`)
  return pagina
}
let pagina
try {
  // 1. Cliente cadastrado: a ficha única, em leitura, com as mesmas seções de Cadastros.
  pagina = await abrir()
  const ficha = pagina.getByRole('dialog', { name: 'Ficha do cliente', exact: true })
  const resumo = ficha.getByRole('region', { name: 'Resumo de Marina Teste', exact: true })
  await resumo.waitFor()
  await ficha.getByLabel('Números do cliente').getByText('compras válidas').waitFor()
  assert.equal(await ficha.locator('[data-resumo-do-cliente]').count(), 1, 'a ficha do Atendimento é a mesma de Cadastros')
  assert.equal(await ficha.getByLabel('Dados de contato').count(), 1, 'o contato vem em leitura')
  assert.equal(await ficha.getByRole('button', { name: 'Editar dados', exact: true }).count(), 1)
  assert.equal(await ficha.getByRole('textbox', { name: 'Nome', exact: true }).isVisible(), false, 'abre em leitura, sem formulário')
  assert.equal(await pagina.getByRole('article', { name: /^Ficha de / }).count(), 0, 'o modal antigo não abre para quem já tem cadastro')
  assert.equal(await ficha.getByRole('button', { name: 'Abrir ficha completa' }).count(), 0, 'não há segunda ficha para abrir')
  assert.equal(await ficha.locator('[data-complemento-do-atendimento]').count(), 1, 'o que é da conversa segue na ficha')
  assert.ok(lidas.includes('cadastro') && lidas.includes('dossie'), 'lê o cadastro e o relacionamento pelo cliente')
  // Foto e PDF, que só existiam no modal do Atendimento, agora são da ficha única.
  assert.equal(await resumo.getByRole('button', { name: 'Baixar PDF', exact: true }).count(), 1)
  assert.equal(await resumo.getByRole('button', { name: 'Adicionar foto', exact: true }).count(), 1)
  const dossiesAntes = lidas.filter(l => l === 'dossie').length
  await resumo.getByLabel('Foto do cliente').setInputFiles({ name: 'foto.png', mimeType: 'image/png', buffer: PNG })
  await ficha.getByRole('button', { name: 'Trocar foto', exact: true }).waitFor()
  assert.deepEqual(escritas, [`POST /api/clientes/${ID}/foto`], 'a foto grava pelo cliente')
  assert.equal(await resumo.getByRole('button', { name: 'Remover foto', exact: true }).count(), 1)
  assert.ok(lidas.filter(l => l === 'dossie').length > dossiesAntes, 'a ficha relê o cliente depois da foto')
  await pagina.screenshot({ path: resolve(pasta, 'ficha-atendimento-1280.png') })
  await ficha.getByRole('button', { name: 'Fechar', exact: true }).last().click()
  await pagina.waitForFunction(() => window.__fechou === 1)
  await pagina.close()

  // 2. Quem pediu para editar cai com o formulário aberto.
  pagina = await abrir('cena=editar')
  await pagina.getByRole('dialog', { name: 'Ficha do cliente', exact: true }).getByRole('textbox', { name: 'Nome', exact: true }).waitFor({ state: 'visible' })
  await pagina.close()

  // 3. Lead ainda sem cadastro: segue o cadastro rápido da conversa.
  pagina = await abrir('cena=lead')
  await pagina.getByRole('article', { name: /^Ficha de / }).waitFor()
  assert.equal(await pagina.locator('[data-resumo-do-cliente]').count(), 0)
  await pagina.close()

  // 4. Leitura do cadastro falhou: o clique não morre, abre a ficha da conversa.
  falharCadastro = true
  pagina = await abrir()
  await pagina.getByRole('article', { name: /^Ficha de / }).waitFor()
  assert.equal(await pagina.locator('[data-resumo-do-cliente]').count(), 0)
  assert.deepEqual(erros, [])
  console.log('#2849 Atendimento: cliente cadastrado abre a ficha única; lead e falha de leitura seguem na ficha da conversa.')
} catch (erro) {
  await pagina?.screenshot({ path: resolve(pasta, 'falha.png') }).catch(() => {})
  console.error(erros.join('\n')); throw erro
} finally { await browser.close() }
