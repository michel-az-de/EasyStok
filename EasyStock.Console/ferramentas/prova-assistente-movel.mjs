// #3210 (S86): o Assistente da conversa abre num painel móvel (arrasta, encaixa, lembra a posição), não numa modal.
// Chromium, o ModalAssistente real ao lado de uma "conversa" de mentira (botão clicável, rolagem e o campo de
// mensagem em [data-area-envio]) e HTTP controlado. Confere em 1440 e 390 px: abre como painel (sem dialog[open] modal,
// sem ::backdrop), a conversa atrás continua clicável, o campo de mensagem fica livre, arrastar muda a posição e ela
// persiste ao reabrir, Esc e o botão de fechar devolvem o foco ao campo, Esc com a confirmação aberta fecha só a de cima.
// FASE=antes só tira a foto do estado atual (o resto falha de propósito até a troca). Prints em PRINTS_3210.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const fase = process.env.FASE ?? 'depois'
const pastaPrints = process.env.PRINTS_3210
if (pastaPrints) mkdirSync(pastaPrints, { recursive: true })
const pasta = resolve(import.meta.dirname, '../.build/3210/assistente-movel')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React, {useState} from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {ModalAssistente} from '../../../src/features/atendimento/ModalAssistente';
import '../../../src/estilos/base.css';
document.body.style.cssText='margin:0;background:var(--fundo);font-family:var(--f-corpo);color:var(--tinta)';
const conversa={id:'c5',nome:'Felipe Azevedo',contato:{nome:'Felipe Azevedo'},numeroComanda:'5',mensagens:[]};
function Cena(){
  const [aberto,setAberto]=useState(false); const [cliques,setCliques]=useState(0);
  return <div style={{display:'flex',flexDirection:'column',height:'100dvh'}}>
    <div id="fio" style={{flex:1,overflow:'auto',padding:16}}>
      <button type="button" onClick={()=>setCliques(n=>n+1)}>Mensagem do cliente</button> <output id="cliques">{cliques}</output>
      <button type="button" onClick={()=>setAberto(true)}>Checar o Assistente</button>
      <div style={{height:1600}}>conversa</div>
    </div>
    <div data-area-envio style={{flexShrink:0,padding:8}}><textarea aria-label="Mensagem" style={{width:'100%',height:72}}/></div>
    {aberto && <ModalAssistente conversa={conversa} aoFechar={()=>setAberto(false)} escopoMovel="assistente:prova:u1"/>}
  </div>;
}
createRoot(document.getElementById('root')).render(<ContextoAcessoModulos.Provider value={{acoes:{editarAtendimento:true},permite:()=>true}}><Cena/></ContextoAcessoModulos.Provider>);
`)

const checagem = {
  ativo: true, responde: true, bloqueio: null, entradasPendentes: 0,
  etapas: [{ codigo: 'situacao', titulo: 'Quem atende', estado: 'Passa', detalhe: 'O Assistente atende esta conversa.' }],
  prompt: 'Você é o assistente de atendimento da Casa.', ferramentas: [{ nome: 'consultar_pedido', descricao: 'Consulta o pedido', ligada: true }],
}
const browser = await chromium.launch({ headless: true })
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/3210/assistente-movel/index.html`
const erros = []
const NOME = 'Assistente nesta conversa'

async function rodar(nome, largura, altura) {
  const contexto = await browser.newContext({ viewport: { width: largura, height: altura }, locale: 'pt-BR', reducedMotion: 'reduce' })
  await contexto.route(url => url.pathname.startsWith('/api/'), route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/atendimento/conversas/c5/assistente') return route.fulfill({ json: { data: checagem } })
    return route.fulfill({ status: 404, json: { error: { message: 'fora da prova: ' + path } } })
  })
  const pagina = await contexto.newPage()
  pagina.setDefaultTimeout(10000)
  pagina.on('pageerror', e => erros.push(e.message)); 
  const painel = pagina.getByRole('dialog', { name: NOME })
  const campo = pagina.getByRole('textbox', { name: 'Mensagem' })
  const abrir = async () => { await pagina.getByRole('button', { name: 'Checar o Assistente' }).click(); await painel.waitFor() }
  const focoNoCampo = () => pagina.evaluate(() => document.activeElement?.getAttribute('aria-label') === 'Mensagem')
  const print = (sufixo) => pastaPrints ? pagina.screenshot({ path: resolve(pastaPrints, `assistente-${fase}-${nome}${sufixo ?? ''}.png`) }) : null
  try {
    // Aquece como o rodar-provas: o Vite recarrega a página uma vez ao otimizar dependências.
    await pagina.goto(base, { waitUntil: 'networkidle' }); await pagina.waitForTimeout(800); await pagina.reload({ waitUntil: 'networkidle' })
    await pagina.getByRole('button', { name: 'Checar o Assistente' }).click()
    await pagina.getByText('Assistente ligado na loja').waitFor()
    await pagina.waitForTimeout(300)
    await print()
    // 1. painel, não modal.
    assert.equal(await pagina.locator('dialog[open]').count(), 0, `${nome}: nenhuma modal (dialog[open]) aberta`)
    await painel.waitFor()
    assert.equal(await painel.getAttribute('aria-modal'), null, `${nome}: sem aria-modal`)
    assert.equal(await painel.evaluate(e => e.matches(':modal')), false, `${nome}: não é modal`)
    assert.ok(await painel.locator('[role="switch"]').isVisible(), `${nome}: o conteúdo do Assistente está no painel`)
    // 2. foco entra no painel; cabe na tela; sem rolagem lateral; não cobre o campo de mensagem.
    assert.equal(await painel.evaluate(e => e.contains(document.activeElement)), true, `${nome}: o foco entra no painel`)
    const caixa = await painel.boundingBox()
    assert.ok(caixa.x >= 0 && caixa.y >= 0 && caixa.x + caixa.width <= largura && caixa.y + caixa.height <= altura, `${nome}: cabe na tela ${JSON.stringify(caixa)}`)
    assert.ok(await pagina.evaluate(() => document.documentElement.scrollWidth) <= largura, `${nome}: sem rolagem lateral`)
    const campoLivre = await campo.evaluate(e => { const r = e.getBoundingClientRect(); const no = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2); return no === e })
    assert.equal(campoLivre, true, `${nome}: o painel não cobre o campo de mensagem`)
    // 3. a conversa atrás continua clicável e rolável.
    if (largura >= 1000) {
      await pagina.getByRole('button', { name: 'Mensagem do cliente' }).click()
      assert.equal(await pagina.locator('#cliques').textContent(), '1', `${nome}: clique na conversa atrás funciona`)
      await pagina.mouse.move(40, 300); await pagina.mouse.wheel(0, 400); await pagina.waitForTimeout(300)
      assert.ok(await pagina.locator('#fio').evaluate(e => e.scrollTop) > 0, `${nome}: a conversa rola com o painel aberto`)
    }
    await campo.click(); await campo.fill('rascunho')
    assert.equal(await campo.inputValue(), 'rascunho', `${nome}: dá para digitar com o painel aberto`)
    await campo.fill('')
    // 4. arrastar muda a posição e ela persiste ao reabrir.
    const alca = painel.getByRole('button', { name: `Mover ${NOME}` })
    const a = await alca.boundingBox()
    await pagina.mouse.move(a.x + 20, a.y + a.height / 2); await pagina.mouse.down()
    await pagina.mouse.move(a.x - 20, a.y + 40, { steps: 6 }); await pagina.mouse.move(a.x - 60, a.y + 60, { steps: 6 }); await pagina.mouse.up()
    const movido = await painel.boundingBox()
    assert.ok(Math.abs(movido.x - caixa.x) > 5 || Math.abs(movido.y - caixa.y) > 5, `${nome}: arrastar muda a posição (${caixa.x},${caixa.y}) -> (${movido.x},${movido.y})`)
    assert.ok(await pagina.evaluate(() => Object.keys(localStorage).some(k => k.includes('assistente'))), `${nome}: a posição é guardada no localStorage`)
    await print('-movido')
    // 4b. Esc com movimento ou tamanho a desfazer (teclado ou mouse) desfaz e NÃO fecha; o Esc seguinte fecha e o foco volta ao campo.
    const perto = (x, y, msg) => assert.ok(Math.abs(x.x - y.x) < 2 && Math.abs(x.y - y.y) < 2 && Math.abs(x.width - y.width) < 2 && Math.abs(x.height - y.height) < 2, `${nome}: ${msg} (${JSON.stringify(x)} vs ${JSON.stringify(y)})`)
    if (largura >= 1000) { // no celular o painel ocupa a tela e as setas não têm para onde ir: o Esc sem o que desfazer fecha (passo 5)
      const redimensionar = painel.getByRole('button', { name: `Redimensionar ${NOME}` })
      await campo.focus(); await alca.focus()
      const antesSetas = await painel.boundingBox()
      for (const t of ['ArrowLeft', 'ArrowLeft', 'ArrowUp', 'ArrowDown']) await pagina.keyboard.press(t)
      assert.notEqual((await painel.boundingBox()).x, antesSetas.x, `${nome}: as setas movem o painel`)
      await pagina.keyboard.press('Escape')
      assert.equal(await painel.count(), 1, `${nome}: Esc depois de mover pelas setas desfaz e não fecha`)
      perto(await painel.boundingBox(), antesSetas, 'Esc volta o painel à posição anterior')
      await campo.focus(); await redimensionar.focus()
      const antesTamanho = await painel.boundingBox()
      for (const t of ['ArrowLeft', 'ArrowLeft', 'ArrowUp', 'ArrowUp']) await pagina.keyboard.press(t)
      assert.notEqual((await painel.boundingBox()).width, antesTamanho.width, `${nome}: as setas mudam o tamanho`)
      await pagina.keyboard.press('Escape')
      assert.equal(await painel.count(), 1, `${nome}: Esc depois de redimensionar pelo teclado desfaz e não fecha`)
      perto(await painel.boundingBox(), antesTamanho, 'Esc volta o painel ao tamanho anterior')
      {
        const r = await redimensionar.boundingBox()
        await pagina.mouse.move(r.x + r.width / 2, r.y + r.height / 2); await pagina.mouse.down()
        await pagina.mouse.move(r.x - 40, r.y - 40, { steps: 4 })
        assert.notEqual((await painel.boundingBox()).width, antesTamanho.width, `${nome}: o mouse muda o tamanho`)
        await pagina.keyboard.press('Escape'); await pagina.mouse.up()
        assert.equal(await painel.count(), 1, `${nome}: Esc no meio do redimensionamento com o mouse desfaz e não fecha`)
        perto(await painel.boundingBox(), antesTamanho, 'Esc no meio do arraste volta o tamanho')
      }
      await pagina.keyboard.press('Escape')
      await painel.waitFor({ state: 'detached' })
      assert.equal(await focoNoCampo(), true, `${nome}: o segundo Esc fecha e devolve o foco ao campo`)
      await abrir()
    }
    // 5. Esc (foco na alça, o de quando abre) fecha e o foco volta ao campo de mensagem.
    // Um arraste de mouse já concluído não tem o que desfazer: o Esc seguinte fecha e a posição lembrada é a do arraste.
    const b = await alca.boundingBox()
    await pagina.mouse.move(b.x + 20, b.y + b.height / 2); await pagina.mouse.down()
    await pagina.mouse.move(b.x + 50, b.y + 30, { steps: 4 }); await pagina.mouse.up()
    const arrastado = await painel.boundingBox()
    if (largura >= 1000) assert.ok(Math.abs(arrastado.x - movido.x) > 5 || Math.abs(arrastado.y - movido.y) > 5, `${nome}: o segundo arraste muda a posição`)
    await pagina.keyboard.press('Escape')
    await painel.waitFor({ state: 'detached' })
    assert.equal(await focoNoCampo(), true, `${nome}: Esc devolve o foco ao campo de mensagem`)
    await abrir()
    const reaberto = await painel.boundingBox()
    assert.ok(Math.abs(reaberto.x - arrastado.x) < 2 && Math.abs(reaberto.y - arrastado.y) < 2, `${nome}: reabre onde o arraste deixou (${arrastado.x},${arrastado.y}) -> (${reaberto.x},${reaberto.y})`)
    // 6. Esc com o foco no conteúdo.
    await painel.getByText('O Assistente responde a próxima mensagem.').click(); await pagina.keyboard.press('Escape')
    await painel.waitFor({ state: 'detached' })
    assert.equal(await focoNoCampo(), true, `${nome}: Esc no conteúdo devolve o foco ao campo`)
    // 7. botões Fechar e Recolher.
    await abrir()
    // Checar de novo e Fechar eram o rodapé fixo da modal: seguem à vista sem rolar o painel.
    const moldura = await painel.boundingBox()
    const checar = await painel.getByRole('button', { name: 'Checar de novo' }).boundingBox()
    assert.ok(checar.y >= moldura.y && checar.y + checar.height <= moldura.y + moldura.height + 1, `${nome}: Checar de novo à vista sem rolar (botão em ${checar.y}, painel até ${moldura.y + moldura.height})`)
    await painel.getByRole('button', { name: 'Fechar', exact: true }).click()
    await painel.waitFor({ state: 'detached' })
    assert.equal(await focoNoCampo(), true, `${nome}: Fechar devolve o foco ao campo`)
    await abrir(); await painel.getByRole('button', { name: `Recolher ${NOME}` }).click()
    await painel.waitFor({ state: 'detached' })
    assert.equal(await focoNoCampo(), true, `${nome}: Recolher devolve o foco ao campo`)
    // 8. Esc com a confirmação aberta fecha só a de cima.
    await abrir()
    await painel.getByRole('switch').click({ force: true })
    const confirmacao = pagina.locator('dialog[open]')
    await confirmacao.waitFor()
    await pagina.keyboard.press('Escape')
    await confirmacao.waitFor({ state: 'detached' })
    await painel.waitFor()
    // 9. restaurar posição volta ao encaixe inicial.
    await painel.getByRole('button', { name: `Restaurar posição de ${NOME}` }).click()
    const inicial = await painel.boundingBox()
    assert.ok(inicial.x + inicial.width <= largura && inicial.y + inicial.height <= altura, `${nome}: a posição inicial cabe`)
    await print('-inicial')
  } catch (e) { console.error(nome, 'erros da página:', erros.join(' | ')); if (pastaPrints) await pagina.screenshot({ path: resolve(pastaPrints, `assistente-${fase}-${nome}-falha.png`) }).catch(() => {}); throw e }
  finally { await contexto.close() }
}

try {
  if (!process.env.SO || process.env.SO === 'desktop') await rodar('desktop', 1440, 900)
  if (!process.env.SO || process.env.SO === 'celular') await rodar('celular', 390, 844)
  // Trocar de conversa com o painel aberto fecha o painel: o Painel de atendimento só abre o modal da conversa
  // que o pediu (modalDoMenu.conversaId), então nunca mostra dado da conversa errada.
  const painelAtendimento = readFileSync(resolve(import.meta.dirname, '../src/features/atendimento/PainelAtendimento.jsx'), 'utf8')
  assert.match(painelAtendimento, /modalDoMenu\?\.conversaId === selecionada\.id/)
  assert.match(painelAtendimento, /modalAberto === 'assistente' && <ModalAssistente key=\{selecionada\.id\}/)
  assert.deepEqual(erros, [])
  console.log('#3210 prova-assistente-movel: o Assistente da conversa abre como painel móvel (não modal), arrasta, lembra a posição, Esc e Fechar devolvem o foco ao campo; ok em 1440 e 390 px.')
} finally { await browser.close() }
