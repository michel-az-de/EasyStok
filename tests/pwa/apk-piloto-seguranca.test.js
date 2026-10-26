'use strict';

module.exports = function ({ test, runInSandbox, sandbox, assert }) {
  const run = body => JSON.parse(runInSandbox(`(function(){ ${body} })()`));

  test('#2063 impressão: duplo toque durante permissão gera somente um envio', () => {
    const result = run(`
      const old={bs:window.bluetoothSerial,permission:ensureBluetoothPermission,printer:getSavedPrinter};
      const permissions=[];let writes=0;
      try {
        releasePrinting(); getSavedPrinter=()=>({name:'Fixture',address:'A',profile:'escpos'});
        ensureBluetoothPermission=cb=>permissions.push(cb);
        window.bluetoothSerial={disconnect:ok=>ok(),connect:(addr,ok)=>ok(),write:(bytes,ok)=>{writes++;ok();}};
        printViaBluetooth([1]);printViaBluetooth([1]);
        permissions.forEach(cb=>cb(true));
        return JSON.stringify({permissions:permissions.length,writes});
      } finally {releasePrinting();window.bluetoothSerial=old.bs;ensureBluetoothPermission=old.permission;getSavedPrinter=old.printer;}
    `);
    assert.deepStrictEqual(result, { permissions: 1, writes: 1 });
  });

  test('#2063 impressão: callback de envio expirado não registra sucesso nem libera próximo trabalho', () => {
    const result = run(`
      const old={bs:window.bluetoothSerial,permission:ensureBluetoothPermission,printer:getSavedPrinter,
        timeout:setTimeout,clear:clearTimeout,log:logAction};
      const writes=[],timers=[],logs=[];
      try {
        releasePrinting();getSavedPrinter=()=>({name:'Fixture',address:'A',profile:'escpos'});
        ensureBluetoothPermission=cb=>cb(true);setTimeout=fn=>{timers.push(fn);return timers.length;};clearTimeout=id=>{timers[id-1]=null;};
        logAction=entry=>logs.push(entry);
        window.bluetoothSerial={disconnect:ok=>ok(),connect:(addr,ok)=>ok(),write:(bytes,ok,fail)=>writes.push({ok,fail})};
        const job={entityId:'fixture',entityType:'batch',quantity:1,copies:1};
        printViaBluetooth([1],null,job);timers.find(Boolean)();
        printViaBluetooth([2],null,job);writes[0].ok();writes[0].fail('late failure');
        const staleLogs=logs.length;printViaBluetooth([3],null,job);const whileBusy=writes.length;
        writes[1].ok();printViaBluetooth([4],null,job);
        return JSON.stringify({staleLogs,whileBusy,logs:logs.length,writes:writes.length});
      } finally {
        releasePrinting();window.bluetoothSerial=old.bs;ensureBluetoothPermission=old.permission;getSavedPrinter=old.printer;
        setTimeout=old.timeout;clearTimeout=old.clear;logAction=old.log;
      }
    `);
    assert.deepStrictEqual(result, { staleLogs: 0, whileBusy: 2, logs: 1, writes: 3 });
  });

  test('#2063 impressão: permissão sem resposta expira e callback tardio não envia', () => {
    const result = run(`
      const old={bs:window.bluetoothSerial,permission:ensureBluetoothPermission,printer:getSavedPrinter,timeout:setTimeout,clear:clearTimeout};
      let permission,expire,writes=0;
      try {
        releasePrinting();getSavedPrinter=()=>({name:'Fixture',address:'A',profile:'escpos'});
        ensureBluetoothPermission=cb=>{permission=cb;};setTimeout=fn=>{expire=fn;return 1;};clearTimeout=()=>{};
        window.bluetoothSerial={disconnect:ok=>ok(),connect:(addr,ok)=>ok(),write:(bytes,ok)=>{writes++;ok();}};
        printViaBluetooth([1]);expire();permission(true);
        return JSON.stringify({writes,busy:isPrinting});
      } finally {releasePrinting();window.bluetoothSerial=old.bs;ensureBluetoothPermission=old.permission;getSavedPrinter=old.printer;setTimeout=old.timeout;clearTimeout=old.clear;}
    `);
    assert.deepStrictEqual(result, { writes: 0, busy: false });
  });

  test('#2063 impressão: permissão negada libera nova tentativa; falha parcial não reenvia automaticamente', () => {
    const result = run(`
      const old={bs:window.bluetoothSerial,permission:ensureBluetoothPermission,printer:getSavedPrinter,error:showError};
      let permitted=false,writes=0,connections=0;
      try {
        releasePrinting();getSavedPrinter=()=>({name:'Fixture',address:'A',profile:'escpos'});
        ensureBluetoothPermission=cb=>cb(permitted);showError=()=>{};
        window.bluetoothSerial={disconnect:ok=>ok(),connect:(addr,ok)=>{connections++;ok();},write:(bytes,ok,fail)=>{writes++;fail('broken pipe');}};
        printViaBluetooth([1]);permitted=true;printViaBluetooth([1]);
        return JSON.stringify({writes,connections,busy:isPrinting});
      } finally {releasePrinting();window.bluetoothSerial=old.bs;ensureBluetoothPermission=old.permission;getSavedPrinter=old.printer;showError=old.error;}
    `);
    assert.deepStrictEqual(result, { writes: 1, connections: 1, busy: false });
  });

  test('#2063 impressão: troca encerra conexão anterior e Oasis sem driver não envia bytes', () => {
    const result = run(`
      const old={bs:window.bluetoothSerial,permission:ensureBluetoothPermission,printer:getSavedPrinter};
      const steps=[];let selected={name:'Atual',address:'A',profile:'escpos'},connected=null;
      try {
        releasePrinting();getSavedPrinter=()=>selected;ensureBluetoothPermission=cb=>cb(true);
        window.bluetoothSerial={
          disconnect:ok=>{steps.push('disconnect');connected=null;ok();},
          connect:(addr,ok)=>{steps.push('connect:'+addr);connected=addr;ok();},
          write:(bytes,ok)=>{steps.push('write:'+connected);ok();}
        };
        printViaBluetooth([1]);selected={name:'Segunda compatível',address:'B',profile:'escpos'};printViaBluetooth([2]);
        selected={name:'Oasis OIA-8381-B',address:'C',profile:'oasis-pending'};printViaBluetooth([3]);
        return JSON.stringify(steps);
      } finally {releasePrinting();window.bluetoothSerial=old.bs;ensureBluetoothPermission=old.permission;getSavedPrinter=old.printer;}
    `);
    assert.deepStrictEqual(result, ['disconnect','connect:A','write:A','disconnect','connect:B','write:B']);
  });

  async function pilotPairingCase(fn) {
    const sync = sandbox.window.cdbSync;
    const keys = ['cdb-pairing','cdb-pilot-data-context','cdb-products','cdb-orders','cdb-clients','cdb-batches','cdb-cash','cdb-cash-closings','cdb-shopping','cdb-audit-log'];
    const stored = keys.map(key => sandbox.localStorage.getItem(key));
    const original = { config:sandbox.window.CDB_CONFIG, fetch:sandbox.fetch, abort:sandbox.AbortController, params:sandbox.URLSearchParams };
    let requests = [];
    try {
      sandbox.window.CDB_CONFIG={isPilot:true};
      keys.forEach(key=>sandbox.localStorage.removeItem(key));
      sync.clearPairing();sync.clearQueue();sandbox.AbortController=AbortController;sandbox.URLSearchParams=URLSearchParams;
      sandbox.fetch=async (url,options)=>{
        requests.push({url:String(url),options});
        const body=String(url).includes('/devices/pair')
          ? {apiKey:'fixture-key',empresaId:'fixture-company',lojaId:'fixture-store',deviceId:'fixture-device'}
          : String(url).includes('/sync/pull') ? {serverTime:0,mutations:[]} : {};
        return {ok:true,status:200,headers:{get(){return null;}},json:async()=>body};
      };
      await fn(sync,requests);
      await sync.flush();
    } finally {
      sandbox.window.CDB_CONFIG=original.config;
      sync.clearPairing();sync.clearQueue();
      keys.forEach((key,i)=>stored[i]===null?sandbox.localStorage.removeItem(key):sandbox.localStorage.setItem(key,stored[i]));
      sandbox.fetch=original.fetch;sandbox.AbortController=original.abort;sandbox.URLSearchParams=original.params;
    }
  }

  test('#2063 piloto: primeiro vínculo é permitido e conserva contexto de dados', async () => {
    await pilotPairingCase(async (sync,requests)=>{
      sandbox.localStorage.setItem('cdb-products','[{"id":"fixture-local-product"}]');
      await sync.pairWithCode('123456','Fixture');
      assert.equal(requests.filter(r=>r.url.endsWith('/devices/pair')).length,1);
      assert.equal(sync.getPairing().lojaId,'fixture-store');
      assert.equal(JSON.parse(sandbox.localStorage.getItem('cdb-pilot-data-context')).lojaId,'fixture-store');
    });
  });

  test('#2063 piloto: re-pareamento com fila preservada não faz POST nem consome código', async () => {
    await pilotPairingCase(async (sync,requests)=>{
      sandbox.localStorage.setItem('cdb-pairing',JSON.stringify({apiKey:'revoked-fixture',empresaId:'fixture-company',lojaId:'old-store'}));
      sandbox.localStorage.setItem('cdb-sync-queue',JSON.stringify([{id:'fixture-mutation',type:'order.upsert',payload:{id:'fixture-pending-order'}}]));
      await sync._internal.initQueueStore();
      const queueBefore=sync.queueSize();
      await assert.rejects(()=>sync.pairWithCode('123456','Fixture'),/dados vinculados/);
      assert.equal(requests.length,0);
      assert.equal(sync.queueSize(),queueBefore);
      assert.equal(sync.getPairing().lojaId,'old-store');
      // Não deve disparar o flush de limpeza do helper com a fila que o teste protege.
      sync.clearQueue();
    });
  });

  test('#2063 piloto: erro ao salvar chave não anuncia pareamento nem inicia sync', async () => {
    await pilotPairingCase(async (sync,requests)=>{
      sandbox.localStorage.setItem('cdb-products','[{"id":"fixture-local-product"}]');
      const original=sandbox.localStorage.setItem;
      try {
        sandbox.localStorage.setItem=(key,value)=>{
          if(key==='cdb-pairing') { const error=new Error('quota');error.name='QuotaExceededError';throw error; }
          original(key,value);
        };
        await assert.rejects(()=>sync.pairWithCode('123456','Fixture'),/conexão não pôde ser salva/);
        assert.equal(requests.length,1,'somente o POST de pareamento, sem flush/pull');
        assert.ok(requests[0].url.endsWith('/devices/pair'));
        assert.equal(sync.getPairing(),null);
        assert.equal(sync.isPairingInvalid(),true);
        assert.equal(sandbox.localStorage.getItem('cdb-products'),'[{"id":"fixture-local-product"}]');
      } finally { sandbox.localStorage.setItem=original; }
    });
  });

  test('#2063 piloto: desconectar não permite atribuir dados antigos a outro vínculo', async () => {
    await pilotPairingCase(async (sync,requests)=>{
      sandbox.localStorage.setItem('cdb-pairing',JSON.stringify({apiKey:'fixture',empresaId:'fixture-company',lojaId:'old-store'}));
      sandbox.localStorage.setItem('cdb-batches','[{"id":"fixture-batch"}]');
      sync.clearPairing();
      await assert.rejects(()=>sync.pairWithCode('123456','Fixture'),/dados vinculados/);
      assert.equal(requests.length,0);
      assert.equal(sandbox.localStorage.getItem('cdb-batches'),'[{"id":"fixture-batch"}]');
      assert.equal(JSON.parse(sandbox.localStorage.getItem('cdb-pilot-data-context')).lojaId,'old-store');
    });
  });

  test('#2063 piloto: troca de loja com dados locais não chama servidor', async () => {
    await pilotPairingCase(async (sync,requests)=>{
      sandbox.localStorage.setItem('cdb-pairing',JSON.stringify({apiKey:'fixture',empresaId:'fixture-company',lojaId:'old-store'}));
      sandbox.localStorage.setItem('cdb-orders','[{"id":"fixture-order"}]');
      await assert.rejects(()=>sync.switchLoja('new-store'),/dados vinculados/);
      assert.equal(requests.length,0);
      assert.equal(sync.getPairing().lojaId,'old-store');
    });
  });

  test('#2063 piloto: contexto anterior corrompido não é tratado como primeiro vínculo', async () => {
    await pilotPairingCase(async (sync,requests)=>{
      sandbox.localStorage.setItem('cdb-orders','[{"id":"fixture-order"}]');
      for (const damaged of ['{invalid','null','{}','{"empresaId":"fixture-company"}']) {
        sandbox.localStorage.setItem('cdb-pilot-data-context',damaged);
        await assert.rejects(()=>sync.pairWithCode('123456','Fixture'),/dados vinculados/);
      }
      assert.equal(requests.length,0);
      assert.equal(sandbox.localStorage.getItem('cdb-orders'),'[{"id":"fixture-order"}]');
    });
  });
};
