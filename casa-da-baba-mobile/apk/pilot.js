(function (root) {
  'use strict';

  function validateManifest(data, config) {
    if (!data || data.schemaVersion !== 1 || data.appId !== config.appId
        || !Number.isSafeInteger(data.versionCode) || data.versionCode < 1
        || typeof data.version !== 'string' || !/^[0-9a-f]{64}$/i.test(data.sha256 || '')) {
      throw new Error('Não foi possível conferir esta atualização.');
    }
    const url = new URL(data.url);
    if (url.origin !== 'https://easystok.online' || url.username || url.password
        || !/^\/downloads\/casa-da-baba\/[a-zA-Z0-9._-]+\.apk$/.test(url.pathname)
        || url.search || url.hash) throw new Error('Endereço de atualização inválido.');
    return { ...data, available: data.versionCode > config.versionCode };
  }

  function connectionStatus({ health, paired, invalid, pending, importPending }) {
    const status = health === 'online' ? 'EasyStok acessível' : health === 'checking' ? 'Conferindo conexão' : 'Sem conexão confirmada';
    const pairing = invalid ? 'Reconecte este aparelho: o pareamento está inválido.'
      : paired ? 'Produção pareada com o EasyStok.' : 'Produção só neste aparelho. Conecte com o código de pareamento para sincronizar.';
    const queue = pending > 0 ? pending + (pending === 1 ? ' alteração aguardando envio.' : ' alterações aguardando envio.')
      : 'Nenhuma alteração na fila de envio deste aparelho.';
    const syncState = invalid ? 'invalid' : importPending ? 'pending' : !paired ? 'local' : health === 'online' && pending > 0 ? 'pending' : health;
    const railStatus = invalid ? 'Reconectar aparelho' : importPending ? 'Conferir dados importados' : !paired ? 'Só neste aparelho' : status;
    return { status, pairing, queue: importPending ? 'Envio pausado até conferir os dados importados. ' + queue : queue, syncState, rail: railStatus + (pending > 0 ? ' · ' + pending + ' pendente' + (pending === 1 ? '' : 's') : '') };
  }

  function otaStatusText(state) {
    if (state.pendingVersion) return 'Versão ' + state.pendingVersion + ' preparada. Será aplicada na próxima abertura do app. Seu trabalho atual continua aberto.';
    if (state.rolledBack) return 'A versão anterior foi recuperada. Seus pedidos e sua produção foram preservados.';
    if (state.lastError) return 'A atualização não foi concluída. A versão atual foi mantida; tente verificar novamente quando houver conexão.';
    return 'Conteúdo ' + state.activeVersion + '. Novas versões são preparadas automaticamente ao abrir ou retomar o app.';
  }

  async function confirmOtaHealth(environment, appConfig, updater) {
    if (!environment.cdbSync?.localReady) throw new Error('Inicialização local indisponível.');
    if (environment.document && environment.document.readyState !== 'complete') {
      await new Promise(resolve => environment.addEventListener('load', resolve, { once: true }));
    }
    const health = await environment.cdbSync.localReady;
    for (const name of ['etqAbrirImprimir', 'etqImpImprimir', 'etqEditorAbrir', 'initEditor']) {
      if (typeof environment[name] !== 'function') throw new Error('Módulos de etiquetas indisponíveis.');
    }
    if (health?.ready !== true) throw new Error('Armazenamento local não confirmado.');
    const state = environment.cdbApp?.getState?.();
    for (const key of ['products', 'clients', 'orders', 'cashEntries', 'batches', 'cashClosings']) {
      if (!Array.isArray(state?.[key])) throw new Error('Dados locais indisponíveis.');
    }
    const storage = environment.localStorage;
    if (storage.getItem('cdb-schema-version') !== String(appConfig.dataSchemaVersion)) throw new Error('Versão dos dados incompatível.');
    for (const key of ['cdb-products', 'cdb-clients', 'cdb-orders', 'cdb-cash', 'cdb-batches', 'cdb-cash-closings']) {
      const value = storage.getItem(key);
      if (value !== null && !Array.isArray(JSON.parse(value))) throw new Error('Dados locais inválidos.');
    }
    // Testa escrita sem sobrescrever dados operacionais, inclusive com armazenamento cheio.
    const key = 'cdb-ota-health-probe';
    const previous = storage.getItem(key);
    try {
      storage.setItem(key, appConfig.bundleVersion);
      if (storage.getItem(key) !== appConfig.bundleVersion) throw new Error('Escrita local não confirmada.');
    } finally {
      if (previous === null) storage.removeItem(key); else storage.setItem(key, previous);
    }
    await updater.ready({ bundleVersion: appConfig.bundleVersion });
  }

  if (typeof module !== 'undefined' && module.exports) module.exports = { validateManifest, connectionStatus, otaStatusText, confirmOtaHealth };
  if (!root.document || !root.CDB_CONFIG?.isPilot) return;
  const config = root.CDB_CONFIG;
  const doc = root.document;
  let latest = null;
  let lastFocus = null;
  let health = 'checking';
  let checkingUpdate = false;
  let nativeVersion = null;
  let lastAutomaticCheck = 0;
  let pendingContent = '';
  let checkingConnection = false;
  let confirmingImport = false;
  let undoingImport = false;
  let migratingImport = false;
  let importUndoRequest = 0;
  const native = () => root.Capacitor?.Plugins?.PilotApp;
  const updater = () => root.Capacitor?.Plugins?.PilotUpdater;
  const rail = doc.createElement('div');
  rail.className = 'pilot-rail';
  rail.innerHTML = '<button type="button" id="pilotHomeButton">Casa da Baba <span id="pilotRailVersion">Piloto</span></button><span id="pilotRailStatus" role="status">Conferindo conexão</span>';
  doc.body.prepend(rail);
  doc.body.classList.add('pilot-app');

  const home = doc.createElement('dialog');
  home.id = 'pilotHome';
  home.className = 'pilot-home';
  home.setAttribute('aria-labelledby', 'pilotTitle');
  home.innerHTML = `
    <div class="pilot-sheet">
      <div class="pilot-topline"><span>CASA DA BABA · PILOTO</span><button type="button" data-action="close" aria-label="Fechar início">×</button></div>
      <h1 id="pilotTitle" tabindex="-1">Produção e pedidos.</h1>
      <p class="pilot-intro">Escolha o que precisa fazer agora.</p>
      <div class="pilot-connection"><p id="pilotConnection" role="status" tabindex="-1">Conferindo a conexão com o EasyStok…</p><button type="button" id="pilotReconnect" data-action="reconnect" hidden>Verificar conexão</button></div>
      <p id="pilotError" class="pilot-feedback pilot-error" role="alert" tabindex="-1"></p>
      <section id="pilotImportReview" class="pilot-import-review" hidden aria-labelledby="pilotImportTitle"><strong id="pilotImportTitle">Confira os dados importados</strong><p id="pilotImportStatus" role="status"></p><button type="button" id="pilotConfirmImport" data-action="confirm-import">Confirmar origem e sincronizar</button><button type="button" id="pilotUndoImport" data-action="undo-import" hidden>Desfazer importação</button><p id="pilotUndoImportStatus" role="status"></p><div id="pilotMigration" hidden><p id="pilotMigrationStatus"></p><button type="button" id="pilotMigrateImport" data-action="migrate-import">Enviar primeira migração</button></div></section>
      <details id="pilotMigrationReceipt" class="pilot-help" hidden><summary>Última migração confirmada</summary><p id="pilotMigrationResult" class="pilot-feedback" role="status" tabindex="-1"></p></details>
      <div class="pilot-actions">
        <button type="button" class="pilot-card pilot-production" data-action="production"><span class="pilot-step">PRODUÇÃO NESTE APARELHO</span><strong>Produzir e etiquetar</strong><span>Cadastro, validade, lote e impressão.</span><b aria-hidden="true">↗</b></button>
        <button type="button" class="pilot-card pilot-orders" data-action="workspace"><span class="pilot-step">ATENDIMENTO ONLINE</span><strong>Pedidos e cobranças</strong><span>Abra o EasyStok para frete, mensagens e assistente.</span><b aria-hidden="true">↗</b></button>
      </div>
      <p class="pilot-hint">No Atendimento, entre com seu acesso da Casa da Baba. As alterações são feitas na operação real.</p>
      <button type="button" class="pilot-local" data-action="local-orders"><strong>Registrar pedido no aparelho</strong><span>Sem cálculo de frete nem envio automático. A sincronização exige pareamento.</span></button>
      <details class="pilot-help pilot-migration"><summary>Já usava o PWA antigo?</summary><p>No Android da operação, abra os Ajustes do PWA antigo e exporte o backup em JSON. Importe esse arquivo aqui antes de conectar este app. Não apague o PWA antigo até conferir os dados.</p><button type="button" data-action="import-pwa">Trazer dados do PWA</button><p>Se está começando agora, basta conectar a produção.</p></details>
      <div class="pilot-shortcuts">
        <button type="button" data-action="printers">Impressoras</button>
        <button type="button" data-action="notifications">Notificações</button>
        <button type="button" data-action="migration-guide">Como trazer meus dados</button>
        <button type="button" id="pilotPairButton" data-action="pair" aria-describedby="pilotPairHint">Conectar produção</button>
      </div>
      <p id="pilotPairHint" class="pilot-hint">Solicite o código de pareamento à equipe da Casa da Baba para conectar este aparelho.</p>
      <details class="pilot-help"><summary>O que funciona sem conexão?</summary><p>O cadastro local de produtos e pedidos, a produção e o histórico ficam neste aparelho. Após o primeiro pareamento, as alterações aguardam a conexão para sincronizar. A impressão exige uma impressora compatível conectada.</p><p>Frete, cobrança online, envio pelo atendimento e IA precisam de conexão com o EasyStok. Uma mensagem preparada ou um pedido salvo no aparelho ainda não confirmam envio nem pagamento.</p></details>
      <details class="pilot-help"><summary>Atualizações e recuperação</summary><p id="pilotOtaStatus" role="status">A atualização automática de conteúdo está disponível no APK Android.</p><p id="pilotPushStatus" role="status"></p><p>O app guarda a versão anterior do conteúdo. Voltar a ela preserva os pedidos, a produção e a fila de envio. O Atendimento online segue a versão publicada no EasyStok.</p><button type="button" id="pilotRollback" data-action="rollback" hidden>Voltar à versão anterior</button></details>
      <div class="pilot-update"><span id="pilotVersion" role="status"></span><button type="button" id="pilotUpdateButton" data-action="update">Verificar atualização</button></div>
      <p id="pilotUpdateStatus" class="pilot-feedback" role="status" tabindex="-1"></p>
      <button type="button" id="pilotDownload" data-action="download" hidden>Atualizar APK</button>
    </div>`;
  doc.body.append(home);
  function installedConfig() { return { ...config, versionCode: nativeVersion?.code || config.versionCode }; }
  function showVersion() {
    const installed = nativeVersion ? 'APK ' + nativeVersion.version + ' (' + nativeVersion.code + ')' : (root.Capacitor?.isNativePlatform?.() ? 'APK: aguardando confirmação do aparelho' : 'Prévia no navegador');
    const content = config.bundleVersion || config.version || 'não identificado';
    doc.getElementById('pilotVersion').textContent = installed + ' · Conteúdo ' + content
      + (pendingContent ? ' · Próximo conteúdo: ' + pendingContent + ' ao reabrir' : '');
    doc.getElementById('pilotRailVersion').textContent = (nativeVersion ? 'APK ' + nativeVersion.version : 'Piloto')
      + (latest?.available ? ' · atualização disponível' : pendingContent ? ' · conteúdo pronto' : '');
  }
  showVersion();

  function openHome() {
    if (home.open) return;
    lastFocus = doc.activeElement;
    home.showModal();
    doc.getElementById('pilotTitle').focus();
    refreshHealth();
    refreshOtaStatus();
  }
  function closeHome() { home.close(); }
  function showError(message) {
    const error = doc.getElementById('pilotError');
    error.textContent = message;
    if (message) { error.focus(); error.scrollIntoView({ block: 'nearest' }); }
  }
  function showConnection() {
    const pairing = root.cdbSync?.getPairing?.();
    const review = root.cdbSync?.importReview?.();
    const invalid = !!root.cdbSync?.isPairingInvalid?.();
    const state = connectionStatus({ health, paired: !!pairing, invalid, pending: root.cdbSync?.queueSize?.() || 0, importPending: !!review });
    doc.getElementById('pilotRailStatus').textContent = state.rail;
    doc.getElementById('pilotConnection').textContent = state.status + '. ' + state.pairing + ' ' + state.queue;
    const reconnect = doc.getElementById('pilotReconnect');
    reconnect.hidden = health !== 'offline' && !checkingConnection;
    reconnect.disabled = checkingConnection;
    reconnect.textContent = checkingConnection ? 'Conferindo…' : 'Verificar conexão';
    doc.getElementById('pilotPairButton').textContent = state.syncState === 'invalid' ? 'Reconectar produção' : root.cdbSync?.getPairing?.() ? 'Alterar conexão' : 'Conectar produção';
    rail.dataset.sync = state.syncState;
    doc.getElementById('pilotImportReview').hidden = !review;
    const confirm = doc.getElementById('pilotConfirmImport');
    confirm.disabled = confirmingImport || undoingImport || migratingImport || !pairing || invalid || !!review?.invalid || !!review?.requiresReconciliation;
    confirm.hidden = !!review?.requiresReconciliation;
    showMigration(review);
    confirm.textContent = confirmingImport ? 'Conferindo e sincronizando…' : 'Confirmar origem e sincronizar';
    showImportUndo(review);
    if (!migratingImport && !review) showMigrationReceipt(root.cdbSync?.getLastLegacyMigration?.());
    if (review) {
      const photos = Number(review.missingPhotos) > 0 ? ' Há ' + review.missingPhotos + ' foto(s) ausente(s) no arquivo; confira no PWA de origem.' : '';
      doc.getElementById('pilotImportStatus').textContent = (review.invalid
        ? 'A revisão deste backup não está disponível. Importe novamente o arquivo de origem. Os dados continuam neste aparelho.'
        : review.requiresReconciliation
          ? 'Dados importados aguardam conferência. Revise com a equipe os registros do PWA de origem e do EasyStok para identificar o que ainda precisa ser enviado. Não apague os dados de origem.'
          : !pairing || invalid
            ? 'Os dados estão neste aparelho, com envio pausado. Conecte a produção e confirme a empresa e a loja de origem para liberar a sincronização.'
            : 'Os dados estão neste aparelho, com envio pausado. Confira se o backup pertence a ' + pairingDestination(pairing) + ' antes de liberar a sincronização.') + photos;
    }
  }
  function migrationCounts(counts = {}) {
    const labels = {products:['produto','produtos'],clients:['cliente','clientes'],orders:['pedido','pedidos'],cashEntries:['lançamento de caixa','lançamentos de caixa'],batches:['lote','lotes'],cashClosings:['fechamento de caixa','fechamentos de caixa']};
    return Object.entries(labels).filter(([key]) => Number.isInteger(counts[key]) && counts[key] >= 0)
      .map(([key,label]) => counts[key] + ' ' + label[counts[key] === 1 ? 0 : 1]).join(', ');
  }
  function migrationArchiveNote(state) {
    return state.counts?.cashClosings > 0 || state.archiveOnlyCollections?.includes('cashClosings')
      ? 'Fechamentos antigos serão preservados no backup; não reabrem nem recalculam caixa do EasyStok.' : '';
  }
  function migrationWarnings(state) {
    return [...new Set([...(Array.isArray(state.warnings) ? state.warnings : []), migrationArchiveNote(state)].filter(Boolean))].join(' ');
  }
  function showMigration(review) {
    const section = doc.getElementById('pilotMigration');
    section.hidden = !review?.requiresReconciliation || typeof root.cdbSync?.getImportedMigrationState !== 'function';
    if (section.hidden) return;
    const state = root.cdbSync.getImportedMigrationState();
    const button = doc.getElementById('pilotMigrateImport');
    button.disabled = !state.available || migratingImport || undoingImport || confirmingImport;
    button.textContent = migratingImport ? 'Enviando primeira migração…' : state.prepared ? 'Retomar migração' : 'Enviar primeira migração';
    doc.getElementById('pilotMigrationStatus').textContent = (state.available
      ? 'Somente para registros que nunca foram enviados ao EasyStok. Confira: ' + migrationCounts(state.counts) + '. '
      : (state.reason || 'A primeira migração ainda não está disponível.') + ' ') + migrationWarnings(state);
  }
  function showMigrationReceipt(ack, focus = false) {
    if (!ack) return;
    const receipt = doc.getElementById('pilotMigrationReceipt'), result = doc.getElementById('pilotMigrationResult');
    receipt.hidden = false;
    result.textContent = 'Primeira migração confirmada pelo EasyStok. ' + migrationCounts(ack.counts) + '. ' + migrationWarnings(ack);
    if (focus) { receipt.open = true; result.focus(); result.scrollIntoView({block:'nearest'}); }
  }
  async function migrateImport() {
    if (migratingImport || undoingImport || confirmingImport) return;
    const state = root.cdbSync.getImportedMigrationState(), pairing = root.cdbSync.getPairing();
    if (!state.available || !pairing) throw new Error(state.reason || 'Conecte a produção e confira o backup antes de migrar.');
    const question = (state.prepared
      ? 'Retomar a mesma migração? O servidor pode já ter recebido a primeira tentativa. Confirme a origem dos registros, que estavam apenas no Android antes desta migração.'
      : 'Confirma que estes registros do Android nunca foram enviados ao EasyStok?') + '\n\nDestino: ' + pairingDestination(pairing)
      + '.\nRegistros: ' + migrationCounts(state.counts) + '.\n\n' + migrationWarnings(state)
      + '\nDepois de exportar, registre novos pedidos e produção apenas neste app. Preserve o PWA antigo para consulta, sem reenviar sua fila.'
      + '\nPedidos históricos prontos, entregues ou cancelados não geram novo movimento nem pagamento. ' + (state.prepared ? 'Retomar esta migração?' : 'Enviar esta primeira migração?');
    if (!root.confirm(question)) return;
    migratingImport = true;
    doc.getElementById('pilotMigrationReceipt').hidden = true;
    doc.getElementById('pilotMigrationResult').textContent = '';
    const pairButton = doc.getElementById('pilotPairButton'); pairButton.disabled = true;
    showConnection();
    try {
      const ack = await root.cdbSync.migrateImportedLegacyData({sourceNeverSynced:true,expectedEmpresaId:pairing.empresaId,expectedLojaId:pairing.lojaId});
      showMigrationReceipt(ack, true);
    } finally { migratingImport = false; pairButton.disabled = false; showConnection(); }
  }
  async function showImportUndo(review) {
    const request = ++importUndoRequest;
    const button = doc.getElementById('pilotUndoImport');
    const message = doc.getElementById('pilotUndoImportStatus');
    button.hidden = !review || typeof root.getBackupImportUndoState !== 'function';
    button.disabled = true;
    message.textContent = '';
    if (button.hidden) return;
    button.textContent = undoingImport ? 'Desfazendo importação…' : 'Desfazer importação';
    try {
      const state = await root.getBackupImportUndoState();
      if (request !== importUndoRequest) return;
      button.disabled = !state.available || confirmingImport || undoingImport || migratingImport;
      message.textContent = state.available ? 'Desfazer restaura os dados e a fila anteriores à importação, desde que não haja trabalho novo.' : state.reason;
    } catch {
      if (request === importUndoRequest) message.textContent = 'Não foi possível conferir a restauração. Preserve os dados e tente novamente.';
    }
  }
  async function undoImport() {
    if (undoingImport || confirmingImport || migratingImport || !root.cdbSync?.importReview?.()) return;
    const state = await root.getBackupImportUndoState();
    if (!state.available) throw new Error(state.reason || 'Há alterações posteriores à importação. Preserve os dados antes de continuar.');
    if (!root.confirm('Desfazer todos os arquivos importados nesta revisão? Os dados e a fila anteriores à importação serão restaurados. O app será recarregado.')) return;
    undoingImport = true; showConnection();
    try { await root.undoBackupImport(); root.location.reload(); }
    finally { undoingImport = false; showConnection(); }
  }
  function pairingDestination(pairing) {
    return 'empresa ' + (pairing.empresaNome || pairing.empresaId) + ', loja ' + (pairing.lojaNome || pairing.lojaId);
  }
  async function confirmImport() {
    if (confirmingImport || undoingImport || migratingImport) return;
    const review = root.cdbSync?.importReview?.(), pairing = root.cdbSync?.getPairing?.();
    if (!review) return;
    if (!pairing || root.cdbSync?.isPairingInvalid?.()) throw new Error('Conecte a produção antes de confirmar a origem do backup.');
    if (review.invalid || review.requiresReconciliation) throw new Error('Revise com a equipe os dados do PWA de origem e do EasyStok antes de liberar a sincronização.');
    const photos = Number(review.missingPhotos) > 0 ? '\nO arquivo não contém ' + review.missingPhotos + ' foto(s). Confira essas imagens no PWA de origem.' : '';
    if (!root.confirm('Confirma que o backup importado pertence a ' + pairingDestination(pairing) + '? Ao confirmar, o app poderá enviar as alterações pendentes para essa operação.' + photos)) return;
    confirmingImport = true; showConnection();
    try { await root.cdbSync.confirmImportedDataContext(); }
    finally { confirmingImport = false; showConnection(); }
    const target = doc.getElementById('pilotConnection'); target.focus(); target.scrollIntoView({ block: 'nearest' });
  }
  async function refreshHealth() {
    if (checkingConnection) return;
    health = root.navigator?.onLine === false ? 'offline' : 'checking'; showConnection();
    if (health === 'offline') return;
    checkingConnection = true; showConnection();
    try {
      const response = await fetch(config.apiBaseUrl + '/api/mobile/version', { signal: AbortSignal.timeout(8000), cache: 'no-store' });
      if (!response.ok) throw new Error();
      const body = await response.json();
      health = body.databaseOk === true && root.navigator?.onLine !== false ? 'online' : 'offline';
    } catch { health = 'offline'; }
    finally { checkingConnection = false; }
    showConnection();
  }
  async function openWorkspace() {
    if (root.navigator?.onLine === false) {
      showError('O aparelho está sem conexão. Verifique a conexão ou use “Registrar pedido no aparelho” para continuar localmente.');
      return;
    }
    if (native()?.openWorkspace) await native().openWorkspace();
    else root.open('https://app.easystok.online/#/m/atendimento/balcao', '_blank', 'noopener,noreferrer');
  }
  async function connectProduction() {
    const button = doc.getElementById('pilotPairButton');
    button.disabled = true;
    let result;
    try { result = await root.pairDevicePrompt(); }
    finally { button.disabled = false; }
    showConnection();
    const target = result?.paired ? doc.getElementById('pilotConnection') : button;
    target.focus(); target.scrollIntoView({ block: 'nearest' });
  }
  function showOtaStatus(state) {
    if (Number.isSafeInteger(state.nativeVersionCode) && state.nativeVersionCode > 0
        && typeof state.builtinVersion === 'string' && state.builtinVersion) {
      nativeVersion = { code: state.nativeVersionCode, version: state.builtinVersion };
    }
    pendingContent = state.pendingVersion || '';
    showVersion();
    doc.getElementById('pilotOtaStatus').textContent = otaStatusText(state);
    doc.getElementById('pilotRollback').hidden = !state.canRollback || !!state.pendingVersion;
    doc.getElementById('pilotPushStatus').textContent = state.pushSubscribed
      ? 'Canal de atualização conectado. O Android pode adiar o recebimento em economia de bateria. A verificação ao abrir continua disponível.'
      : state.pushError ? 'O canal de atualização ainda não conectou. A verificação ao abrir continua disponível.' : 'Conectando o canal de atualização…';
  }
  async function refreshOtaStatus() {
    if (!updater()) return;
    try { showOtaStatus(await updater().status()); } catch { /* Uma falha de consulta não interrompe a operação. */ }
  }
  async function rollbackContent() {
    if (!updater() || !root.confirm('Preparar a versão anterior para a próxima abertura? Os pedidos e a produção serão preservados.')) return;
    const button = doc.getElementById('pilotRollback');
    button.disabled = true;
    try { showOtaStatus(await updater().rollback()); }
    finally { button.disabled = false; }
  }
  async function startUpdater() {
    if (!updater()) return;
    try {
      await confirmOtaHealth(root, config, updater());
      showOtaStatus(await updater().check());
    } catch {
      doc.getElementById('pilotOtaStatus').textContent = 'Não foi possível confirmar a atualização. Se esta for uma versão nova, a anterior será recuperada na próxima abertura.';
    }
  }
  async function checkUpdate(options = {}) {
    const automatic = options.automatic === true;
    if (!automatic) openHome();
    if (checkingUpdate) return;
    if (automatic) {
      if (root.navigator?.onLine === false || doc.hidden || (lastAutomaticCheck && Date.now() - lastAutomaticCheck < 60000)) return;
      lastAutomaticCheck = Date.now();
    }
    checkingUpdate = true;
    const message = doc.getElementById('pilotUpdateStatus');
    const download = doc.getElementById('pilotDownload');
    const button = doc.getElementById('pilotUpdateButton');
    button.disabled = true;
    download.hidden = true; latest = null; showVersion();
    message.textContent = 'Conferindo a versão disponível…';
    if (!automatic) { message.focus(); message.scrollIntoView({ block: 'nearest' }); }
    try {
      if (updater()) {
        try { showOtaStatus(await (automatic ? updater().status() : updater().check())); }
        catch { doc.getElementById('pilotOtaStatus').textContent = 'Não foi possível preparar a atualização de conteúdo. A versão atual foi mantida.'; }
      }
      let data;
      const http = root.Capacitor?.Plugins?.CapacitorHttp;
      if (root.Capacitor?.isNativePlatform?.() && http) {
        const response = await http.get({ url: config.updateManifestUrl, connectTimeout: 10000, readTimeout: 10000, headers: { 'Cache-Control': 'no-cache' } });
        if (response.status !== 200) throw new Error('Não foi possível consultar a atualização. Tente novamente com conexão.');
        data = typeof response.data === 'string' ? JSON.parse(response.data) : response.data;
      } else {
        const response = await fetch(config.updateManifestUrl, { cache: 'no-store', signal: AbortSignal.timeout(10000) });
        if (!response.ok) throw new Error('Não foi possível consultar a atualização. Tente novamente com conexão.');
        data = await response.json();
      }
      if (root.Capacitor?.isNativePlatform?.() && !nativeVersion) throw new Error('Versão nativa não confirmada.');
      latest = validateManifest(data, installedConfig());
      message.textContent = latest.available ? 'Versão ' + latest.version + ' disponível. Toque em Atualizar APK e confirme a instalação no Android, sem desinstalar.' : 'O APK instalado está atualizado. A versão do conteúdo aparece em Atualizações e recuperação.';
      download.hidden = !latest.available;
      showVersion();
    } catch {
      message.textContent = 'Não foi possível conferir a atualização. Tente novamente com conexão.';
    } finally {
      checkingUpdate = false;
      button.disabled = false;
    }
  }
  async function downloadUpdate() {
    if (!latest?.available) return;
    const checked = validateManifest(latest, installedConfig());
    if (native()?.openDownload) await native().openDownload({ url: checked.url });
    else root.open(checked.url, '_blank', 'noopener,noreferrer');
  }
  home.addEventListener('click', async function (event) {
    const action = event.target.closest('[data-action]')?.dataset.action;
    if (!action) return;
    showError('');
    try {
      if (action === 'close') closeHome();
      if (action === 'production') { closeHome(); root.go('producao'); }
      if (action === 'local-orders') { closeHome(); root.go('pedidos'); }
      if (action === 'printers') { closeHome(); root.openPrinterPicker(); }
      if (action === 'import-pwa') {
        const input = doc.getElementById('backupFileInput');
        if (!input) throw new Error('Importação indisponível. Abra os Ajustes e tente novamente.');
        closeHome(); input.click();
      }
      if (action === 'notifications' || action === 'migration-guide') {
        const app = root.Capacitor?.Plugins?.PilotApp;
        const method = action === 'notifications' ? 'notifications' : 'migrationGuide';
        if (!app?.[method]) throw new Error('Atualize o APK para usar notificações e o guia no Android.');
        await app[method]();
      }
      if (action === 'pair') await connectProduction();
      if (action === 'confirm-import') await confirmImport();
      if (action === 'undo-import') await undoImport();
      if (action === 'migrate-import') await migrateImport();
      if (action === 'workspace') await openWorkspace();
      if (action === 'reconnect') await refreshHealth();
      if (action === 'update') await checkUpdate();
      if (action === 'download') await downloadUpdate();
      if (action === 'rollback') await rollbackContent();
    } catch (error) {
      openHome();
      showError(error.message || 'Não foi possível abrir. Tente novamente.');
    }
  });
  home.addEventListener('close', () => {
    const target = lastFocus?.isConnected && lastFocus.getClientRects().length ? lastFocus : doc.getElementById('pilotHomeButton');
    target.focus();
  });
  doc.getElementById('pilotHomeButton').addEventListener('click', openHome);
  root.addEventListener('online', () => { refreshHealth(); checkUpdate({ automatic: true }); });
  root.addEventListener('focus', () => checkUpdate({ automatic: true }));
  doc.addEventListener?.('visibilitychange', () => { if (!doc.hidden) checkUpdate({ automatic: true }); });
  root.addEventListener('offline', () => { health = 'offline'; showConnection(); });
  root.addEventListener('cdb-sync-success', showConnection);
  root.addEventListener('cdb-sync-state-change', showConnection);
  root.addEventListener('cdb-initial-pull-done', showConnection);
  root.addEventListener('cdb-mutations-applied', showConnection);
  root.addEventListener('cdb-operator-ready', openHome);
  root.forceCheckUpdate = checkUpdate;
  if (updater()?.addListener) updater().addListener('status', state => {
    showOtaStatus(state);
    checkUpdate({ automatic: true });
  }).catch(() => {});
  startUpdater();
  Promise.resolve().then(() => checkUpdate({ automatic: true }));
  if (root.cdbApp?.getOperator?.()?.trim()) openHome();
})(typeof window !== 'undefined' ? window : globalThis);
