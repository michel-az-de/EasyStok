'use strict';
// #2886: card de produção acessível. "+", "-" e Foto levam o nome do produto, o foco volta ao
// mesmo botão depois do redesenho da grade e quantidade e toast passam pela região viva única.
module.exports = function ({ test, runInSandbox, assert }) {
  const run = body => JSON.parse(runInSandbox('(function(){ ' + body + ' })()'));

  test('#2886 card: "+", "-" e Foto têm o nome do produto no rótulo', () => {
    const html = run(`return JSON.stringify(productTileHtml({id:'p1',name:'Nhoque <batata>',unit:'kg',count:2,stock:5,photo:''}));`);
    assert.match(html, /aria-label="Diminuir Nhoque &lt;batata&gt;"/);
    assert.match(html, /aria-label="Aumentar Nhoque &lt;batata&gt;"/);
    assert.match(html, /aria-label="Foto de Nhoque &lt;batata&gt;"/);
    const withPhoto = run(`return JSON.stringify(productTileHtml({id:'p1',name:'Nhoque',count:1,stock:0,photo:'data:x'}));`);
    assert.match(withPhoto, /aria-label="Trocar foto de Nhoque"/);
  });

  test('#2886 incProduct/decProduct devolvem o foco ao mesmo botão e anunciam a quantidade', () => {
    const result = run(`
      const old={qsa:document.querySelectorAll,get:document.getElementById,active:Object.getOwnPropertyDescriptor(document,'activeElement'),render:renderProductGrid,products};
      const focused=[];const live={textContent:''};
      const button=kind=>({kind,disabled:false,focus(){focused.push(kind);}});
      const tile={dataset:{productId:'p1'},buttons:{plus:button('plus'),minus:button('minus')},querySelector(sel){return sel.endsWith('.plus')?this.buttons.plus:this.buttons.minus;}};
      try {
        products=[{id:'p1',name:'Nhoque',count:0}];
        renderProductGrid=()=>{};
        Object.defineProperty(document,'activeElement',{configurable:true,get:()=>({closest:s=>s==='#productGrid'?{}:null})});
        document.querySelectorAll=sel=>sel==='#productGrid .product-tile'?[tile]:[];
        document.getElementById=id=>id==='srStatus'?live:old.get.call(document,id);
        incProduct('p1');const afterInc=live.textContent;
        decProduct('p1');const afterDec=live.textContent;
        tile.buttons.minus.disabled=true;products[0].count=1;decProduct('p1');
        return JSON.stringify({focused,afterInc,afterDec});
      } finally {
        document.querySelectorAll=old.qsa;document.getElementById=old.get;renderProductGrid=old.render;products=old.products;
        if(old.active)Object.defineProperty(document,'activeElement',old.active);else delete document.activeElement;
      }
    `);
    assert.deepStrictEqual(result.focused, ['plus', 'minus', 'plus'], 'foco volta ao botão; "-" desativado cai no "+"');
    assert.strictEqual(result.afterInc, 'Nhoque: 1');
    assert.strictEqual(result.afterDec.replace(/ /g, ''), 'Nhoque: 0');
  });

  test('#2886 incProduct não rouba o foco quando ele estava fora da grade', () => {
    const result = run(`
      const old={qsa:document.querySelectorAll,active:Object.getOwnPropertyDescriptor(document,'activeElement'),render:renderProductGrid,products};
      let focused=0;const tile={dataset:{productId:'p1'},querySelector:()=>({disabled:false,focus(){focused++;}})};
      try {
        products=[{id:'p1',name:'Nhoque',count:0}];renderProductGrid=()=>{};
        Object.defineProperty(document,'activeElement',{configurable:true,get:()=>({closest:()=>null})});
        document.querySelectorAll=()=>[tile];
        incProduct('p1');
        return JSON.stringify({focused});
      } finally {
        document.querySelectorAll=old.qsa;renderProductGrid=old.render;products=old.products;
        if(old.active)Object.defineProperty(document,'activeElement',old.active);else delete document.activeElement;
      }
    `);
    assert.strictEqual(result.focused, 0);
  });

  test('#2886 showToast também fala pela região viva, inclusive repetindo a mesma mensagem', () => {
    const result = run(`
      const old=document.getElementById;const nodes={toast:{classList:{add(){},remove(){}}},toastMsg:{textContent:''},srStatus:{textContent:''}};
      try {
        document.getElementById=id=>nodes[id]||old.call(document,id);
        showToast('Produção registrada');const first=nodes.srStatus.textContent;
        showToast('Produção registrada');const second=nodes.srStatus.textContent;
        return JSON.stringify({first,second});
      } finally { document.getElementById=old; }
    `);
    assert.strictEqual(result.first, 'Produção registrada');
    assert.notStrictEqual(result.second, result.first, 'texto muda para o leitor repetir');
    assert.strictEqual(result.second.replace(/ /g, ''), 'Produção registrada');
  });
};
