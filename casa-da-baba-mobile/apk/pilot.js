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
    const pairing = invalid ? 'Conexão do aparelho inválida. Conecte novamente.'
      : paired ? 'Aparelho vinculado ao EasyStok.' : 'Produção só neste aparelho. Conecte com o código de pareamento para sincronizar.';
    const queue = pending > 0 ? pending + (pending === 1 ? ' alteração aguardando envio.' : ' alterações aguardando envio.')
      : 'Nenhuma alteração na fila de envio deste aparelho.';
    const syncState = invalid ? 'invalid' : importPending ? 'pending' : !paired ? 'local' : health === 'online' && pending > 0 ? 'pending' : health;
    const railStatus = invalid ? 'Reconectar aparelho' : importPending ? 'Conferir dados importados' : !paired ? 'Só neste aparelho' : status;
    const visibleQueue = importPending ? 'Envio pausado até conferir os dados importados. ' + queue : queue;
    const summary = status + '. ' + (invalid ? 'Conexão do aparelho inválida.' : paired ? 'Aparelho vinculado ao EasyStok.' : 'Produção só neste aparelho.') + (pending > 0 || importPending ? ' ' + visibleQueue : '');
    return { status, pairing, summary, queue: visibleQueue, syncState, rail: railStatus + (pending > 0 ? ' · ' + pending + ' pendente' + (pending === 1 ? '' : 's') : '') };
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

  function noticePresentation(state, online = true) {
    if (!state?.supported) return { tone: 'neutral', title: state?.native ? 'Atualize o app para receber avisos' : 'Avisos no app Android', body: state?.native ? 'Esta instalação ainda não oferece a central de avisos. Use Atualizar APK na tela inicial.' : 'Esta é uma prévia no navegador. O recebimento e as confirmações funcionam no APK instalado.', action: '' };
    if (state.permission === 'denied' || state.permission === 'blocked') return { tone: 'attention', title: 'Permita receber avisos', body: 'As notificações estão desativadas neste aparelho. Você ainda pode consultar o histórico aqui.', action: 'Ativar avisos neste aparelho' };
    if (!online) return { tone: 'attention', title: 'Sem conexão agora', body: 'Os avisos salvos continuam disponíveis. As confirmações aguardam a volta da conexão.', action: 'Configurar notificações' };
    if (state.checking) return { tone: 'neutral', title: 'Conferindo os avisos', body: 'Verificando o registro deste aparelho e as confirmações pendentes.', action: 'Configurar notificações' };
    if (state.lastError) return { tone: 'attention', title: 'Não foi possível atualizar os avisos', body: 'O registro ou a atualização dos avisos não foi concluído. Toque em Atualizar avisos para tentar novamente.', action: 'Configurar notificações' };
    if (!state.registered) return { tone: 'attention', title: 'Registro ainda não confirmado', body: 'A permissão está ativada. Toque em Atualizar avisos para registrar este aparelho com internet.', action: 'Configurar notificações' };
    return { tone: 'ready', title: 'Aparelho registrado para avisos', body: 'A permissão está ativada. Isso ainda não confirma a entrega de uma nova notificação.', action: 'Configurar notificações' };
  }

  // Subconjunto canônico do Console: MarcaIcone.jsx (Casa da Baba V4, grid32) e Icone.jsx (grid24).
  // Geometria sem redesenho; pilot-icons.test.cjs verifica paridade com essas fontes vivas.
  const CONSOLE_ICONS = {
    "link": { "grid": 24, "size": 24, "body": "<path d=\"M10 13a5 5 0 0 0 7.5.5l3-3a5 5 0 0 0-7-7l-1.7 1.7M14 11a5 5 0 0 0-7.5-.5l-3 3a5 5 0 0 0 7 7l1.7-1.7\"/>" },
    "relogio": { "grid": 24, "size": 24, "body": "<path d=\"M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18ZM12 7v5l3 2\"/>" },
    "check": { "grid": 24, "size": 24, "body": "<path d=\"M4 12.5 9.5 18 20 6.5\"/>" },
    "plus": { "grid": 24, "size": 24, "body": "<path d=\"M12 5v14M5 12h14\"/>" },
    "copy": { "grid": 24, "size": 24, "body": "<path d=\"M9 9h11a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1H9a1 1 0 0 1-1-1V10a1 1 0 0 1 1-1ZM5 15H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v1\"/>" },
    "lapis": { "grid": 24, "size": 24, "body": "<path d=\"M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497zM15 5l4 4\"/>" },
    "lixeira": { "grid": 24, "size": 24, "body": "<path d=\"M3 6h18M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M10 11v6M14 11v6\"/>" },
    "caixa": {
      "grid": 32, "size": 24,
      "body": "<g fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M7 5H25L27 20H5Z M3 20H29V28H3Z M8 24H12\"/><rect x=\"10\" y=\"8\" width=\"12\" height=\"5\" rx=\"1.5\"/><path d=\"M11 17H12 M16 17H17 M21 17H22\"/></g>"
    },
    "estoque": {
      "grid": 32, "size": 24,
      "body": "<g fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M4 4V28 M28 4V28 M4 16H28 M4 28H28\"/><rect x=\"7\" y=\"5\" width=\"8\" height=\"11\" rx=\"1.5\"/><rect x=\"18\" y=\"8\" width=\"7\" height=\"8\" rx=\"1.5\"/><rect x=\"8\" y=\"20\" width=\"16\" height=\"8\" rx=\"1.5\"/><path d=\"M11 5V9 M16 20V24\"/></g>"
    },
    "shopping-cart": {
      "grid": 24, "size": 24,
      "body": "<path d=\"M3 4h2.2l2.68 13.4a2 2 0 0 0 2 1.6h8.24a2 2 0 0 0 1.97-1.66L21.4 8H6M9 21a1.3 1.3 0 1 0 0-2.6 1.3 1.3 0 0 0 0 2.6ZM18 21a1.3 1.3 0 1 0 0-2.6 1.3 1.3 0 0 0 0 2.6Z\"/>"
    },
    "archive": {
      "grid": 24, "size": 24,
      "body": "<path d=\"M3 3h18a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1ZM4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8M10 12h4\"/>"
    },
    "ficha": {
      "grid": 24, "size": 24,
      "body": "<path d=\"M7 3h10a1 1 0 0 1 1 1v16l-6-3-6 3V4a1 1 0 0 1 1-1ZM9 8h6M9 11h4\"/>"
    },
    "historico": {
      "grid": 24, "size": 24,
      "body": "<path d=\"M3 12a9 9 0 1 0 3-6.7M3 4v4h4M12 8v4.5l3 1.8\"/>"
    },
    "ajuda": {
      "grid": 24, "size": 24,
      "body": "<path d=\"M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18ZM9.6 9.4a2.5 2.5 0 0 1 4.9.7c0 1.7-2.5 2.1-2.5 3.9M12 17h.01\"/>"
    },
    "producao": {
      "grid": 32,
      "size": 32,
      "body": "<g fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M6 11H26V23C26 28 6 28 6 23Z M3 11H29 M6 15H3V20H6 M26 15H29V20H26 M9 7C11 5 8 4 10 2 M16 7C18 5 15 4 17 2 M23 7C25 5 22 4 24 2\"/></g>"
    },
    "mensagem": {
      "grid": 32,
      "size": 24,
      "body": "<g fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M4 5H28V22H14L6 29V22H4Z M9 11H23 M9 16H19\"/></g>"
    },
    "pedido-em-preparo": {
      "grid": 32,
      "size": 32,
      "body": "<g fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M8 28V4H24V14 M8 28L11 26 14 28 17 26\"/><path d=\"M11 8H20 M11 12H16 M18 22H29V27H18Z M17 22H30 M21 19V17 M26 19V17\"/></g>"
    },
    "produto": {
      "grid": 32,
      "size": 24,
      "body": "<g fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M8 4H24L27 9V28H5V9Z M5 9H27 M8 4 10 9 M24 4 22 9\"/><rect x=\"10\" y=\"15\" width=\"12\" height=\"8\" rx=\"2\"/></g>"
    },
    "configuracoes": {
      "grid": 32,
      "size": 24,
      "body": "<g fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M4 8H12 M20 8H28 M4 24H19 M27 24H28 M16 4V12 M23 20V28 M4 16H5 M13 16H28\"/><circle cx=\"16\" cy=\"8\" r=\"4\"/><circle cx=\"9\" cy=\"16\" r=\"4\"/><circle cx=\"23\" cy=\"24\" r=\"4\"/></g>"
    },
    "bell": {
      "grid": 24,
      "size": 24,
      "body": "<path d=\"M10.268 21a2 2 0 0 0 3.464 0M3.262 15.326A1 1 0 0 0 4 17h16a1 1 0 0 0 .74-1.673C19.41 13.956 18 12.499 18 8A6 6 0 0 0 6 8c0 4.499-1.411 5.956-2.738 7.326\"/>"
    },
    "fechar": {
      "grid": 24,
      "size": 24,
      "body": "<path d=\"M6 6l12 12M18 6 6 18\"/>"
    },
    "chevron-right": {
      "grid": 24,
      "size": 24,
      "body": "<path d=\"M9 18l6-6-6-6\"/>"
    },
    "printer": {
      "grid": 24,
      "size": 24,
      "body": "<path d=\"M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2M6 9V3a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v6M6 14h12v8H6Z\"/>"
    },
    "download": {
      "grid": 24,
      "size": 24,
      "body": "<path d=\"M12 15V3M7 10l5 5 5-5M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4\"/>"
    },
    "enviar": {
      "grid": 24,
      "size": 24,
      "body": "<path d=\"M4 12 20 5l-7 15-2-6-7-2Z\"/>"
    },
    "refresh-cw": {
      "grid": 24,
      "size": 24,
      "body": "<path d=\"M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8M21 3v5h-5M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16M8 16H3v5\"/>"
    },
    "search": {
      "grid": 24,
      "size": 24,
      "body": "<path d=\"M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14ZM16.5 16.5 21 21\"/>"
    },
    "house": {
      "grid": 24,
      "size": 24,
      "body": "<path d=\"M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8M3 10a2 2 0 0 1 .709-1.528l7-6a2 2 0 0 1 2.582 0l7 6A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z\"/>"
    },
    "ellipsis": {
      "grid": 24,
      "size": 24,
      "body": "<path d=\"M12 11a1 1 0 1 0 0 2a1 1 0 0 0 0-2ZM19 11a1 1 0 1 0 0 2a1 1 0 0 0 0-2ZM5 11a1 1 0 1 0 0 2a1 1 0 0 0 0-2Z\"/>"
    }
  };
  function pilotIcon(name) {
    const icon = CONSOLE_ICONS[name];
    if (!icon) return '';
    return '<svg data-pilot-icon="' + name + '" viewBox="0 0 ' + icon.grid + ' ' + icon.grid + '" width="' + icon.size + '" height="' + icon.size + '" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' + icon.body + '</svg>';
  }
  root.cdbPilotIcon = pilotIcon;
  function applyConsoleIcons(document) {
    if (!document.querySelectorAll) return;
    // Apenas controles fixos. Não seleciona ícones de produtos nem a prévia de etiquetas.
    for (const [selector, name] of [
      ['.nav-btn[data-nav="home"] .nav-icon svg', 'house'],
      ['.nav-btn[data-nav="producao"] .nav-icon svg', 'producao'],
      ['.nav-btn[data-nav="pedidos"] .nav-icon svg', 'pedido-em-preparo'],
      ['.nav-btn[data-nav="more"] .nav-icon svg', 'ellipsis'],
      ['#globalSearchBtn svg', 'search'], ['#ajustesBtn svg', 'configuracoes']
    ]) for (const element of document.querySelectorAll(selector)) element.outerHTML = pilotIcon(name);
    for (const element of document.querySelectorAll('#moreMenu .more-menu-search-icon')) element.innerHTML = pilotIcon('search');
    for (const [action, name] of [
      ["go('caixa')", 'caixa'], ["go('compras')", 'shopping-cart'], ["go('finalizados')", 'archive'],
      ["go('estoque')", 'estoque'], ["go('clientes')", 'ficha'], ["go('historico')", 'historico'],
      ['closeMoreMenu(); if(window.etqAbrirModelos)etqAbrirModelos()', 'printer'],
      ['closeMoreMenu(); openAjustes()', 'configuracoes'], ["go('suporte')", 'ajuda']
    ]) for (const element of document.querySelectorAll('#moreMenu button[onclick="' + action + '"] .more-menu-icon')) element.innerHTML = pilotIcon(name);
  }

  function installAppChrome(environment, openHome) {
    const document = environment.document;
    const header = document.querySelector?.('.phone > .header');
    if (!header) return;
    for (const [id, targetId] of [['globalSearchBtn', 'pilotRailActions'], ['ajustesBtn', 'pilotRailActions'], ['opBadge', 'pilotOperator'], ['syncChip', 'pilotSyncDetails']]) {
      const control = document.getElementById(id), target = document.getElementById(targetId);
      if (control && target) target.append(control);
    }
    header.hidden = true;
    const homeLabel = document.querySelector('.nav-btn[data-nav="home"] .label');
    if (homeLabel) homeLabel.textContent = 'Início';
    const navigate = environment.go;
    if (typeof navigate === 'function') environment.go = function (screen, options) {
      const result = navigate.call(this, screen, options);
      if (screen === 'home') openHome();
      return result;
    };
  }

  function refreshOperatorReview(document, name) {
    for (const label of document.querySelectorAll('.pilot-current-operator')) label.textContent = name?.trim() || 'Não identificado';
  }

  function installLabelWorkspace(environment) {
    const document = environment.document;
    const overlay = document.getElementById('etiquetasPreview');
    const sheet = overlay?.querySelector?.('.etq-sheet');
    if (!sheet || sheet.dataset.pilotLabels === 'true') return;
    const config = document.getElementById('etqConfigBar');
    const preview = document.getElementById('etqBody');
    const note = sheet.querySelector('.etq-note-bar');
    const actions = sheet.querySelector('.etq-actions');
    const copies = document.getElementById('etqCopiesSelect');
    const retry = document.getElementById('etqRetryBtn');
    const copiesRow = copies?.parentElement;
    const buttons = ['print', 'save', 'share'].map(name => actions?.querySelector('.etq-' + name));
    if (!config || !preview || !note || !actions || !copiesRow || !retry || buttons.some(button => !button)) return;
    if ([config, preview, note, actions, copiesRow].some(node => node.parentElement !== sheet) || retry.parentElement !== copiesRow) return;
    const options = document.createElement('details');
    options.className = 'pilot-label-options';
    const summary = document.createElement('summary');
    summary.textContent = 'Ajustar etiqueta';
    options.append(summary);
    sheet.insertBefore(options, config);
    options.append(config, note);
    const retryRow = document.createElement('div');
    retryRow.className = 'pilot-label-retry';
    sheet.insertBefore(retryRow, actions);
    retryRow.append(retry);
    copiesRow.classList.add('pilot-label-copies');
    copies.setAttribute('aria-label', 'Quantidade de cópias');
    actions.classList.add('pilot-label-toolbar');
    actions.insertBefore(copiesRow, buttons[0]);
    for (const [index, icon, label] of [[0, 'printer', 'Imprimir'], [1, 'download', 'Salvar PNG'], [2, 'enviar', 'Enviar']]) {
      const button = buttons[index];
      button.innerHTML = pilotIcon(icon) + (index === 0 ? '<span>Imprimir</span>' : '');
      button.setAttribute('aria-label', label);
      button.setAttribute('title', label);
    }
    options.addEventListener('toggle', () => {
      const resize = () => environment.scaleEtqCards?.();
      if (environment.requestAnimationFrame) environment.requestAnimationFrame(resize);
      else environment.setTimeout(resize, 0);
    });
    sheet.dataset.pilotLabels = 'true';
  }

  if (typeof module !== 'undefined' && module.exports) module.exports = { validateManifest, connectionStatus, otaStatusText, confirmOtaHealth, noticePresentation, pilotIcon, applyConsoleIcons, installAppChrome, refreshOperatorReview, installLabelWorkspace };
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
  const bellIcon = pilotIcon('bell');
  const closeIcon = pilotIcon('fechar');
  const arrowIcon = pilotIcon('chevron-right');
  const productionIcon = pilotIcon('producao');
  applyConsoleIcons(doc);
  const bell = (id, badge) => '<button type="button" id="' + id + '" class="pilot-bell" aria-label="Abrir avisos" title="Avisos" aria-haspopup="dialog" data-action="notices">' + bellIcon + '<span id="' + badge + '" class="pilot-bell-count" hidden aria-hidden="true"></span></button>';
  const rail = doc.createElement('div');
  rail.className = 'pilot-rail';
  rail.innerHTML = '<button type="button" id="pilotHomeButton" aria-label="Casa da Baba, início"><img class="pilot-brand-mark" src="casa-da-baba.png" alt="" width="48" height="40"><span class="pilot-wordmark">Casa da Baba<span id="pilotRailVersion">Piloto</span></span></button><div id="pilotRailActions">' + bell('pilotNoticeBell', 'pilotNoticeCount') + '</div><span id="pilotRailStatus" role="status">Conferindo conexão</span>';
  doc.body.prepend(rail);
  doc.body.classList.add('pilot-app');
  if (root.ResizeObserver) new root.ResizeObserver(() => {
    doc.body.style.setProperty('--pilot-rail-height', Math.ceil(rail.getBoundingClientRect().height) + 'px');
  }).observe(rail);

  const home = doc.createElement('dialog');
  home.id = 'pilotHome';
  home.className = 'pilot-home';
  home.setAttribute('aria-labelledby', 'pilotTitle');
  home.innerHTML = `
    <div class="pilot-sheet">
      <div class="pilot-topline"><span class="pilot-home-brand"><img class="pilot-brand-mark" src="casa-da-baba.png" alt="" width="60" height="44"><span>Casa da Baba<small id="pilotHomeVersion">PILOTO</small></span></span><div class="pilot-top-actions">${bell('pilotHomeNoticeBell', 'pilotHomeNoticeCount')}<button type="button" data-action="close" aria-label="Fechar início">${closeIcon}</button></div></div>
      <h1 id="pilotTitle" tabindex="-1">Produção e pedidos.</h1>
      <p id="pilotError" class="pilot-feedback pilot-error" role="alert" tabindex="-1"></p>
      <section id="pilotImportReview" class="pilot-import-review" hidden aria-labelledby="pilotImportTitle"><strong id="pilotImportTitle">Confira os dados importados</strong><p id="pilotImportStatus" role="status"></p><button type="button" id="pilotConfirmImport" data-action="confirm-import">Confirmar origem e sincronizar</button><button type="button" id="pilotUndoImport" data-action="undo-import" hidden>Desfazer importação</button><p id="pilotUndoImportStatus" role="status"></p><div id="pilotMigration" hidden><p id="pilotMigrationStatus"></p><button type="button" id="pilotMigrateImport" data-action="migrate-import">Enviar primeira migração</button></div></section>
      <details id="pilotMigrationReceipt" class="pilot-help" hidden><summary>Última migração confirmada</summary><p id="pilotMigrationResult" class="pilot-feedback" role="status" tabindex="-1"></p></details>
      <div class="pilot-actions">
        <button type="button" class="pilot-card pilot-production" data-action="production"><span class="pilot-action-icon">${productionIcon}</span><span class="pilot-card-copy"><strong>Produção</strong></span></button>
        <button type="button" class="pilot-card pilot-orders" data-action="local-orders"><span class="pilot-action-icon">${pilotIcon('pedido-em-preparo')}</span><span class="pilot-card-copy"><strong>Pedidos</strong></span></button>
      </div>
      <div class="pilot-connection"><p id="pilotConnection" role="status" tabindex="-1">Conferindo a conexão com o EasyStok…</p><div class="pilot-connection-controls"><button type="button" id="pilotPairButton" data-action="pair" aria-describedby="pilotPairHint">Conectar produção</button><button type="button" id="pilotReconnect" data-action="reconnect" hidden aria-label="Verificar conexão" title="Verificar conexão">${pilotIcon('refresh-cw')}</button><details class="pilot-connection-details"><summary>Detalhes</summary><p id="pilotConnectionDetail"></p><p id="pilotPairHint">Solicite o código de pareamento à equipe da Casa da Baba para conectar este aparelho.</p><div id="pilotSyncDetails"></div></details></div></div>
      <div class="pilot-shortcuts">
        <button type="button" data-action="printers">${pilotIcon('printer')}<span>Impressoras</span></button>
      </div>
      <details class="pilot-help pilot-support"><summary>Configurações</summary>
      <div class="pilot-operator-row"><span>Registrando como</span><div id="pilotOperator"></div></div>
      <details class="pilot-help pilot-migration"><summary>Backup do PWA</summary><p>Exporte o JSON nos Ajustes do PWA antigo e importe aqui. Mantenha o original até verificar os dados.</p><button type="button" data-action="import-pwa">Importar</button><button type="button" data-action="migration-guide">Guia</button></details>
      <details class="pilot-help"><summary>Offline</summary><p>Produtos, pedidos e lotes ficam salvos neste aparelho. Após conectar, o envio ao EasyStok é retomado com internet.</p></details>
      <details class="pilot-help"><summary>Atualizações</summary><p id="pilotOtaStatus" role="status">O conteúdo é atualizado ao reabrir.</p><p id="pilotPushStatus" role="status"></p><p>A versão anterior preserva seus dados e envios pendentes.</p><button type="button" id="pilotRollback" data-action="rollback" hidden>Recuperar</button></details>
      </details>
      <div class="pilot-update"><span id="pilotVersion" role="status"></span><button type="button" id="pilotUpdateButton" data-action="update">Verificar atualização</button></div>
      <p id="pilotUpdateStatus" class="pilot-feedback" role="status" tabindex="-1"></p>
      <button type="button" id="pilotDownload" data-action="download" hidden>Atualizar APK</button>
    </div>`;
  doc.body.append(home);
  installAppChrome(root, openHome);
  installLabelWorkspace(root);
  if (doc.querySelector && root.MutationObserver) {
    for (const id of ['newProductModal', 'productionConfirmModal']) {
      const sheet = doc.querySelector('#' + id + ' .modal-sheet');
      const subtitle = sheet?.querySelector('.modal-sub');
      if (!subtitle) continue;
      const context = doc.createElement('div');
      context.className = 'pilot-operator-review';
      context.innerHTML = '<span>Registrando como <strong class="pilot-current-operator"></strong></span><button type="button">Trocar</button>';
      context.querySelector('button').addEventListener('click', () => root.changeOperator());
      subtitle.after(context);
    }
    const refreshOperator = () => refreshOperatorReview(doc, root.cdbApp?.getOperator?.());
    refreshOperator();
    const operatorLabel = doc.getElementById('opName');
    if (operatorLabel) new root.MutationObserver(refreshOperator).observe(operatorLabel, { childList: true, characterData: true, subtree: true });
  }
  const notices = doc.createElement('dialog');
  notices.id = 'pilotNotices';
  notices.className = 'pilot-home pilot-notices';
  notices.setAttribute('aria-labelledby', 'pilotNoticesTitle');
  notices.innerHTML = `<div id="pilotNoticeSheet" class="pilot-sheet">
    <div class="pilot-topline"><span class="pilot-notice-brand">${bellIcon} CASA DA BABA</span><button type="button" data-notice-action="close" aria-label="Fechar avisos">${closeIcon}</button></div>
    <div class="pilot-notice-heading"><h1 id="pilotNoticesTitle" tabindex="-1">Avisos.</h1><div id="pilotNoticeListHeading" class="pilot-notice-list-heading"><h2 class="pilot-visually-hidden">Neste aparelho</h2><span id="pilotNoticeUnread"></span></div></div>
    <p id="pilotNoticeIntro" class="pilot-visually-hidden">Orientações para sua operação.</p>
    <section id="pilotNoticeHealth" class="pilot-notice-health" aria-labelledby="pilotNoticeHealthTitle"><strong id="pilotNoticeHealthTitle"></strong><p id="pilotNoticeHealthBody"></p><div id="pilotNoticeControls" class="pilot-notice-controls"><button type="button" id="pilotNoticePermission" data-notice-action="permission"></button><button type="button" id="pilotNoticeRefresh" data-notice-action="refresh">Atualizar avisos</button></div></section>
    <p id="pilotNoticeFeedback" class="pilot-feedback pilot-error" role="alert"></p>
    <div id="pilotNoticeListView"><div id="pilotNoticeEmpty" class="pilot-notice-empty" role="status">${bellIcon}<strong id="pilotNoticeEmptyTitle"></strong><p id="pilotNoticeEmptyBody"></p></div><div id="pilotNoticeList" class="pilot-notice-list"></div></div>
    <article id="pilotNoticeDetail" class="pilot-notice-detail" hidden><button type="button" data-notice-action="back" class="pilot-notice-back">← Voltar ao histórico</button><h2 id="pilotNoticeDetailTitle" tabindex="-1"></h2><p id="pilotNoticeDetailBody"></p><details class="pilot-help"><summary>Recebimento deste aviso</summary><p id="pilotNoticeDetailSource" class="pilot-notice-source"></p></details><button type="button" id="pilotNoticeGuide" data-notice-action="guide" hidden>Ver passo a passo da migração</button></article>
    <details id="pilotNoticeDiagnostics" class="pilot-help pilot-notice-diagnostics"><summary>Configurações e confirmações</summary><p id="pilotNoticePush"></p><p id="pilotNoticeSync"></p><p id="pilotNoticeReceipts" role="status"></p><p id="pilotNoticeError"></p><p>Um aviso recuperado pelo histórico não comprova que o push chegou. A abertura é registrada quando você entra no detalhe do aviso.</p>
    <div class="pilot-notice-device"><span id="pilotNoticeDevice"></span><button type="button" id="pilotNoticeCopy" data-notice-action="copy">Copiar identificação</button></div></details>
  </div>`;
  doc.body.append(notices);
  let noticeState = { supported: false, native: root.Capacitor?.isNativePlatform?.() === true, items: [] };
  let noticeFocus = null;
  let selectedNotice = null;
  let noticeLoading = false;
  let pendingOpenHandled = '';
  const noticeTime = value => Number.isFinite(value) && value > 0 ? new Date(value).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }) : 'Ainda não confirmado';
  function noticeSource(item) { return item.source === 'push' ? 'Recebido por push' : item.source === 'sync' ? 'Recuperado pelo histórico' : 'Origem não identificada'; }
  function renderNotices() {
    const presentation = noticePresentation({ ...noticeState, checking: noticeLoading || noticeState.checking }, root.navigator?.onLine !== false);
    const contentFirst = noticeState.registered === true && noticeState.permission === 'granted' && !noticeState.lastError && root.navigator?.onLine !== false;
    notices.dataset.tone = contentFirst ? 'ready' : presentation.tone;
    const sheet = doc.getElementById('pilotNoticeSheet'), health = doc.getElementById('pilotNoticeHealth'), history = doc.getElementById('pilotNoticeListView');
    if (notices.dataset.contentFirst !== String(contentFirst)) {
      sheet.insertBefore(contentFirst ? history : health, contentFirst ? health : history);
      notices.dataset.contentFirst = String(contentFirst);
    }
    doc.getElementById('pilotNoticeList').setAttribute('aria-busy', String(noticeLoading || noticeState.checking === true));
    const refreshButton = doc.getElementById('pilotNoticeRefresh');
    const refreshHost = doc.getElementById(contentFirst ? 'pilotNoticeListHeading' : 'pilotNoticeControls');
    const restoreRefreshFocus = refreshButton.parentElement !== refreshHost && doc.activeElement === refreshButton;
    if (refreshButton.parentElement !== refreshHost) refreshHost.append(refreshButton);
    const permissionOff = noticeState.permission === 'denied' || noticeState.permission === 'blocked';
    // Configuração fica secundária; falha de rede não exige mudar a permissão.
    const permissionHost = doc.getElementById(permissionOff ? 'pilotNoticeControls' : 'pilotNoticeDiagnostics');
    const permissionControl = doc.getElementById('pilotNoticePermission');
    const restorePermissionFocus = doc.activeElement === permissionControl && !permissionOff && !permissionHost.open;
    if (permissionControl.parentElement !== permissionHost) {
      if (permissionOff) permissionHost.insertBefore(permissionControl, doc.getElementById('pilotNoticeRefresh'));
      else permissionHost.append(permissionControl);
    }
    doc.getElementById('pilotNoticeHealth').dataset.tone = presentation.tone;
    doc.getElementById('pilotNoticeHealth').dataset.permission = noticeState.permission || '';
    doc.getElementById('pilotNoticeHealth').hidden = !!selectedNotice;
    doc.getElementById('pilotNoticeIntro').hidden = !!selectedNotice;
    doc.getElementById('pilotNoticeHealthTitle').textContent = presentation.title;
    doc.getElementById('pilotNoticeHealthBody').textContent = presentation.body;
    const permission = doc.getElementById('pilotNoticePermission');
    permission.textContent = presentation.action; permission.hidden = !noticeState.supported;
    const refresh = doc.getElementById('pilotNoticeRefresh');
    refresh.hidden = !noticeState.supported; refresh.disabled = noticeLoading || noticeState.checking === true;
    const refreshLabel = noticeLoading || noticeState.checking ? 'Conferindo…' : 'Atualizar avisos';
    refresh.innerHTML = pilotIcon('refresh-cw') + '<span>' + refreshLabel + '</span>';
    refresh.dataset.compact = String(contentFirst); refresh.setAttribute('aria-label', refreshLabel); refresh.setAttribute('title', refreshLabel);
    if (restorePermissionFocus || restoreRefreshFocus) (selectedNotice ? doc.getElementById('pilotNoticeDetailTitle') : !refresh.disabled && !refresh.hidden ? refresh : doc.getElementById('pilotNoticesTitle')).focus();
    const unread = Number.isSafeInteger(noticeState.unread) && noticeState.unread > 0 ? noticeState.unread : 0;
    for (const [buttonId, badgeId] of [['pilotNoticeBell', 'pilotNoticeCount'], ['pilotHomeNoticeBell', 'pilotHomeNoticeCount']]) {
      const button = doc.getElementById(buttonId), badge = doc.getElementById(badgeId);
      button.dataset.permissionOff = String(permissionOff);
      const label = (unread ? 'Abrir avisos: ' + unread + ' não aberto' + (unread > 1 ? 's' : '') : 'Abrir avisos') + (permissionOff ? '. Notificações desativadas neste aparelho' : '');
      button.setAttribute('aria-label', label); button.setAttribute('title', label);
      badge.textContent = unread > 99 ? '99+' : String(unread); badge.hidden = unread === 0;
    }
    doc.getElementById('pilotNoticeUnread').textContent = unread ? unread + ' não aberto' + (unread > 1 ? 's' : '') : 'Todos abertos';
    const list = doc.getElementById('pilotNoticeList');
    const focusedId = doc.activeElement?.dataset?.noticeId;
    list.textContent = '';
    const items = Array.isArray(noticeState.items) ? noticeState.items : [];
    doc.getElementById('pilotNoticeEmpty').hidden = items.length > 0;
    const waiting = noticeLoading || noticeState.checking === true;
    doc.getElementById('pilotNoticeEmptyTitle').textContent = waiting ? 'Buscando seus avisos…' : noticeState.lastError ? 'Nenhum aviso salvo neste aparelho' : 'Seus avisos ficam aqui';
    doc.getElementById('pilotNoticeEmptyBody').textContent = waiting ? 'Você pode continuar usando o app.' : 'Orientações e novidades aparecerão aqui. O sino mostra quantos você ainda não abriu.';
    if (!items.length) doc.getElementById('pilotNoticeUnread').textContent = '';
    for (const item of items) {
      if (!item || typeof item.id !== 'string') continue;
      const button = doc.createElement('button'); button.type = 'button'; button.className = 'pilot-notice-item';
      button.dataset.noticeAction = 'open'; button.dataset.noticeId = item.id;
      button.dataset.unread = item.openedAt > 0 ? 'false' : 'true';
      const title = doc.createElement('strong'); title.textContent = item.title || 'Aviso';
      button.setAttribute('aria-label', 'Abrir aviso: ' + (item.title || 'Aviso') + (item.openedAt > 0 ? ', já aberto' : ', novo'));
      const description = doc.createElement('span'); description.textContent = (item.openedAt > 0 ? 'Aberto · ' : 'Novo · ') + noticeTime(item.receivedAt);
      const preview = doc.createElement('p'); preview.className = 'pilot-notice-preview'; preview.textContent = item.body || '';
      const arrow = doc.createElement('span'); arrow.className = 'pilot-notice-item-arrow'; arrow.innerHTML = arrowIcon;
      button.append(title, description, preview, arrow); list.append(button);
      if (focusedId === item.id && !selectedNotice) button.focus();
    }
    doc.getElementById('pilotNoticePush').textContent = 'Último push recebido: ' + noticeTime(noticeState.lastPushAt);
    doc.getElementById('pilotNoticeSync').textContent = 'Última consulta ao histórico: ' + noticeTime(noticeState.lastSyncAt);
    const pending = Number.isSafeInteger(noticeState.pendingReceipts) ? Math.max(0, noticeState.pendingReceipts) : 0;
    doc.getElementById('pilotNoticeReceipts').textContent = pending ? pending + (pending > 1 ? ' confirmações aguardam' : ' confirmação aguarda') + ' envio ao servidor. O app tentará novamente com conexão.' : 'Nenhuma confirmação aguardando envio neste aparelho.';
    doc.getElementById('pilotNoticeError').textContent = noticeState.lastError ? 'Diagnóstico: ' + String(noticeState.lastError).slice(0, 300) : '';
    const id = typeof noticeState.installationId === 'string' ? noticeState.installationId : '';
    doc.getElementById('pilotNoticeDevice').textContent = noticeState.supported ? (id ? 'Aparelho ' + id.slice(0, 12) : 'Identificação ainda não disponível') + ' · APK ' + (noticeState.apkVersion || 'não identificado') : 'Prévia no navegador · sem registro de aparelho';
    doc.getElementById('pilotNoticeCopy').hidden = !id;
  }
  function applyNoticeState(state) {
    if (state && typeof state === 'object') noticeState = { ...state, supported: state.supported === true };
    renderNotices();
    const id = noticeState.pendingOpenId;
    if (!id) pendingOpenHandled = '';
    if (!doc.hidden && typeof id === 'string' && id && id !== pendingOpenHandled && noticeState.items?.some(item => item.id === id)) {
      pendingOpenHandled = id; openNotices(false); showNotice(id);
    }
  }
  async function loadNoticeState(refresh = false) {
    if (noticeLoading) return;
    if (!native()?.noticeStatus) { applyNoticeState({ supported: false, native: root.Capacitor?.isNativePlatform?.() === true, items: [] }); return; }
    noticeLoading = true; renderNotices();
    try {
      const method = refresh ? 'noticeRefresh' : 'noticeStatus';
      applyNoticeState(await native()[method]());
    } catch {
      noticeState = { ...noticeState, supported: true, lastError: 'Não foi possível consultar os avisos. Tente novamente.' };
      renderNotices();
    } finally { noticeLoading = false; renderNotices(); }
  }
  function openNotices(load = true) {
    if (!notices.open) {
      noticeFocus = doc.activeElement; selectedNotice = null;
      doc.getElementById('pilotNoticeHealth').hidden = false; doc.getElementById('pilotNoticeIntro').hidden = false;
      doc.getElementById('pilotNoticeListView').hidden = false; doc.getElementById('pilotNoticeDetail').hidden = true;
      notices.showModal(); doc.getElementById('pilotNoticesTitle').focus();
    }
    if (load) loadNoticeState(root.navigator?.onLine !== false);
  }
  function backToNotices() {
    selectedNotice = null; doc.getElementById('pilotNoticeHealth').hidden = false; doc.getElementById('pilotNoticeIntro').hidden = false; doc.getElementById('pilotNoticeListView').hidden = false; doc.getElementById('pilotNoticeDetail').hidden = true;
    doc.getElementById('pilotNoticesTitle').focus();
  }
  async function showNotice(id) {
    const item = noticeState.items?.find(entry => entry.id === id);
    if (!item) return;
    selectedNotice = id;
    doc.getElementById('pilotNoticeHealth').hidden = true; doc.getElementById('pilotNoticeIntro').hidden = true;
    doc.getElementById('pilotNoticeDetailTitle').textContent = item.title || 'Aviso';
    doc.getElementById('pilotNoticeDetailBody').textContent = item.body || '';
    doc.getElementById('pilotNoticeDetailSource').textContent = noticeSource(item) + ' · ' + noticeTime(item.receivedAt);
    doc.getElementById('pilotNoticeGuide').hidden = item.screen !== 'migration-guide';
    doc.getElementById('pilotNoticeListView').hidden = true; doc.getElementById('pilotNoticeDetail').hidden = false;
    doc.getElementById('pilotNoticeDetailTitle').focus();
    if (root.requestAnimationFrame) await new Promise(resolve => root.requestAnimationFrame(resolve));
    if (!notices.open || doc.hidden || selectedNotice !== id) return;
    try { applyNoticeState(await native().noticeOpen({ id })); }
    catch { doc.getElementById('pilotNoticeFeedback').textContent = 'O aviso está aberto, mas não foi possível registrar a confirmação. Tente abrir o aviso novamente.'; }
  }
  notices.addEventListener('click', async event => {
    const control = event.target.closest('[data-notice-action]');
    if (!control) return;
    doc.getElementById('pilotNoticeFeedback').textContent = '';
    try {
      switch (control.dataset.noticeAction) {
        case 'close': notices.close(); break;
        case 'back': backToNotices(); break;
        case 'open': await showNotice(control.dataset.noticeId); break;
        case 'refresh': await loadNoticeState(true); break;
        case 'permission': await native().notifications({ configure: noticeState.permission === 'granted' }); await loadNoticeState(); break;
        case 'guide': await native().migrationGuide(); break;
        case 'copy':
          if (!root.navigator?.clipboard?.writeText) throw new Error('copy');
          await root.navigator.clipboard.writeText('Casa da Baba · aparelho ' + noticeState.installationId + ' · APK ' + (noticeState.apkVersion || 'não identificado'));
          doc.getElementById('pilotNoticeFeedback').textContent = 'Identificação copiada. Compartilhe com o suporte para conferir este aparelho.'; break;
      }
    } catch { doc.getElementById('pilotNoticeFeedback').textContent = 'Não foi possível concluir. Tente novamente ou informe ao suporte a identificação abaixo.'; }
  });
  notices.addEventListener('cancel', event => { if (selectedNotice) { event.preventDefault(); backToNotices(); } });
  notices.addEventListener('close', () => {
    selectedNotice = null;
    const target = noticeFocus?.isConnected && noticeFocus.getClientRects().length ? noticeFocus : doc.getElementById(home.open ? 'pilotHomeNoticeBell' : 'pilotNoticeBell');
    target.focus();
  });
  doc.getElementById('pilotNoticeBell').addEventListener('click', () => openNotices());
  renderNotices();
  if (native()?.addListener) Promise.resolve(native().addListener('noticeStatus', applyNoticeState)).catch(() => {});
  root.addEventListener('focus', () => loadNoticeState());
  doc.addEventListener?.('visibilitychange', () => { if (!doc.hidden) loadNoticeState(); });
  root.addEventListener('online', () => { renderNotices(); if (notices.open) loadNoticeState(true); });
  root.addEventListener('offline', renderNotices);
  Promise.resolve().then(() => loadNoticeState());
  function installedConfig() { return { ...config, versionCode: nativeVersion?.code || config.versionCode }; }
  function showVersion() {
    const installed = nativeVersion ? 'APK ' + nativeVersion.version + ' (' + nativeVersion.code + ')' : (root.Capacitor?.isNativePlatform?.() ? 'APK: aguardando confirmação do aparelho' : 'Prévia no navegador');
    doc.getElementById('pilotHomeVersion').textContent = nativeVersion ? 'APK ' + nativeVersion.version : (root.Capacitor?.isNativePlatform?.() ? 'Confirmando versão do APK' : 'Prévia no navegador');
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
  root.cdbHandleNativeBack = function () {
    if (root.cdbIsPrinting?.()) return root.cdbHandleBack?.() === true;
    if (notices.open) {
      if (selectedNotice) backToNotices(); else notices.close();
      return true;
    }
    // Início é a raiz da navegação: o Android pode minimizar sem fechar o dialog.
    if (home.open) return false;
    const handled = root.cdbHandleBack?.() === true;
    if (handled && doc.querySelector?.('.screen.active')?.dataset.screen === 'home'
        && root.cdbApp?.getOperator?.()?.trim()) openHome();
    return handled;
  };
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
    doc.getElementById('pilotConnection').textContent = review ? 'Envio pausado · revise a importação' : state.rail;
    doc.getElementById('pilotConnectionDetail').textContent = state.status + '. ' + state.pairing + ' ' + state.queue;
    const reconnect = doc.getElementById('pilotReconnect');
    reconnect.hidden = health !== 'offline' && !checkingConnection;
    reconnect.disabled = checkingConnection;
    reconnect.innerHTML = pilotIcon('refresh-cw');
    reconnect.setAttribute('aria-label', checkingConnection ? 'Conferindo conexão' : 'Verificar conexão');
    reconnect.setAttribute('title', checkingConnection ? 'Conferindo conexão' : 'Verificar conexão');
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
      ? 'Aparelho inscrito para receber atualizações. A inscrição não confirma o recebimento de uma atualização. A verificação ao abrir continua disponível.'
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
      message.textContent = latest.available ? 'Versão ' + latest.version + ' disponível. Toque em Atualizar APK e confirme a instalação no Android, sem desinstalar.' : 'Não há APK mais novo publicado. Versão publicada: ' + latest.version + ' (' + latest.versionCode + '). A versão do conteúdo aparece em Atualizações e recuperação.';
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
      if (action === 'notices') openNotices();
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
    if (doc.querySelector?.('.screen.active')?.dataset.screen === 'home') root.go('producao', { preserveHistory: true });
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
  if (updater()?.addListener) {
    try {
      // Plugins do bridge Android retornam um handle síncrono; o proxy moderno
      // pode retornar Promise. Nenhuma falha deste listener deve impedir o início.
      Promise.resolve(updater().addListener('status', state => {
        showOtaStatus(state);
        checkUpdate({ automatic: true });
      })).catch(() => {});
    } catch { /* A consulta automática ao abrir continua disponível. */ }
  }
  startUpdater();
  Promise.resolve().then(() => checkUpdate({ automatic: true }));
  if (root.cdbApp?.getOperator?.()?.trim()) openHome();
})(typeof window !== 'undefined' ? window : globalThis);
