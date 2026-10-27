'use strict';
module.exports = function ({ test, sandbox, reset, assert, runInSandbox }) {
  const sync = () => sandbox.window.cdbSync;
  const clone = value => JSON.parse(JSON.stringify(value));
  const mutation = (id = 'a89d667d-7cfa-43bb-bfd2-b5357cc734b4') => ({ id, type: 'stock.delta', payload: { id: 'movement-1', productId: 'legacy', qty: 7 }, ts: 10, schemaVersion: 2 });
  const payload = () => ({ appId: 'casa-da-baba', version: 2, state: { products: [{ id: 'legacy', name: 'Legado', stock: 7 }], clients: [], orders: [], batches: [], cashEntries: [], cashClosings: [] }, localStorage: { 'cdb-sync-queue': JSON.stringify([mutation()]) } });
  async function isolated(fn) {
    const oldFetch = sandbox.fetch, oldOnline = sandbox.navigator.onLine, oldConfig = sandbox.window.CDB_CONFIG, oldPhotos = sandbox.window.cdbPhotoStore, oldQueueStore = sandbox.window.cdbQueueStore, oldCrypto = sandbox.window.crypto, oldAbort = sandbox.AbortController, oldParams = sandbox.URLSearchParams, oldSet = sandbox.localStorage.setItem;
    reset(); sandbox.localStorage.clear(); sync().clearPairing(); sync().clearQueue(); sandbox.navigator.onLine = false;
    sandbox.window.CDB_CONFIG = { isPilot: true }; sandbox.window.crypto = require('node:crypto').webcrypto; sandbox.AbortController = AbortController; sandbox.URLSearchParams = URLSearchParams;
    try { await fn(); }
    finally { sandbox.localStorage.setItem = oldSet; sandbox.fetch = oldFetch; sandbox.navigator.onLine = oldOnline; sandbox.window.CDB_CONFIG = oldConfig; sandbox.window.crypto = oldCrypto; sandbox.AbortController = oldAbort; sandbox.URLSearchParams = oldParams; sandbox.window.cdbPhotoStore = oldPhotos; sandbox.window.cdbQueueStore = oldQueueStore; sandbox.localStorage.clear(); sync().clearPairing(); sync().clearQueue(); reset(); }
  }
  test('#2069 import: preserva dados atuais e MutationId sem criar delta de estoque', () => isolated(async () => {
    runInSandbox("products = [{ id: 'current', name: 'Atual', stock: 3, count: 0 }]; persistAll();");
    sync().clearQueue();
    const p = payload();
    p.localStorage['cdb-device-id'] = 'device-old';
    p.localStorage['cdb-pairing'] = JSON.stringify({ apiKey: 'NEVER_COPY', empresaId: 'e1', lojaId: 'l1' });
    p.localStorage['cdb-pull-cursor'] = '999999';
    p.localStorage['unrelated'] = 'never';
    sandbox.localStorage.setItem('cdb-device-id', 'device-new');
    sandbox.localStorage.setItem('cdb-pull-cursor', '123');
    const result = await sandbox.applyBackupPayload(p);
    assert.strictEqual(result.added, 1);
    assert.strictEqual(result.queueAdded, 1);
    assert.deepStrictEqual(clone(sync().getBackupQueue()), [mutation()]);
    assert.strictEqual(runInSandbox("products.find(p => p.id === 'current').stock"), 3);
    assert.strictEqual(runInSandbox("products.find(p => p.id === 'legacy').stock"), 7);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-device-id'), 'device-new');
    assert.strictEqual(sandbox.localStorage.getItem('cdb-pairing'), null);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-pull-cursor'), '123');
    assert.strictEqual(sandbox.localStorage.getItem('unrelated'), null);
    const safety = JSON.parse(sandbox.localStorage.getItem('cdb-before-import-backup'));
    assert.strictEqual(safety.state.products[0].id, 'current');
    assert.ok(!JSON.stringify(safety).includes('NEVER_COPY'));
    assert.strictEqual(await sync().flush(), 'review');
    runInSandbox('persistAll()');
    assert.deepStrictEqual(clone(sync().getBackupQueue()), [mutation()], 'persistir após importar não pode inventar novos movimentos');
  }));
  test('#2069 import: importar o mesmo backup duas vezes não duplica registros nem fila', () => isolated(async () => {
    const p = payload();
    await sandbox.applyBackupPayload(p);
    const safety = sandbox.localStorage.getItem('cdb-before-import-backup');
    const result = await sandbox.applyBackupPayload(p);
    assert.strictEqual(result.added, 0);
    assert.strictEqual(result.queueAdded, 0);
    assert.strictEqual(runInSandbox('products.length'), 1);
    assert.strictEqual(sync().queueSize(), 1);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-before-import-backup'), safety);
  }));
  test('#2069 import: mesmo ID divergente bloqueia sem tocar dados ou fila', () => isolated(async () => {
    runInSandbox("products = [{ id: 'legacy', name: 'Versão atual', stock: 11 }]; persistAll();");
    const before = sandbox.localStorage.getItem('cdb-products'), queue = clone(sync().getBackupQueue());
    await assert.rejects(sandbox.applyBackupPayload(payload()), /mesmo código e dados diferentes/);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-products'), before);
    assert.deepStrictEqual(clone(sync().getBackupQueue()), queue);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-before-import-backup'), null);
  }));
  test('#2069 import: empresa ou loja diferente bloqueia antes da cópia de segurança', () => isolated(async () => {
    sandbox.localStorage.setItem('cdb-pairing', JSON.stringify({ empresaId: 'current-company', lojaId: 'current-store', apiKey: 'current-key' }));
    const p = payload(); p.sync = { context: { empresaId: 'other-company', lojaId: 'other-store' }, queue: [] };
    await assert.rejects(sandbox.applyBackupPayload(p), /outra empresa ou loja/);
    assert.strictEqual(runInSandbox('products.length'), 0);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-before-import-backup'), null);
    assert.strictEqual(sync().getPairing().apiKey, 'current-key');
  }));
  test('#2069 import: sem espaço para backup bloqueia sem alterar estado', () => isolated(async () => {
    const write = sandbox.localStorage.setItem;
    sandbox.localStorage.setItem = (k, v) => { if (k === 'cdb-before-import-backup') throw new Error('quota'); write(k, v); };
    await assert.rejects(sandbox.applyBackupPayload(payload()), /quota/);
    assert.strictEqual(runInSandbox('products.length'), 0);
    assert.strictEqual(sync().queueSize(), 0);
    assert.strictEqual(sync().importReview(), null);
  }));
  test('#2069 import: falha no meio da gravação reverte dados e mantém a cópia anterior', () => isolated(async () => {
    runInSandbox("products = [{id:'current', name:'Atual', stock:2}]; persistAll();"); sync().clearQueue();
    const before = sandbox.localStorage.getItem('cdb-products'), write = sandbox.localStorage.setItem;
    sandbox.localStorage.setItem = (k, v) => { if (k === 'cdb-orders') throw new Error('quota no pedido'); write(k, v); };
    await assert.rejects(sandbox.applyBackupPayload(payload()), /quota no pedido/);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-products'), before);
    assert.strictEqual(runInSandbox('products.length'), 1);
    assert.strictEqual(sync().queueSize(), 0);
    assert.strictEqual(sync().importReview(), null);
    assert.ok(sandbox.localStorage.getItem('cdb-before-import-backup'));
  }));
  test('#2069 import: snapshot automático usa o mesmo caminho sem restaurar credencial', () => isolated(async () => {
    const result = await sandbox.applyBackupPayload({ schema: 'cdb-backup-v1', data: { 'cdb-products': '[{"id":"p1","name":"Produto","stock":1}]', 'cdb-pairing': '{"apiKey":"old","empresaId":"e1","lojaId":"l1"}', 'cdb-device-id':'old-device' } });
    assert.strictEqual(result.added, 1);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-pairing'), null);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-device-id'), null);
    assert.deepStrictEqual(clone(sync().importReview().sourceContext), {empresaId:'e1',lojaId:'l1'});
  }));
  test('#2069 import: backup com recusadas ou conflitos exige revisão na origem', () => isolated(async () => {
    const p = payload(); p.sync = { queue: [], deadletter: [{id:'rejected'}] };
    await assert.rejects(sandbox.applyBackupPayload(p), /recusadas ou conflitos/);
    assert.strictEqual(runInSandbox('products.length'), 0);
  }));
  test('#2069 import: fotos ausentes do arquivo antigo ficam explicitamente pendentes', () => isolated(async () => {
    const p = payload(); p.state.batches = [{ id:'batch-old', batchPhotoHash:'photo-old', items:[] }];
    const plan = sandbox.prepareBackupImport(p);
    assert.strictEqual(plan.missingPhotos, 1);
    await sandbox.applyBackupPayload(p);
    assert.strictEqual(sync().importReview().missingPhotos, 1);
  }));
  test('#2069 backup: exporta fotos e fila sem copiar API key e identidade do aparelho', () => isolated(async () => {
    const photo = { hash:'ph1', dataUrl:'data:image/png;base64,YQ==', batchId:'b1', field:'batch' };
    sandbox.window.cdbPhotoStore = {ready:true, listPending:async()=>[photo],listUploaded:async()=>[]};
    sandbox.localStorage.setItem('cdb-pairing', JSON.stringify({apiKey:'secret',empresaId:'e1',lojaId:'l1'}));
    sandbox.localStorage.setItem('cdb-device-id','old-device');
    const result = await sandbox.buildTransferBackupPayload();
    assert.strictEqual(result.version,3);
    assert.deepStrictEqual(clone(result.photos),[photo]);
    assert.deepStrictEqual(clone(result.sync.context),{empresaId:'e1',lojaId:'l1'});
    assert.ok(!JSON.stringify(result).includes('secret'));
    assert.ok(!JSON.stringify(result).includes('old-device'));
  }));
  test('#2069 import: revisão pendente impede chamadas de envio e pull', () => isolated(async () => {
    await sandbox.applyBackupPayload(payload());
    sandbox.navigator.onLine=true; let calls=0; sandbox.fetch=async()=>{calls++;throw Error('não chamar');};
    assert.strictEqual(await sync().flush(), 'review'); await sync().pull();
    assert.strictEqual(calls,0);
    await assert.rejects(sync().confirmImportedDataContext(), /Conecte este aparelho/);
    assert.ok(sync().importReview());
  }));
  test('#2069 import: confirmação exige loja correspondente e só então libera o envio', () => isolated(async () => {
    const p=payload();p.version=3;p.sync={context:{empresaId:'e1',lojaId:'l1'},queue:[mutation()],queueComplete:true};
    await sandbox.applyBackupPayload(p);
    sandbox.localStorage.setItem('cdb-pairing',JSON.stringify({empresaId:'e2',lojaId:'l2',apiKey:'new'}));
    await assert.rejects(sync().confirmImportedDataContext(),/outra empresa ou loja/);
    assert.ok(sync().importReview());
    sandbox.localStorage.setItem('cdb-pairing',JSON.stringify({empresaId:'e1',lojaId:'l1',apiKey:'new'}));
    await sync().confirmImportedDataContext(); // offline: libera revisão, preserva fila para a próxima conexão
    assert.strictEqual(sync().importReview(),null);
    assert.strictEqual(sync().queueSize(),1);
  }));
  test('#2069 pull: não sobrescreve estoque com movimento local pendente', () => isolated(async () => {
    runInSandbox("products = [{id:'legacy', name:'Local', stock:7}]; persistAll();");
    sync().clearQueue();
    sync().finishBackupImport([mutation()], runInSandbox('({products,clients,orders,cashEntries,batches})'));
    sandbox.localStorage.setItem('cdb-pairing',JSON.stringify({apiKey:'key',empresaId:'e1',lojaId:'l1'}));
    sandbox.localStorage.setItem('cdb-pull-cursor','0'); sandbox.navigator.onLine=true;
    let pullCalls=0; sandbox.fetch=async()=>{pullCalls++;return {ok:true,status:200,headers:{get:()=>null},json:async()=>({serverTime:500,mutations:[{type:'product.upsert',payload:{id:'legacy',name:'Servidor antigo',stock:2}}]})};};
    await sync().pull(); assert.strictEqual(pullCalls,1);
    assert.strictEqual(runInSandbox("products.find(p=>p.id==='legacy').stock"),7);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-pull-cursor'),'0','cursor não pode avançar e esquecer dado protegido');
  }));
  test('#2069 import: legado sem fila permanece bloqueado mesmo pareado e não pode ser certificado por reexport', () => isolated(async () => {
    const p=payload();p.localStorage['cdb-sync-queue']='[]';
    await sandbox.applyBackupPayload(p);
    sandbox.localStorage.setItem('cdb-pairing',JSON.stringify({empresaId:'e1',lojaId:'l1',apiKey:'key'}));
    await assert.rejects(sync().confirmImportedDataContext(),/não comprova a fila completa/);
    assert.strictEqual(sync().importReview().requiresReconciliation,true);
    const exported=await sandbox.buildTransferBackupPayload();
    assert.strictEqual(exported.sync.queueComplete,false);
    await sandbox.applyBackupPayload(exported);
    assert.strictEqual(sync().importReview().requiresReconciliation,true);
    assert.strictEqual(sync().queueSize(),0);
    assert.strictEqual(runInSandbox("products.find(p=>p.id==='legacy').stock"),7);
  }));
  test('#2069 import: reexport com fila completa não certifica ACK histórico nem libera migração inicial', () => isolated(async () => {
    const p=payload();p.localStorage['cdb-sync-queue']='[]';
    await sandbox.applyBackupPayload(p);
    const incomplete=clone(p);incomplete.state.products=[{id:'second',name:'Segundo',stock:1}];
    await sandbox.applyBackupPayload(incomplete);
    const complete=clone(p);complete.version=3;complete.sync={queueComplete:true,queue:[],context:{empresaId:'e1',lojaId:'l1'}};
    await sandbox.applyBackupPayload(complete);
    assert.strictEqual(sync().importReview().requiresReconciliation,true,'não liberar outro arquivo importado ainda sem prova');
    complete.state.products=[incomplete.state.products[0]];
    const result=await sandbox.applyBackupPayload(complete);
    assert.strictEqual(result.added,0);
    assert.strictEqual(sync().importReview().requiresReconciliation,true);
    assert.strictEqual(sync().queueSize(),0);
  }));
  test('#2069 import: reenvio do mesmo MutationId preserva tentativas atuais sem conflito de transporte', () => isolated(async () => {
    const p=payload();await sandbox.applyBackupPayload(p);
    const retried=mutation();retried.attempts=5;retried.lastAttemptAt=1500;
    sync().finishBackupImport([retried],runInSandbox('({products,clients,orders,cashEntries,batches})'));
    const result=await sandbox.applyBackupPayload(p);
    assert.strictEqual(result.queueAdded,0);
    assert.deepStrictEqual(clone(sync().getBackupQueue()),[retried]);
  }));
  test('#2069 import: fotos omitidas podem ser recuperadas em reimport sem duplicar registros', () => isolated(async () => {
    const p=payload();p.state.batches=[{id:'batch-old',batchPhotoHash:'ph1',items:[]}];
    await sandbox.applyBackupPayload(p);
    const saved=new Map();sandbox.window.cdbPhotoStore={ready:true,get:async h=>saved.get(h),save:async(h,dataUrl,batchId,field)=>saved.set(h,{hash:h,dataUrl,batchId,field})};
    p.photos=[{hash:'ph1',dataUrl:'data:image/png;base64,YQ==',batchId:'batch-old',field:'batch'}];
    const result=await sandbox.applyBackupPayload(p);
    assert.strictEqual(result.added,0);assert.strictEqual(result.updated,true);
    assert.strictEqual(saved.size,1);assert.strictEqual(sync().importReview().missingPhotos,0);
    assert.strictEqual(runInSandbox('batches.length'),1);
    const again=await sandbox.applyBackupPayload(p);assert.strictEqual(again.updated,undefined);
  }));
  test('#2069 import: revisão bloqueia reenfileiramento completo e upload de fotos', () => isolated(async () => {
    await sandbox.applyBackupPayload(payload());
    const before=clone(sync().getBackupQueue());
    assert.throws(()=>sync().pushAll(),/Revise a importação/);
    assert.deepStrictEqual(clone(sync().getBackupQueue()),before);
    let reads=0;sandbox.window.cdbPhotoStore={ready:true,listPending:async()=>{reads++;return [];}};
    await sandbox.window.cdbPhotoUpload.flush();assert.strictEqual(reads,0);
  }));
  test('#2069 import: marcador de revisão inválido não pode liberar sincronização', () => isolated(async () => {
    sandbox.localStorage.setItem('cdb-import-review','true');
    sandbox.localStorage.setItem('cdb-pairing',JSON.stringify({empresaId:'e1',lojaId:'l1',apiKey:'key'}));
    assert.strictEqual(sync().importReview().invalid,true);
    await assert.rejects(sync().confirmImportedDataContext(),/Conecte este aparelho/);
    assert.strictEqual(await sync().flush(),'review');
  }));

  test('#2069 import: v3 sem empresa e loja de origem continua sem liberação de envio', () => isolated(async () => {
    const p=payload();p.version=3;p.sync={queueComplete:true,queue:[]};
    await sandbox.applyBackupPayload(p);
    sandbox.localStorage.setItem('cdb-pairing',JSON.stringify({empresaId:'e1',lojaId:'l1',apiKey:'key'}));
    await assert.rejects(sync().confirmImportedDataContext(),/não comprova a fila completa/);
    assert.strictEqual(sync().importReview().requiresReconciliation,true);
  }));
  test('#2069 import: aparelho sem pareamento não mistura backups de empresas diferentes', () => isolated(async () => {
    const p=payload();p.version=3;p.sync={context:{empresaId:'e1',lojaId:'l1'},queueComplete:true,queue:[]};
    await sandbox.applyBackupPayload(p);
    const other=clone(p);other.sync.context={empresaId:'e2',lojaId:'l2'};other.state.products=[{id:'other',name:'Outra empresa'}];
    const before=sandbox.localStorage.getItem('cdb-products');
    await assert.rejects(sandbox.applyBackupPayload(other),/outra empresa ou loja/);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-products'),before);
    assert.strictEqual(sync().importReview().sourceContext.empresaId,'e1');
  }));
  test('#2069 sync: falha ao guardar conflito ou recusa mantém a alteração original na fila', () => isolated(async () => {
    sandbox.navigator.onLine=true;
    sandbox.localStorage.setItem('cdb-pairing',JSON.stringify({empresaId:'e1',lojaId:'l1',apiKey:'key'}));
    sandbox.window.cdbQueueStore={ready:true,degraded:false,saveAll:async()=>{},addConflict:async()=>{throw Error('quota');},moveToDeadletter:async()=>{throw Error('quota');}};
    for (const reason of ['conflict: servidor alterado','peso obrigatório']) {
      sync().finishBackupImport([mutation()],runInSandbox('({products,clients,orders,cashEntries,batches})'));
      sandbox.fetch=async()=>({ok:true,status:200,headers:{get:()=>null},json:async()=>({acceptedIds:[],rejected:[{mutationId:mutation().id,reason}]})});
      assert.strictEqual(await sync().flush(),'retry');
      assert.strictEqual(sync().getBackupQueue()[0].id,mutation().id);
    }
  }));
  test('#2069 pull: conflito e recusa aguardando revisão protegem o saldo local', () => isolated(async () => {
    runInSandbox("products=[{id:'legacy',name:'Local',stock:7}];persistAll();");sync().clearQueue();
    sandbox.localStorage.setItem('cdb-pairing',JSON.stringify({empresaId:'e1',lojaId:'l1',apiKey:'key'}));sandbox.navigator.onLine=true;
    for (const kind of ['conflicts','deadletter']) {
      sandbox.window.cdbQueueStore={ready:true,degraded:false,saveAll:async()=>{},getConflicts:async()=>kind==='conflicts'?[mutation()]:[],getDeadletter:async()=>kind==='deadletter'?[mutation()]:[]};
      sandbox.localStorage.setItem('cdb-pull-cursor','0');
      sandbox.fetch=async()=>({ok:true,status:200,headers:{get:()=>null},json:async()=>({serverTime:500,mutations:[{type:'product.upsert',payload:{id:'legacy',name:'Servidor',stock:2}}]})});
      await sync().pull();assert.strictEqual(runInSandbox('products[0].stock'),7);assert.strictEqual(sandbox.localStorage.getItem('cdb-pull-cursor'),'0');
    }
  }));

  test('#2069 import: desfazer restaura dados e fila anteriores e remove todos os arquivos desta revisão', () => isolated(async () => {
    runInSandbox("products=[{id:'current',name:'Atual',stock:3}];persistAll();");
    const before=clone(sync().getBackupQueue());sandbox.localStorage.setItem('cdb-operator-name','Atual');
    const p=payload();p.localStorage['cdb-empresa-name']='Importada';await sandbox.applyBackupPayload(p);
    const safety=sandbox.localStorage.getItem('cdb-before-import-backup');
    const second=payload();second.state.products=[{id:'second',name:'Segundo',stock:1}];second.localStorage['cdb-sync-queue']='[]';await sandbox.applyBackupPayload(second);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-before-import-backup'),safety);
    assert.strictEqual((await sandbox.getBackupImportUndoState()).available,true);
    await sandbox.undoBackupImport();
    assert.strictEqual(runInSandbox('products.length'),1);assert.strictEqual(runInSandbox('products[0].id'),'current');
    assert.deepStrictEqual(clone(sync().getBackupQueue()),before);assert.strictEqual(sync().importReview(),null);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-operator-name'),'Atual');assert.strictEqual(sandbox.localStorage.getItem('cdb-empresa-name'),null);
  }));
  test('#2069 import: trabalho posterior bloqueia desfazer e outro import não reabilita apagar esse trabalho', () => isolated(async () => {
    await sandbox.applyBackupPayload(payload());
    runInSandbox("products.push({id:'work',name:'Trabalho novo',stock:5});persistAll();");
    assert.strictEqual((await sandbox.getBackupImportUndoState()).available,false);
    await assert.rejects(sandbox.undoBackupImport(),/alterações ou conexão diferente/);
    const second=payload();second.state.products=[{id:'second',name:'Segundo',stock:1}];second.localStorage['cdb-sync-queue']='[]';await sandbox.applyBackupPayload(second);
    assert.strictEqual((await sandbox.getBackupImportUndoState()).available,false);
    await assert.rejects(sandbox.undoBackupImport(),/Houve trabalho/);
    assert.ok(runInSandbox("products.some(p=>p.id==='work')"));assert.strictEqual(sync().importReview().undoBlocked,true);
  }));
  test('#2069 import: desfazer bloqueia contexto diferente e adulteração da cópia de segurança', () => isolated(async () => {
    await sandbox.applyBackupPayload(payload());
    sandbox.localStorage.setItem('cdb-pairing',JSON.stringify({empresaId:'e1',lojaId:'l1',apiKey:'key'}));
    await assert.rejects(sandbox.undoBackupImport(),/alterações ou conexão diferente/);sandbox.localStorage.removeItem('cdb-pairing');
    const safety=JSON.parse(sandbox.localStorage.getItem('cdb-before-import-backup'));safety.state.products=[{id:'tampered'}];sandbox.localStorage.setItem('cdb-before-import-backup',JSON.stringify(safety));
    await assert.rejects(sandbox.undoBackupImport(),/cópia anterior foi alterada/);
    assert.strictEqual(runInSandbox('products[0].id'),'legacy');
  }));
  test('#2069 import: falha de armazenamento ao desfazer mantém o estado importado e sua revisão', () => isolated(async () => {
    await sandbox.applyBackupPayload(payload());
    const before=sandbox.localStorage.getItem('cdb-products'),review=sandbox.localStorage.getItem('cdb-import-review'),write=sandbox.localStorage.setItem;
    sandbox.localStorage.setItem=(k,v)=>{if(k==='cdb-orders')throw Error('quota');write(k,v);};
    await assert.rejects(sandbox.undoBackupImport(),/quota/);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-products'),before);assert.strictEqual(sandbox.localStorage.getItem('cdb-import-review'),review);
    assert.strictEqual(runInSandbox('products[0].id'),'legacy');assert.strictEqual(sync().queueSize(),1);
  }));

  const migrationOptions = {sourceNeverSynced:true,expectedEmpresaId:'e1',expectedLojaId:'l1'};
  function connectForMigration() { sandbox.localStorage.setItem('cdb-pairing',JSON.stringify({empresaId:'e1',lojaId:'l1',apiKey:'key'}));sandbox.navigator.onLine=true; }
  const sourceCapture = () => JSON.parse(sandbox.localStorage.getItem('cdb-import-source'));
  const response = (data,status=200) => ({ok:status>=200&&status<300,status,headers:{get:()=>null},json:async()=>data});
  function receiptFor(request, extra={}) {const state=JSON.parse(request.snapshotJson);return {importId:request.importId,snapshotSha256:request.snapshotSha256,empresaId:'e1',lojaId:'l1',coveredMutationIds:request.originMutationIds,counts:Object.fromEntries(Object.keys(state).map(k=>[k,state[k].length])),replayed:false,...extra};}
  function successFetch(extra={},duringPost=()=>{}) {
    let request;
    sandbox.fetch=async(url,init)=>{
      if(init.method==='POST') {request=JSON.parse(init.body);duringPost(request);return response(receiptFor(request,extra));}
      assert.ok(String(url).endsWith('/snapshot'));sandbox.navigator.onLine=false;
      return response({importId:request.importId,snapshotSha256:request.snapshotSha256,snapshotJson:request.snapshotJson});
    };
  }
  test('#2069 primeira migração: captura somente a origem, mantém importId e hash exato, sem incluir trabalho do APK', () => isolated(async () => {
    runInSandbox("products=[{id:'apk-current',name:'Atual',stock:3}];persistAll();");
    const p=payload();p.state.products[0].photo='data:image/png;base64,YQ==';await sandbox.applyBackupPayload(p);
    const before=sourceCapture();const state=JSON.parse(before.snapshotJson);
    assert.deepStrictEqual(state.products.map(p=>p.id),['legacy']);assert.deepStrictEqual(before.originMutationIds,[mutation().id]);
    assert.strictEqual(state.products[0].photo,p.state.products[0].photo);
    assert.strictEqual(require('node:crypto').createHash('sha256').update(before.snapshotJson,'utf8').digest('hex'),before.snapshotSha256);
    await sandbox.applyBackupPayload(p);assert.deepStrictEqual(sourceCapture(),before);
    runInSandbox("products.push({id:'work-after',name:'Novo',stock:2});persistAll();");
    assert.deepStrictEqual(sourceCapture(),before);
  }));
  test('#2069 primeira migração: confirmação nunca enviado e destino são obrigatórios antes da rede', () => isolated(async () => {
    await sandbox.applyBackupPayload(payload());connectForMigration();let calls=0;sandbox.fetch=async()=>{calls++;throw Error('não chamar');};
    await assert.rejects(sync().migrateImportedLegacyData({...migrationOptions,sourceNeverSynced:false}),/nunca foi enviado/);
    await assert.rejects(sync().migrateImportedLegacyData({...migrationOptions,expectedLojaId:'wrong'}),/empresa ou loja mudou/);
    assert.strictEqual(calls,0);assert.ok(!sourceCapture().preparedRequest);assert.ok(sync().importReview());
  }));
  test('#2069 primeira migração: ACK e readback removem somente IDs cobertos e preservam fila criada durante envio', () => isolated(async () => {
    runInSandbox("products=[{id:'apk-current',name:'Atual',stock:3}];persistAll();");
    const before=clone(sync().getBackupQueue());await sandbox.applyBackupPayload(payload());connectForMigration();
    let newQueue;
    successFetch({},request=>{
      assert.strictEqual(request.sourceNeverSynced,true);assert.deepStrictEqual(request.originMutationIds,[mutation().id]);
      runInSandbox("products.find(p=>p.id==='legacy').stock+=2;persistAll();");
      newQueue=clone(sync().getBackupQueue()).filter(m=>m.id!==mutation().id);
    });
    const receipt=await sync().migrateImportedLegacyData(migrationOptions);
    assert.deepStrictEqual(clone(sync().getBackupQueue()),newQueue);assert.ok(newQueue.length>before.length);
    assert.strictEqual(runInSandbox("products.find(p=>p.id==='legacy').stock"),9);
    assert.strictEqual(sync().importReview(),null);assert.strictEqual(sourceCapture(),null);
    assert.strictEqual(sync().getLastLegacyMigration().importId,receipt.importId);
  }));
  test('#2069 primeira migração: timeout mantém revisão, bloqueia desfazer e repete request idêntico após trabalho posterior', () => isolated(async () => {
    await sandbox.applyBackupPayload(payload());connectForMigration();let firstBody;
    sandbox.fetch=async(_url,init)=>{firstBody=init.body;throw Error('network lost');};
    await assert.rejects(sync().migrateImportedLegacyData(migrationOptions),/Retome a mesma migração/);
    assert.strictEqual(sync().getImportedMigrationState().prepared,true);assert.strictEqual(await sync().flush(),'review');
    await assert.rejects(sandbox.undoBackupImport(),/migração já foi iniciada/);
    const second=payload();second.state.products=[{id:'another',name:'Outro',stock:1}];
    await assert.rejects(sandbox.applyBackupPayload(second),/migração já foi preparada/);
    runInSandbox("products.push({id:'after-timeout',name:'Novo depois',stock:4});persistAll();");
    const pending=clone(sync().getBackupQueue()).filter(m=>m.id!==mutation().id);let retryBody;
    let request;sandbox.fetch=async(_url,init)=>{if(init.method==='POST'){retryBody=init.body;request=JSON.parse(init.body);return response(receiptFor(request,{replayed:true}));}sandbox.navigator.onLine=false;return response({importId:request.importId,snapshotSha256:request.snapshotSha256,snapshotJson:request.snapshotJson});};
    const receipt=await sync().migrateImportedLegacyData(migrationOptions);
    assert.strictEqual(retryBody,firstBody);assert.strictEqual(receipt.replayed,true);assert.deepStrictEqual(clone(sync().getBackupQueue()),pending);
    assert.ok(runInSandbox("products.some(p=>p.id==='after-timeout')"));
  }));
  test('#2069 primeira migração: recibo de outro hash, empresa ou IDs não retira nenhuma pendência', () => isolated(async () => {
    await sandbox.applyBackupPayload(payload());connectForMigration();
    for(const invalid of [{snapshotSha256:'bad'},{empresaId:'other'},{coveredMutationIds:['unrelated']},{coveredMutationIds:[]},{coveredMutationIds:[mutation().id.toUpperCase()]}]) {
      sandbox.fetch=async(_url,init)=>response(receiptFor(JSON.parse(init.body),invalid));
      await assert.rejects(sync().migrateImportedLegacyData(migrationOptions),/recibo da migração não corresponde/);
      assert.strictEqual(sync().queueSize(),1);assert.ok(sync().importReview());
    }
  }));
  test('#2069 primeira migração: cópia divergente no servidor mantém request para retomada', () => isolated(async () => {
    await sandbox.applyBackupPayload(payload());connectForMigration();let request;
    sandbox.fetch=async(_url,init)=>{if(init.method==='POST'){request=JSON.parse(init.body);return response(receiptFor(request));}return response({importId:request.importId,snapshotSha256:request.snapshotSha256,snapshotJson:'{}'});};
    await assert.rejects(sync().migrateImportedLegacyData(migrationOptions),/cópia do histórico ainda não pôde ser conferida/);
    assert.strictEqual(sync().queueSize(),1);assert.ok(sync().importReview());assert.strictEqual(sourceCapture().preparedRequest.importId,request.importId);
  }));
  test('#2069 primeira migração: erro explícito de negócio mantém dados e fila offline', () => isolated(async () => {
    await sandbox.applyBackupPayload(payload());connectForMigration();
    sandbox.fetch=async()=>response({code:'LEGACY_RECORD_ALREADY_EXISTS',message:'Produto já existe no destino; a equipe precisa conferir.'},409);
    await assert.rejects(sync().migrateImportedLegacyData(migrationOptions),error=>error.code==='LEGACY_RECORD_ALREADY_EXISTS'&&/Produto já existe/.test(error.message));
    assert.strictEqual(sync().queueSize(),1);assert.strictEqual(runInSandbox('products[0].stock'),7);assert.ok(sync().importReview());
  }));
  test('#2069 primeira migração: fechamentos e fotos ficam na captura e ressalvas sobrevivem ao ACK', () => isolated(async () => {
    const p=payload();p.state.cashClosings=[{id:'closing-old',total:123,items:[{count:3}]}];p.state.batches=[{id:'old-batch',batchPhoto:'data:image/png;base64,YQ==',items:[]}];
    await sandbox.applyBackupPayload(p);connectForMigration();const state=sync().getImportedMigrationState();
    assert.strictEqual(state.counts.cashClosings,1);assert.deepStrictEqual(clone(state.archiveOnlyCollections),['cashClosings']);assert.ok(state.warnings[0].includes('não reabrem'));
    const warnings=['Fechamentos antigos preservados no backup; não reabrem nem recalculam caixa do EasyStok'];
    successFetch({archiveOnlyCollections:['cashClosings'],warnings},request=>{const captured=JSON.parse(request.snapshotJson);assert.strictEqual(captured.cashClosings[0].items[0].count,3);assert.strictEqual(captured.batches[0].batchPhoto,p.state.batches[0].batchPhoto);});
    await sync().migrateImportedLegacyData(migrationOptions);assert.deepStrictEqual(clone(sync().getLastLegacyMigration().warnings),warnings);
  }));
  test('#2069 primeira migração: sem espaço para congelar pedido não envia nem marca tentativa', () => isolated(async () => {
    await sandbox.applyBackupPayload(payload());connectForMigration();const write=sandbox.localStorage.setItem;let calls=0;
    sandbox.localStorage.setItem=(k,v)=>{if(k==='cdb-import-review'&&JSON.parse(v).migrationStartedAt)throw Error('quota');write(k,v);};sandbox.fetch=async()=>{calls++;throw Error('não chamar');};
    await assert.rejects(sync().migrateImportedLegacyData(migrationOptions),/quota/);
    assert.strictEqual(calls,0);assert.ok(!sourceCapture().preparedRequest);assert.ok(!sync().importReview().migrationStartedAt);
  }));
  test('#2069 primeira migração: sem espaço para guardar ACK conserva fila e request idempotente', () => isolated(async () => {
    await sandbox.applyBackupPayload(payload());connectForMigration();const write=sandbox.localStorage.setItem;
    sandbox.localStorage.setItem=(k,v)=>{if(k==='cdb-last-legacy-import')throw Error('quota');write(k,v);};successFetch();
    await assert.rejects(sync().migrateImportedLegacyData(migrationOptions),/quota/);
    assert.strictEqual(sync().queueSize(),1);assert.ok(sync().importReview());assert.ok(sourceCapture().preparedRequest);
    assert.strictEqual(JSON.parse(sandbox.localStorage.getItem('cdb-sync-queue')).length,1);
  }));
  test('#2069 primeira migração: troca de conexão durante readback não libera fila para outra empresa', () => isolated(async () => {
    await sandbox.applyBackupPayload(payload());connectForMigration();let request;
    sandbox.fetch=async(_url,init)=>{if(init.method==='POST'){request=JSON.parse(init.body);return response(receiptFor(request));}sandbox.localStorage.setItem('cdb-pairing',JSON.stringify({empresaId:'e2',lojaId:'l2',apiKey:'other'}));return response({importId:request.importId,snapshotSha256:request.snapshotSha256,snapshotJson:request.snapshotJson});};
    await assert.rejects(sync().migrateImportedLegacyData(migrationOptions),/conexão mudou/);
    assert.strictEqual(sync().queueSize(),1);assert.ok(sync().importReview());assert.strictEqual(sync().getImportedMigrationState().available,false);
  }));

  test('#2069 primeira migração: backup restaura request preparado e trabalho posterior sem copiar credenciais', () => isolated(async () => {
    await sandbox.applyBackupPayload(payload());connectForMigration();let originalBody;
    sandbox.fetch=async(_url,init)=>{originalBody=init.body;throw Error('lost');};await assert.rejects(sync().migrateImportedLegacyData(migrationOptions),/Retome/);
    runInSandbox("products.find(p=>p.id==='legacy').stock+=2;persistAll();");
    const exported=clone(await sandbox.buildTransferBackupPayload());const pending=clone(sync().getBackupQueue()).filter(m=>m.id!==mutation().id);
    assert.ok(exported.sync.legacyImportSource.preparedRequest);assert.ok(!JSON.stringify(exported).includes('"apiKey"'));
    sandbox.localStorage.clear();sync().clearPairing();sync().clearQueue();reset();sandbox.navigator.onLine=false;
    await sandbox.applyBackupPayload(exported);
    assert.strictEqual(sync().getPairing(),null);assert.strictEqual(runInSandbox('products[0].stock'),9);
    assert.strictEqual(JSON.stringify(sourceCapture().preparedRequest),originalBody);
    await assert.rejects(sandbox.undoBackupImport(),/migração já foi iniciada/);
    connectForMigration();let retry;
    successFetch({replayed:true},request=>{retry=JSON.stringify(request);assert.strictEqual(JSON.parse(request.snapshotJson).products[0].stock,7);});
    await sync().migrateImportedLegacyData(migrationOptions);
    assert.strictEqual(retry,originalBody);assert.deepStrictEqual(clone(sync().getBackupQueue()),pending);
  }));
  test('#2069 primeira migração: cápsula preparada de outra loja não mistura dados nem permite confirmar envio comum', () => isolated(async () => {
    await sandbox.applyBackupPayload(payload());connectForMigration();sandbox.fetch=async()=>{throw Error('lost');};await assert.rejects(sync().migrateImportedLegacyData(migrationOptions),/Retome/);
    const exported=clone(await sandbox.buildTransferBackupPayload());exported.sync.context=null;
    sandbox.localStorage.clear();sync().clearPairing();sync().clearQueue();reset();sandbox.localStorage.setItem('cdb-pairing',JSON.stringify({empresaId:'e2',lojaId:'l2',apiKey:'other'}));
    await assert.rejects(sandbox.applyBackupPayload(exported),/outra empresa ou loja/);assert.strictEqual(runInSandbox('products.length'),0);
    sandbox.localStorage.removeItem('cdb-pairing');await sandbox.applyBackupPayload(exported);
    assert.strictEqual(sync().importReview().requiresReconciliation,true);
    connectForMigration();await assert.rejects(sync().confirmImportedDataContext(),/não comprova a fila completa/);
  }));

  test('#2069 histórico migrado: lotes não podem recriar nem remover estoque antes da revisão', () => isolated(async () => {
    const p=payload();p.state.batches=[{id:'historical-batch',code:'HIST',items:[{productId:'legacy',qty:5}],deleted:true,discarded:true,status:'open'}];
    await sandbox.applyBackupPayload(p);const before=runInSandbox('JSON.stringify(batches)'),queue=clone(sync().getBackupQueue());
    sandbox.deleteBatch('historical-batch');sandbox.restoreBatch('historical-batch');sandbox._silentRestoreBatch('historical-batch');sandbox.revertBatchDiscard('historical-batch');sandbox.toggleBatchStatus('historical-batch');
    runInSandbox("_discardingBatchId='historical-batch';confirmBatchDiscard();");
    assert.strictEqual(runInSandbox('JSON.stringify(batches)'),before);assert.strictEqual(runInSandbox('products[0].stock'),7);assert.deepStrictEqual(clone(sync().getBackupQueue()),queue);
    assert.strictEqual(sandbox.assertBatchEditable(runInSandbox('batches[0]')),false);
  }));
  test('#2069 histórico migrado: entregue não estorna e pronto só entrega com forma explícita, sem novo delta', () => isolated(async () => {
    const p=payload();p.state.orders=[{id:'done',status:'entregue',items:[{productId:'legacy',qty:2}],total:20},{id:'ready',status:'pronto',items:[{productId:'legacy',qty:1}],total:10,history:[]}];
    await sandbox.applyBackupPayload(p);const before=clone(sync().getBackupQueue());
    sandbox.cancelOrder('done');sandbox._proceedAdvance('done','aguardando');sandbox.cancelOrder('ready');sandbox.applyConfirmation('ready','adjust',{adjustments:[{correctedQty:9}]});
    assert.strictEqual(runInSandbox("orders.find(o=>o.id==='done').status"),'entregue');assert.strictEqual(runInSandbox('products[0].stock'),7);assert.deepStrictEqual(clone(sync().getBackupQueue()),before);
    sandbox.applyAdvanceTransition('ready','entregue');assert.strictEqual(runInSandbox("orders.find(o=>o.id==='ready').status"),'pronto');
    sandbox.escolherFormaEntrega('pix');assert.strictEqual(runInSandbox("orders.find(o=>o.id==='ready').status"),'entregue');assert.strictEqual(runInSandbox('products[0].stock'),7);
    const created=sync().getBackupQueue().filter(m=>!before.some(old=>old.id===m.id));assert.ok(created.some(m=>m.type==='order.upsert'&&m.payload.id==='ready'));assert.ok(!created.some(m=>m.type==='stock.delta'));
    sandbox.cancelOrder('ready');assert.strictEqual(runInSandbox("orders.find(o=>o.id==='ready').status"),'entregue');
  }));
  test('#2069 histórico migrado: aguardando e preparando continuam operacionais com estoque normal', () => isolated(async () => {
    const p=payload();p.state.orders=[{id:'active',status:'aguardando',clientSnapshot:{name:'Cliente de teste'},items:[{productId:'legacy',qty:1}],total:10,history:[]}];await sandbox.applyBackupPayload(p);
    sandbox.applyAdvanceTransition('active','preparando');sandbox.applyAdvanceTransition('active','pronto');
    assert.strictEqual(runInSandbox("orders[0].status"),'pronto');assert.strictEqual(runInSandbox('products[0].stock'),6);
    assert.ok(sync().getBackupQueue().some(m=>m.type==='stock.delta'&&m.payload.qty===-1));
  }));
  test('#2069 histórico migrado: marcas do pull bloqueiam ações mesmo sem recibo local', () => isolated(async () => {
    runInSandbox("products=[{id:'p1',stock:8}];batches=[{id:'b1',legacyImported:true,items:[{productId:'p1',qty:3}]}];orders=[{id:'o1',status:'entregue',legacyHistoryStatus:'entregue',items:[{productId:'p1',qty:2}]}];");
    sandbox.deleteBatch('b1');sandbox.cancelOrder('o1');assert.strictEqual(runInSandbox('products[0].stock'),8);assert.strictEqual(runInSandbox('orders[0].status'),'entregue');assert.ok(!runInSandbox('batches[0].deleted'));assert.strictEqual(sync().queueSize(),0);
  }));
  test('#2069 primeira migração: fotos separadas do IndexedDB também integram o snapshot arquivado', () => isolated(async () => {
    const p=payload();const photo={hash:'photo-history',dataUrl:'data:image/png;base64,YQ==',batchId:'photo-batch',field:'batchPhoto',uploadedAt:123};p.photos=[photo];p.state.batches=[{id:'photo-batch',batchPhotoHash:photo.hash,items:[]}];
    const photos=new Map();sandbox.window.cdbPhotoStore={ready:true,get:async h=>photos.get(h),save:async(h,dataUrl)=>photos.set(h,{dataUrl})};
    await sandbox.applyBackupPayload(p);assert.deepStrictEqual(JSON.parse(sourceCapture().snapshotJson).photos,[photo]);assert.strictEqual(sourceCapture().counts.batches,1);
  }));

  test('#2069 pull: lote local recebe marca histórica sem perder seus itens e fotos', () => isolated(async () => {
    runInSandbox("batches=[{id:'b-local',batchPhoto:'foto-local',items:[{productId:'p-local',qty:4}]}];persistAll();");sync().clearQueue();connectForMigration();let calls=0;
    sandbox.fetch=async()=>{calls++;return response({serverTime:900,mutations:[{type:'batch.upsert',payload:{id:'b-local',legacyImported:true,items:[]}}]});};
    await sync().pull();assert.strictEqual(calls,1);assert.strictEqual(runInSandbox('batches[0].legacyImported'),true);assert.strictEqual(runInSandbox('batches[0].items[0].qty'),4);assert.strictEqual(runInSandbox('batches[0].batchPhoto'),'foto-local');
    assert.strictEqual(sandbox.assertImportedBatchAction('b-local'),false);
  }));
  test('#2069 pull: produto mantém foto local ausente no DTO sem gerar mutation ao persistir depois', () => isolated(async () => {
    runInSandbox("products=[{id:'photo-product',name:'Local',photo:'data:image/png;base64,YQ==',stock:3}];persistAll();");sync().clearQueue();connectForMigration();let calls=0;
    sandbox.fetch=async()=>{calls++;return response({serverTime:900,mutations:[{type:'product.upsert',payload:{id:'photo-product',name:'Servidor',stock:4}}]});};
    await sync().pull();assert.strictEqual(calls,1);assert.strictEqual(runInSandbox('products[0].photo'),'data:image/png;base64,YQ==');assert.strictEqual(runInSandbox('products[0].stock'),4);
    runInSandbox('persistAll();');assert.strictEqual(sync().queueSize(),0);
  }));


  test('#2069 backup: compras e auditoria preservam bytes como anexos sem aplicar listas nem credenciais', () => isolated(async () => {
    const p=payload(), archive='[ { "event": "histórico", "deviceId": "origem" } ]';
    p.localStorage['cdb-shopping']='[{"id":"compra-antiga","qty":2}]';p.localStorage['cdb-audit-log']=archive;
    p.localStorage['cdb-audit-log-archive']='[{"event":"arquivo anterior"}]';p.localStorage['cdb-pairing']=JSON.stringify({apiKey:'NEVER_ARCHIVE',empresaId:'e1',lojaId:'l1'});
    sandbox.localStorage.setItem('cdb-shopping','[{"id":"compra-atual"}]');sandbox.localStorage.setItem('cdb-audit-log','[{"event":"atual"}]');
    await sandbox.applyBackupPayload(p);const snapshot=JSON.parse(sourceCapture().snapshotJson);
    assert.strictEqual(snapshot.localArchive.length,3);assert.strictEqual(snapshot.localArchive.find(e=>e.key==='cdb-audit-log').value,archive);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-shopping'),'[{"id":"compra-atual"}]');assert.strictEqual(sandbox.localStorage.getItem('cdb-audit-log'),'[{"event":"atual"}]');
    const exported=clone(await sandbox.buildTransferBackupPayload());assert.ok(exported.localArchive.some(e=>e.value===archive));assert.ok(!JSON.stringify(exported).includes('NEVER_ARCHIVE'));
    assert.strictEqual((await sandbox.getBackupImportUndoState()).available,true);
    await sandbox.undoBackupImport();assert.strictEqual(sandbox.localStorage.getItem('cdb-import-archives'),null);assert.strictEqual(sandbox.localStorage.getItem('cdb-shopping'),'[{"id":"compra-atual"}]');
  }));
  test('#2069 primeira migração: anexos repetidos não duplicam e ressalva permanece depois da confirmação', () => isolated(async () => {
    const p=payload();p.localStorage['cdb-audit-log']='[{"event":"antigo"}]';
    await sandbox.applyBackupPayload(p);await sandbox.applyBackupPayload(p);
    assert.strictEqual(JSON.parse(sourceCapture().snapshotJson).localArchive.length,1);
    const second=payload();second.localStorage['cdb-audit-log']='[{"event":"outra versão"}]';await sandbox.applyBackupPayload(second);
    assert.strictEqual(JSON.parse(sourceCapture().snapshotJson).localArchive.length,2);
    assert.ok(sync().getImportedMigrationState().warnings.some(w=>w.includes('Compras e auditoria')));
    connectForMigration();successFetch();const receipt=await sync().migrateImportedLegacyData(migrationOptions);
    assert.ok(receipt.warnings.some(w=>w.includes('Compras e auditoria')));assert.deepStrictEqual(clone(receipt.archivedStorageKeys),['cdb-audit-log']);
    assert.strictEqual((await sandbox.buildTransferBackupPayload()).localArchive.length,2);
  }));
  test('#2069 backup: campo explícito de anexos não permite transferir chaves arbitrárias', () => isolated(async () => {
    const p=payload();p.localArchive=[{key:'cdb-device-id',value:'old-device'}];
    await assert.rejects(sandbox.applyBackupPayload(p),/Anexo operacional inválido/);
    assert.strictEqual(runInSandbox('products.length'),0);assert.strictEqual(sync().importReview(),null);
  }));

};
