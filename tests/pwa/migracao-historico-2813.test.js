'use strict';
// #2813 - migracao do historico, defeitos do PWA.
//  B3: backup com recusadas ou conflitos era rejeitado sem caminho na tela.
//  B6: lista de clientes cortada em 20, sem busca por texto.
module.exports = function ({ test, sandbox, reset, assert, runInSandbox }) {
  const sync = () => sandbox.window.cdbSync;
  const clone = value => JSON.parse(JSON.stringify(value));
  const esperar = ms => new Promise(resolve => setTimeout(resolve, ms));
  async function ate(condicao, descricao) {
    for (let i = 0; i < 400 && !condicao(); i++) await esperar(5);
    if (!condicao()) throw new Error('Tempo esgotado esperando: ' + descricao);
  }
  // Mesmo isolamento de backup-migration.test.js, mais os stubs de tela que estes testes trocam.
  async function isolated(fn) {
    const oldFetch = sandbox.fetch, oldOnline = sandbox.navigator.onLine, oldConfig = sandbox.window.CDB_CONFIG, oldPhotos = sandbox.window.cdbPhotoStore, oldQueueStore = sandbox.window.cdbQueueStore, oldCrypto = sandbox.window.crypto, oldAbort = sandbox.AbortController, oldParams = sandbox.URLSearchParams, oldSet = sandbox.localStorage.setItem;
    const oldAlert = sandbox.alert, oldConfirm = sandbox.confirm, oldReader = sandbox.FileReader, oldCreate = sandbox.document.createElement, oldGetById = sandbox.document.getElementById;
    // O import aceito agenda location.reload() 800 ms depois (index.html). O timer não pode sobreviver ao teste: ele cairia no
    // contador de reloads do pull-carimbo, que começa ~0,5 s depois, e o #1520 veria 2 reloads onde espera 1.
    const oldSetTimeout = sandbox.setTimeout, timersDeReload = [];
    sandbox.setTimeout = (fn, ms, ...args) => {
      const id = oldSetTimeout.call(sandbox, fn, ms, ...args);
      if (typeof fn === 'function' && /location\.reload/.test(String(fn))) timersDeReload.push(id);
      return id;
    };
    reset(); sandbox.localStorage.clear(); sync().clearPairing(); sync().clearQueue(); sandbox.navigator.onLine = false;
    sandbox.window.CDB_CONFIG = { isPilot: true }; sandbox.window.crypto = require('node:crypto').webcrypto; sandbox.AbortController = AbortController; sandbox.URLSearchParams = URLSearchParams;
    try { await fn(); }
    finally {
      sandbox.setTimeout = oldSetTimeout; timersDeReload.forEach(id => sandbox.clearTimeout(id));
      sandbox.localStorage.setItem = oldSet; sandbox.fetch = oldFetch; sandbox.navigator.onLine = oldOnline; sandbox.window.CDB_CONFIG = oldConfig; sandbox.window.crypto = oldCrypto; sandbox.AbortController = oldAbort; sandbox.URLSearchParams = oldParams; sandbox.window.cdbPhotoStore = oldPhotos; sandbox.window.cdbQueueStore = oldQueueStore;
      sandbox.alert = oldAlert; sandbox.confirm = oldConfirm; sandbox.FileReader = oldReader; sandbox.document.createElement = oldCreate; sandbox.document.getElementById = oldGetById;
      sandbox.localStorage.clear(); sync().clearPairing(); sync().clearQueue(); reset();
    }
  }

  // ---------- B3: backup com recusadas ou conflitos ----------
  const OPT_IN = { aceitarRecusadosEConflitos: true };
  const mutation = () => ({ id: 'a89d667d-7cfa-43bb-bfd2-b5357cc734b4', type: 'stock.delta', payload: { id: 'movement-1', productId: 'legacy', qty: 7 }, ts: 10, schemaVersion: 2 });
  const recusadas = () => [
    { id: 'rec-1', type: 'stock.delta', payload: { id: 'mov-rec-1', productId: 'legacy', qty: -2 }, ts: 11, schemaVersion: 2, reason: 'estoque insuficiente' },
    { id: 'rec-2', type: 'order.create', payload: { id: 'ped-rec-2', total: 30 }, ts: 12, schemaVersion: 2, reason: 'pedido repetido' }
  ];
  const conflitos = () => [{ id: 'conf-1', type: 'product.upsert', payload: { id: 'legacy', name: 'Legado local', stock: 9 }, ts: 13, schemaVersion: 2, server: { id: 'legacy', name: 'Legado servidor', stock: 4 } }];
  const backupComRecusadas = () => ({
    appId: 'casa-da-baba', version: 2, localStorage: {},
    state: { products: [{ id: 'legacy', name: 'Legado', stock: 7 }], clients: [], orders: [], batches: [], cashEntries: [], cashClosings: [] },
    sync: { queue: [mutation()], deadletter: recusadas(), conflicts: conflitos() }
  });

  test('#2813 B3 import: sem opt-in segue recusando e o erro traz as contagens para a tela', () => isolated(async () => {
    const p = backupComRecusadas();
    let erro = null;
    try { sandbox.prepareBackupImport(p); } catch (e) { erro = e; }
    assert.ok(erro && /recusadas ou conflitos/.test(erro.message), 'sem opt-in o backup precisa continuar recusado');
    assert.strictEqual(erro.code, 'BACKUP_RECUSADOS_CONFLITOS');
    assert.strictEqual(erro.recusadas, 2);
    assert.strictEqual(erro.conflitos, 1);
    await assert.rejects(sandbox.applyBackupPayload(p), /recusadas ou conflitos/);
    await assert.rejects(sandbox.applyBackupPayload(p, true, { aceitarRecusadosEConflitos: false }), /recusadas ou conflitos/);
    assert.strictEqual(runInSandbox('products.length'), 0);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-import-archives'), null);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-before-import-backup'), null);
  }));

  test('#2813 B3 import: com opt-in importa, tira recusadas e conflito do importador e guarda as 3 em localArchive', () => isolated(async () => {
    const plan = sandbox.prepareBackupImport(backupComRecusadas(), OPT_IN);
    assert.deepStrictEqual(clone(plan.sourceQueue), [mutation()], 'recusadas e conflitos não entram na fila');
    assert.strictEqual(plan.sourceArchive.filter(e => /^cdb-sync-(deadletter|conflicts)$/.test(e.key)).length, 3);

    const result = await sandbox.applyBackupPayload(backupComRecusadas(), false, OPT_IN);
    assert.strictEqual(result.added, 1);
    assert.strictEqual(runInSandbox("products.find(p => p.id === 'legacy').stock"), 7);
    assert.deepStrictEqual(clone(sync().getBackupQueue()), [mutation()], 'a fila só recebe a pendência normal');

    const arquivo = JSON.parse(sandbox.localStorage.getItem('cdb-import-archives'));
    const guardadas = chave => arquivo.filter(e => e.key === chave).map(e => JSON.parse(e.value));
    assert.deepStrictEqual(guardadas('cdb-sync-deadletter'), recusadas());
    assert.deepStrictEqual(guardadas('cdb-sync-conflicts'), conflitos());

    const fonte = JSON.parse(sandbox.localStorage.getItem('cdb-import-source'));
    const { localArchive, ...snapshot } = JSON.parse(fonte.snapshotJson);
    assert.ok(!('deadletter' in snapshot) && !('conflicts' in snapshot), 'recusadas e conflitos não viram coleção do snapshot');
    ['rec-1', 'rec-2', 'conf-1'].forEach(id => assert.ok(!JSON.stringify(snapshot).includes(id), id + ' vazou para o que vai ao importador'));
    assert.deepStrictEqual(fonte.originMutationIds, [mutation().id]);
    assert.strictEqual(localArchive.filter(e => /^cdb-sync-(deadletter|conflicts)$/.test(e.key)).length, 3, 'o snapshot guarda as 3 só como anexo');

    const outraVez = await sandbox.applyBackupPayload(backupComRecusadas(), false, OPT_IN);
    assert.strictEqual(outraVez.added, 0);
    assert.strictEqual(JSON.parse(sandbox.localStorage.getItem('cdb-import-archives')).length, 3, 'reimportar não duplica os anexos');
  }));

  test('#2813 B3 tela do import: recusa mostra as contagens e o botão "Importar mesmo assim"; aceitar importa, cancelar não', () => isolated(async () => {
    const ROTULO = 'Importar mesmo assim (as recusadas ficam guardadas no aparelho e não são enviadas)';
    const texto = JSON.stringify(backupComRecusadas()), avisos = [], confirmacoes = [], criados = [];
    sandbox.alert = mensagem => avisos.push(String(mensagem));
    sandbox.confirm = mensagem => { confirmacoes.push(String(mensagem)); return true; };
    sandbox.FileReader = class { readAsText() { setTimeout(() => this.onload({ target: { result: texto } }), 0); } };
    sandbox.document.createElement = () => {
      const botoes = {};
      const elemento = { style: {}, innerHTML: '', removido: false, setAttribute() {}, appendChild() {}, remove() { elemento.removido = true; },
        querySelector(seletor) { return botoes[seletor] || (botoes[seletor] = { onclick: null }); }, botoes };
      criados.push(elemento); return elemento;
    };
    const telas = () => criados.filter(el => /recusada/i.test(el.innerHTML));
    const escolherArquivo = () => sandbox.importBackup({ target: { files: [{ size: texto.length }], value: 'backup.json' } });
    const clicar = (tela, rotulo) => {
      const botao = Array.from(tela.innerHTML.matchAll(/<button\b[^>]*\bid="([^"]+)"[^>]*>([^<]*)<\/button>/g)).find(m => m[2].trim().startsWith(rotulo));
      assert.ok(botao, 'a tela precisa ter o botão "' + rotulo + '"');
      return tela.botoes['#' + botao[1]].onclick();
    };

    escolherArquivo();
    await ate(() => telas().length === 1 || avisos.length > 0, 'a tela abrir o aviso do backup com recusadas');
    assert.strictEqual(telas().length, 1, 'sem saída na tela: só apareceu o alerta "' + avisos[0] + '"');
    const primeira = telas()[0];
    assert.ok(primeira.innerHTML.includes('2 alterações recusadas e 1 conflito'), 'a tela precisa mostrar as contagens');
    assert.ok(primeira.innerHTML.includes(ROTULO), 'a tela precisa ter o botão claro');
    clicar(primeira, 'Cancelar');
    await esperar(30);
    assert.ok(primeira.removido, 'cancelar fecha a tela');
    assert.strictEqual(runInSandbox('products.length'), 0, 'cancelar não importa nada');
    assert.strictEqual(sandbox.localStorage.getItem('cdb-import-archives'), null);
    assert.deepStrictEqual(confirmacoes, []);

    escolherArquivo();
    await ate(() => telas().length === 2, 'a tela abrir de novo');
    clicar(telas()[1], 'Importar mesmo assim');
    await ate(() => runInSandbox('products.length') === 1, 'a importação aceita concluir');
    assert.strictEqual(confirmacoes.length, 1, 'a confirmação final de sempre continua existindo');
    assert.ok(/2 alterações recusadas e 1 conflito/.test(confirmacoes[0]) && /não serão enviad/.test(confirmacoes[0]), 'a confirmação final repete o que fica guardado');
    assert.strictEqual(JSON.parse(sandbox.localStorage.getItem('cdb-import-archives')).length, 3);
    assert.deepStrictEqual(avisos, [], 'aceitar não pode terminar em alerta de erro');
  }));

  // ---------- B3: o aviso do arquivamento depende de QUAIS anexos foram guardados ----------
  const FRASE_COMPRAS = 'Compras e auditoria antigas preservadas como anexos no backup; não alteram as listas atuais do app nem do EasyStok.';
  const MIGRACAO = { sourceNeverSynced: true, expectedEmpresaId: 'e1', expectedLojaId: 'l1' };
  const resposta = (data, status = 200) => ({ ok: status >= 200 && status < 300, status, headers: { get: () => null }, json: async () => data });
  const chavesArquivadas = () => JSON.parse(sandbox.localStorage.getItem('cdb-import-source')).archivedStorageKeys;
  // Backup com a pendência normal mais só os anexos pedidos: 'recusadas' e 'conflitos' (fila de envio) ou 'compras' e 'auditoria' (localStorage).
  function backupComAnexos(...anexos) {
    const p = backupComRecusadas();
    if (!anexos.includes('recusadas')) delete p.sync.deadletter;
    if (!anexos.includes('conflitos')) delete p.sync.conflicts;
    if (anexos.includes('compras')) p.localStorage['cdb-shopping'] = '[{"id":"compra-antiga","qty":2}]';
    if (anexos.includes('auditoria')) p.localStorage['cdb-audit-log'] = '[{"event":"histórico"}]';
    return p;
  }
  async function migrar() {
    sandbox.localStorage.setItem('cdb-pairing', JSON.stringify({ empresaId: 'e1', lojaId: 'l1', apiKey: 'key' })); sandbox.navigator.onLine = true;
    let pedido;
    sandbox.fetch = async (url, init) => {
      if (init.method === 'POST') {
        pedido = JSON.parse(init.body);
        const estado = JSON.parse(pedido.snapshotJson);
        return resposta({ importId: pedido.importId, snapshotSha256: pedido.snapshotSha256, empresaId: 'e1', lojaId: 'l1', coveredMutationIds: pedido.originMutationIds, counts: Object.fromEntries(Object.keys(estado).map(k => [k, estado[k].length])), replayed: false });
      }
      sandbox.navigator.onLine = false;
      return resposta({ importId: pedido.importId, snapshotSha256: pedido.snapshotSha256, snapshotJson: pedido.snapshotJson });
    };
    return sync().migrateImportedLegacyData(MIGRACAO);
  }
  function soRecusadasEConflitos(avisos) {
    const texto = avisos.join(' ');
    assert.ok(!/Compras e auditoria/i.test(texto), 'sem compras nem auditoria no backup o aviso não pode falar delas: "' + texto + '"');
    assert.ok(/recusad/i.test(texto) && /conflito/i.test(texto), 'o aviso precisa citar as recusadas e os conflitos: "' + texto + '"');
    assert.ok(/guardad/i.test(texto) && /não entram/i.test(texto), 'o aviso precisa dizer que ficam guardados e não entram no EasyStok: "' + texto + '"');
  }

  [['deadletter', ['recusadas']], ['conflicts', ['conflitos']]].forEach(([chave, anexos]) =>
    test('#2813 B3 aviso: backup que arquiva só cdb-sync-' + chave + ' não afirma compras e auditoria e cita as recusadas e os conflitos', () => isolated(async () => {
      await sandbox.applyBackupPayload(backupComAnexos(...anexos), false, OPT_IN);
      assert.deepStrictEqual(chavesArquivadas(), ['cdb-sync-' + chave]);
      soRecusadasEConflitos(sync().getImportedMigrationState().warnings);
    })));

  test('#2813 B3 aviso: o recibo da migração com só recusadas também não afirma compras e auditoria', () => isolated(async () => {
    await sandbox.applyBackupPayload(backupComAnexos('recusadas'), false, OPT_IN);
    const recibo = await migrar();
    assert.deepStrictEqual(clone(recibo.archivedStorageKeys), ['cdb-sync-deadletter']);
    soRecusadasEConflitos(recibo.warnings);
  }));

  test('#2813 B3 aviso: backup que arquiva só compras mantém a frase antiga e não fala de recusadas', () => isolated(async () => {
    await sandbox.applyBackupPayload(backupComAnexos('compras'), false, OPT_IN);
    assert.deepStrictEqual(chavesArquivadas(), ['cdb-shopping']);
    assert.deepStrictEqual(clone(sync().getImportedMigrationState().warnings), [FRASE_COMPRAS]);
  }));

  test('#2813 B3 aviso: backup que arquiva compras e recusadas junta as duas frases, no estado da migração e no recibo', () => isolated(async () => {
    await sandbox.applyBackupPayload(backupComAnexos('auditoria', 'recusadas', 'conflitos'), false, OPT_IN);
    assert.deepStrictEqual(chavesArquivadas().sort(), ['cdb-audit-log', 'cdb-sync-conflicts', 'cdb-sync-deadletter']);
    const juntas = avisos => {
      assert.strictEqual(avisos.length, 1, 'as duas frases saem juntas, num só aviso: ' + JSON.stringify(avisos));
      assert.ok(avisos[0].includes(FRASE_COMPRAS), 'a frase de compras e auditoria continua inteira: ' + avisos[0]);
      assert.ok(/recusad/i.test(avisos[0]) && /conflito/i.test(avisos[0]) && /não entram/i.test(avisos[0]), 'a frase das recusadas e dos conflitos acompanha: ' + avisos[0]);
    };
    juntas(sync().getImportedMigrationState().warnings);
    const recibo = await migrar();
    assert.deepStrictEqual(clone(recibo.archivedStorageKeys).sort(), ['cdb-audit-log', 'cdb-sync-conflicts', 'cdb-sync-deadletter']);
    juntas(recibo.warnings);
  }));

  // ---------- B6: lista de clientes cortada em 20 ----------
  const fs = require('fs'), path = require('path');
  const PAGINA = fs.readFileSync(process.env.PWA_HTML || path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa/index.html'), 'utf8');
  const clienteN = n => { const s = String(n).padStart(3, '0'); return { id: 'cli-' + s, name: 'Cliente ' + s + ' Ávila', phone: '119' + String(n).padStart(8, '0') }; };
  function abrirTelaClientes(total) {
    const cache = {}, original = sandbox.document.getElementById;
    sandbox.document.getElementById = id => cache[id] || (cache[id] = original(id));
    runInSandbox('orders = []; clientFilter = "todos"; clients = ' + JSON.stringify(Array.from({ length: total }, (_, i) => clienteN(i + 1))) + ';');
    if (typeof sandbox.setClientListQuery === 'function') sandbox.setClientListQuery('');
    return cache; // renderClientesScreen grava clientValueGrid e clientRecurrenceList neste cache
  }
  const cartoes = html => (html.match(/class="client-card"/g) || []).length;
  const restantes = html => { const m = /Mostrar mais \((\d+) restantes?\)/.exec(html); return m ? Number(m[1]) : null; };
  const clicarMostrarMais = html => {
    const botao = /<button\b[^>]*\bonclick="([^"]*)"[^>]*>\s*Mostrar mais/.exec(html);
    assert.ok(botao, 'falta o botão "Mostrar mais" na lista de clientes');
    runInSandbox(botao[1]);
  };

  test('#2813 B6 clientes: 25 clientes (o caso real) aparecem todos na primeira tela, sem clicar e sem botão "Mostrar mais"', () => isolated(async () => {
    const tela = abrirTelaClientes(25);
    sandbox.renderClientesScreen();
    const html = tela.clientRecurrenceList.innerHTML;
    assert.strictEqual(cartoes(html), 25, 'a operadora precisa ver os 25 clientes migrados sem clicar em nada');
    [1, 20, 21, 25].forEach(n => assert.ok(html.includes(clienteN(n).name), 'o cliente ' + n + ' precisa aparecer'));
    assert.strictEqual(restantes(html), null, 'com 25 clientes não sobra nada para o "Mostrar mais"');
    assert.ok(!/Mostrar mais/.test(html), 'o botão não aparece quando tudo já está na tela');
    assert.ok(tela.clientValueGrid.innerHTML.includes('>25<'), 'o resumo conta os 25 clientes');
  }));

  test('#2813 B6 clientes: 250 clientes mostram os 100 primeiros e o botão diz "Mostrar mais (150 restantes)"', () => isolated(async () => {
    const tela = abrirTelaClientes(250);
    sandbox.renderClientesScreen();
    const html = tela.clientRecurrenceList.innerHTML;
    assert.strictEqual(cartoes(html), 100);
    assert.ok(html.includes('Cliente 001 Ávila') && html.includes('Cliente 100 Ávila') && !html.includes('Cliente 101 Ávila'), 'o primeiro bloco são os 100 primeiros');
    assert.strictEqual(restantes(html), 150, 'falta o botão "Mostrar mais (150 restantes)"');
    assert.ok(tela.clientValueGrid.innerHTML.includes('>250<'), 'o resumo continua contando todos os clientes');
  }));

  test('#2813 B6 clientes: cada clique em "Mostrar mais" acrescenta 100 até os 250 e então o botão some', () => isolated(async () => {
    const tela = abrirTelaClientes(250);
    sandbox.renderClientesScreen();
    const passos = [{ cartoes: 100, restantes: 150 }, { cartoes: 200, restantes: 50 }, { cartoes: 250, restantes: null }];
    for (let i = 0; i < passos.length; i++) {
      const html = tela.clientRecurrenceList.innerHTML;
      assert.strictEqual(cartoes(html), passos[i].cartoes, 'cards depois de ' + i + ' cliques');
      assert.strictEqual(restantes(html), passos[i].restantes, 'restantes depois de ' + i + ' cliques');
      if (i < passos.length - 1) clicarMostrarMais(html);
    }
    const ultimo = tela.clientRecurrenceList.innerHTML;
    assert.ok(!/Mostrar mais/.test(ultimo), 'com todos na tela o botão some');
    [1, 100, 101, 200, 201, 250].forEach(n => assert.ok(ultimo.includes(clienteN(n).name), 'o cliente ' + n + ' precisa aparecer'));
  }));

  test('#2813 B6 clientes: buscar trecho do nome ou do telefone encontra o cliente 195, depois da primeira página, sem diferenciar maiúscula e acento', () => isolated(async () => {
    const tela = abrirTelaClientes(250);
    assert.strictEqual(typeof sandbox.setClientListQuery, 'function', 'falta a busca por nome ou telefone na tela de clientes');
    const ini = PAGINA.indexOf('data-screen="clientes"'), fim = PAGINA.indexOf('id="clientRecurrenceList"'), caixa = PAGINA.indexOf('id="clientListSearch"');
    assert.ok(ini > 0 && caixa > ini && caixa < fim, 'a caixa de busca precisa estar na tela de clientes, acima da lista');
    assert.ok(/<input\b[^>]*\bid="clientListSearch"[^>]*\boninput="setClientListQuery\(this\.value\)"/.test(PAGINA), 'a caixa de busca precisa chamar setClientListQuery');
    const buscar = texto => { sandbox.setClientListQuery(texto); return tela.clientRecurrenceList.innerHTML; };
    const alvo = clienteN(195).name;
    sandbox.renderClientesScreen();
    assert.ok(!tela.clientRecurrenceList.innerHTML.includes(alvo), 'o cliente 195 fica depois da primeira página: sem busca ele não aparece');
    ['CLIENTE 195 AVILA', '195 ávila', '90000-0195', '(11) 90000-0195', '00000195'].forEach(texto => {
      const html = buscar(texto);
      assert.strictEqual(cartoes(html), 1, 'buscar "' + texto + '" deve achar só um cliente');
      assert.ok(html.includes(alvo), 'buscar "' + texto + '" deve achar ' + alvo);
    });
    let html = buscar('avila');
    assert.strictEqual(cartoes(html), 100, 'busca que acha todos volta ao primeiro bloco');
    assert.strictEqual(restantes(html), 150);
    clicarMostrarMais(html);
    assert.strictEqual(cartoes(tela.clientRecurrenceList.innerHTML), 200, 'o botão também pagina o resultado da busca');
    assert.ok(/Nenhum cliente encontrado/.test(buscar('zzz')), 'busca sem resultado avisa em vez de mostrar lista vazia');
    html = buscar('');
    assert.strictEqual(cartoes(html), 100);
    assert.strictEqual(restantes(html), 150, 'limpar a busca volta à lista inteira paginada');
  }));
};
