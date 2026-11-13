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
const entrada = `import React,{useCallback,useState,useSyncExternalStore}from '${raiz}/node_modules/react/index.js';
import{createRoot}from '${raiz}/node_modules/react-dom/client.js';
import{ModalNota}from '${raiz}/src/features/notas/ModalNota.jsx';
import{ModalCliente}from '${raiz}/src/features/ficha-cliente/ModalCliente.jsx';
import{ContextoRascunhosDosAjustes,useGravacoesDosAjustes}from '${raiz}/src/aplicacao/rascunhosDosAjustes.js';
import{PainelMovel}from '${raiz}/src/componentes/PainelMovel.jsx';
import{PainelAtendimento}from '${raiz}/src/features/atendimento/PainelAtendimento.jsx';
import{ContextoAcoes,ContextoCatalogo,ContextoEstado}from '${raiz}/src/aplicacao/contextos.js';
import{CANAIS}from '${raiz}/src/infra/catalogo.js';
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
</>};
function ProvaFoco(){const[nota,anotar]=useState(false);const[cliente,trocar]=useState('Marina');const[visivel,mostrar]=useState(true);const[mobile,celular]=useState(true);const[campo,escrever]=useState('Mensagem pública preservada');const agora=Date.now();const noop=()=>{};
const conversa={id:cliente,nome:cliente,canal:'WhatsApp',estado:'Aberto',conta:'cliente',cliente:{tags:[],notas:[],pedidos:1},pedido:null,passagem:null,mensagens:[{id:'m1',dir:'in',texto:'Bom dia!',em:new Date(agora-60000).toISOString()}],ultimaEm:new Date(agora-60000).toISOString()};
const acoes={descarregarRascunho:({texto})=>escrever(texto),enviarMidia:noop,definirRascunho:noop,devolverAutomatico:noop,selecionar:noop,abrirEncerramento:noop,reabrir:noop,listarAtendentes:async()=>[],enviar:noop,assumirAtendimento:async()=>true};
const estado={selecionada:conversa,visiveis:[conversa],agora,automaticoPausado:{[cliente]:true},rascunho:campo,escritaDeFora:0,fonteApi:false};const catalogo={janelas:[],canais:CANAIS,respostasProntas:[{id:'cardapio',titulo:'Cardápio',atalho:'/cardapio',texto:'Cardápio de hoje',arquivada:false}],cardapio:[],linhas:[],adicionais:[]};
return <ContextoEstado value={estado}><ContextoAcoes value={acoes}><ContextoCatalogo value={catalogo}>
<button onClick={()=>{trocar('Carlos');anotar(false)}}>Trocar cliente durante nota</button><button onClick={()=>mostrar(true)}>Voltar ao painel</button><button onClick={()=>{celular(false);anotar(false)}}>Modo desktop</button>
<section hidden={!visivel} style={{display:visivel?'flex':'none',height:600,flexDirection:'column',overflow:'hidden'}}>
<PainelAtendimento focarAoTrocar={mobile} visivel={visivel} editorNotaVisivel={nota&&mobile} aoAbrirNota={()=>anotar(true)} aoAbrirGaleria={noop} aoAbrirBiblioteca={noop}
slotEditorNota={<ModalNota embutida={mobile} visivel={nota} nomeCliente={cliente} aoFechar={()=>anotar(false)} aoComanda={()=>{anotar(false);mostrar(false)}} aoSalvar={async()=>false}/>}/>
</section></ContextoCatalogo></ContextoAcoes></ContextoEstado>}
function ProvaCadastro(){
 const[cliente,trocar]=useState('Marina');const[aberto,mostrar]=useState(true);const[rascunhos,definir]=useState({});const gravacoes=useGravacoesDosAjustes();
 const guardar=useCallback((chave,valor)=>definir(antes=>{const depois={...antes};if(valor===null)delete depois[chave];else depois[chave]=valor;return depois}),[]);
 window.montarCadastro=mostrar;window.trocarClienteCadastro=trocar;
 const conversa={id:cliente,clienteId:'cadastro-'+cliente,cadastroId:'cadastro-'+cliente,nome:cliente,canal:'WhatsApp',estado:'Aberto',conta:'cliente',
 cliente:{tags:[],notas:[],pedidos:0,pontos:0,telefone:'(11) 90000-0000',endereco:'Rua de teste, 100'},pedido:null,mensagens:[]};
 const estado={fonteApi:true,conversas:[conversa],historico:[],agora:Date.now(),automaticoPausado:{},fidelidade:{recompensas:[],sorteios:[],resgatesPorCadastro:{}}};
 const acoes={salvarNota:async(id,texto)=>{window.gravacoesCadastro.push({id,texto});return new Promise(resolve=>{window.responderCadastro=resolve})},
 editarDadoCliente:async(id,campo,texto)=>{window.gravacoesCampos.push({id,campo,texto});return new Promise(resolve=>{window.responderCampo=resolve})}};
 return <ContextoRascunhosDosAjustes value={{rascunhos,definir:guardar,...gravacoes}}><ContextoEstado value={estado}><ContextoAcoes value={acoes}><ContextoCatalogo value={{canais:CANAIS,motivosBloqueio:[],prefixosCepAtendidos:[],restricoes:[]}}>
 {aberto&&<ModalCliente key={cliente} conversa={conversa} aoFechar={()=>mostrar(false)}/>}
 </ContextoCatalogo></ContextoAcoes></ContextoEstado></ContextoRascunhosDosAjustes>
}
const raizReact=createRoot(document.getElementById('root'));raizReact.render(<Prova/>);window.mostrarPainelReal=()=>raizReact.render(<ProvaFoco/>);
window.mostrarCadastroReal=()=>{window.gravacoesCadastro=[];window.gravacoesCampos=[];raizReact.render(<ProvaCadastro/>)};`
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
  await pagina.setViewportSize({ width: 390, height: 844 })
  await pagina.evaluate(() => window.mostrarPainelReal())
  const publico = pagina.getByRole('textbox', { name: 'Mensagem para o cliente', exact: true })
  await publico.fill('Mensagem pública com seleção preservada')
  await publico.evaluate(no => { window.publicoOriginal = no; no.setSelectionRange(9, 16) })
  await pagina.getByRole('button', { name: 'Nota interna', exact: true }).click()
  await pagina.getByRole('textbox', { name: 'Texto da nota de Marina', exact: true }).fill('Privada')
  await pagina.getByRole('button', { name: 'Conversa', exact: true }).click()
  assert.equal(await publico.evaluate(no => document.activeElement === no), true, 'Conversa devolve foco ao compositor público real depois de desocultar')
  assert.equal(await publico.evaluate(no => no === window.publicoOriginal), true)
  assert.deepEqual(await publico.evaluate(no => [no.selectionStart, no.selectionEnd]), [9, 16])
  await publico.fill('/cardapio')
  const respostasRapidas = pagina.getByRole('group', { name: 'Respostas rápidas', exact: true })
  await respostasRapidas.waitFor({ state: 'visible' })
  await pagina.getByRole('button', { name: 'Nota interna', exact: true }).click()
  await pagina.getByRole('textbox', { name: 'Texto da nota de Marina', exact: true }).waitFor({ state: 'visible' })
  assert.equal(await pagina.evaluate(() => window.publicoOriginal.value), '/cardapio', 'pointerdown na Nota não descarta o prefixo público de resposta rápida')
  assert.equal(await respostasRapidas.isVisible(), false, 'portal de respostas rápidas não cobre a nota privada')
  await pagina.getByRole('button', { name: 'Conversa', exact: true }).click()
  assert.equal(await publico.inputValue(), '/cardapio', 'Conversa retoma o prefixo literal, sem inserir a resposta')
  assert.equal(await publico.evaluate(no => document.activeElement === no), true)
  await publico.fill('Mensagem pública com seleção preservada')
  await pagina.getByRole('button', { name: 'Nota interna', exact: true }).click()
  await pagina.getByRole('button', { name: 'Comanda', exact: true }).click()
  assert.equal(await pagina.evaluate(() => document.activeElement === window.publicoOriginal), false, 'Comanda não rouba foco para compositor oculto')
  await pagina.getByRole('button', { name: 'Voltar ao painel', exact: true }).click()
  await pagina.getByRole('button', { name: 'Nota interna', exact: true }).click()
  await pagina.getByRole('button', { name: 'Trocar cliente durante nota', exact: true }).click()
  assert.equal(await publico.evaluate(no => document.activeElement === no), false, 'trocar cliente não abre teclado por causa da nota anterior')
  await pagina.getByRole('button', { name: 'Modo desktop', exact: true }).click()
  await pagina.getByRole('button', { name: 'Nota interna', exact: true }).click()
  await pagina.getByRole('button', { name: 'Recolher', exact: true }).click()
  assert.equal(await publico.evaluate(no => document.activeElement === no), false, 'desktop mantém seu retorno de foco existente')
  // #2360: o cadastro usa o modal e o contexto reais; remontar simula a troca de composição no celular.
  await pagina.evaluate(() => window.mostrarCadastroReal())
  const cadastroCliente = pagina.getByRole('dialog', { name: 'Cadastro do cliente', exact: true })
  await cadastroCliente.waitFor({ timeout: 5000 }).catch(erro => { assert.deepEqual(erros, []); throw erro })
  const abrirNotas = () => cadastroCliente.getByRole('tab', { name: /^Notas/ }).click()
  const notaCadastro = pagina.getByRole('textbox', { name: 'Nova nota', exact: true })
  await abrirNotas(); await notaCadastro.fill('Privada da Marina')
  await cadastroCliente.getByRole('button', { name: 'Fechar', exact: true }).click()
  await cadastroCliente.getByText('Descartar o que foi digitado?', { exact: true }).waitFor()
  await cadastroCliente.getByRole('button', { name: 'Continuar editando', exact: true }).click()
  assert.equal(await notaCadastro.inputValue(), 'Privada da Marina')
  await pagina.evaluate(() => window.montarCadastro(false)); await cadastroCliente.waitFor({ state: 'detached' })
  await pagina.setViewportSize({ width: 1440, height: 900 })
  await pagina.evaluate(() => window.montarCadastro(true)); await abrirNotas()
  assert.equal(await notaCadastro.inputValue(), 'Privada da Marina', 'remonte recupera o texto do contexto da visita')
  await pagina.evaluate(() => window.trocarClienteCadastro('Carlos')); await abrirNotas()
  assert.equal(await notaCadastro.inputValue(), '', 'outro cliente não recebe o rascunho')
  await pagina.evaluate(() => window.trocarClienteCadastro('Marina')); await abrirNotas()
  assert.equal(await notaCadastro.inputValue(), 'Privada da Marina')
  await cadastroCliente.getByRole('button', { name: 'Salvar nota', exact: true }).click()
  await pagina.waitForFunction(() => [...document.querySelectorAll('button')].some(botao => botao.textContent === 'Fechar' && botao.disabled))
  assert.equal(await cadastroCliente.getByRole('button', { name: 'Fechar', exact: true }).isDisabled(), true)
  await pagina.evaluate(() => window.responderCadastro(false))
  await cadastroCliente.getByRole('alert').filter({ hasText: 'Sem confirmação da nota' }).waitFor()
  assert.equal(await notaCadastro.inputValue(), 'Privada da Marina', 'falha preserva o rascunho')
  await cadastroCliente.getByRole('button', { name: 'Salvar nota', exact: true }).click()
  await pagina.evaluate(() => window.responderCadastro(true))
  await pagina.waitForFunction(() => document.querySelector('textarea').value === '')
  assert.deepEqual(await pagina.evaluate(() => window.gravacoesCadastro), [
    { id: 'Marina', texto: 'Privada da Marina' }, { id: 'Marina', texto: 'Privada da Marina' },
  ])
  await notaCadastro.fill('Descartar somente esta nota')
  await cadastroCliente.getByRole('button', { name: 'Fechar', exact: true }).click()
  await cadastroCliente.getByRole('button', { name: 'Descartar', exact: true }).click()
  await cadastroCliente.waitFor({ state: 'detached' })
  await pagina.evaluate(() => window.montarCadastro(true)); await abrirNotas()
  assert.equal(await notaCadastro.inputValue(), '', 'descarte explícito limpa a memória do rascunho')
  // #2365: os campos do cadastro também sobrevivem à troca de composição, sem vazar entre clientes.
  const nomeCadastro = cadastroCliente.getByRole('textbox', { name: 'Nome do cliente', exact: true })
  const remontarCadastro = async () => {
    await pagina.evaluate(() => window.montarCadastro(false)); await cadastroCliente.waitFor({ state: 'detached' })
    await pagina.evaluate(() => window.montarCadastro(true)); await cadastroCliente.waitFor({ state: 'visible' })
  }
  await cadastroCliente.getByRole('button', { name: 'Editar Nome do cliente', exact: true }).click()
  await nomeCadastro.fill('Marina revisada')
  await remontarCadastro()
  assert.equal(await nomeCadastro.count(), 1, 'remonte mantém o editor de nome aberto')
  assert.equal(await nomeCadastro.inputValue(), 'Marina revisada')
  assert.equal(await pagina.evaluate(() => window.gravacoesCampos.length), 0, 'reabrir o rascunho não salva por mudança automática de foco')
  await pagina.evaluate(() => window.trocarClienteCadastro('Carlos'))
  await cadastroCliente.getByRole('button', { name: 'Editar Nome do cliente', exact: true }).waitFor()
  assert.equal(await nomeCadastro.count(), 0, 'outro cliente não herda a edição')
  await pagina.evaluate(() => window.trocarClienteCadastro('Marina'))
  await nomeCadastro.waitFor(); assert.equal(await nomeCadastro.inputValue(), 'Marina revisada')
  await nomeCadastro.press('Enter')
  await pagina.waitForFunction(() => window.gravacoesCampos.length === 1)
  await remontarCadastro()
  await nomeCadastro.press('Enter')
  assert.equal(await pagina.evaluate(() => window.gravacoesCampos.length), 1, 'remonte durante a ida não permite reenvio')
  assert.equal(await cadastroCliente.getByRole('button', { name: 'Fechar', exact: true }).isDisabled(), true)
  await pagina.evaluate(() => window.responderCampo(false))
  await cadastroCliente.getByRole('alert').filter({ hasText: 'Não foi possível salvar' }).waitFor()
  assert.equal(await nomeCadastro.inputValue(), 'Marina revisada', 'recusa preserva o texto')
  await remontarCadastro(); assert.equal(await nomeCadastro.inputValue(), 'Marina revisada')
  await nomeCadastro.press('Enter')
  await pagina.waitForFunction(() => window.gravacoesCampos.length === 2)
  await pagina.evaluate(() => window.responderCampo(true))
  await cadastroCliente.getByRole('button', { name: 'Editar Nome do cliente', exact: true }).waitFor()
  await remontarCadastro(); assert.equal(await nomeCadastro.count(), 0, 'sucesso limpa o rascunho')
  await cadastroCliente.getByRole('button', { name: 'Editar Nome do cliente', exact: true }).click()
  await nomeCadastro.fill('Marina descartada')
  await cadastroCliente.getByRole('button', { name: 'Desfazer', exact: true }).click()
  await remontarCadastro(); assert.equal(await nomeCadastro.count(), 0, 'Desfazer limpa o rascunho')
  assert.deepEqual(await pagina.evaluate(() => window.gravacoesCampos), [
    { id: 'Marina', campo: 'nome', texto: 'Marina revisada' }, { id: 'Marina', campo: 'nome', texto: 'Marina revisada' },
  ])
  assert.deepEqual(erros, [])
  console.log('ok #2178: DOM/seleção/scroll/Undo, portal oculto preservado, falha e confirmação por dono; nenhuma escrita pública')
  console.log('ok #2360: cadastro real confirma descarte e preserva nota no remonte, por cliente e após falha; confirmação limpa')
  console.log('ok #2365: edição por cliente sobrevive ao remonte pendente sem reenvio, mantém falha visível e limpa após salvar ou Desfazer')
} finally { await navegador.close() }
