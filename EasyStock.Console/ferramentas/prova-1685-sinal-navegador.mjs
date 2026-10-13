// Componentes reais com ações controladas. Não movimenta dinheiro nem envia mensagens.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5750'
const temporarios = resolve(import.meta.dirname, '../.build/sinal-manual')
const evidencias = process.env.EVIDENCIAS_UX ?? '.build/sinal-manual'
mkdirSync(temporarios, { recursive: true })
mkdirSync(evidencias, { recursive: true })
writeFileSync(join(temporarios, 'index.html'), '<!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><div id="root"></div><script type="module" src="/.build/sinal-manual/index.jsx"></script></html>')
writeFileSync(join(temporarios, 'index.jsx'), `
import React from 'react'; import { createRoot } from 'react-dom/client';
import { ContextoEstado, ContextoAcoes, ContextoCatalogo } from '/src/aplicacao/contextos.js';
import { ContextoAcessoModulos } from '/src/aplicacao/acessoModulos.jsx';
import { BlocoCobranca } from '/src/features/ficha-cliente/BlocoCobranca.jsx';
import { BarraProximoPasso } from '/src/features/ficha-cliente/BarraProximoPasso.jsx';
import '/src/estilos/base.css';
const q = new URLSearchParams(location.search); const pago = Number(q.get('pago') || 0); const online = q.has('online'); const hoje = q.has('hoje');
const pedido = { pedidoId:'pedido-controlado', numero:'1', estado:pago >= 50 ? 'pago' : 'aguardando', statusApi:pago >= 50 ? 'aguardando' : 'aguardando_pagamento', sinalNaReserva:!hoje, totalApi:100, totalPagoApi:pago, alocadoApi:pago,
 itens:[{sku:'massa',qtd:1,nome:'Massa de teste',precoUnitario:100}], pagamentos:[], pagamentosApi:[], cobranca:{id:'cobranca-controlada',valor:online ? 50 : 100,meio:online ? 'pix' : 'maquininha',link:online ? 'https://example.test/pagamento' : null,expiraEm:online ? Date.now()+600000 : null}, };
window.recebimentosDaProva=[]; const confirmar = async (valor,metodo) => { window.recebimentosDaProva.push({valor,metodo}); return {}; };
const props={pedido,conversaId:'conversa-controlada',nomeCliente:'Cliente de teste',agora:Date.now(),editavel:true,aoConfirmarPagamento:confirmar};
createRoot(document.getElementById('root')).render(<ContextoAcessoModulos value={{permite:()=>true,acoes:{editarAtendimento:true}}}><ContextoEstado value={{fonteApi:true,fidelidade:{cupons:[]},conversas:[]}}><ContextoCatalogo value={{cardapio:[],entregadores:[]}}><ContextoAcoes value={{confirmarPagamento:(_id,valor,metodo)=>confirmar(valor,metodo)}}><main style={{maxWidth:420,margin:'24px auto',padding:16}}>{q.has('barra') ? <BarraProximoPasso {...props}/> : <BlocoCobranca {...props}/>}</main></ContextoAcoes></ContextoCatalogo></ContextoEstado></ContextoAcessoModulos>);
`)
const casos = [
  { nome:'sinal-inicial', query:'', valor:50, sinal:true, metodo:'pix' },
  { nome:'sinal-parcial', query:'pago=20', valor:30, sinal:true, metodo:'dinheiro' },
  { nome:'saldo-na-fila', query:'pago=50', valor:50, sinal:false, metodo:'credito' },
  { nome:'pedido-hoje', query:'hoje=1', valor:100, sinal:false, metodo:'debito' },
]
const falhas=[]; const browser=await chromium.launch({headless:true})
try {
  for (const width of [1440,390]) for (const barra of [false,true]) for (const caso of casos) {
    if (barra && caso.nome === 'saldo-na-fila') continue // O saldo é recebido pelo bloco Cobrança.
    const nome=caso.nome+'-'+(barra?'barra':'bloco')+'-'+width
    const rotulo=caso.sinal ? 'Registrar sinal' : 'Marcar pago'
    const context=await browser.newContext({viewport:{width,height:960},reducedMotion:'reduce'})
    await context.grantPermissions(['local-network-access'],{origin:origem})
    const page=await context.newPage(); page.setDefaultTimeout(8000)
    const erros=[]; page.on('pageerror',erro=>erros.push(erro.message))
    try {
      await page.goto(origem+'/.build/sinal-manual/index.html?'+caso.query+(barra?'&barra=1':''))
      await page.getByRole('button',{name:rotulo,exact:true}).waitFor()
      if (!barra && caso.sinal) await page.getByText(/Sinal a receber/).waitFor()
      if (!barra && caso.nome === 'saldo-na-fila') assert.match(await page.locator('main').innerText(), /Receber na entrega[\s\S]*R\$\s*50,00/)
      if (barra && caso.sinal) {
        await page.getByText('Próximo: Fila de preparo',{exact:true}).waitFor()
        assert.equal(await page.getByText('Próximo: Pago',{exact:true}).count(),0)
      }
      await page.screenshot({path:join(evidencias,nome+'.png'),fullPage:true})
      await page.getByRole('button',{name:rotulo,exact:true}).click()
      const campo=page.getByRole('textbox',{name:'Valor recebido',exact:true})
      await campo.waitFor(); assert.equal((await campo.inputValue()).replace(/\s/g,' '), 'R$ '+caso.valor.toFixed(2).replace('.',','))
      await page.getByRole('combobox',{name:'Como recebeu',exact:true}).selectOption(caso.metodo)
      await page.getByRole('button',{name:rotulo,exact:true}).click()
      assert.deepEqual(await page.evaluate(()=>window.recebimentosDaProva),[{valor:caso.valor,metodo:caso.metodo}])
      assert.deepEqual(erros,[])
      console.log('ok '+nome)
    } catch(erro) { await page.screenshot({path:join(evidencias,nome+'-falha.png'),fullPage:true}); falhas.push(nome+': '+erro.message.split('\n')[0]) }
    finally { await context.close() }
  }
} finally { await browser.close() }
assert.deepEqual(falhas,[],falhas.join('\n'))
console.log('OK: 14 jornadas nos dois formulários, desktop/celular e Pix/dinheiro/crédito/débito; ações controladas.')
