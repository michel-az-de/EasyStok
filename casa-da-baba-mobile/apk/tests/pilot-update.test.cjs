'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { validateManifest } = require('../pilot.js');
const config = { appId: 'com.casadababa.piloto', versionCode: 1 };
const manifest = {
  schemaVersion: 1, appId: config.appId, versionCode: 2, version: '0.1.1',
  url: 'https://easystok.online/downloads/casa-da-baba/casa-da-baba-2.apk',
  sha256: 'a'.repeat(64),
};
test('oferece versão superior e não oferece downgrade ou reinstalação', () => {
  assert.equal(validateManifest(manifest, config).available, true);
  assert.equal(validateManifest({ ...manifest, versionCode: 1 }, config).available, false);
  assert.equal(validateManifest(manifest, { ...config, versionCode: 3 }).available, false);
});
test('rejeita pacote de outro aplicativo e metadados corrompidos', () => {
  for (const extra of [{ appId: 'com.casadababa.app' }, { versionCode: 2.5 }, { versionCode: -1 }, { sha256: '' }, { schemaVersion: 0 }, { version: 2 }]) {
    assert.throws(() => validateManifest({ ...manifest, ...extra }, config));
  }
});
test('não abre links de atualização fora da distribuição aprovada', () => {
  for (const url of ['http://easystok.online/downloads/casa-da-baba/a.apk', 'https://evil.example/a.apk', 'https://easystok.online.evil.example/downloads/casa-da-baba/a.apk', 'https://user@easystok.online/downloads/casa-da-baba/a.apk', 'https://easystok.online/downloads/a.apk', 'https://easystok.online/downloads/casa-da-baba/a.apk?redirect=evil', 'https://easystok.online/downloads/casa-da-baba/a.apk#fragment', 'https://easystok.online/downloads/casa-da-baba/../../a.apk', 'https://easystok.online/downloads/casa-da-baba/a%2Ffile.apk']) {
    assert.throws(() => validateManifest({ ...manifest, url }, config));
  }
});

const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const { connectionStatus } = require('../pilot.js');

test('diferencia servidor acessível de aparelho pareado e envio pendente', () => {
  const local = connectionStatus({ health: 'online', paired: false, invalid: false, pending: 2 });
  assert.match(local.status, /acessível/);
  assert.match(local.pairing, /só neste aparelho/);
  assert.equal(local.queue, '2 alterações aguardando envio.');
  const offline = connectionStatus({ health: 'offline', paired: true, invalid: false, pending: 1 });
  assert.match(offline.status, /Sem conexão confirmada/);
  assert.match(offline.pairing, /vinculado/);
  assert.equal(offline.queue, '1 alteração aguardando envio.');
  const revoked = connectionStatus({ health: 'online', paired: true, invalid: true, pending: 0 });
  assert.match(revoked.pairing, /inválida/);
  assert.doesNotMatch(revoked.queue, /sincronizad|confirmad/);
});

function pilotHarness({ operator = 'Operadora', connected = true, manifestError = false, healthFetch } = {}) {
  const nodes = new Map();
  const events = new Map();
  const doc = { activeElement: null, hidden: false, addEventListener: (name, callback) => events.set(name, callback), getElementById: id => nodes.get(id) };
  const makeElement = tag => {
    const handlers = new Map();
    const el = { tag, dataset: {}, open: false, hidden: false, disabled: false, textContent: '', isConnected: true,
      classList: { add() {} }, children: [], append(...children) { this.children.push(...children); }, insertBefore(child, reference) { this.children = this.children.filter(item => item !== child); const index = this.children.indexOf(reference); this.children.splice(index < 0 ? this.children.length : index, 0, child); }, setAttribute() {}, getClientRects: () => [1], scrollIntoView() {},
      focus() { doc.activeElement = el; },
      showModal() { el.open = true; },
      close() { el.open = false; handlers.get('close')?.(); },
      addEventListener(name, callback) { handlers.set(name, callback); },
      async click(action) { await handlers.get('click')?.({ target: { closest: () => ({ dataset: { action } }) } }); }
    };
    Object.defineProperty(el, 'innerHTML', { set(html) {
      for (const match of html.matchAll(/id="([^"]+)"/g)) nodes.set(match[1], makeElement('element'));
    } });
    return el;
  };
  doc.createElement = makeElement;
  doc.body = { classList: { add() {} }, prepend() {}, append(el) { nodes.set(el.id, el); } };
  const calls = [];
  const root = { document: doc, navigator: { onLine: connected },
    CDB_CONFIG: { ...config, isPilot: true, version: '0.1.0', apiBaseUrl: 'https://api.easystok.online', updateManifestUrl: 'https://easystok.online/downloads/casa-da-baba/latest.json' },
    cdbApp: { getOperator: () => operator },
    cdbSync: { getPairing: () => null, isPairingInvalid: () => false, queueSize: () => 0 },
    addEventListener: (name, callback) => events.set(name, callback),
    open: url => calls.push(url), go: screen => calls.push(screen),
  };
  const requests = [];
  const fetch = async url => {
    requests.push(url);
    if (url.endsWith('/api/mobile/version')) return healthFetch ? healthFetch() : { ok: true, json: async () => ({ databaseOk: true }) };
    if (manifestError) throw new Error('Network failure');
    return { ok: true, json: async () => manifest };
  };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '..', 'pilot.js'), 'utf8'), { window: root, fetch, URL, AbortSignal });
  return { root, doc, nodes, events, calls, requests };
}

test('atualização acionada fora do início abre diálogo, informa resultado e restaura foco', async () => {
  const { root, nodes, doc } = pilotHarness();
  const home = nodes.get('pilotHome');
  home.close();
  const origin = { isConnected: true, getClientRects: () => [1], focus() { doc.activeElement = origin; } };
  doc.activeElement = origin;
  await root.forceCheckUpdate();
  assert.equal(home.open, true);
  assert.match(nodes.get('pilotUpdateStatus').textContent, /0\.1\.1 disponível/);
  assert.equal(nodes.get('pilotDownload').hidden, false);
  assert.equal(nodes.get('pilotUpdateButton').disabled, false);
  assert.equal(doc.activeElement, nodes.get('pilotUpdateStatus'));
  home.close();
  assert.equal(doc.activeElement, origin);
});

test('falha na consulta de atualização mantém retorno visível e permite tentar novamente', async () => {
  const { root, nodes } = pilotHarness({ manifestError: true });
  nodes.get('pilotHome').close();
  await root.forceCheckUpdate();
  assert.equal(nodes.get('pilotHome').open, true);
  assert.match(nodes.get('pilotUpdateStatus').textContent, /Não foi possível conferir/);
  assert.equal(nodes.get('pilotDownload').hidden, true);
  assert.equal(nodes.get('pilotUpdateButton').disabled, false);
});

test('início aguarda identificação do operador, sem sobrepor boas-vindas', () => {
  const { nodes, events } = pilotHarness({ operator: '' });
  assert.equal(nodes.get('pilotHome').open, false);
  events.get('cdb-operator-ready')();
  assert.equal(nodes.get('pilotHome').open, true);
});

test('offline mantém registro local disponível e explica por que atendimento não abre', async () => {
  const { nodes, doc, calls } = pilotHarness({ connected: false });
  await nodes.get('pilotHome').click('workspace');
  assert.equal(calls.length, 0);
  assert.match(nodes.get('pilotError').textContent, /sem conexão/);
  assert.equal(doc.activeElement, nodes.get('pilotError'));
  await nodes.get('pilotHome').click('local-orders');
  assert.deepEqual(calls, ['pedidos']);
});

test('preview online abre o balcão real do atendimento', async () => {
  const { nodes, calls } = pilotHarness();
  await nodes.get('pilotHome').click('workspace');
  assert.deepEqual(calls, ['https://app.easystok.online/#/m/atendimento/balcao']);
});

test('textos pequenos do início mantêm contraste mínimo de 4,5:1', () => {
  const css = fs.readFileSync(path.join(__dirname, '..', 'pilot.css'), 'utf8');
  const style = selector => {
    const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const body = css.match(new RegExp(escaped + '\\s*\\{([^}]+)\\}'))?.[1];
    assert.ok(body, selector);
    return Object.fromEntries(body.split(';').filter(part => part.includes(':')).map(part => part.split(':').map(value => value.trim())));
  };
  const luminance = hex => {
    const rgb = hex.slice(1).match(/.{2}/g).map(value => parseInt(value, 16) / 255);
    return rgb.map(value => value <= .04045 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4)
      .reduce((sum, value, index) => sum + value * [.2126, .7152, .0722][index], 0);
  };
  for (const [text, background] of [['.pilot-topline', '.pilot-home'], ['.pilot-update', '.pilot-home'], ['.pilot-production', '.pilot-production'], ['.pilot-orders', '.pilot-orders'], ['.pilot-rail button span', '.pilot-rail'], ['.pilot-app .etq-actions .etq-save', '.pilot-app .etq-actions .etq-save'], ['.pilot-app .etq-actions .etq-share', '.pilot-app .etq-actions .etq-share']]) {
    const levels = [luminance(style(text).color), luminance(style(background).background)].sort((a, b) => a - b);
    assert.ok((levels[1] + .05) / (levels[0] + .05) >= 4.5, text);
  }
  assert.equal(style('.pilot-card .pilot-step').opacity, undefined);
});

test('cancelar pareamento conserva início e devolve foco ao atalho', async () => {
  const { root, nodes, doc } = pilotHarness();
  root.pairDevicePrompt = async () => ({ cancelled: true });
  await nodes.get('pilotHome').click('pair');
  assert.equal(nodes.get('pilotHome').open, true);
  assert.equal(doc.activeElement, nodes.get('pilotPairButton'));
  assert.equal(nodes.get('pilotPairButton').disabled, false);
});

test('sucesso e falha de pareamento ficam visíveis sem abrir diagnóstico', async () => {
  const { root, nodes, doc } = pilotHarness();
  root.pairDevicePrompt = async () => {
    root.cdbSync.getPairing = () => ({ empresaId: 'empresa', lojaId: 'loja' });
    return { paired: true };
  };
  await nodes.get('pilotHome').click('pair');
  assert.equal(nodes.get('pilotHome').open, true);
  assert.equal(doc.activeElement, nodes.get('pilotConnection'));
  assert.match(nodes.get('pilotConnection').textContent, /Aparelho vinculado/);
  root.pairDevicePrompt = async () => { throw new Error('Código inválido'); };
  await nodes.get('pilotHome').click('pair');
  assert.equal(doc.activeElement, nodes.get('pilotError'));
  assert.match(nodes.get('pilotError').textContent, /Código inválido/);
  assert.equal(nodes.get('pilotPairButton').disabled, false);
});

test('barra mantém explícito que aparelho sem pareamento ainda opera só localmente', () => {
  const local = connectionStatus({ health: 'online', paired: false, invalid: false, pending: 0 });
  assert.equal(local.rail, 'Só neste aparelho');
  assert.equal(local.syncState, 'local');
  const invalid = connectionStatus({ health: 'online', paired: true, invalid: true, pending: 3 });
  assert.equal(invalid.rail, 'Reconectar aparelho · 3 pendentes');
  assert.equal(invalid.syncState, 'invalid');
});

test('fila e pareamento atualizam a barra mesmo sem envio bem-sucedido', () => {
  const { root, nodes, events } = pilotHarness();
  root.cdbSync.queueSize = () => 3;
  events.get('cdb-sync-state-change')();
  assert.match(nodes.get('pilotRailStatus').textContent, /3 pendentes/);
  root.cdbSync.isPairingInvalid = () => true;
  events.get('cdb-sync-state-change')();
  assert.match(nodes.get('pilotRailStatus').textContent, /Reconectar aparelho/);
  assert.equal(nodes.get('pilotPairButton').textContent, 'Reconectar produção');
});

test('consulta de conexão em andamento bloqueia chamadas repetidas e libera o botão ao terminar', async () => {
  let complete;
  const { nodes, requests } = pilotHarness({ healthFetch: () => new Promise(resolve => { complete = resolve; }) });
  assert.equal(nodes.get('pilotReconnect').disabled, true);
  await nodes.get('pilotHome').click('reconnect');
  await nodes.get('pilotHome').click('reconnect');
  assert.equal(requests.filter(url => url.endsWith('/api/mobile/version')).length, 1);
  complete({ ok: true, json: async () => ({ databaseOk: true }) });
  await new Promise(setImmediate);
  assert.equal(nodes.get('pilotReconnect').disabled, false);
  assert.equal(nodes.get('pilotReconnect').hidden, true);
});

test('APK atualizado não esconde conteúdo OTA ainda aguardando abertura', async () => {
  const { root, nodes } = pilotHarness();
  root.CDB_CONFIG.versionCode = manifest.versionCode;
  root.Capacitor = { Plugins: { PilotUpdater: { check: async () => ({ pendingVersion: '0.4.1' }) } } };
  await root.forceCheckUpdate();
  assert.match(nodes.get('pilotUpdateStatus').textContent, /APK instalado está atualizado/);
  assert.match(nodes.get('pilotOtaStatus').textContent, /0.4.1 preparada/);
});

test('atalho do PWA fecha o modal antes de abrir o seletor de backup existente', async () => {
  const { nodes } = pilotHarness();
  let opened = false;
  nodes.set('backupFileInput', { click() { assert.equal(nodes.get('pilotHome').open, false); opened = true; } });
  await nodes.get('pilotHome').click('import-pwa');
  assert.equal(opened, true);
});


test('backup importado mantém aviso de envio pausado e pede conexão antes da confirmação', () => {
  const {root,nodes,events}=pilotHarness();
  root.cdbSync.importReview=()=>({missingPhotos:2});
  events.get('cdb-sync-state-change')();
  assert.equal(nodes.get('pilotImportReview').hidden,false);
  assert.equal(nodes.get('pilotConfirmImport').disabled,true);
  assert.match(nodes.get('pilotImportStatus').textContent,/Conecte a produção/);
  assert.match(nodes.get('pilotImportStatus').textContent,/2 foto/);
  assert.match(nodes.get('pilotRailStatus').textContent,/Conferir dados importados/);
  assert.match(nodes.get('pilotConnection').textContent,/Envio pausado/);
});

test('origem importada exige confirmação de empresa e loja e permite cancelar sem liberar envio', async () => {
  const {root,nodes,events}=pilotHarness();
  let review={missingPhotos:1},confirmed=0;
  root.cdbSync.importReview=()=>review;
  root.cdbSync.getPairing=()=>({empresaNome:'Casa da Baba',lojaNome:'Operação'});
  root.cdbSync.confirmImportedDataContext=async()=>{confirmed++;review=null;};
  events.get('cdb-sync-state-change')();
  root.confirm=message=>{assert.match(message,/empresa Casa da Baba, loja Operação/);assert.match(message,/1 foto/);return false;};
  await nodes.get('pilotHome').click('confirm-import');
  assert.equal(confirmed,0);
  assert.equal(nodes.get('pilotImportReview').hidden,false);
  root.confirm=()=>true;
  await nodes.get('pilotHome').click('confirm-import');
  assert.equal(confirmed,1);
  assert.equal(nodes.get('pilotImportReview').hidden,true);
});

test('falha de origem mantém revisão visível e restaura botão após erro', async () => {
  const {root,nodes,events,doc}=pilotHarness();
  root.cdbSync.importReview=()=>({});
  root.cdbSync.getPairing=()=>({empresaNome:'Outra empresa',lojaNome:'Loja'});
  root.cdbSync.confirmImportedDataContext=async()=>{throw new Error('O backup pertence a outra empresa ou loja.');};
  root.confirm=()=>true;
  events.get('cdb-sync-state-change')();
  await nodes.get('pilotHome').click('confirm-import');
  assert.equal(nodes.get('pilotImportReview').hidden,false);
  assert.equal(nodes.get('pilotConfirmImport').disabled,false);
  assert.match(nodes.get('pilotError').textContent,/outra empresa ou loja/);
  assert.equal(doc.activeElement,nodes.get('pilotError'));
});

test('backup sem proveniência exige revisão e nunca confirma envio só pelo pareamento', async () => {
  const {root,nodes,events}=pilotHarness();
  let confirmed=0;
  root.cdbSync.importReview=()=>({requiresReconciliation:true});
  root.cdbSync.getPairing=()=>({empresaNome:'Casa da Baba',lojaNome:'Loja'});
  root.cdbSync.confirmImportedDataContext=async()=>{confirmed++;};
  events.get('cdb-sync-state-change')();
  assert.equal(nodes.get('pilotConfirmImport').disabled,true);
  assert.match(nodes.get('pilotImportStatus').textContent,/PWA de origem/);
  assert.match(nodes.get('pilotImportStatus').textContent,/Revise com a equipe/);
  assert.match(nodes.get('pilotImportStatus').textContent,/EasyStok/);
  await nodes.get('pilotHome').click('confirm-import');
  assert.equal(confirmed,0);
});


test('desfazer importação só aparece durante revisão e cancelamento não altera dados', async () => {
  const {root,nodes,events}=pilotHarness();
  let review={requiresReconciliation:true},undone=0;
  root.cdbSync.importReview=()=>review;
  root.getBackupImportUndoState=async()=>({available:true});
  root.undoBackupImport=async()=>{undone++;};
  root.confirm=message=>{assert.match(message,/todos os arquivos importados nesta revisão/);assert.match(message,/dados e a fila anteriores/);return false;};
  events.get('cdb-sync-state-change')();
  await new Promise(setImmediate);
  assert.equal(nodes.get('pilotUndoImport').hidden,false);
  assert.equal(nodes.get('pilotUndoImport').disabled,false);
  await nodes.get('pilotHome').click('undo-import');
  assert.equal(undone,0);
  review=null;events.get('cdb-sync-state-change')();
  assert.equal(nodes.get('pilotUndoImport').hidden,true);
});

test('desfazer verifica novamente trabalho posterior e mantém dados quando bloqueado', async () => {
  const {root,nodes,events}=pilotHarness();
  let available=true,undone=0;
  root.cdbSync.importReview=()=>({requiresReconciliation:true});
  root.getBackupImportUndoState=async()=>({available,reason:'Há trabalho novo. Exporte os dados antes de continuar.'});
  root.undoBackupImport=async()=>{undone++;};
  events.get('cdb-sync-state-change')();
  await new Promise(setImmediate);
  available=false;
  await nodes.get('pilotHome').click('undo-import');
  assert.equal(undone,0);
  assert.match(nodes.get('pilotError').textContent,/Há trabalho novo/);
  events.get('cdb-sync-state-change')();await new Promise(setImmediate);
  assert.equal(nodes.get('pilotUndoImport').disabled,true);
  assert.match(nodes.get('pilotUndoImportStatus').textContent,/Exporte os dados/);
});

test('desfazer só recarrega após restauração confirmada e concluída', async () => {
  const {root,nodes}=pilotHarness();
  const steps=[];let review={requiresReconciliation:true};
  root.cdbSync.importReview=()=>review;
  root.getBackupImportUndoState=async()=>({available:true});
  root.confirm=()=>{steps.push('confirmar');return true;};
  root.undoBackupImport=async()=>{steps.push('restaurar');review=null;};
  root.location={reload:()=>steps.push('recarregar')};
  await nodes.get('pilotHome').click('undo-import');
  assert.deepEqual(steps,['confirmar','restaurar','recarregar']);
  assert.equal(nodes.get('pilotImportReview').hidden,true);
});

test('erro na restauração mantém revisão e não recarrega o aplicativo', async () => {
  const {root,nodes}=pilotHarness();let reloaded=false;
  root.cdbSync.importReview=()=>({requiresReconciliation:true});
  root.getBackupImportUndoState=async()=>({available:true});
  root.confirm=()=>true;
  root.undoBackupImport=async()=>{throw new Error('O estado mudou. Preserve o trabalho novo.');};
  root.location={reload:()=>{reloaded=true;}};
  await nodes.get('pilotHome').click('undo-import');
  assert.equal(reloaded,false);
  assert.equal(nodes.get('pilotImportReview').hidden,false);
  assert.match(nodes.get('pilotError').textContent,/Preserve o trabalho novo/);
  assert.equal(nodes.get('pilotUndoImport').textContent,'Desfazer importação');
});


function migrationHarness() {
  const harness=pilotHarness(), {root}=harness;
  const counts={products:2,clients:1,orders:3,cashEntries:1,batches:2,cashClosings:1};
  const state={available:true,prepared:false,counts,archiveOnlyCollections:['cashClosings'],warnings:['Compras e auditoria antigas preservadas como anexos no backup; não alteram as listas atuais.']};
  root.cdbSync.importReview=()=>({requiresReconciliation:true});
  root.cdbSync.getPairing=()=>({empresaId:'empresa-1',lojaId:'loja-1',empresaNome:'Casa da Baba',lojaNome:'Operação'});
  root.cdbSync.getImportedMigrationState=()=>state;
  return {...harness,state,counts};
}

test('primeira migração exige confirmação separada de origem, destino, contagens e ressalva do caixa', async () => {
  const {root,nodes,events}=migrationHarness();let called=false;
  root.cdbSync.migrateImportedLegacyData=async()=>{called=true;};
  root.confirm=message=>{
    assert.match(message,/nunca foram enviados ao EasyStok/);
    assert.match(message,/empresa Casa da Baba, loja Operação/);
    assert.match(message,/2 produtos, 1 cliente, 3 pedidos, 1 lançamento de caixa, 2 lotes, 1 fechamento de caixa/);
    assert.match(message,/Fechamentos antigos serão preservados no backup; não reabrem nem recalculam caixa/);
    assert.match(message,/Compras e auditoria antigas preservadas/);
    assert.match(message,/apenas neste app/);
    assert.match(message,/sem reenviar sua fila/);
    return false;
  };
  events.get('cdb-sync-state-change')();
  assert.equal(nodes.get('pilotConfirmImport').hidden,true);
  assert.match(nodes.get('pilotMigrationStatus').textContent,/Compras e auditoria antigas preservadas/);
  assert.equal(nodes.get('pilotMigrateImport').disabled,false);
  await nodes.get('pilotHome').click('migrate-import');
  assert.equal(called,false);
});

test('ACK de migração preserva avisos visíveis e envia destino explicitamente confirmado', async () => {
  const {root,nodes,counts,doc}=migrationHarness();let received;
  root.confirm=()=>true;
  root.cdbSync.migrateImportedLegacyData=async options=>{received=JSON.parse(JSON.stringify(options));root.cdbSync.importReview=()=>null;return {counts,warnings:['Confira as fotos ausentes.'],archiveOnlyCollections:['cashClosings']};};
  await nodes.get('pilotHome').click('migrate-import');
  assert.deepEqual(received,{sourceNeverSynced:true,expectedEmpresaId:'empresa-1',expectedLojaId:'loja-1'});
  assert.equal(nodes.get('pilotMigrationReceipt').hidden,false);
  assert.equal(nodes.get('pilotMigrationReceipt').open,true);
  assert.match(nodes.get('pilotMigrationResult').textContent,/confirmada pelo EasyStok/);
  assert.match(nodes.get('pilotMigrationResult').textContent,/Confira as fotos ausentes/);
  assert.match(nodes.get('pilotMigrationResult').textContent,/não reabrem nem recalculam caixa/);
  assert.equal(doc.activeElement,nodes.get('pilotMigrationResult'));
});

test('falha mantém revisão e oferece retomar migração já preparada sem afirmar nova conclusão', async () => {
  const {root,nodes,state}=migrationHarness();
  root.confirm=()=>true;
  root.cdbSync.migrateImportedLegacyData=async()=>{state.prepared=true;throw new Error('Sem resposta. Retome a mesma migração.');};
  await nodes.get('pilotHome').click('migrate-import');
  assert.equal(nodes.get('pilotImportReview').hidden,false);
  assert.equal(nodes.get('pilotMigrationReceipt').hidden,true);
  assert.equal(nodes.get('pilotMigrateImport').textContent,'Retomar migração');
  assert.equal(nodes.get('pilotMigrateImport').disabled,false);
  assert.match(nodes.get('pilotError').textContent,/Sem resposta/);
  root.confirm=message=>{assert.match(message,/O servidor pode já ter recebido a primeira tentativa/);return false;};
  await nodes.get('pilotHome').click('migrate-import');
});

test('migração indisponível mostra motivo e não chama endpoint', async () => {
  const {root,nodes,state,events}=migrationHarness();let called=false;
  state.available=false;state.reason='Conecte o aparelho à empresa correta.';
  root.cdbSync.migrateImportedLegacyData=async()=>{called=true;};
  events.get('cdb-sync-state-change')();
  assert.equal(nodes.get('pilotMigrateImport').disabled,true);
  assert.match(nodes.get('pilotMigrationStatus').textContent,/empresa correta/);
  await nodes.get('pilotHome').click('migrate-import');
  assert.equal(called,false);
});

test('recibo guardado restaura ressalvas após nova abertura sem tirar foco da navegação', () => {
  const {root,nodes,events,doc}=migrationHarness();
  const previousFocus=doc.activeElement;
  root.cdbSync.importReview=()=>null;
  root.cdbSync.getLastLegacyMigration=()=>({counts:{cashClosings:1},warnings:['Um registro exige conferência.'],archiveOnlyCollections:['cashClosings']});
  events.get('cdb-sync-state-change')();
  assert.equal(nodes.get('pilotMigrationReceipt').hidden,false);
  assert.match(nodes.get('pilotMigrationResult').textContent,/registro exige conferência/);
  assert.match(nodes.get('pilotMigrationResult').textContent,/Fechamentos antigos/);
  assert.equal(doc.activeElement,previousFocus);
});


test('consulta APK automaticamente sem abrir diálogo nem tirar foco da operação', async () => {
 const {nodes,doc,requests}=pilotHarness({operator:''});
 const field={};doc.activeElement=field;
 await new Promise(setImmediate);
 assert.equal(nodes.get('pilotHome').open,false);assert.equal(doc.activeElement,field);
 assert.equal(requests.filter(url=>url.endsWith('/latest.json')).length,1);
 assert.match(nodes.get('pilotUpdateStatus').textContent,/disponível/);
 assert.equal(nodes.get('pilotDownload').hidden,false);
 assert.match(nodes.get('pilotRailVersion').textContent,/atualização disponível/);
});
test('rodapé usa APK nativo mesmo com conteúdo antigo, sem oferecer reinstalação', async () => {
 const {root,nodes}=pilotHarness({operator:''});
 root.CDB_CONFIG.versionCode=3;root.CDB_CONFIG.version='0.3.0';root.CDB_CONFIG.bundleVersion='0.3.1';
 root.Capacitor={Plugins:{PilotUpdater:{status:async()=>({builtinVersion:'0.5.0',nativeVersionCode:5,pendingVersion:'0.5.2'})}}};
 await new Promise(setImmediate);
 assert.match(nodes.get('pilotVersion').textContent,/APK 0\.5\.0 \(5\)/);
 assert.equal(nodes.get('pilotHomeVersion').textContent, 'APK 0.5.0');
 assert.match(nodes.get('pilotVersion').textContent,/Conteúdo 0\.3\.1/);
 assert.match(nodes.get('pilotVersion').textContent,/0\.5\.2 ao reabrir/);
 assert.equal(nodes.get('pilotDownload').hidden,true);
});
test('retomadas repetidas não duplicam consulta, e retorno da rede verifica sem interromper',async()=>{
 const {root,nodes,events,requests}=pilotHarness({operator:'',connected:false});
 await new Promise(setImmediate);assert.equal(requests.length,0);
 root.navigator.onLine=true;events.get('online')();events.get('focus')();events.get('visibilitychange')();
 await new Promise(setImmediate);
 assert.equal(requests.filter(url=>url.endsWith('/latest.json')).length,1);assert.equal(nodes.get('pilotHome').open,false);
});
test('falha automática mantém a operação aberta e oferece nova verificação manual',async()=>{
 const {root,nodes,doc}=pilotHarness({operator:'',manifestError:true});const field={};doc.activeElement=field;
 await new Promise(setImmediate);assert.equal(doc.activeElement,field);assert.equal(nodes.get('pilotHome').open,false);
 assert.equal(nodes.get('pilotUpdateButton').disabled,false);assert.match(nodes.get('pilotUpdateStatus').textContent,/Não foi possível/);
 await root.forceCheckUpdate();assert.equal(nodes.get('pilotHome').open,true);
});

test('falha da consulta nativa não declara APK atualizado usando versão do conteúdo',async()=>{
 const {root,nodes}=pilotHarness({operator:''});root.CDB_CONFIG.versionCode=99;
 root.Capacitor={isNativePlatform:()=>true,Plugins:{PilotUpdater:{status:async()=>{throw new Error('bridge indisponível');}}}};
 await new Promise(setImmediate);
 assert.doesNotMatch(nodes.get('pilotUpdateStatus').textContent,/está atualizado/);
 assert.match(nodes.get('pilotVersion').textContent,/aguardando confirmação/);
 assert.equal(nodes.get('pilotHomeVersion').textContent, 'Confirmando versão do APK');
 assert.equal(nodes.get('pilotDownload').hidden,true);
});

test('resumo compacto mantém rede, vínculo, fila pendente e bloqueio de importação explícitos', () => {
  const empty = connectionStatus({ health: 'online', paired: false, invalid: false, pending: 0 });
  assert.match(empty.summary, /EasyStok acessível.*só neste aparelho/);
  assert.doesNotMatch(empty.summary, /Nenhuma alteração/);
  assert.match(empty.queue, /Nenhuma alteração/, 'detalhes preservam o estado de fila vazia');
  const pending = connectionStatus({ health: 'offline', paired: true, invalid: false, pending: 9 });
  assert.match(pending.summary, /Sem conexão confirmada.*Aparelho vinculado.*9 alterações aguardando envio/);
  const invalid = connectionStatus({ health: 'online', paired: true, invalid: true, pending: 1 });
  assert.match(invalid.summary, /inválida.*1 alteração aguardando envio/);
  const importing = connectionStatus({ health: 'online', paired: true, invalid: false, pending: 2, importPending: true });
  assert.match(importing.summary, /Envio pausado.*2 alterações aguardando envio/);
});
