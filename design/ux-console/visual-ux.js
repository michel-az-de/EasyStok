/* Captura visual REAL: Chromium headless, screenshots de todas as telas
   + sequência da fila com Dia ao vivo ligado. Saída em ./shots/ */
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');
const { spawn } = require('child_process');

const OUT = path.join(__dirname, 'shots');
fs.mkdirSync(OUT, { recursive: true });

(async () => {
  const server = spawn(process.execPath, ['server.js', '--port', '7654'], { cwd: __dirname });
  await new Promise(r => setTimeout(r, 900));
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  page.on('pageerror', e => console.log('PAGE ERROR:', e.message.split('\n')[0]));
  page.on('console', m => { if(m.type() === 'error') console.log('CONSOLE ERROR:', m.text().slice(0, 140)); });

  await page.goto('http://127.0.0.1:7654/', { waitUntil: 'networkidle' });
  await page.waitForTimeout(1200);

  const shot = async name => { await page.screenshot({ path: path.join(OUT, name + '.png') }); console.log('📸', name); };

  await shot('01-home');
  await page.evaluate(() => openModule('operacao', 'pedidos'));
  await page.waitForTimeout(700);
  await shot('02-pedidos');
  await page.evaluate(() => openDetail('pedido', window.S.orders[0].id));
  await page.waitForTimeout(700);
  await shot('03-pedido-detalhe');
  await page.evaluate(() => { closeDetail(); openModule('operacao', 'kds'); });
  await page.waitForTimeout(700);
  await shot('04-kds');
  await page.evaluate(() => openModule('operacao', 'novo-pedido'));
  await page.waitForTimeout(600);
  await page.evaluate(() => { pickClient('cli_m4k2'); });
  await page.waitForTimeout(300);
  await page.evaluate(() => { const i = document.getElementById('item-q'); i.value = 'frango'; i.dispatchEvent(new Event('input', { bubbles:true })); });
  await page.waitForTimeout(300);
  await shot('05-novo-pedido');
  await page.evaluate(() => openModule('operacao', 'caixa'));
  await page.waitForTimeout(700);
  await shot('06-caixa');
  await page.evaluate(() => { S.cashSeg = 'saldo'; rerender(); });
  await page.waitForTimeout(500);
  await shot('07-caixa-saldo');
  await page.evaluate(() => openModule('operacao', 'cardapio'));
  await page.waitForTimeout(700);
  await shot('08-cardapio');
  await page.evaluate(() => openModule('estoque', 'producao'));
  await page.waitForTimeout(700);
  await shot('09-producao');
  await page.evaluate(() => openModule('financeiro', 'visao'));
  await page.waitForTimeout(700);
  await shot('10-financeiro');
  await page.evaluate(() => openModule('clientes', 'atendimento'));
  await page.waitForTimeout(700);
  await shot('11-atendimento');
  await page.evaluate(() => openModule('clientes', 'crm-clientes'));
  await page.waitForTimeout(700);
  await shot('12-crm');
  await page.evaluate(() => openModule('clientes', 'avaliacoes'));
  await page.waitForTimeout(600);
  await shot('13-avaliacoes');
  await page.evaluate(() => openModule('gestao', 'dashboard'));
  await page.waitForTimeout(700);
  await shot('14-dashboard');
  await page.evaluate(() => openModule('gestao', 'integracoes'));
  await page.waitForTimeout(600);
  await shot('15-integracoes');
  await page.evaluate(() => openA11y());
  await page.waitForTimeout(600);
  await shot('16-a11y');
  await page.evaluate(() => closeDrawer());
  /* tema casa da babá */
  await page.evaluate(() => setTheme('casa'));
  await page.evaluate(() => goHome());
  await page.waitForTimeout(700);
  await shot('17-home-casa');
  await page.evaluate(() => setTheme('light'));
  /* simulação: frames da fila */
  await page.evaluate(() => openModule('operacao', 'pedidos'));
  await page.waitForTimeout(600);
  await page.evaluate(() => simPedido('iFood'));
  await shot('18-sim-entra');
  await page.waitForTimeout(250);
  await shot('19-sim-meio');
  await page.waitForTimeout(400);
  await shot('20-sim-fim');
  /* peek */
  await page.evaluate(() => peek('produto', 'prd_a1'));
  await page.waitForTimeout(500);
  await shot('21-peek-produto');
  await page.evaluate(() => closePeek());
  /* eventos */
  await page.evaluate(() => toggleEvents(true));
  await page.waitForTimeout(600);
  await shot('22-eventos');

  await browser.close();
  server.kill();
  console.log('\nfeito. shots em', OUT);
  process.exit(0);
})().catch(e => { console.error('FALHOU:', e.message); process.exit(1); });
