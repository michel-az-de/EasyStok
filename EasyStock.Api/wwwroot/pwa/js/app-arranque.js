
// LiveUpdater (@capgo/capacitor-updater): notifica que o bundle subiu OK.
// Sem isso, o plugin faz rollback automatico apos appReadyTimeout (30s).
// Roda logo no boot — antes de qualquer await/inicializacao pesada.
(function setupLiveUpdater() {
  try {
    var Plugin = window.Capacitor
              && window.Capacitor.Plugins
              && window.Capacitor.Plugins.CapacitorUpdater;
    if (!Plugin) return; // browser: ignora
    Plugin.notifyAppReady()
      .then(() => console.log('[LiveUpdater] notifyAppReady OK'))
      .catch(e => console.warn('[LiveUpdater] notifyAppReady falhou:', e));

    // Listeners pra visibilidade do que o plugin esta fazendo. Toast leve
    // apenas pra eventos importantes — operador nao deve se incomodar.
    function showLiveToast(msg) {
      try { showToast(msg); } catch (e) { console.log('[LiveUpdater]', msg); }
    }
    Plugin.addListener('downloadComplete', (info) => {
      console.log('[LiveUpdater] downloadComplete:', info);
      showLiveToast('Atualizacao baixada — abrindo nova versao...');
    });
    Plugin.addListener('updateAvailable', (info) => {
      console.log('[LiveUpdater] updateAvailable:', info);
    });
    Plugin.addListener('updateFailed', (info) => {
      console.warn('[LiveUpdater] updateFailed:', info);
    });
    Plugin.addListener('downloadFailed', (info) => {
      console.warn('[LiveUpdater] downloadFailed:', info);
    });
    Plugin.addListener('noNeedUpdate', () => {
      console.log('[LiveUpdater] noNeedUpdate (ja na versao mais nova)');
    });
  } catch (e) { /* nao-Capacitor — ignora */ }
})();

// ================================================================
// DEEP LINKING — handler de cdb://*
// Disparado pelos App Shortcuts (long-press no icone) e por chamadas
// externas (adb am start, links em apps de mensagem). Mapeamento:
//   cdb://orders/new   → tela Pedidos, aba Novo
//   cdb://orders/<id>  → tela Pedidos, aba Andamento (foco no pedido)
//   cdb://cash         → tela Caixa
//   cdb://scan         → tela Conferencia (scanner ativo)
//   cdb://producao     → tela Producao
//   cdb://home         → tela Home (default)
// ================================================================
(function setupDeepLinking() {
  function route(url) {
    if (!url) return;
    var rawUrl = String(url);
    // Rejeita tentativa de path traversal — switch ja restringe a whitelist,
    // mas defesa em profundidade contra "cdb://orders/../../admin" ou variantes
    // url-encoded. Cobre %2e%2e (..) e %2f (/).
    if (/\.\.|%2e%2e|%2f/i.test(rawUrl)) {
      try { console.warn('[deeplink] rejeitado por path traversal:', rawUrl); } catch (_) {}
      return;
    }
    try {
      // Aceita "cdb://orders/new" e tambem "cdb:orders/new" (variantes
      // que alguns launchers/apps geram). Normaliza removendo o esquema.
      var path = rawUrl.replace(/^cdb:(\/\/)?/, '');
      var parts = path.split('?')[0].split('/').filter(Boolean);
      var head = (parts[0] || 'home').toLowerCase();

      console.log('[deeplink]', url, '→', head, parts.slice(1));

      switch (head) {
        case 'orders':
        case 'pedidos':
          if (typeof go === 'function') go('pedidos');
          if (parts[1] === 'new' || parts[1] === 'novo') {
            if (typeof goPedidosTab === 'function') setTimeout(function () { goPedidosTab('novo'); }, 100);
          }
          break;
        case 'cash':
        case 'caixa':
          if (typeof go === 'function') go('caixa');
          break;
        case 'scan':
        case 'conferencia':
        case 'conferir':
          if (typeof go === 'function') go('conferencia');
          break;
        case 'producao':
        case 'production':
          if (typeof go === 'function') go('producao');
          break;
        case 'home':
        default:
          if (typeof go === 'function') go('home');
      }
    } catch (e) {
      console.warn('[deeplink] erro:', e);
    }
  }

  // Capacitor App plugin emite 'appUrlOpen' quando o app eh aberto via
  // intent VIEW (cdb://...). Em browser puro, nao acontece — sem efeito.
  function attachListener() {
    try {
      var App = window.Capacitor && window.Capacitor.Plugins && window.Capacitor.Plugins.App;
      if (!App || typeof App.addListener !== 'function') return false;
      App.addListener('appUrlOpen', function (event) {
        if (event && event.url) route(event.url);
      });
      // Tambem checa launch URL (caso o app tenha sido ABERTO por deep link
      // do estado morto — appUrlOpen so dispara em re-abertura).
      if (typeof App.getLaunchUrl === 'function') {
        App.getLaunchUrl().then(function (res) {
          if (res && res.url) route(res.url);
        }).catch(function () {});
      }
      console.log('[deeplink] listener instalado');
      return true;
    } catch (e) {
      return false;
    }
  }

  if (!attachListener()) {
    document.addEventListener('deviceready', attachListener, false);
    document.addEventListener('DOMContentLoaded', attachListener, false);
    setTimeout(attachListener, 800);
    setTimeout(attachListener, 2500);
  }
})();

// ================================================================
// HARDWARE BACK BUTTON — handler inteligente
// Sem isso, o Capacitor por padrao chama App.exitApp() e o app SAI no
// primeiro toque do back. Felipe reportou que durante impressao apertar
// back fechava o app inteiro — perdia contexto da tela.
//
// Prioridades (do mais especifico pro mais geral):
//  1. Imprimindo agora       → IGNORA back (proteção critica do fluxo)
//  2. Modal aberto           → fecha modal (incluindo Ajustes, Etiquetas,
//                              Cardapio, Diagnostico, etc)
//  3. Mais menu / busca      → fecha o overlay
//  4. Tela != home           → goBack() (volta tela anterior do historico)
//  5. Em home no top         → minimiza app (NAO fecha — preserva estado)
// ================================================================
(function setupBackButton() {
  // Lista de IDs de modais conhecidos do app. Quando qualquer um esta com
  // .show ou .open, o back fecha ele em vez de navegar/minimizar.
  var MODAL_IDS = [
    // Ordem = prioridade: overlays abertos por cima de outros vêm primeiro.
    'updateModal','autoBackupModal','noPrinterModal','printerPickerModal','productActionsModal',
    'ajustarModal','problemaModal','conferProdModal','prodProblemaModal',
    // #2735: folhas que o voltar ignorava; o app saía da tela com elas abertas.
    'pagamentoEntregaModal','batchDiscardModal','cestaCalcModal','conferModal','historicoModal','batchDetailsModal',
    'cashCloseModal','etiquetasPreview','ajustesModal','cashEntryModal',
    'newProductModal','productionConfirmModal','cobrancaModal','newClientModal','itemPickerModal',
    'productModal','clientModal','batchModal','novoProdutoModal',
    'globalSearchModal','diagnosticsModal','supportModal'
  ];

  function findOpenModal() {
    for (var i = 0; i < MODAL_IDS.length; i++) {
      var el = document.getElementById(MODAL_IDS[i]);
      if (el && (el.classList.contains('show') || el.classList.contains('open') || el.classList.contains('active'))) {
        return el;
      }
    }
    // Fallback: procura qualquer overlay generico .np-overlay aberto.
    // Overlays sao position: fixed (offsetParent sempre null); getClientRects
    // so fica vazio quando o elemento nao esta renderizado.
    var overlays = document.querySelectorAll('.np-overlay.show, .modal.show, .overlay.show');
    for (var j = 0; j < overlays.length; j++) {
      if (overlays[j].getClientRects().length > 0) return overlays[j];
    }
    return null;
  }

  function tryCloseModal(el) {
    if (!el) return false;
    var id = el.id || '';
    // Mapeia IDs pra fechadores especificos quando existem (evita só remover
    // a classe e deixar listeners ativos / estado inconsistente).
    var closers = {
      ajustesModal: 'closeAjustes',
      etiquetasPreview: 'closeEtiquetasPreview',
      newProductModal: 'closeProductModal',
      cobrancaModal: 'closeCobranca',
      newClientModal: 'closeClientModal',
      itemPickerModal: 'closeItemPicker',
      productionConfirmModal: 'closeProductionConfirm',
      noPrinterModal: 'closeNoPrinterModal',
      printerPickerModal: 'closePrinterPicker',
      updateModal: 'closeUpdateModal',
      autoBackupModal: function () { autoBackupAction && autoBackupAction('skip'); },
      cashEntryModal: 'closeCashModal',
      cashCloseModal: 'closeCashCloseModal',
      productModal: 'closeProductModal',
      novoProdutoModal: 'closeProductModal',
      clientModal: 'closeClientModal',
      diagnosticsModal: 'closeDiagnostics',
      globalSearchModal: 'closeGlobalSearch',
      supportModal: 'closeSupport',
      pagamentoEntregaModal: 'closePagamentoEntrega',
      batchDiscardModal: 'closeBatchDiscardModal',
      cestaCalcModal: 'cestaCalcFechar',
      conferModal: 'closeConfer',
      historicoModal: 'closeHistorico',
      batchDetailsModal: 'closeBatchDetails'
    };
    var closer = closers[id];
    try {
      if (typeof closer === 'function') { closer(); return true; }
      if (typeof closer === 'string' && typeof window[closer] === 'function') {
        window[closer](); return true;
      }
    } catch (e) { console.warn('[backbutton] closer falhou:', e); }
    // Fallback genérico: tira as classes de visibilidade
    el.classList.remove('show'); el.classList.remove('open'); el.classList.remove('active');
    return true;
  }

  function isMoreMenuOpen() {
    var el = document.getElementById('moreMenu');
    return el && (el.classList.contains('show') || el.classList.contains('open'));
  }

  function handleBack(event) {
    try {
      // 1. Imprimindo: protege o fluxo
      if (isPrinting === true) {
        try { showToast && showToast('Aguarde a impressão terminar'); } catch (_) {}
        return true; // intercept silencioso
      }
      // 2. Modal aberto
      var modal = findOpenModal();
      if (modal) {
        tryCloseModal(modal);
        return true;
      }
      // 3. Mais menu aberto
      if (isMoreMenuOpen() && typeof closeMoreMenu === 'function') {
        closeMoreMenu();
        return true;
      }
      // No APK, o formulário pertence à lista de Pedidos; mantém o rascunho.
      if (window.CDB_CONFIG?.isPilot && typeof currentScreen !== 'undefined' && currentScreen === 'pedidos'
          && document.querySelector('[data-screen="pedidos"] .pedidos-tab-section[data-tab="novo"]')?.style.display === ''
          && typeof goPedidosTab === 'function') {
        goPedidosTab('andamento');
        return true;
      }
      // No APK, Produção e Pedidos são abas de topo: o Voltar leva ao Início,
      // sem depender de qual aba foi visitada antes.
      if (window.CDB_CONFIG?.isPilot && typeof currentScreen !== 'undefined' && (currentScreen === 'producao' || currentScreen === 'pedidos')
          && typeof go === 'function') {
        go('home');
        return true;
      }
      // 4. Não estamos em home — volta na história
      if (typeof currentScreen !== 'undefined' && currentScreen !== 'home'
          && typeof goBack === 'function') {
        goBack();
        return true;
      }
      // 5. Em home, sem modais — minimiza ao invés de fechar (preserva estado)
      try {
        var App = window.Capacitor && window.Capacitor.Plugins && window.Capacitor.Plugins.App;
        if (App && typeof App.minimizeApp === 'function') {
          App.minimizeApp();
        }
      } catch (e) { /* noop */ }
    } catch (e) {
      console.warn('[backbutton] handler erro:', e);
    }
    return false;
  }

  // O APK usa a ponte própria; não depende do plugin opcional @capacitor/app.
  window.cdbHandleBack = handleBack;
  window.cdbIsPrinting = function () { return isPrinting === true; };

  function attach() {
    try {
      var App = window.Capacitor && window.Capacitor.Plugins && window.Capacitor.Plugins.App;
      if (!App || typeof App.addListener !== 'function') return false;
      App.addListener('backButton', handleBack);
      console.log('[backbutton] listener instalado');
      return true;
    } catch (e) { return false; }
  }

  if (!attach()) {
    document.addEventListener('deviceready', attach, false);
    document.addEventListener('DOMContentLoaded', attach, false);
    setTimeout(attach, 800);
    setTimeout(attach, 2500);
  }

  // Browser/PWA: também tratar popstate pra manter consistência ao testar fora do APK
  window.addEventListener('popstate', function () {
    handleBack();
    // Adiciona uma entrada de novo pra evitar sair do app no proximo back
    try { history.pushState({ cdb: true }, '', location.href); } catch (_) {}
  });
  try { history.pushState({ cdb: true }, '', location.href); } catch (_) {}
})();

// Forca checagem manual de update (botao Ajustes > Verificar atualizacoes).
// Usa alert() ao inves de showToast porque o modal Ajustes tem z-index alto
// e o toast ficava enterrado embaixo (invisivel). alert() e nativo do
// Android WebView e sempre aparece em cima de TUDO.
function forceCheckUpdate() {
  try {
    var Plugin = window.Capacitor
              && window.Capacitor.Plugins
              && window.Capacitor.Plugins.CapacitorUpdater;
    if (!Plugin) {
      alert('Plugin LiveUpdater não disponível.\n\nIsso só funciona dentro do APK Android, não no navegador.');
      return;
    }
    alert('Verificando atualizacoes no servidor...');
    // getLatest consulta updates.json e baixa se houver versao nova.
    // Em seguida, set() ativa o bundle baixado pra proxima reabertura.
    Plugin.getLatest().then(function (info) {
      console.log('[LiveUpdater] getLatest:', info);
      if (info && info.url) {
        alert('Versao ' + info.version + ' encontrada.\n\nBaixando...');
        return Plugin.download({ url: info.url, version: info.version }).then(function (bundle) {
          console.log('[LiveUpdater] download OK:', bundle);
          return Plugin.set({ id: bundle.id }).then(function () {
            alert('Atualizacao baixada!\n\nFeche o app (deslize pra cima na lista de recentes) e abra de novo.');
          });
        });
      } else {
        alert('Você já está na última versão.\n\nNada a baixar.');
      }
    }).catch(function (e) {
      console.warn('[LiveUpdater] forceCheckUpdate falhou:', e);
      alert('Erro ao verificar atualizacao:\n\n' + (e && e.message ? e.message : JSON.stringify(e)));
    });
  } catch (e) {
    alert('Erro inesperado:\n\n' + e.message);
  }
}

// Modal "Sem impressora" — abre quando operador toca em Imprimir mas nao
// tem impressora termica pareada. Substitui o confirm() cinza do Android.
function openNoPrinterModal() {
  rememberOperationalFocus('noPrinterModal');
  const modal = document.getElementById('noPrinterModal');
  modal.classList.add('show');
  modal.querySelector('button').focus();
}
function closeNoPrinterModal() {
  document.getElementById('noPrinterModal').classList.remove('show');
  const target = operationalFocus.get('noPrinterModal');
  operationalFocus.delete('noPrinterModal');
  if (target?.isConnected && target.offsetParent !== null) target.focus();
}

// Centraliza handlers dos botoes do noPrinterModal com try/catch + log
// no btStep trail (visivel via Ajustes > Diagnostico). Se algum botao
// nao responde, conseguimos ver o erro mesmo apos reiniciar o app.
function npModalAction(kind) {
  btStep('np-modal:' + kind + ':start');
  try {
    closeNoPrinterModal();
    btStep('np-modal:' + kind + ':closed');
    if (kind === 'configure') {
      try { closeEtiquetasPreview(); } catch (e1) { btStep('np-modal:configure:closeEtq-err ' + (e1 && e1.message || e1)); }
      try { openAjustes(); btStep('np-modal:configure:ajustes-ok'); }
      catch (e2) {
        btStep('np-modal:configure:ajustes-err ' + (e2 && e2.message || e2));
        showToast('Erro ao abrir Ajustes: ' + (e2 && e2.message || e2));
      }
    } else if (kind === 'share') {
      try { etqAction('share'); btStep('np-modal:share:ok'); }
      catch (e3) {
        btStep('np-modal:share:err ' + (e3 && e3.message || e3));
        showToast('Erro ao compartilhar: ' + (e3 && e3.message || e3));
      }
    }
    // 'cancel' so fecha — ja feito acima
  } catch (e) {
    btStep('np-modal:' + kind + ':OUTER-ERR ' + (e && e.message || e));
    showToast('Erro: ' + (e && e.message || e));
  }
}

// Modal "App atualizado": compara version do bundle atual com a ultima
// vista pelo operador. Se mudou, mostra modal com release notes do commit.
function closeUpdateModal() {
  document.getElementById('updateModal').classList.remove('show');
}
(function checkAppUpdate() {
  try {
    var info = window.__BUNDLE_INFO__;
    if (!info || !info.version) return;
    var ver = String(info.version);
    // Placeholder nao substituido (build local) -> nao mostra
    if (ver.indexOf('__') === 0) return;
    // Versoes "apk-*" sao do bundle empacotado original (primeira instalacao
    // sem LiveUpdater ainda). Nao incomoda com modal — modal so pra updates
    // de verdade vindos do servidor (versoes "1.0.X").
    if (ver.indexOf('apk-') === 0) return;

    var KEY = 'cdb-last-version';
    var last = null;
    try { last = localStorage.getItem(KEY); } catch (e) {}
    if (last === ver) return; // mesma versao: nada a fazer

    // Persiste a nova versao ANTES de mostrar (evita loop se modal crashar)
    try { localStorage.setItem(KEY, ver); } catch (e) {}

    // Atualizou (ou primeiro update via LiveUpdater): mostra modal
    document.getElementById('updateModalVersion').textContent = 'v' + ver;
    var notes = (info.releaseNotes && !String(info.releaseNotes).startsWith('__'))
      ? info.releaseNotes
      : 'Melhorias e correcoes';
    document.getElementById('updateModalNotes').textContent = notes;
    // Espera o DOM principal carregar pra mostrar (visual mais natural)
    setTimeout(function () {
      document.getElementById('updateModal').classList.add('show');
    }, 600);
  } catch (e) { console.warn('[update modal]', e); }
})();

// Auto-update silencioso: ao detectar novo SW assumindo controle, recarrega
// a pagina sem pedir nada pro usuario. Combinado com network-first no sw.js,
// cada git push -> deploy SWA -> proxima abertura do app ja tem versao nova.
//
// Onda 9 — Atualizacao orquestrada pelo servidor:
// (a) /api/mobile/version reporta Ota.PwaCacheVersion (lido de sw.js).
// (b) sync.js compara com cdb-pwa-installed-version local; quando difere, chama
//     reg.update() — se ha SW novo, o updatefound listener abaixo manda
//     SKIP_WAITING pra ele ativar imediatamente, e o controllerchange recarrega.
// (c) Web Admin pode forcar isso via comando pwa_update (ver sync.js executor).
// D2 (Robustez): detecta se a runtime atual nao suporta Service Worker
// (Capacitor WebView Android antigo, WebView corporativa restrita, etc).
// Marca flag pra Diagnostico exibir + telemetria; cache offline degrada
// mas app segue funcionando online-only.
if (!('serviceWorker' in navigator)) {
  try { localStorage.setItem('cdb-degraded-mode', 'no-sw'); } catch (_) {}
  try { console.warn('[degraded] runtime sem ServiceWorker — cache offline desabilitado'); } catch (_) {}
} else {
  try { localStorage.removeItem('cdb-degraded-mode'); } catch (_) {}
}

// F10-C-2/C-4: Banner persistente de quota + dead-letter
window.addEventListener('cdb-storage-exhausted', function () {
  try {
    var el = document.getElementById('storageBanner');
    if (el) { el.hidden = false; return; }
    var banner = document.createElement('div');
    banner.id = 'storageBanner';
    banner.style.cssText = 'position:fixed;top:0;left:0;right:0;z-index:99999;background:#dc2626;color:#fff;padding:12px 16px;font-size:14px;font-weight:600;text-align:center;box-shadow:0 2px 8px rgba(0,0,0,.3)';
    banner.textContent = 'Armazenamento local cheio. Conecte a internet e sincronize para liberar espaco.';
    document.body.appendChild(banner);
  } catch (_) {}
});
window.addEventListener('cdb-deadletter-full', function () {
  try {
    var el = document.getElementById('deadletterBanner');
    if (el) { el.hidden = false; return; }
    var banner = document.createElement('div');
    banner.id = 'deadletterBanner';
    banner.style.cssText = 'position:fixed;top:0;left:0;right:0;z-index:99998;background:#f59e0b;color:#000;padding:12px 16px;font-size:14px;font-weight:600;text-align:center;box-shadow:0 2px 8px rgba(0,0,0,.3)';
    banner.textContent = 'Fila de erros cheia (200). Exporte pelo Diagnostico antes de continuar.';
    document.body.appendChild(banner);
  } catch (_) {}
});

// #2735: o pacote do APK não leva sw.js (copy-web.cjs remove); registrar ali só gerava
// um erro 404 a cada abertura do app.
if ('serviceWorker' in navigator && window.CDB_CONFIG?.isPilot !== true) {
  let refreshing = false;
  navigator.serviceWorker.addEventListener('controllerchange', () => {
    if (refreshing) return;
    refreshing = true;
    location.reload();
  });
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('sw.js?v=20260511b').then(reg => {
      // Quando um SW novo entra em "installing", aguarda terminar e manda
      // SKIP_WAITING — assim ele assume controle sem o operador precisar
      // fechar/reabrir o app.
      function attachUpdateListener(installing) {
        if (!installing) return;
        installing.addEventListener('statechange', () => {
          if (installing.state === 'installed' && navigator.serviceWorker.controller) {
            try { installing.postMessage({ type: 'SKIP_WAITING' }); } catch (e) {}
          }
        });
      }
      reg.addEventListener('updatefound', () => attachUpdateListener(reg.installing));
      // Caso ja exista um SW em waiting na hora do registro (recarga depois de
      // crash, por exemplo), pula direto.
      if (reg.waiting && navigator.serviceWorker.controller) {
        try { reg.waiting.postMessage({ type: 'SKIP_WAITING' }); } catch (e) {}
      }
      // Forca check de update toda vez que o app vira visivel
      // (usuario abre o app, ou volta do background) — Felipe pediu "so ao abrir".
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') reg.update().catch(() => {});
      });
    }).catch(err => console.warn('SW falhou:', err));
  });
}

// Oferta inicial única. Recusar não impede usar o app nem provoca novo prompt
// pelo fallback. Impressoras/Imprimir continuam pedindo a permissão ao usar.
let initialBluetoothOfferStarted = false;
function offerInitialBluetoothPermission() {
  if (initialBluetoothOfferStarted || typeof ensureBluetoothPermission !== 'function') return;
  // Só depois do onboarding: antes disso a permissão sairia por cima da boas-vindas.
  // O piloto não mostra o tour (nunca grava cdb-onboarding-done): ali basta a boas-vindas concluída.
  try {
    const tourOk = localStorage.getItem('cdb-onboarding-done') === '1';
    const pilotoIdentificado = !!(window.CDB_CONFIG && window.CDB_CONFIG.isPilot)
      && !!(localStorage.getItem('cdb-operator-name') || '').trim();
    if (!tourOk && !pilotoIdentificado) return;
  } catch (_) { return; }
  const nativePrinter = window.Capacitor && window.Capacitor.Plugins && window.Capacitor.Plugins.ThermalPrinter;
  const legacyPermissions = window.cordova && window.cordova.plugins && window.cordova.plugins.permissions;
  if (!(nativePrinter && typeof nativePrinter.requestPermissions === 'function')
      && !(legacyPermissions && typeof legacyPermissions.requestPermissions === 'function')) return;
  const offeredKey = 'cdb-bluetooth-initial-offer-v1';
  try { if (localStorage.getItem(offeredKey) === '1') return; } catch (_) {}
  initialBluetoothOfferStarted = true;
  // A marca descreve a oferta, não o estado da permissão, e precede o diálogo.
  try { localStorage.setItem(offeredKey, '1'); } catch (_) {}
  try { ensureBluetoothPermission(() => {}); } catch (_) {}
}
document.addEventListener('deviceready', offerInitialBluetoothPermission, false);
// Piloto: a boas-vindas avisa quando a operadora se identificou.
window.addEventListener('cdb-operator-ready', offerInitialBluetoothPermission);
// Capacitor pode disponibilizar o bridge sem emitir deviceready.
window.addEventListener('load', () => {
  setTimeout(offerInitialBluetoothPermission, 2000);
});

// Hooks globais de erro — registram window.onerror e unhandledrejection
// pra alimentar o cdb-error-log. Diagnostico mostra/exporta/limpa.
try { setupGlobalErrorHandlers(); } catch (e) { console.warn('setupGlobalErrorHandlers falhou', e); }

// Atalho de campo: tap longo (>=800ms) no chip de sync abre o Diagnostico.
// Tap curto continua chamando syncNow() via onclick existente.
document.addEventListener('DOMContentLoaded', function () {
  const chip = document.getElementById('syncChip');
  if (!chip) return;
  let pressTimer = null;
  let longPressed = false;
  function start() {
    longPressed = false;
    clearTimeout(pressTimer);
    pressTimer = setTimeout(function () {
      longPressed = true;
      if (navigator.vibrate) try { navigator.vibrate(20); } catch (e) {}
      openDiagnostics();
    }, 800);
  }
  function cancel() { clearTimeout(pressTimer); pressTimer = null; }
  chip.addEventListener('pointerdown', start);
  chip.addEventListener('pointerup', cancel);
  chip.addEventListener('pointerleave', cancel);
  chip.addEventListener('pointercancel', cancel);
  // Suprime o syncNow() onclick se foi long-press
  chip.addEventListener('click', function (ev) {
    if (longPressed) { ev.stopImmediatePropagation(); ev.preventDefault(); longPressed = false; }
  }, true);
});

