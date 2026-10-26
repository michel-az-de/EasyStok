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

  if (typeof module !== 'undefined' && module.exports) module.exports = { validateManifest };
  if (!root.document || !root.CDB_CONFIG?.isPilot) return;
  const config = root.CDB_CONFIG;
  const doc = root.document;
  let latest = null;
  let lastFocus = null;
  let health = 'checking';
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
      <h1 id="pilotTitle">A cozinha e os pedidos,<br>na palma da mão.</h1>
      <p class="pilot-intro">Escolha por onde começar.</p>
      <p class="pilot-connection" id="pilotConnection" role="status">Conferindo a conexão com o EasyStok…</p>
      <div class="pilot-actions">
        <button type="button" class="pilot-card pilot-production" data-action="production"><span class="pilot-step">01 · PRODUÇÃO</span><strong>Produzir e etiquetar</strong><span>Cadastro, validade, lote e impressão.</span><b aria-hidden="true">↗</b></button>
        <button type="button" class="pilot-card pilot-orders" data-action="workspace"><span class="pilot-step">02 · ATENDIMENTO ONLINE</span><strong>Pedidos e cobranças</strong><span>Frete, esteira, mensagens e assistente da Casa.</span><b aria-hidden="true">↗</b></button>
      </div>
      <p class="pilot-hint">No Atendimento, entre com seu acesso da Casa da Baba. As alterações são feitas na operação real.</p>
      <div class="pilot-shortcuts">
        <button type="button" data-action="local-orders">Pedidos locais</button>
        <button type="button" data-action="printers">Impressoras</button>
        <button type="button" data-action="pair">Conectar produção</button>
      </div>
      <details class="pilot-help"><summary>O que funciona sem conexão?</summary><p>O cadastro local de produtos e pedidos, a produção e o histórico ficam neste aparelho. Após o primeiro pareamento, as alterações aguardam a conexão para sincronizar. A impressão exige uma impressora compatível conectada.</p><p>Frete, cobrança online, envio pelo atendimento e IA precisam de conexão com o EasyStok. Uma mensagem preparada ou um pedido salvo no aparelho ainda não confirmam envio nem pagamento.</p></details>
      <div class="pilot-update"><span id="pilotVersion"></span><button type="button" data-action="update">Verificar atualização</button></div>
      <p id="pilotUpdateStatus" class="pilot-feedback" role="status"></p>
      <button type="button" id="pilotDownload" data-action="download" hidden>Baixar atualização</button>
      <p id="pilotError" class="pilot-feedback pilot-error" role="alert"></p>
    </div>`;
  doc.body.append(home);
  doc.getElementById('pilotVersion').textContent = 'Versão ' + config.version;

  function openHome() {
    if (home.open) return;
    lastFocus = doc.activeElement;
    home.showModal();
    refreshHealth();
  }
  function closeHome() { home.close(); }
  function showError(message) { doc.getElementById('pilotError').textContent = message; }
  function showConnection() {
    const pairing = root.cdbSync?.getPairing?.();
    const paired = !!pairing && !root.cdbSync?.isPairingInvalid?.();
    const pending = root.cdbSync?.queueSize?.() || 0;
    const status = health === 'online' ? 'EasyStok acessível' : health === 'checking' ? 'Conferindo conexão' : 'Trabalhando no aparelho';
    doc.getElementById('pilotRailStatus').textContent = status;
    doc.getElementById('pilotConnection').textContent = status + '. ' +
      (paired ? 'Produção conectada.' : 'Conecte a produção com o código do aparelho.') +
      (pending ? ' ' + pending + ' alteração(ões) aguardando envio.' : '');
    rail.dataset.online = String(health === 'online');
  }
  async function refreshHealth() {
    health = 'checking'; showConnection();
    try {
      const response = await fetch(config.apiBaseUrl + '/api/mobile/version', { signal: AbortSignal.timeout(8000), cache: 'no-store' });
      if (!response.ok) throw new Error();
      const body = await response.json();
      health = body.databaseOk === true ? 'online' : 'offline';
    } catch { health = 'offline'; }
    showConnection();
  }
  async function openWorkspace() {
    showError('');
    if (native()?.openWorkspace) {
      await native().openWorkspace();
    } else {
      // Browser preview uses the same real service; native build opens its own activity.
      root.open('https://app.easystok.online', '_blank', 'noopener,noreferrer');
    }
  }
  async function checkUpdate() {
    const message = doc.getElementById('pilotUpdateStatus');
    const download = doc.getElementById('pilotDownload');
    download.hidden = true; latest = null;
    message.textContent = 'Conferindo a versão disponível…';
    try {
      let data;
      const http = root.Capacitor?.Plugins?.CapacitorHttp;
      if (root.Capacitor?.isNativePlatform?.() && http) {
        const response = await http.get({ url: config.updateManifestUrl, connectTimeout: 10000, readTimeout: 10000, headers: { 'Cache-Control': 'no-cache' } });
        if (response.status !== 200) throw new Error('A atualização ainda não está disponível.');
        data = typeof response.data === 'string' ? JSON.parse(response.data) : response.data;
      } else {
        const response = await fetch(config.updateManifestUrl, { cache: 'no-store', signal: AbortSignal.timeout(10000) });
        if (!response.ok) throw new Error('A atualização ainda não está disponível.');
        data = await response.json();
      }
      latest = validateManifest(data, config);
      message.textContent = latest.available ? 'Versão ' + latest.version + ' disponível. O Android pedirá a confirmação para instalar.' : 'Você está na versão mais recente disponível.';
      download.hidden = !latest.available;
    } catch (error) {
      message.textContent = error.message || 'Não foi possível verificar agora. Tente com conexão.';
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
      if (action === 'pair') { closeHome(); root.openDiagnostics(); }
      if (action === 'workspace') await openWorkspace();
      if (action === 'update') await checkUpdate();
      if (action === 'download') await downloadUpdate();
    } catch (error) { showError(error.message || 'Não foi possível abrir. Tente novamente.'); }
  });
  home.addEventListener('close', () => { if (lastFocus?.isConnected) lastFocus.focus(); });
  doc.getElementById('pilotHomeButton').addEventListener('click', openHome);
  root.addEventListener('online', refreshHealth);
  root.addEventListener('offline', () => { health = 'offline'; showConnection(); });
  root.addEventListener('cdb-sync-success', showConnection);
  root.forceCheckUpdate = checkUpdate;
  openHome();
})(typeof window !== 'undefined' ? window : globalThis);
