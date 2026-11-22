// #3156: Chromium e componentes reais (FaixaApi + AvisosDoDispositivo). A 390 só uma faixa por vez (a da loja
// desligada na frente; "Notificações bloqueadas" cede), "Fechar aviso" mede ao menos 44x44, e fechado ele não volta ao
// recarregar enquanto o estado da loja não muda. Alturas das faixas medidas a 1440 e a 390.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/3156/faixas-pe')
mkdirSync(pasta, { recursive: true })
writeFileSync(resolve(pasta, 'index.html'), '<html lang="pt-BR"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>html,body,#root{height:100%;margin:0}</style><div id="root"></div><script type="module" src="./cena.jsx"></script></html>')
writeFileSync(resolve(pasta, 'cena.jsx'), `
import React,{useState} from 'react'; import {createRoot} from 'react-dom/client';
import {ContextoEstado,ContextoAcoes,ContextoCatalogo} from '../../../src/aplicacao/contextos';
import {ContextoAcessoModulos} from '../../../src/aplicacao/acessoModulos';
import {FaixaApi} from '../../../src/app/FaixaApi';
import {AvisosDoDispositivo} from '../../../src/app/AvisosDoDispositivo';
import '../../../src/estilos/base.css';
const busca=new URLSearchParams(location.search);
const AVISO=busca.get('aviso')??'A loja online está desligada. Ligue a loja em Configurações para receber pedidos.';
function Cena(){
  const [avisoFixo,definir]=useState(busca.get('aviso')==='nenhum'?null:AVISO);
  const acoes={fecharAvisoApi:({fixo}={})=>{if(fixo)definir(null)}};
  const estado={fonteApi:true,sessao:{persistente:true},agora:Date.now(),sincronizacao:{estado:'ok',avisoFixo},som:false,audioBloqueado:false,permissaoNotificacao:'denied'};
  const catalogo=busca.get('cardapio')==='lido'?{cardapioLido:true,cardapioFalha:null}:{cardapioLido:false};
  return <ContextoAcessoModulos.Provider value={{acoes:{},permite:()=>false}}><ContextoEstado.Provider value={estado}><ContextoAcoes.Provider value={acoes}><ContextoCatalogo.Provider value={catalogo}>
    <AvisosDoDispositivo><div data-conteudo-console style={{height:'100%'}}>tela<FaixaApi/></div></AvisosDoDispositivo>
  </ContextoCatalogo.Provider></ContextoAcoes.Provider></ContextoEstado.Provider></ContextoAcessoModulos.Provider>
}
createRoot(document.getElementById('root')).render(<Cena/>);
`)
const erros = []
const browser = await chromium.launch({ headless: true })
const base = `${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/.build/3156/faixas-pe/index.html`
const medidas = {}
try {
  for (const largura of [1440, 390]) {
    // Contexto novo por largura: o localStorage é do aparelho e precisa nascer limpo.
    const contexto = await browser.newContext({ viewport: { width: largura, height: largura === 390 ? 844 : 900 }, locale: 'pt-BR', reducedMotion: 'reduce', hasTouch: largura === 390 })
    const pagina = await contexto.newPage()
    pagina.setDefaultTimeout(10000)
    pagina.on('pageerror', e => erros.push(e.message))
    const faixaDaLoja = pagina.locator('output', { hasText: 'loja online' })
    const aparelho = pagina.locator('[role="status"]', { hasText: 'Notificações bloqueadas' })
    const altura = async (alvo) => (await alvo.count() && await alvo.first().isVisible()) ? Math.round((await alvo.first().boundingBox()).height) : 0
    await pagina.goto(base)
    await faixaDaLoja.waitFor()
    await aparelho.waitFor({ state: 'attached' })
    const faixaH = await altura(faixaDaLoja)
    // A faixa de notificações é a div pai do status (ícone, rótulo e Dispensar).
    const aparelhoH = await altura(aparelho.locator('xpath=..'))
    medidas[largura] = { faixaDaLoja: faixaH, notificacoes: aparelhoH, total: faixaH + aparelhoH }
    if (largura === 390) assert.ok(faixaH > 0 && aparelhoH === 0, `A 390 só uma faixa por vez: loja ${faixaH} px, notificações ${aparelhoH} px`)
    else assert.ok(faixaH > 0 && aparelhoH > 0, 'No desktop as duas cabem e seguem à vista')
    const fechar = pagina.getByRole('button', { name: 'Fechar aviso', exact: true })
    const caixa = await fechar.boundingBox()
    assert.ok(caixa.width >= 44 && caixa.height >= 44, `Fechar aviso mede ${Math.round(caixa.width)}x${Math.round(caixa.height)} px (mínimo 44x44)`)
    medidas[largura].fecharAviso = `${Math.round(caixa.width)}x${Math.round(caixa.height)}`
    await fechar.click()
    await faixaDaLoja.waitFor({ state: 'detached' })
    // Recarregar: o aviso da mesma loja desligada não volta; a faixa de notificações aparece no lugar.
    await pagina.goto(base)
    await aparelho.waitFor()
    assert.equal(await faixaDaLoja.count(), 0, 'Fechado, o aviso não volta ao recarregar')
    if (largura === 390) medidas[largura].depoisDeFechar = await altura(aparelho.locator('xpath=..'))
    // O estado da loja mudou (outro texto): o aviso novo aparece.
    await pagina.goto(`${base}?aviso=${encodeURIComponent('Cardápio: não carregou.')}`)
    await pagina.locator('output', { hasText: 'Cardápio: não carregou.' }).waitFor()
    // A loja voltou (cardápio lido, sem aviso): a dispensa sai; se desligar de novo, o aviso aparece.
    await pagina.goto(`${base}?aviso=nenhum&cardapio=lido`)
    await aparelho.waitFor()
    await pagina.goto(base)
    await faixaDaLoja.waitFor()
    await contexto.close()
  }
  assert.deepEqual(erros, [], 'Sem erro de página')
  console.log('prova-3156-faixas-pe-navegador: ok', JSON.stringify(medidas))
} finally { await browser.close() }
