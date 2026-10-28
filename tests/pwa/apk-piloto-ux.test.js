'use strict';
module.exports = function ({ test, runInSandbox, assert }) {
  const run = body => JSON.parse(runInSandbox('(function(){ ' + body + ' })()'));
  test('pedido: salvar tem ação explícita em todos os estados e preserva validação', () => {
    const result = run(`
      const old={client:selectedClient,items:currentOrderItems,get:document.getElementById};
      const elements={};document.getElementById=id=>elements[id]||(elements[id]=old.get.call(document,id));
      const states=[];const btn=document.getElementById('saveOrderBtn'),notes=document.getElementById('orderNotes');
      const capture=()=>{updateSaveOrderBtn();states.push({text:btn.textContent,disabled:btn.disabled});};
      try {
        selectedClient=null;currentOrderItems=[{name:'Item',qty:1,unitPrice:34}];notes.value='';capture();
        selectedClient={id:'fixture',name:'Cliente'};currentOrderItems=[];capture();
        notes.value='Encomenda';capture();
        notes.value='';currentOrderItems=[{name:'Item',qty:1,unitPrice:34}];capture();
        currentOrderItems=[{name:'Item',qty:1,unitPrice:0}];capture();
        return JSON.stringify(states);
      } finally {selectedClient=old.client;currentOrderItems=old.items;document.getElementById=old.get;}
    `);
    assert.deepStrictEqual(result, [true, true, false, false, false].map(disabled => ({ text: 'Salvar pedido', disabled })));
  });
  test('impressora: recusa orienta pelo nome do APK e mantém erro técnico recolhido', () => {
    const result = run(`
      const old={get:document.getElementById,bs:window.bluetoothSerial,permission:ensureBluetoothPermission,config:window.CDB_CONFIG};
      const nodes={printerPickerList:{innerHTML:''},printerPickerHint:{innerHTML:''}};let listings=0;
      try {
        document.getElementById=id=>nodes[id]||old.get.call(document,id);
        window.CDB_CONFIG={isPilot:true};window.bluetoothSerial={list:()=>listings++};
        ensureBluetoothPermission=cb=>cb(false);loadPrinterDevices();
        return JSON.stringify({html:nodes.printerPickerHint.innerHTML,listings});
      } finally {document.getElementById=old.get;window.bluetoothSerial=old.bs;ensureBluetoothPermission=old.permission;window.CDB_CONFIG=old.config;}
    `);
    assert.equal(result.listings, 0);
    const visible = result.html.split('<details>')[0];
    assert.match(visible, /Casa da Baba Piloto/);
    assert.match(visible, /Permita usar dispositivos próximos/);
    assert.match(visible, /Buscar de novo/);
    assert.doesNotMatch(visible, /BLUETOOTH_CONNECT|\[permission\]|Easy Stock Mobile/);
    assert.match(result.html, /<details><summary>Detalhes técnicos<\/summary>/);
    assert.match(result.html, /BLUETOOTH_CONNECT/);
  });
  test('impressora: falha de listagem escapa detalhe e permite tentar novamente', () => {
    const result = run(`
      const old={get:document.getElementById,bs:window.bluetoothSerial,permission:ensureBluetoothPermission,config:window.CDB_CONFIG};
      const nodes={printerPickerList:{innerHTML:''},printerPickerHint:{innerHTML:''}};let listings=0;
      try {
        document.getElementById=id=>nodes[id]||old.get.call(document,id);
        window.CDB_CONFIG={isPilot:true};window.bluetoothSerial={list:(ok,fail)=>{listings++;fail('<img src=x onerror=alert(1)>');}};
        ensureBluetoothPermission=cb=>cb(true);loadPrinterDevices();loadPrinterDevices();
        return JSON.stringify({html:nodes.printerPickerHint.innerHTML,listings});
      } finally {document.getElementById=old.get;window.bluetoothSerial=old.bs;ensureBluetoothPermission=old.permission;window.CDB_CONFIG=old.config;}
    `);
    assert.equal(result.listings, 2);
    assert.match(result.html, /Não foi possível buscar as impressoras/);
    assert.doesNotMatch(result.html, /<img/);
    assert.match(result.html, /&lt;img/);
    assert.match(result.html, /<details><summary>Detalhes técnicos<\/summary>/);
  });
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
