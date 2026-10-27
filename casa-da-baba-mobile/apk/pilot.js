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

  function connectionStatus({ health, paired, invalid, pending }) {
    const status = health === 'online' ? 'EasyStok acessível' : health === 'checking' ? 'Conferindo conexão' : 'Sem conexão confirmada';
    const pairing = invalid ? 'Reconecte este aparelho: o pareamento está inválido.'
      : paired ? 'Produção pareada com o EasyStok.' : 'Produção só neste aparelho. Conecte com o código de pareamento para sincronizar.';
    const queue = pending > 0 ? pending + (pending === 1 ? ' alteração aguardando envio.' : ' alterações aguardando envio.')
      : 'Nenhuma alteração na fila de envio deste aparelho.';
    return { status, pairing, queue, rail: status + (pending > 0 ? ' · ' + pending + ' pendente' + (pending === 1 ? '' : 's') : '') };
  }

  if (typeof module !== 'undefined' && module.exports) module.exports = { validateManifest, connectionStatus };
  if (!root.document || !root.CDB_CONFIG?.isPilot) return;
  const config = root.CDB_CONFIG;
  const doc = root.document;
  let latest = null;
  let lastFocus = null;
  let health = 'checking';
  let checkingUpdate = false;
  const native = () => root.Capacitor?.Plugins?.PilotApp;
  const rail = doc.createElement('div');
  rail.className = 'pilot-rail';
  rail.innerHTML = '<button type="button" id="pilotHomeButton">Casa da Baba <span>Piloto</span></button><span id="pilotRailStatus" role="status">Conferindo conexão</span>';
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
      <div class="pilot-actions">
        <button type="button" class="pilot-card pilot-production" data-action="production"><span class="pilot-step">PRODUÇÃO NESTE APARELHO</span><strong>Produzir e etiquetar</strong><span>Cadastro, validade, lote e impressão.</span><b aria-hidden="true">↗</b></button>
        <button type="button" class="pilot-card pilot-orders" data-action="workspace"><span class="pilot-step">ATENDIMENTO ONLINE</span><strong>Pedidos e cobranças</strong><span>Abra o EasyStok para frete, mensagens e assistente.</span><b aria-hidden="true">↗</b></button>
      </div>
      <p class="pilot-hint">No Atendimento, entre com seu acesso da Casa da Baba. As alterações são feitas na operação real.</p>
      <button type="button" class="pilot-local" data-action="local-orders"><strong>Registrar pedido no aparelho</strong><span>Sem cálculo de frete nem envio automático. A sincronização exige pareamento.</span></button>
      <div class="pilot-shortcuts">
        <button type="button" data-action="printers">Impressoras</button>
        <button type="button" id="pilotPairButton" data-action="pair" aria-describedby="pilotPairHint">Conectar produção</button>
      </div>
      <p id="pilotPairHint" class="pilot-hint">Solicite o código de pareamento à equipe da Casa da Baba para conectar este aparelho.</p>
      <details class="pilot-help"><summary>O que funciona sem conexão?</summary><p>O cadastro local de produtos e pedidos, a produção e o histórico ficam neste aparelho. Após o primeiro pareamento, as alterações aguardam a conexão para sincronizar. A impressão exige uma impressora compatível conectada.</p><p>Frete, cobrança online, envio pelo atendimento e IA precisam de conexão com o EasyStok. Uma mensagem preparada ou um pedido salvo no aparelho ainda não confirmam envio nem pagamento.</p></details>
      <div class="pilot-update"><span id="pilotVersion"></span><button type="button" id="pilotUpdateButton" data-action="update">Verificar atualização</button></div>
      <p id="pilotUpdateStatus" class="pilot-feedback" role="status" tabindex="-1"></p>
      <button type="button" id="pilotDownload" data-action="download" hidden>Baixar atualização</button>
    </div>`;
  doc.body.append(home);
  doc.getElementById('pilotVersion').textContent = 'Versão ' + config.version;

  function openHome() {
    if (home.open) return;
    lastFocus = doc.activeElement;
    home.showModal();
    doc.getElementById('pilotTitle').focus();
    refreshHealth();
  }
  function closeHome() { home.close(); }
  function showError(message) {
    const error = doc.getElementById('pilotError');
    error.textContent = message;
    if (message) { error.focus(); error.scrollIntoView({ block: 'nearest' }); }
  }
  function showConnection() {
    const state = connectionStatus({ health, paired: !!root.cdbSync?.getPairing?.(), invalid: !!root.cdbSync?.isPairingInvalid?.(), pending: root.cdbSync?.queueSize?.() || 0 });
    doc.getElementById('pilotRailStatus').textContent = state.rail;
    doc.getElementById('pilotConnection').textContent = state.status + '. ' + state.pairing + ' ' + state.queue;
    doc.getElementById('pilotReconnect').hidden = health !== 'offline';
    rail.dataset.online = String(health === 'online');
  }
  async function refreshHealth() {
    health = root.navigator?.onLine === false ? 'offline' : 'checking'; showConnection();
    if (health === 'offline') return;
    try {
      const response = await fetch(config.apiBaseUrl + '/api/mobile/version', { signal: AbortSignal.timeout(8000), cache: 'no-store' });
      if (!response.ok) throw new Error();
      const body = await response.json();
      health = body.databaseOk === true && root.navigator?.onLine !== false ? 'online' : 'offline';
    } catch { health = 'offline'; }
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
  async function checkUpdate() {
    openHome();
    if (checkingUpdate) return;
    checkingUpdate = true;
    const message = doc.getElementById('pilotUpdateStatus');
    const download = doc.getElementById('pilotDownload');
    const button = doc.getElementById('pilotUpdateButton');
    button.disabled = true;
    download.hidden = true; latest = null;
    message.textContent = 'Conferindo a versão disponível…';
    message.focus(); message.scrollIntoView({ block: 'nearest' });
    try {
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
      latest = validateManifest(data, config);
      message.textContent = latest.available ? 'Versão ' + latest.version + ' disponível. O Android pedirá a confirmação para instalar.' : 'Você está na versão mais recente disponível.';
      download.hidden = !latest.available;
    } catch {
      message.textContent = 'Não foi possível conferir a atualização. Tente novamente com conexão.';
    } finally {
      checkingUpdate = false;
      button.disabled = false;
    }
  }
  async function downloadUpdate() {
    if (!latest?.available) return;
    const checked = validateManifest(latest, config);
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
      if (action === 'pair') await connectProduction();
      if (action === 'workspace') await openWorkspace();
      if (action === 'reconnect') await refreshHealth();
      if (action === 'update') await checkUpdate();
      if (action === 'download') await downloadUpdate();
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
  root.addEventListener('online', refreshHealth);
  root.addEventListener('offline', () => { health = 'offline'; showConnection(); });
  root.addEventListener('cdb-sync-success', showConnection);
  root.addEventListener('cdb-operator-ready', openHome);
  root.forceCheckUpdate = checkUpdate;
  if (root.cdbApp?.getOperator?.()?.trim()) openHome();
})(typeof window !== 'undefined' ? window : globalThis);
