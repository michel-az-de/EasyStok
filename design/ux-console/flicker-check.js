/* Mede a piscada: captura frames a cada 60ms durante cliques em tabs/chips
   e reporta quanto da tela muda por frame (mudança grande = piscada). */
const { chromium } = require('playwright');
const { spawn } = require('child_process');

(async () => {
  const server = spawn(process.execPath, ['server.js', '--port', '7655'], { cwd: __dirname });
  await new Promise(r => setTimeout(r, 900));
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  await page.goto('http://127.0.0.1:7655/', { waitUntil: 'networkidle' });
  await page.waitForTimeout(800);

  async function flickerReport(label, action){
    await page.evaluate(() => new Promise(res => {
      window.__frames = [];
      let last = null;
      const t0 = performance.now();
      function sample(){
        const main = document.getElementById('main');
        /* métrica simples: quantos elementos visíveis com opacity < 1 (animando) + mutações */
        const animando = main ? main.querySelectorAll('*').length - main.querySelectorAll(':not([style])').length : 0;
        window.__frames.push({ t: performance.now() - t0, html: main ? main.innerHTML.length : 0 });
        if(performance.now() - t0 < 1200) requestAnimationFrame(sample);
        else res();
      }
      requestAnimationFrame(sample);
    }).then(() => action()).then(() => new Promise(r => setTimeout(r, 1300))).then(() => {
      const f = window.__frames;
      let mutations = 0;
      for(let i = 1; i < f.length; i++) if(Math.abs(f[i].html - f[i-1].html) > 50) mutations++;
      window.__result = { frames: f.length, mutations };
    }));
    const r = await page.evaluate(() => window.__result);
    console.log(`${label}: ${r.frames} frames, ${r.mutations} mutações grandes de DOM`);
  }

  await page.evaluate(() => openModule('operacao', 'pedidos'));
  await page.waitForTimeout(700);
  await flickerReport('troca de tab (Pedidos→Caixa)', () => setModuleTab('caixa'));
  await flickerReport('clique em chip (Extrato→Saldo)', () => { S.cashSeg = 'saldo'; rerender(); });
  await flickerReport('voltar pra Hoje', () => goHome());

  await browser.close();
  server.kill();
  process.exit(0);
})().catch(e => { console.error('FALHOU:', e.message); process.exit(1); });
