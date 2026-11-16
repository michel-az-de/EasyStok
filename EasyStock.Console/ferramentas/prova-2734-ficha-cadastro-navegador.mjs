// #2734 (C-03, C-07, C-11): a nota do cliente aparece na Ficha do Atendimento sem clique, a observação do cadastro
// fica à vista junto das notas e a Ficha leva ao cadastro completo em Cadastros. Chromium, componentes reais.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2734/ficha-cadastro')
mkdirSync(pasta, { recursive: true })
const ID = '3f2a8c1e-7b4d-4e9a-9c61-0d5f2b7a8e14'
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width, initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React,{useState} from 'react';import {createRoot} from 'react-dom/client';
import {ContextoEstado,ContextoCatalogo,ContextoAcoes} from '../../../src/aplicacao/contextos';
import {ContextoRascunhosDosAjustes,useGravacoesDosAjustes} from '../../../src/aplicacao/rascunhosDosAjustes';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {NotasDoCliente} from '../../../src/features/ficha-cliente/NotasDoCliente';
import {ModalCliente} from '../../../src/features/ficha-cliente/ModalCliente';
import {CANAIS} from '../../../src/infra/catalogo';import '../../../src/estilos/base.css';
const busca=new URLSearchParams(location.search);const cena=busca.get('cena');
const NOTAS=[{id:'n1',texto:'Prefere entrega à tarde',autor:'Ana',em:'01/11, 07:00',criadoEm:'2026-11-01T10:00:00Z'},
{id:'n2',texto:'Alérgica a amendoim',autor:'Ana',em:'05/11, 07:00',criadoEm:'2026-11-05T10:00:00Z'},
{id:'n3',texto:'Portão azul, tocar duas vezes',autor:'Bia',em:'09/11, 07:00',criadoEm:'2026-11-09T10:00:00Z'}];
const LONGA='Cliente desde 2019, não ligar antes das 10h. '.repeat(8).trim();
function Notas(){const vazia=cena==='vazia';const estreita=cena==='longa'||cena==='curta';
const observacao=vazia?null:cena==='longa'?LONGA:cena==='curta'?'Portão azul.':'Cliente desde 2019. Não ligar antes das 10h.';
return <div style={estreita?{maxWidth:340}:undefined}><NotasDoCliente conversaId="conversa" clienteId="${ID}" clienteNome="Marina" notas={vazia?[]:estreita?NOTAS.slice(0,1):NOTAS} observacao={observacao}/></div>}
window.__fechou=0;
function Cadastro(){const [aberto,mostrar]=useState(true);const [rascunhos,guardar]=useState({});const gravacoes=useGravacoesDosAjustes();
const lead=cena==='lead';
const conversa={id:'conversa',clienteId:lead?null:'${ID}',cadastroId:lead?null:'${ID}',nome:'Marina',canal:'WhatsApp',estado:'Aberto',conta:lead?'lead':'cliente',
cliente:{tags:[],notas:[],pedidos:0,pontos:0,telefone:'(11) 90000-0000',endereco:'Rua de teste, 100'},pedido:null,mensagens:[]};
const estado={fonteApi:true,conversas:[conversa],historico:[],agora:Date.now(),automaticoPausado:{},fidelidade:{recompensas:[],sorteios:[],resgatesPorCadastro:{},config:{}}};
const acoes={salvarNota:async()=>false,editarDadoCliente:async()=>false,carregarClienteDaConversa:async()=>true};
return <ContextoAcessoModulos.Provider value={{acoes:{},permite:(modulo)=>busca.get('acesso')!=='nao'||modulo!=='cadastros'}}>
<ContextoRascunhosDosAjustes.Provider value={{rascunhos,definir:(c,v)=>guardar(e=>{const d={...e};if(v===null)delete d[c];else d[c]=v;return d}),...gravacoes}}>
<ContextoEstado.Provider value={estado}><ContextoAcoes.Provider value={acoes}><ContextoCatalogo.Provider value={{canais:CANAIS,motivosBloqueio:[],prefixosCepAtendidos:[],restricoes:[]}}>
{aberto&&<ModalCliente conversa={conversa} aoFechar={()=>{window.__fechou+=1;mostrar(false)}}/>}
</ContextoCatalogo.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoRascunhosDosAjustes.Provider></ContextoAcessoModulos.Provider>}
createRoot(document.getElementById('root')).render(['notas','vazia','longa','curta'].includes(cena)?<Notas/>:<Cadastro/>);
`)
const browser = await chromium.launch({ headless: true })
const contexto = await browser.newContext({ viewport: { width: 1280, height: 720 }, locale: 'pt-BR', reducedMotion: 'reduce' })
// A cena não tem servidor: toda leitura da API responde "não encontrado" e a tela segue com o que recebeu por prop.
// Só o caminho /api/ da raiz: os módulos de src/infra/api/ também têm "api" no endereço e precisam chegar.
await contexto.route(/^https?:\/\/[^/]+\/api\//, rota => rota.fulfill({ status: 404, json: {} }))
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://127.0.0.1:5209'}/.build/2734/ficha-cadastro/index.html`
const erros = []
async function abrir(consulta) {
  const pagina = await contexto.newPage(); pagina.setDefaultTimeout(8000)
  pagina.on('pageerror', e => erros.push(e.message))
  await pagina.goto(`${base}?${consulta}`)
  return pagina
}
try {
  // 1. A nota aparece sem clique: recolhida, a coluna mostra a observação do cadastro e as 2 notas mais recentes.
  let p = await abrir('cena=notas')
  const coluna = p.getByRole('region', { name: 'Notas internas do cliente', exact: true })
  const textos = () => coluna.locator('article p').allTextContents()
  const cabecalho = coluna.getByRole('button', { name: /^Notas/ })
  await coluna.getByText('Portão azul, tocar duas vezes', { exact: true }).waitFor()
  assert.equal(await cabecalho.getAttribute('aria-expanded'), 'false')
  assert.deepEqual(await textos(), ['Cliente desde 2019. Não ligar antes das 10h.', 'Portão azul, tocar duas vezes', 'Alérgica a amendoim'])
  // O mesmo nome do campo em Cadastros ("Observações"), e a nota diz quem escreveu e quando, como lá.
  assert.equal(await coluna.locator('[data-observacao-do-cadastro] small').textContent(), 'Observações do cadastro')
  assert.deepEqual(await coluna.locator('article:not([data-observacao-do-cadastro]) small').allTextContents(), ['Nota · Bia · 09/11, 07:00', 'Nota · Ana · 05/11, 07:00'])
  // "Destacar nota" se repete a cada nota: a descrição diz de qual nota é o botão.
  const descrito = await coluna.getByRole('button', { name: 'Destacar nota', exact: true }).first().getAttribute('aria-describedby')
  assert.ok(descrito, 'o botão Destacar nota aponta para o texto da nota')
  assert.equal(await p.locator(`[id="${descrito}"]`).textContent(), 'Portão azul, tocar duas vezes')
  assert.equal(await coluna.getByText('Portão azul, tocar duas vezes', { exact: true }).isVisible(), true, 'a nota está na tela sem abrir a lista')
  assert.equal(await coluna.locator('article p').first().evaluate(el => getComputedStyle(el).webkitLineClamp), '2', 'recolhida, cada nota mostra 2 linhas')
  // O que ficou de fora tem número e leva ao resto.
  const verMais = coluna.getByRole('button', { name: 'Ver mais 1 nota', exact: true })
  await verMais.click()
  assert.equal(await cabecalho.getAttribute('aria-expanded'), 'true')
  assert.deepEqual(await textos(), ['Cliente desde 2019. Não ligar antes das 10h.', 'Portão azul, tocar duas vezes', 'Alérgica a amendoim', 'Prefere entrega à tarde'])
  assert.equal(await verMais.count(), 0, 'aberta, o convite sai')
  assert.equal(await coluna.locator('article p').first().evaluate(el => getComputedStyle(el).webkitLineClamp), 'none', 'aberta, a nota mostra o texto inteiro')
  // O cabeçalho recolhe de volta para a prévia.
  await cabecalho.click()
  assert.deepEqual((await textos()).length, 3)
  await p.close()

  // 2. Sem nota e sem observação, a área não ocupa espaço.
  p = await abrir('cena=vazia')
  await p.getByRole('region', { name: 'Notas internas do cliente', exact: true }).waitFor()
  assert.equal(await p.locator('#notas-conversa').evaluate(el => getComputedStyle(el).display), 'none')
  assert.equal(await p.locator('#notas-conversa article').count(), 0)
  await p.close()

  // 2b. Texto cortado em 2 linhas sem nota escondida: a Ficha oferece o texto inteiro. Texto que cabe não ganha botão.
  p = await abrir('cena=longa')
  const longas = p.getByRole('region', { name: 'Notas internas do cliente', exact: true })
  const inteiro = longas.getByRole('button', { name: 'Ver texto inteiro', exact: true })
  const cortada = () => longas.locator('[data-observacao-do-cadastro] p').evaluate(el => el.scrollHeight > el.clientHeight + 1)
  await longas.locator('[data-observacao-do-cadastro] p').waitFor()
  assert.equal(await cortada(), true, 'recolhida, a observação longa está cortada')
  assert.equal(await longas.getByRole('button', { name: /^Ver (mais|todas)/ }).count(), 0, 'não há nota escondida')
  await inteiro.waitFor()
  await inteiro.click()
  assert.equal(await longas.getByRole('button', { name: /^Notas/ }).getAttribute('aria-expanded'), 'true')
  assert.equal(await cortada(), false, 'aberta, a observação aparece inteira')
  assert.equal(await inteiro.count(), 0, 'aberta, o convite sai')
  await p.close()
  p = await abrir('cena=curta')
  await p.getByText('Portão azul.', { exact: true }).waitFor()
  await p.waitForTimeout(300)
  assert.equal(await p.getByRole('button', { name: 'Ver texto inteiro', exact: true }).count(), 0, 'texto que cabe não ganha botão')
  // Sem nota escondida e sem texto cortado, o cabeçalho não se oferece para abrir o que já está inteiro na tela.
  // #2792: o cabeçalho inerte é título (h3), não botão desativado.
  const regiaoNotas = p.getByRole('region', { name: 'Notas internas do cliente', exact: true })
  const inerte = regiaoNotas.locator('[data-voltar-notas]')
  assert.equal(await inerte.evaluate(n => n.tagName), 'H3', 'cabeçalho sem o que abrir é título')
  assert.equal(await regiaoNotas.getByRole('heading', { name: /^Notas/ }).isVisible(), true)
  assert.equal(await regiaoNotas.getByRole('button', { name: /^Notas/ }).count(), 0, 'sem botão que não faz nada')
  assert.equal(await inerte.getAttribute('aria-expanded'), null)
  assert.equal(await inerte.locator('svg').count(), 0, 'sem seta quando não há o que abrir')
  await p.close()

  // 3. A Ficha leva ao cadastro completo: o endereço muda para o da pessoa em Cadastros e a janela fecha.
  p = await abrir('cena=cadastro')
  let janela = p.getByRole('dialog', { name: 'Cadastro do cliente', exact: true })
  const atalho = () => janela.getByRole('button', { name: 'Abrir cadastro completo', exact: true })
  await atalho().waitFor()
  assert.equal(await atalho().isEnabled(), true)
  await atalho().click()
  await janela.waitFor({ state: 'detached' })
  assert.equal(await p.evaluate(() => window.location.hash), `#/m/cadastros/clientes/${ID}`)
  assert.equal(await p.evaluate(() => window.__fechou), 1)
  await p.close()

  // 4. Com texto por salvar, o atalho espera e diz por quê; nada é descartado em silêncio.
  p = await abrir('cena=cadastro')
  janela = p.getByRole('dialog', { name: 'Cadastro do cliente', exact: true })
  await atalho().waitFor()
  await janela.getByRole('tab', { name: /^Notas/ }).click()
  await p.getByRole('textbox', { name: 'Nova nota', exact: true }).fill('Ligar depois das 14h')
  const motivo = 'Salve ou descarte o que foi digitado para abrir o cadastro completo.'
  await janela.getByText(motivo, { exact: true }).waitFor()
  assert.equal(await atalho().isDisabled(), true)
  assert.equal(await atalho().getAttribute('title'), motivo)
  assert.equal(await p.evaluate(() => window.location.hash), '')
  assert.equal(await p.evaluate(() => window.__fechou), 0)
  await p.close()

  // 5. Quem não tem acesso a Cadastros e o contato sem cadastro não veem o atalho; o resto do rodapé segue.
  for (const consulta of ['cena=cadastro&acesso=nao', 'cena=lead']) {
    p = await abrir(consulta)
    janela = p.getByRole('dialog', { name: 'Cadastro do cliente', exact: true })
    await janela.getByRole('button', { name: 'Baixar PDF', exact: true }).waitFor()
    assert.equal(await atalho().count(), 0, consulta)
    await p.close()
  }
  assert.deepEqual(erros, [])
  console.log('ok #2734: nota à vista na Ficha, observação do cadastro e atalho para o cadastro completo')
} finally { await browser.close() }
