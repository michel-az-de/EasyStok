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

  test('#2063 fabricação: data local anterior preservada; futuro e data impossível rejeitados', () => {
    const result = run(`
      const now = new Date(2026,9,26,15,30).getTime();
      return JSON.stringify({ today:productionTimestamp('2026-10-26',now), now,
        before:localProductionDate(productionTimestamp('2026-10-25',now)),
        future:productionTimestamp('2026-10-27',now), invalid:productionTimestamp('2026-02-31',now) });
    `);
    assert.strictEqual(result.today, result.now);
    assert.strictEqual(result.before, '2026-10-25');
    assert.strictEqual(result.future, null);
    assert.strictEqual(result.invalid, null);
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
