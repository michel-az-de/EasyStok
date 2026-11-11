(function (root) {
  'use strict';
  if (!root.document) return;
  const doc = root.document;
  const native = () => root.Capacitor?.Plugins?.PilotApp;
  const available = () => typeof native()?.authStatus === 'function';
  let profile = null, restoring = null, linking = null, busy = false, pendingConnection = null;
  let generation = 0, nativeVersion = -1, restoringGeneration = -1, linkingGeneration = -1, operation = null;
  let syncReady = false;
  let dialog, title, description, notice, loginButton, logoutButton, confirmButton, shopSelect, shopButton, shopLabel;

  function sync() { return root.cdbSync; }
  function context() {
    const current = sync()?.getDataContext?.();
    if (current?.invalid) throw new Error('Não foi possível conferir o vínculo dos dados deste aparelho. Seus dados foram mantidos; procure a responsável.');
    const deviceId = sync()?.getDeviceId?.();
    if (!deviceId) throw new Error('Aguarde o aplicativo terminar de abrir e tente novamente.');
    return { dispositivoId: deviceId, ...(current?.empresaId ? { empresaId: current.empresaId } : {}), ...(current?.lojaId ? { lojaId: current.lojaId } : {}) };
  }

  function signal() { root.dispatchEvent(new CustomEvent('cdb-session-state', { detail: { autenticado: !!profile?.autenticado } })); }
  function message(text) { if (notice) notice.textContent = text || ''; }
  function draw() {
    if (!dialog) return;
    const authenticated = !!profile?.autenticado;
    title.textContent = authenticated ? 'Sua conta' : 'Entrar com Google';
    description.textContent = authenticated ? `${profile.usuario?.nome || 'Conta conectada'}${sync()?.getPairing?.()?.empresaNome ? ' · ' + sync().getPairing().empresaNome : ''}`
      : 'Entre uma vez para usar o Atendimento e conectar este aparelho ao EasyStok.';
    loginButton.hidden = authenticated;
    logoutButton.hidden = !authenticated;
    loginButton.disabled = busy;
    logoutButton.disabled = busy;
    confirmButton.disabled = busy || !profile?.autenticado || !pendingConnection;
    shopSelect.disabled = busy || !!linking;
    shopButton.disabled = busy || !!linking || !profile?.autenticado || !shopSelect.value;
    loginButton.textContent = busy ? 'Entrando…' : 'Entrar com Google';
  }

  async function restore() {
    if (!available()) return null;
    const expected = generation;
    if (!restoring || restoringGeneration !== expected) {
      restoringGeneration = expected;
      const current = native().authStatus().then(value => {
        if (expected !== generation || (Number.isFinite(value.versao) && value.versao < nativeVersion)) return profile;
        if (Number.isFinite(value.versao)) nativeVersion = value.versao;
        if (!value.autenticado || value.sessaoId !== profile?.sessaoId) syncReady = false;
        profile = value; draw(); signal(); return value;
      }).finally(() => { if (restoring === current) restoring = null; });
      restoring = current;
    }
    return restoring;
  }

  function open() { install(); draw(); if (!dialog.open) dialog.showModal(); }
  function review(connection) {
    pendingConnection = { connection, generation, owner: profile?.usuario?.id, session: profile?.sessaoId };
    open();
    if (!connection.empresaNome || !connection.lojaNome) {
      message('Não foi possível identificar o nome da empresa e da loja. Seus dados continuam neste aparelho. Procure a responsável antes de conectar.');
      return;
    }
    const count = sync()?.queueSize?.() || 0;
    message(`Este aparelho já tem dados${count ? ' e ' + count + ' envio(s) pendente(s)' : ''}. Confirme que pertencem a ${connection.empresaNome}, ${connection.lojaNome}. Até confirmar, nenhum desses dados será enviado.`);
    confirmButton.textContent = `Vincular a ${connection.lojaNome}`;
    confirmButton.hidden = false;
  }

  async function apply(connection, confirmed, expected = generation) {
    if (!profile?.autenticado || operation === 'logout' || expected !== generation
      || (Number.isFinite(connection.versao) && Number.isFinite(profile.versao) && connection.versao !== profile.versao))
      throw new Error('A conta foi encerrada ou trocada. Nenhum dado foi enviado.');
    sync().applySessionPairing(connection, { confirmLocalContext: confirmed === true });
    syncReady = true;
    pendingConnection = null;
    if (confirmButton) confirmButton.hidden = true;
    if (shopSelect) { shopSelect.hidden = true; shopButton.hidden = true; shopLabel.hidden = true; }
    message('Conta conectada. A sincronização será retomada com internet.');
    signal();
    // Somente depois da validação de origem. As operações mantêm sua idempotência existente.
    sync().startRealtime?.();
    Promise.resolve(sync().flush?.()).catch(() => {});
    Promise.resolve(sync().pull?.()).catch(() => {});
  }

  async function connect(selectedShop) {
    if (!profile?.autenticado) return;
    const expectedGeneration = generation, expectedSession = profile.sessaoId, expectedUser = profile.usuario?.id;
    if (linking && linkingGeneration === expectedGeneration) return linking;
    linkingGeneration = expectedGeneration;
    const currentLink = (async () => {
      try {
        await sync()?.localReady;
        const expected = context();
        if (selectedShop && expected.lojaId && String(selectedShop).toLowerCase() !== String(expected.lojaId).toLowerCase())
          throw new Error('Os dados existentes pertencem a outra loja. Nenhum dado foi enviado.');
        const current = sync()?.getPairing?.();
        const connection = await native().authPair({ ...expected, ...(selectedShop ? { lojaId: selectedShop } : {}),
          ...(current?.apiKey ? (sync()?.isPairingInvalid?.() ? { apiKeyRejeitada: current.apiKey } : { apiKeyAtual: current.apiKey }) : {}) });
        if (expectedGeneration !== generation || !profile?.autenticado || profile.sessaoId !== expectedSession || profile.usuario?.id !== expectedUser) return;
        try { await apply(connection, false, expectedGeneration); }
        catch (error) { if (error.code === 'LOCAL_CONTEXT_REQUIRED') review(connection); else throw error; }
      } catch (error) {
        if (expectedGeneration !== generation || !profile?.autenticado) return;
        syncReady = false;
        const shops = error.data?.lojas || error.lojas;
        if (error.code === 'LOJA_REQUIRED' && Array.isArray(shops)) {
          open(); shopSelect.replaceChildren();
          const placeholder = doc.createElement('option'); placeholder.value = ''; placeholder.textContent = 'Selecione a loja'; shopSelect.append(placeholder);
          for (const shop of shops) {
            const option = doc.createElement('option'); option.value = shop.id; option.textContent = shop.nome; shopSelect.append(option);
          }
          shopSelect.hidden = false; shopButton.hidden = false; shopLabel.hidden = false; shopButton.disabled = true;
          message('Escolha uma vez a loja deste aparelho.');
        } else {
          message(error.message || 'A conta entrou, mas não foi possível conectar a produção. Seus dados foram mantidos.');
          signal();
        }
      }
    })().finally(() => { if (linking === currentLink) { linking = null; draw(); } });
    linking = currentLink; draw();
    return currentLink;
  }

  async function login() {
    if (busy) return profile;
    if (!profile) await restore();
    if (profile?.autenticado) { await connect(); return profile; }
    const expected = ++generation;
    busy = true; operation = 'login'; pendingConnection = null; syncReady = false; draw(); message('');
    try {
      const authenticated = await native().authLogin(context());
      if (expected !== generation || (Number.isFinite(authenticated.versao) && authenticated.versao < nativeVersion)) return profile;
      if (Number.isFinite(authenticated.versao)) nativeVersion = authenticated.versao;
      profile = authenticated; draw(); signal(); await connect(); return profile;
    } finally { if (expected === generation) { busy = false; operation = null; draw(); } }
  }

  async function logout() {
    if (busy) return;
    const expected = ++generation;
    busy = true; operation = 'logout'; profile = { autenticado: false }; pendingConnection = null; syncReady = false;
    sync()?.stopRealtime?.(); sync()?.clearPairing?.();
    confirmButton.hidden = true; shopSelect.hidden = true; shopButton.hidden = true; shopLabel.hidden = true;
    draw(); signal(); message('Encerrando a conta neste aparelho…');
    try {
      await native().authLogout();
      if (expected === generation) message('Você saiu deste aparelho. Os dados e envios pendentes foram mantidos.');
    } catch (error) { if (expected === generation) await restore().catch(() => {}); throw error; }
    finally { if (expected === generation) { busy = false; operation = null; draw(); } }
  }

  function install() {
    if (dialog || !doc.body) return;
    dialog = doc.createElement('dialog'); dialog.id = 'pilotAccountDialog'; dialog.className = 'pilot-account';
    dialog.setAttribute('aria-labelledby', 'pilotAccountTitle');
    dialog.innerHTML = '<div class="pilot-account-heading"><h2 id="pilotAccountTitle">Sua conta</h2><button type="button" data-account="close" aria-label="Fechar conta">Fechar</button></div><p id="pilotAccountDescription"></p><p id="pilotAccountNotice" class="pilot-feedback" role="status"></p><div class="pilot-account-actions"><button type="button" data-account="login">Entrar com Google</button><label id="pilotAccountShopLabel" hidden for="pilotAccountShop">Loja deste aparelho</label><select id="pilotAccountShop" hidden></select><button type="button" data-account="shop" hidden>Conectar loja</button><button type="button" data-account="confirm" hidden>Vincular os dados</button><button type="button" data-account="logout" hidden>Sair da conta</button></div>';
    doc.body.append(dialog);
    title = dialog.querySelector('h2'); description = dialog.querySelector('#pilotAccountDescription'); notice = dialog.querySelector('#pilotAccountNotice');
    loginButton = dialog.querySelector('[data-account="login"]'); logoutButton = dialog.querySelector('[data-account="logout"]');
    confirmButton = dialog.querySelector('[data-account="confirm"]'); shopSelect = dialog.querySelector('select'); shopButton = dialog.querySelector('[data-account="shop"]'); shopLabel = dialog.querySelector('#pilotAccountShopLabel');
    shopSelect.addEventListener('change', () => { shopButton.disabled = !shopSelect.value; });
    dialog.addEventListener('click', async event => {
      const action = event.target.closest('[data-account]')?.dataset.account;
      if (action !== 'close' && busy) return;
      try {
        if (action === 'close') dialog.close();
        if (action === 'login') await login();
        if (action === 'logout') await logout();
        if (action === 'confirm' && pendingConnection && pendingConnection.generation === generation
          && pendingConnection.owner === profile?.usuario?.id && pendingConnection.session === profile?.sessaoId)
          await apply(pendingConnection.connection, true, pendingConnection.generation);
        if (action === 'shop' && shopSelect.value) await connect(shopSelect.value);
      } catch (error) { message(error.message || 'Não foi possível concluir. Tente de novo.'); }
    });
    draw();
  }

  root.cdbAuth = { available, current: () => profile, open, restore,
    canSync: () => !!profile?.autenticado && syncReady && !sync()?.isPairingInvalid?.(),
    async ensureSignedIn() {
      if (!profile) await restore();
      if (!profile?.autenticado) await login();
      if (!profile?.autenticado) throw new Error('Conclua a entrada com Google para abrir o Atendimento.');
      return profile;
    } };

  async function start() {
    if (!available()) return;
    install();
    try {
      await native().addListener('sessionChanged', async event => {
        if (Number.isFinite(event.versao) && event.versao < nativeVersion) return;
        if (Number.isFinite(event.versao)) nativeVersion = event.versao;
        if (event.autenticado === false) {
          if (operation !== 'logout') { generation++; busy = false; operation = null; }
          profile = { autenticado: false }; pendingConnection = null; syncReady = false;
          confirmButton.hidden = true; shopSelect.hidden = true; shopButton.hidden = true; shopLabel.hidden = true;
          sync()?.stopRealtime?.(); sync()?.clearPairing?.(); draw(); signal();
        } else {
          if (operation === 'logout') return;
          if (operation !== 'login') generation++;
          try { await restore(); await connect(); } catch (error) { message(error.message); }
        }
      });
      await restore();
      if (profile?.autenticado) await connect();
    } catch (error) { message(error.message || 'Não foi possível conferir a conta. Seus dados foram mantidos.'); }
  }
  if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', start, { once: true }); else start();
  root.addEventListener('online', () => {
    if (!available()) return;
    restore().then(() => { if (profile?.autenticado && !pendingConnection && !root.cdbAuth.canSync()) connect(); }).catch(() => {});
  });
})(typeof window !== 'undefined' ? window : globalThis);
