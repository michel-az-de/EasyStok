// Fluxos operacionais do piloto Android, sem equipamento físico ou envios externos.
'use strict';
module.exports = function ({ test, runInSandbox, assert }) {
  const run = body => JSON.parse(runInSandbox(`(function(){ ${body} })()`));

  test('#2063 produto: criar e usar abre confirmação sem criar lote; registrar uma vez gera etiquetas', () => {
    const result = run(`
      const old = { get: document.getElementById, products, batches, detail: readProdDetailValues };
      const elements = {};
      document.getElementById = id => elements[id] || (elements[id] = old.get.call(document, id));
      try {
        products = []; batches = [];
        openProductModal('massa');
        document.getElementById('newProdName').value = 'Produto piloto';
        document.getElementById('newProdUnit').value = '500g';
        document.getElementById('newProdPrice').value = '25,00';
        document.getElementById('newProdValidity').value = '12';
        saveNewProduct();
        const before = batches.length;
        const p = products[0];
        readProdDetailValues = () => ({ valid: true, perProduct: new Map([[p.id, {weightG:500, validityDays:p.defaultValidityDays, qty:3}]]) });
        confirmProduction(); confirmProduction();
        const batch = batches[0];
        return JSON.stringify({ before, batches: batches.length, validity:p.defaultValidityDays,
          stock:p.stock, labels:etiquetasFromBatch(batch).length, id:batch.id, labelId:etiquetasFromBatch(batch)[0].batchId });
      } finally { document.getElementById=old.get; products=old.products; batches=old.batches; readProdDetailValues=old.detail; }
    `);
    assert.strictEqual(result.before, 0);
    assert.strictEqual(result.batches, 1);
    assert.strictEqual(result.validity, 12);
    assert.strictEqual(result.stock, 3);
    assert.strictEqual(result.labels, 3);
    assert.strictEqual(result.labelId, result.id);
  });

  test('#2063 produto criado dentro do pedido continua adicionando ao pedido, sem abrir produção', () => {
    const result = run(`
      const old = { get:document.getElementById, products, add:addItemToOrder, open:openProductionConfirm };
      const elements = {}; let added = null, opened = 0;
      document.getElementById = id => elements[id] || (elements[id] = old.get.call(document,id));
      addItemToOrder = p => added = p.id; openProductionConfirm = () => opened++;
      try {
        products=[]; openProductModal('massa','Produto do pedido',true);
        document.getElementById('newProdValidity').value='7'; saveNewProduct();
        return JSON.stringify({added, id:products[0].id, opened, count:products[0].count});
      } finally { document.getElementById=old.get; products=old.products; addItemToOrder=old.add; openProductionConfirm=old.open; }
    `);
    assert.strictEqual(result.added, result.id);
    assert.strictEqual(result.opened, 0);
    assert.strictEqual(result.count, 0);
  });

  test('#2063 sprint: criar e usar no piloto registra uma vez e abre etiquetas, preservando outra seleção', () => {
    const result = run(`
      const old = {get:document.getElementById, products, batches, detail:readProdDetailValues, config:window.CDB_CONFIG, preview:showEtiquetasPreview};
      const elements = {}; let previews = 0;
      document.getElementById = id => elements[id] || (elements[id] = old.get.call(document,id));
      showEtiquetasPreview = () => previews++;
      try {
        window.CDB_CONFIG={isPilot:true};
        products=[{id:'outro',name:'Outro selecionado',count:3,stock:9,unit:'500g'}]; batches=[];
        readProdDetailValues=()=>({valid:true,perProduct:new Map(products.map(p=>[p.id,{weightG:500,validityDays:p.defaultValidityDays||7,qty:p.count}]))});
        openProductModal('massa');
        document.getElementById('newProdName').value='Produto direto';
        document.getElementById('newProdUnit').value='500g';
        document.getElementById('newProdQuantity').value='2';
        document.getElementById('newProdValidity').value='90';
        saveNewProduct(); saveNewProduct();
        const created=products.find(p=>p.id!=='outro');
        return JSON.stringify({products:products.length,batches:batches.length,previews,stock:created.stock,
          validity:created.defaultValidityDays,labels:etiquetasFromBatch(batches[0]).length,
          otherStock:products[0].stock,otherCount:products[0].count,batchItems:batches[0].items.length});
      } finally {document.getElementById=old.get;products=old.products;batches=old.batches;readProdDetailValues=old.detail;window.CDB_CONFIG=old.config;showEtiquetasPreview=old.preview;}
    `);
    assert.deepStrictEqual(result,{products:2,batches:1,previews:1,stock:2,validity:90,labels:2,otherStock:9,otherCount:3,batchItems:1});
  });

  test('#2063 sprint: falha ao salvar lote reverte armazenamento e permite repetir sem duplicar produção', () => {
    const result = run(`
      const old={get:document.getElementById,products,batches,detail:readProdDetailValues,config:window.CDB_CONFIG,
        preview:showEtiquetasPreview,set:localStorage.setItem,hook:window.cdbOnPersist,toast:showToast};
      const elements={}; let previews=0, syncs=0, errors=0;
      document.getElementById=id=>elements[id]||(elements[id]=old.get.call(document,id));
      const keys=['cdb-products','cdb-clients','cdb-orders','cdb-cash','cdb-cash-closings','cdb-batches','cdb-shopping'];
      const before=keys.map(k=>localStorage.getItem(k));
      try {
        products=[];batches=[];window.CDB_CONFIG={isPilot:true};
        window.cdbOnPersist=()=>syncs++;showEtiquetasPreview=()=>previews++;
        showToast=message=>{if(message.includes('Não foi possível salvar'))errors++;};
        readProdDetailValues=()=>({valid:true,perProduct:new Map(products.map(p=>[p.id,{weightG:500,validityDays:7,qty:p.count}]))});
        openProductModal('massa');document.getElementById('newProdName').value='Produto com retry';
        document.getElementById('newProdUnit').value='500g';document.getElementById('newProdQuantity').value='2';
        localStorage.setItem=(key,value)=>{if(key==='cdb-batches')throw new Error('QuotaExceededError');return old.set.call(localStorage,key,value);};
        saveNewProduct();
        const failed={products:products.length,batches:batches.length,previews,syncs,errors,unchanged:keys.every((k,i)=>localStorage.getItem(k)===before[i])};
        localStorage.setItem=old.set;saveNewProduct();
        return JSON.stringify({failed,retry:{products:products.length,batches:batches.length,stock:products[0].stock,previews,syncs}});
      } finally {
        document.getElementById=old.get;products=old.products;batches=old.batches;readProdDetailValues=old.detail;window.CDB_CONFIG=old.config;
        showEtiquetasPreview=old.preview;localStorage.setItem=old.set;window.cdbOnPersist=old.hook;showToast=old.toast;
        keys.forEach((key,i)=>before[i]===null?localStorage.removeItem(key):localStorage.setItem(key,before[i]));
      }
    `);
    assert.deepStrictEqual(result.failed,{products:0,batches:0,previews:0,syncs:0,errors:1,unchanged:true});
    assert.deepStrictEqual(result.retry,{products:1,batches:1,stock:2,previews:1,syncs:1});
  });

  test('#2063 sprint: dados inválidos do cadastro não criam produto nem lote', () => {
    const result=run(`
      const old={get:document.getElementById,products,batches,config:window.CDB_CONFIG,toast:showToast};
      const elements={};let errors=0;
      document.getElementById=id=>elements[id]||(elements[id]=old.get.call(document,id));
      try {
        products=[];batches=[];window.CDB_CONFIG={isPilot:true};showToast=()=>errors++;
        const cases=[['newProdQuantity','1.5'],['newProdManufacturedDate',''],['newProdWeight','0'],['newProdValidity','3.5']];
        for(const [field,value] of cases){
          openProductModal('massa');document.getElementById('newProdName').value='Não deve ser criado';
          document.getElementById('newProdUnit').value='500g';document.getElementById(field).value=value;saveNewProduct();
        }
        return JSON.stringify({products:products.length,batches:batches.length,errors});
      } finally {document.getElementById=old.get;products=old.products;batches=old.batches;window.CDB_CONFIG=old.config;showToast=old.toast;}
    `);
    assert.deepStrictEqual(result,{products:0,batches:0,errors:4});
  });

  test('#2063 sprint: pedido sem persistência não abre cobrança nem apaga o rascunho', () => {
    const result=run(`
      const old={config:window.CDB_CONFIG,cobranca:openCobranca,orders,client:selectedClient,items:currentOrderItems,set:localStorage.setItem,hook:window.cdbOnPersist};
      let opened=0,syncs=0;openCobranca=()=>opened++;window.cdbOnPersist=()=>syncs++;
      try {
        orders=[];window.CDB_CONFIG={isPilot:true};selectedClient='avulso';
        currentOrderItems=[{productId:'teste',name:'Pedido local',qty:1,unitPrice:10}];
        localStorage.setItem=(key,value)=>{if(key==='cdb-orders')throw new Error('QuotaExceededError');return old.set.call(localStorage,key,value);};
        saveOrder();return JSON.stringify({opened,syncs,orders:orders.length,draft:currentOrderItems.length});
      } finally {window.CDB_CONFIG=old.config;openCobranca=old.cobranca;orders=old.orders;selectedClient=old.client;currentOrderItems=old.items;localStorage.setItem=old.set;window.cdbOnPersist=old.hook;}
    `);
    assert.deepStrictEqual(result,{opened:0,syncs:0,orders:0,draft:1});
  });

  test('#2063 sprint: resumo sem PIX não inventa cobrança e marca configurada prevalece no piloto', () => {
    const result=run(`
      const keys=['cdb-pix-key','cdb-pix-holder','cdb-pix-type','cdb-empresa-name'];
      const old=keys.map(k=>localStorage.getItem(k)), config=window.CDB_CONFIG;
      try {
        keys.forEach(k=>localStorage.removeItem(k));window.CDB_CONFIG={isPilot:true};
        const order={id:'pedido-local',clientSnapshot:{name:'Cliente local'},items:[],total:34};
        const without=buildCobrancaMessage(order,'cobranca');
        localStorage.setItem('cdb-pix-key','11999999999');localStorage.setItem('cdb-pix-holder','Titular configurado');
        localStorage.setItem('cdb-empresa-name','Nome configurado');
        const configured=buildCobrancaMessage(order,'cobranca');
        const label=etiquetaProducaoHtml({empresa:getEmpresaName(),productName:'Item',productionDate:Date.now(),codigoBarras:'ABC',lote:'LOT-1'});
        return JSON.stringify({fallback:without.includes('Casa da Baba'),hasEmptyPix:without.includes('Pagamento via PIX'),
          asksPayment:without.includes('confirme o pagamento'),configured:configured.includes('Nome configurado')&&configured.includes('Pagamento via PIX'),
          labelUsesCompany:label.includes('Nome configurado'),labelUsesErpLogo:label.includes('logo-easystok')});
      } finally {window.CDB_CONFIG=config;keys.forEach((k,i)=>old[i]===null?localStorage.removeItem(k):localStorage.setItem(k,old[i]));}
    `);
    assert.deepStrictEqual(result,{fallback:true,hasEmptyPix:false,asksPayment:false,configured:true,labelUsesCompany:true,labelUsesErpLogo:false});
  });

  test('#2063 fabricação: data local anterior preservada; futuro e data impossível rejeitados', () => {
    const result = run(`
      const now = new Date(2026,9,26,15,30).getTime();
      return JSON.stringify({ today:productionTimestamp('2026-10-26',now), now,
        before:localProductionDate(productionTimestamp('2026-10-25',now)),
        future:productionTimestamp('2026-10-27',now), invalid:productionTimestamp('2026-02-31',now), blank:productionTimestamp('',now) });
    `);
    assert.strictEqual(result.today, result.now);
    assert.strictEqual(result.before, '2026-10-25');
    assert.strictEqual(result.future, null);
    assert.strictEqual(result.invalid, null);
    assert.strictEqual(result.blank, null);
  });

  test('#2063 impressoras: seleção preserva as duas e Oasis permanece indisponível para envio', () => {
    const result = run(`
      const keys=['cdb-printer','cdb-printers']; const old=keys.map(k=>localStorage.getItem(k));
      try {
        keys.forEach(k=>localStorage.removeItem(k));
        setSavedPrinter({name:'Atual',address:'A',profile:'escpos'});
        setSavedPrinter({name:'Oasis',address:'B',profile:'oasis-pending'});
        const pending = !printerReady(getSavedPrinter());
        setSavedPrinter(getPrinterProfiles().find(p=>p.address==='A'));
        return JSON.stringify({total:getPrinterProfiles().length,pending,selected:getSavedPrinter().address});
      } finally { keys.forEach((k,i)=>old[i]===null?localStorage.removeItem(k):localStorage.setItem(k,old[i])); }
    `);
    assert.deepStrictEqual(result,{total:2,pending:true,selected:'A'});
  });

  test('#2063 impressão: log usa lote e impressora capturados, somente após envio; falha e teste não registram etiqueta', () => {
    const result = run(`
      const old = {bs:window.bluetoothSerial, permission:ensureBluetoothPermission, error:showError,
        printer:localStorage.getItem('cdb-printer'), logs:localStorage.getItem(AUDIT_LOG_KEY)};
      let succeed, fail, address, disconnected=0;
      ensureBluetoothPermission = fn => fn(true); showError=()=>{};
      window.bluetoothSerial={disconnect:ok=>{disconnected++;ok();},connect:(addr,ok)=>{address=addr;ok();},write:(bytes,ok,err)=>{succeed=ok;fail=err;}};
      localStorage.setItem('cdb-printer',JSON.stringify({name:'Atual',address:'A',profile:'escpos'}));
      localStorage.setItem(AUDIT_LOG_KEY,'[]');
      try {
        const job=etiquetaPrintJob([{batchId:'lote-real'}],'producao',2);
        printViaBluetooth(new Uint8Array([1]),null,job);
        const before=loadAuditLog().filter(e=>e.action==='etiqueta.print').length;
        localStorage.setItem('cdb-printer',JSON.stringify({name:'Outra',address:'B',profile:'escpos'}));
        succeed();
        const logged=loadAuditLog().find(e=>e.action==='etiqueta.print');
        printViaBluetooth(new Uint8Array([2]),null,job); fail('papel ausente');
        printViaBluetooth(new Uint8Array([3]),null); succeed();
        const count=loadAuditLog().filter(e=>e.action==='etiqueta.print').length;
        localStorage.setItem('cdb-printer',JSON.stringify({name:'Oasis',address:'C',profile:'oasis-pending'}));
        const prior=disconnected; printViaBluetooth(new Uint8Array([4]),null,job);
        return JSON.stringify({before,count,id:logged.entityId,printer:logged.diff.printer.address,copies:logged.diff.copies,address,blocked:disconnected===prior});
      } finally {
        releasePrinting(); window.bluetoothSerial=old.bs; ensureBluetoothPermission=old.permission; showError=old.error;
        if(old.printer===null)localStorage.removeItem('cdb-printer');else localStorage.setItem('cdb-printer',old.printer);
        if(old.logs===null)localStorage.removeItem(AUDIT_LOG_KEY);else localStorage.setItem(AUDIT_LOG_KEY,old.logs);
      }
    `);
    assert.deepStrictEqual(result,{before:0,count:1,id:'lote-real',printer:'A',copies:2,address:'B',blocked:true});
  });

  test('#2063 piloto: pedido salvo abre cobrança; PWA normal conserva a ação manual', () => {
    const result = run(`
      const old={config:window.CDB_CONFIG,cobranca:openCobranca,orders,client:selectedClient,items:currentOrderItems};
      const opened=[]; openCobranca=id=>opened.push(id);
      try {
        orders=[];window.CDB_CONFIG={isPilot:true};selectedClient='avulso';
        currentOrderItems=[{productId:'piloto',name:'Item piloto',qty:1,unitPrice:10}];saveOrder();
        const first=orders[0].id;
        window.CDB_CONFIG={isPilot:false};selectedClient='avulso';
        currentOrderItems=[{productId:'piloto',name:'Item piloto',qty:1,unitPrice:10}];saveOrder();
        return JSON.stringify({opened,first,total:orders.length});
      } finally {window.CDB_CONFIG=old.config;openCobranca=old.cobranca;orders=old.orders;selectedClient=old.client;currentOrderItems=old.items;}
    `);
    assert.deepStrictEqual(result.opened,[result.first]);
    assert.strictEqual(result.total,2);
  });
};
