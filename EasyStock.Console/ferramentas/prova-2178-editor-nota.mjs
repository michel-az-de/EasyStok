// Componentes reais, API controlada e navegador isolado. Não usa sessão nem clientes reais.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { writeFile, unlink } from 'node:fs/promises'
import { join } from 'node:path'
import { tmpdir } from 'node:os'
import { build } from 'vite'
import react from '@vitejs/plugin-react'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const raiz = fileURLToPath(new URL('..', import.meta.url)).replaceAll('\\', '/')
const entrada = `import React,{useState,useSyncExternalStore}from '${raiz}/node_modules/react/index.js';
import{createRoot}from '${raiz}/node_modules/react-dom/client.js';
import{ModalNota}from '${raiz}/src/features/notas/ModalNota.jsx';
import{PainelMovel}from '${raiz}/src/componentes/PainelMovel.jsx';
import{criarRascunhosDeNotas}from '${raiz}/src/aplicacao/rascunhosDeNotas.js';
import '${raiz}/src/estilos/tokens.css';
const memoria=criarRascunhosDeNotas('empresa:atendente');window.gravacoes=[];window.respostas={};
function Prova(){const estado=useSyncExternalStore(memoria.assinar,memoria.ler);const[cliente,selecionar]=useState('Marina');const[montado,montar]=useState(true);const[pedido,mostrarPedido]=useState(false);const[cadastrado,cadastrar]=useState(false);const[cadastro,abrirCadastro]=useState(null);
const abrir=(id)=>{selecionar(id);memoria.abrir(id,id);mostrarPedido(false)};
return <><nav><button onClick={()=>abrir('Marina')}>Nota Marina</button><button onClick={()=>abrir('Carlos')}>Nota Carlos</button><button onClick={()=>abrir('Lead')}>Nota Lead</button><button onClick={()=>montar(x=>!x)}>Alternar módulo</button></nav>
<textarea aria-label="Mensagem pública" defaultValue="Mensagem que não é nota"/>
{montado&&estado.rascunhos.map(n=><ModalNota key={n.chave} embutida nomeCliente={n.nome} rascunho={n} visivel={estado.ativa===n.chave} precisaCadastrar={n.id==='Lead'&&!cadastrado} aoCadastrar={()=>{memoria.pausar();abrirCadastro(n.id)}} aoGuardar={campo=>memoria.guardar(n.chave,campo)} aoFechar={()=>memoria.pausar()} aoComanda={()=>{memoria.pausar();mostrarPedido(true)}} aoSalvar={async texto=>{if(!memoria.iniciarGravacao(n.chave))return false;window.gravacoes.push({id:n.id,texto});const ok=await new Promise(resolve=>window.respostas[n.id]=resolve);if(!memoria.concluir(n.chave,ok))memoria.falharGravacao(n.chave,'Sem confirmação');return ok}}/>)}
{cadastro&&<button onClick={()=>{cadastrar(true);abrirCadastro(null)}}>Concluir cadastro de {cadastro}</button>}
<button onClick={()=>abrir(cliente)}>Retomar nota</button>
<PainelMovel visivel={pedido} controle={{solto:true,painelId:'comanda',chave:'teste:comanda',guardar:()=>{},recolher:()=>mostrarPedido(false),devolverFoco:()=>false}} titulo="Comanda de teste" larguraInicial={280} alturaInicial={240} focar><input aria-label="Observação do pedido" defaultValue="Sem molho"/></PainelMovel>
</>};createRoot(document.getElementById('root')).render(<Prova/>);`
const arquivoEntrada = join(tmpdir(), `easystok-prova-2178-${process.pid}.jsx`)
await writeFile(arquivoEntrada, entrada)
let compilado
try {
  compilado = await build({ root: raiz, configFile: false, logLevel: 'silent',
    define: { 'process.env.NODE_ENV': '"production"' }, plugins: [react()],
    resolve: { alias: { 'react/jsx-runtime': `${raiz}/node_modules/react/jsx-runtime.js` } },
    build: { write: false, minify: false, lib: { entry: arquivoEntrada, formats: ['iife'], name: 'ProvaNota' } } })
} finally { await unlink(arquivoEntrada) }
const arquivos = (Array.isArray(compilado) ? compilado[0] : compilado).output
const navegador = await chromium.launch({ headless: true })
try {
  const pagina = await navegador.newPage({ viewport: { width: 390, height: 844 }, reducedMotion: 'reduce' })
  await pagina.route(/^https?:\/\//, rota => rota.abort())
  const erros = []
  pagina.on('pageerror', erro => erros.push(erro.message))
  await pagina.setContent('<div id="root"></div>')
  for (const arquivo of arquivos) {
    if (arquivo.type === 'asset' && arquivo.fileName.endsWith('.css')) await pagina.addStyleTag({ content: String(arquivo.source) })
    if (arquivo.type === 'chunk') await pagina.addScriptTag({ content: arquivo.code })
  }
  await pagina.getByRole('button', { name: 'Nota Marina', exact: true }).click()
  const marina = pagina.getByRole('textbox', { name: 'Texto da nota de Marina', exact: true })
  const texto = Array.from({ length: 18 }, (_, i) => `Linha ${i + 1} da nota privada.`).join('\n')
  await marina.fill(texto)
  await marina.press('Control+End')
  await marina.press('!')
  const posicao = await marina.evaluate(no => {
    window.campoMarina = no; no.setSelectionRange(12, 18); no.scrollTop = 70
    no.dispatchEvent(new Event('select', { bubbles: true })); no.dispatchEvent(new Event('scroll', { bubbles: true }))
    return { inicio: no.selectionStart, fim: no.selectionEnd, rolagem: no.scrollTop }
  })
  await pagina.getByRole('button', { name: 'Comanda', exact: true }).click()
  const observacao = pagina.getByRole('textbox', { name: 'Observação do pedido', exact: true })
  await observacao.evaluate(no => { window.campoPedido = no })
  await pagina.getByRole('button', { name: 'Retomar nota', exact: true }).click()
  assert.equal(await observacao.isVisible(), false, 'portal oculto não cobre a nota')
  assert.deepEqual(await marina.evaluate(no => ({ inicio: no.selectionStart, fim: no.selectionEnd, rolagem: no.scrollTop })), posicao)
  assert.equal(await marina.evaluate(no => no === window.campoMarina), true, 'nota mantém o nó ao visitar a comanda')
  await marina.press('Control+z')
  assert.equal(await marina.inputValue(), texto, 'desfazer nativo sobrevive à pausa')
  await pagina.getByRole('button', { name: 'Comanda', exact: true }).click()
  assert.equal(await observacao.inputValue(), 'Sem molho')
  assert.equal(await observacao.evaluate(no => no === window.campoPedido), true, 'portal conserva o formulário')
  await pagina.getByRole('button', { name: 'Retomar nota', exact: true }).click()
  await pagina.getByRole('button', { name: 'Salvar', exact: true }).click()
  await pagina.getByRole('button', { name: 'Nota Carlos', exact: true }).click()
  const carlos = pagina.getByRole('textbox', { name: 'Texto da nota de Carlos', exact: true })
  await carlos.fill('Rascunho de Carlos')
  await pagina.evaluate(() => window.respostas.Marina(false))
  assert.equal(await carlos.inputValue(), 'Rascunho de Carlos')
  await pagina.getByRole('button', { name: 'Nota Marina', exact: true }).click()
  await pagina.getByRole('alert').filter({ hasText: 'Sem confirmação' }).waitFor()
  assert.equal(await marina.inputValue(), texto)
  await pagina.getByRole('button', { name: 'Salvar', exact: true }).click()
  await pagina.getByRole('button', { name: 'Nota Carlos', exact: true }).click()
  await pagina.evaluate(() => window.respostas.Marina(true))
  await pagina.waitForFunction(() => !document.querySelector('textarea[aria-label="Texto da nota de Marina"]'))
  assert.equal(await carlos.inputValue(), 'Rascunho de Carlos', 'salvar Marina não apaga nem troca Carlos')
  assert.equal(await pagina.getByRole('textbox', { name: 'Mensagem pública', exact: true }).inputValue(), 'Mensagem que não é nota')
  assert.deepEqual(await pagina.evaluate(() => window.gravacoes.map(g => g.id)), ['Marina', 'Marina'])
  await pagina.getByRole('button', { name: 'Nota Lead', exact: true }).click()
  const lead = pagina.getByRole('textbox', { name: 'Texto da nota de Lead', exact: true })
  await lead.fill('Rascunho do contato sem cadastro')
  const cadastrar = pagina.getByRole('button', { name: 'Cadastrar para salvar', exact: true })
  assert.equal(await cadastrar.count(), 1, 'lead recebe caminho de cadastro antes de tentar salvar')
  await pagina.setViewportSize({ width: 320, height: 568 })
  const larguraCadastro = await cadastrar.evaluate(no => ({ conteudo: no.scrollWidth, alvo: no.clientWidth, grade: no.parentElement.clientWidth,
    botoes: [...no.parentElement.children].map(botao => ({ texto: botao.innerText, alvo: botao.clientWidth, conteudo: botao.scrollWidth })) }))
  assert.ok(larguraCadastro.conteudo <= larguraCadastro.alvo, `ação de cadastro cabe em 320 px: ${JSON.stringify(larguraCadastro)}`)
  await lead.press('Control+Enter')
  await pagina.getByRole('button', { name: 'Concluir cadastro de Lead', exact: true }).waitFor()
  assert.equal(await lead.isVisible(), false, 'abrir cadastro pausa o editor')
  assert.equal(await pagina.evaluate(() => window.gravacoes.some(g => g.id === 'Lead')), false, 'atalho não tenta POST de nota sem cadastro')
  await pagina.getByRole('button', { name: 'Concluir cadastro de Lead', exact: true }).click()
  await pagina.getByRole('button', { name: 'Retomar nota', exact: true }).click()
  assert.equal(await lead.inputValue(), 'Rascunho do contato sem cadastro')
  assert.equal(await cadastrar.count(), 0)
  await pagina.getByRole('button', { name: 'Salvar', exact: true }).click()
  assert.equal(await pagina.evaluate(() => window.gravacoes.at(-1).id), 'Lead')
  await pagina.evaluate(() => window.respostas.Lead(true))
  assert.deepEqual(erros, [])
  console.log('ok #2178: DOM/seleção/scroll/Undo, portal oculto preservado, falha e confirmação por dono; nenhuma escrita pública')
} finally { await navegador.close() }
