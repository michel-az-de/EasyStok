/* Auditoria: navega por TODAS as tabs de TODOS os contextos e reporta
   degradação visível: undefined, NaN, [object Object], lista vazia, texto quebrado. */
const fs = require('fs');
const { JSDOM } = require('jsdom');
const html = fs.readFileSync(__dirname + '/index.html', 'utf8');
const dom = new JSDOM(html, { runScripts:'dangerously', pretendToBeVisual:true, url:'http://localhost/' });
const { window } = dom;
window.matchMedia = window.matchMedia || (q => ({ matches:false }));
window.HTMLElement.prototype.scrollIntoView = () => {};
const { document } = window;

const MODULES = window.eval('MODULES');
const issues = [];
for(const m of MODULES){
  for(const [tab] of m.tabs){
    try{
      window.openModule(m.id, tab);
      const t = document.getElementById('main').textContent;
      const len = t.replace(/\s/g, '').length;
      if(len < 30) issues.push(`${m.id}/${tab}: quase vazio (${len} chars)`);
      for(const bad of ['undefined','NaN','[object Object]']){
        if(t.includes(bad)) issues.push(`${m.id}/${tab}: contém "${bad}"`);
      }
    }catch(e){ issues.push(`${m.id}/${tab}: ERRO ${e.message}`); }
  }
}
/* detalhes e overlays */
try{ window.openModule('operacao','pedidos'); window.openDetail('pedido', window.S.orders[0].id);
  const t = document.getElementById('main').textContent;
  for(const bad of ['undefined','NaN','[object Object]']) if(t.includes(bad)) issues.push(`detalhe pedido: "${bad}"`);
}catch(e){ issues.push('detalhe pedido: ERRO ' + e.message); }

/* CSS morto: classes definidas que nenhum HTML/JS referencia mais */
const cssText = [...document.querySelectorAll('style')].map(s => s.textContent).join('\n');
const jsHtml = html;
const definedClasses = [...new Set([...cssText.matchAll(/\.([a-z][a-z0-9-]+)\s*[{.:\[,\s]/g)].map(m => m[1]))];
const dead = definedClasses.filter(c => {
  const re = new RegExp(`class=["'][^"']*\\b${c}\\b|classList\\.\\w+\\('${c}'|class="${c}"`, 'm');
  return !re.test(jsHtml.replace(cssText, '')) && !jsHtml.includes(`'${c}'`) && !jsHtml.includes(`"${c}"`);
});
console.log('═══ PROBLEMAS DE CONTEÚDO ═══');
issues.length ? issues.forEach(i => console.log('•', i)) : console.log('nenhum');
console.log('\n═══ CSS POSSIVELMENTE MORTO ═══');
dead.forEach(c => console.log('•', c));
process.exit(0);
