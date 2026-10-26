'use strict';
module.exports = function ({ test, runInSandbox, assert }) {
  const run = body => JSON.parse(runInSandbox('(function(){ ' + body + ' })()'));
  test('#2069 cliente: abrir foca nome e cancelar devolve foco à ação de origem', () => {
    const result = run(`
      const old = { get: document.getElementById, query: document.querySelector, active: document.activeElement };
      const elements = {}, focused = [];
      document.getElementById = id => {
        if (!elements[id]) { elements[id] = old.get.call(document, id); elements[id].focus = () => focused.push(id); }
        return elements[id];
      };
      document.querySelector = () => null;
      document.activeElement = {isConnected:true,offsetParent:{},focus:()=>focused.push('origem')};
      try { openClientModal('Cliente local'); closeClientModal(); return JSON.stringify(focused); }
      finally {document.getElementById=old.get;document.querySelector=old.query;document.activeElement=old.active;}
    `);
    assert.deepStrictEqual(result, ['newClientName', 'origem']);
  });
  test('#2069 impressora: diálogo recebe foco e cancelar volta à impressão da prévia aberta', () => {
    const result = run(`
      const old = {get:document.getElementById,query:document.querySelector,active:document.activeElement};
      const focused=[], modal={classList:{add(){},remove(){}},querySelector:()=>({focus:()=>focused.push('configurar')})};
      document.getElementById=id=>id==='noPrinterModal'?modal:old.get.call(document,id);
      document.querySelector=()=>({id:'etiquetasPreview'});
      document.activeElement={isConnected:true,offsetParent:{},focus:()=>focused.push('imprimir')};
      try {openNoPrinterModal();closeNoPrinterModal();return JSON.stringify(focused);}
      finally {document.getElementById=old.get;document.querySelector=old.query;document.activeElement=old.active;}
    `);
    assert.deepStrictEqual(result, ['configurar','imprimir']);
  });
  test('#2069 referência local do pedido é a mesma na cobrança, no canhoto e na busca', () => {
    const result = run(`
      const old=orders;
      try {
        const order={id:'fixture-53453900',total:10,clientSnapshot:{name:'Cliente'},items:[]};
        orders=[order];
        return JSON.stringify({card:renderKanbanCard(order,{key:'aguardando',label:'Aguardando'}),message:buildCobrancaMessage(order,'cobranca'),receipt:buildEtiqueta(order),found:findOrderByShortCode('53453900')?.id});
      } finally {orders=old;}
    `);
    assert.match(result.card, /Pedido 53453900/);
    assert.match(result.message, /Pedido 53453900/);
    assert.match(result.receipt, /#53453900/);
    assert.equal(result.found,'fixture-53453900');
  });
  test('#2069 resumo do pedido recebe foco e ao fechar retorna à ação de origem', () => {
    const result=run(`
      const old={get:document.getElementById,query:document.querySelector,active:document.activeElement,orders};
      const elements={},focused=[];
      document.getElementById=id=>{if(!elements[id]){elements[id]=old.get.call(document,id);elements[id].focus=()=>focused.push(id);}return elements[id];};
      document.querySelector=()=>null;
      document.activeElement={isConnected:true,offsetParent:{},focus:()=>focused.push('origem')};
      orders=[{id:'ux-cobranca-53453900',status:STATUS.AGUARDANDO,total:34,items:[],clientSnapshot:{name:'Cliente'}}];
      try {openCobranca(orders[0].id);closeCobranca();return JSON.stringify(focused);}
      finally {document.getElementById=old.get;document.querySelector=old.query;document.activeElement=old.active;orders=old.orders;}
    `);
    assert.deepStrictEqual(result,['cobrancaTitle','origem']);
  });
  test('#2069 cópia recusada pelo navegador nunca informa sucesso', () => {
    const result=run(`
      const old={create:document.createElement,append:document.body.appendChild,remove:document.body.removeChild,exec:document.execCommand,toast:showToast};
      const messages=[];
      document.createElement=()=>({value:'',style:{},select(){}});
      document.body.appendChild=()=>{};document.body.removeChild=()=>{};
      document.execCommand=()=>false;showToast=message=>messages.push(message);
      try {fallbackCopy('Resumo local','Mensagem copiada');return JSON.stringify(messages);}
      finally {document.createElement=old.create;document.body.appendChild=old.append;document.body.removeChild=old.remove;document.execCommand=old.exec;showToast=old.toast;}
    `);
    assert.equal(result.length,1);
    assert.doesNotMatch(result[0],/copiada/i);
    assert.match(result[0],/Não consegui copiar/);
  });

};
