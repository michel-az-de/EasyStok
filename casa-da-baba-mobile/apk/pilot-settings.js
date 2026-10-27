(function (root) {
  'use strict';
  const GROUPS = {
    loja: ['Geral', 'configuracoes', 'Operador, empresa e Pix'],
    eq: ['Etiquetas', 'printer', 'Impressoras e formato'],
    manut: ['Backup', 'archive', 'Exportar, importar e recuperar']
  };
  const STEPS = [
    { title: 'Exporte no Sistema Antigo', image: 'antigo-backup.jpg', alt: 'Sistema Antigo: Ajustes, Manut. e Exportar backup (JSON).',
      body: 'No Sistema Antigo, abra Ajustes → Manut. → Exportar backup (JSON). Guarde o arquivo e uma segunda cópia. Não desinstale nem apague os dados.' },
    { title: 'Importe neste app', image: 'novo-backup.jpg', alt: 'Configurações do app novo, na seção Backup, com a ação Importar.',
      body: 'Neste app, abra Configurações → Backup → Importar. Na tela Arquivos do Android, selecione o arquivo JSON que você exportou.' },
    { title: 'Confira os registros', image: 'catalogo.png', alt: 'Catálogo com produtos de exemplo para conferência.',
      body: 'Compare produtos, pedidos, lotes e histórico com o Sistema Antigo. Confira também as fotos. Os dados ficam neste aparelho e o envio permanece pausado até confirmar a origem.' },
    { title: 'Conecte ao EasyStok', image: 'novo-inicio.png', alt: 'Início do app novo, com o botão Conectar.',
      body: 'Use Conectar e informe o código da equipe. Confira a empresa e a loja antes de confirmar a origem e sincronizar. Depois, registre somente no app novo e preserve o backup original.' }
  ];

  function installSettings(environment) {
    const doc = environment.document, icon = environment.cdbPilotIcon;
    const modal = doc.getElementById('ajustesModal');
    if (!modal || modal.dataset.pilotSettings) return;
    modal.dataset.pilotSettings = 'true';
    const title = modal.querySelector('.ajustes-head h3');
    const tabs = modal.querySelector('.ajustes-tabs');
    const head = modal.querySelector('.ajustes-head');
    const back = doc.createElement('button');
    back.type = 'button'; back.className = 'pilot-settings-back';
    back.innerHTML = icon('chevron-right'); back.setAttribute('aria-label', 'Voltar');
    head.prepend(back);
    const open = environment.openAjustes, tab = environment.setAjustesTab;
    const sheet = modal.querySelector('.ajustes-sheet');
    function showRoot() {
      modal.dataset.view = 'root';
      title.innerHTML = icon('settings') + '<span>Configurações</span>';
      sheet.scrollTop = 0;
    }
    environment.openAjustes = function () { const result = open.apply(this, arguments); modal.dataset.dirty = 'false'; showRoot(); return result; };
    modal.addEventListener('input', () => { modal.dataset.dirty = 'true'; });
    modal.addEventListener('change', () => { modal.dataset.dirty = 'true'; });
    environment.setAjustesTab = function (group) {
      if (!GROUPS[group]) return showRoot();
      const result = tab.call(this, group);
      modal.dataset.view = group;
      title.innerHTML = icon(GROUPS[group][1]) + '<span>' + GROUPS[group][0] + '</span>';
      sheet.scrollTop = 0;
      return result;
    };
    back.addEventListener('click', () => modal.dataset.view === 'root' ? environment.closeAjustes() : showRoot());
    // O botão Voltar do Android retorna à lista antes de fechar Configurações.
    const nativeBack = environment.cdbHandleNativeBack;
    environment.cdbHandleNativeBack = function () {
      if (modal.classList.contains('show') && modal.dataset.view !== 'root'
          && !doc.querySelector('#diagnosticsModal.show, #printerPickerModal.show, #etiquetasPreview.show')) { showRoot(); return true; }
      return nativeBack?.apply(this, arguments);
    };
    tabs.querySelectorAll('button').forEach(button => {
      const [label, glyph, hint] = GROUPS[button.dataset.group];
      button.className = 'pilot-menu-button';
      button.innerHTML = environment.cdbMenuContents(label, glyph, hint);
    });
    // As funções existentes continuam disponíveis; apenas muda a navegação.
    const support = doc.createElement('div'); support.className = 'pilot-settings-support';
    support.innerHTML = environment.cdbMenuButton('diagnostics', 'Diagnóstico', 'activity')
      + environment.cdbMenuButton('guide', 'Sistema Antigo', 'archive');
    support.addEventListener('click', event => {
      const action = event.target.closest('[data-action]')?.dataset.action;
      if (action === 'diagnostics') { environment.closeAjustes(); environment.openDiagnostics(); }
      if (action === 'guide') environment.cdbOpenMigrationGuide();
    });
    tabs.after(support);
    for (const section of modal.querySelectorAll('.ajustes-section')) {
      const heading = section.querySelector('h4');
      if (heading?.textContent.includes('Formato das etiquetas')) section.dataset.group = 'eq';
      if (heading?.textContent.includes('Catálogo de produtos')) section.classList.add('pilot-out-of-scope');
      if (heading?.textContent.includes('Diagnóstico')) section.classList.add('pilot-out-of-scope');
    }
    const update = modal.querySelector('[onclick="forceCheckUpdate()"]');
    if (update) update.onclick = () => { environment.closeAjustes(); environment.go('home'); environment.forceCheckUpdate(); };
    const actions = { Exportar: 'download', Importar: 'upload', Recuperar: 'historico', Atualizar: 'refresh-cw', Diagnóstico: 'activity' };
    for (const button of modal.querySelectorAll('.ajustes-actions-stack > button')) {
      const label = button.dataset.command;
      if (!actions[label]) continue;
      button.classList.remove('pilot-command'); button.classList.add('pilot-menu-button');
      button.innerHTML = environment.cdbMenuContents(label, actions[label]);
    }
    const diagnostics = environment.openDiagnostics;
    environment.openDiagnostics = function () {
      environment.closeAjustes();
      const result = diagnostics.apply(this, arguments);
      doc.querySelectorAll('#diagnosticsModal .diag-head-btn').forEach(button => {
        button.innerHTML = icon(button.getAttribute('title') === 'Atualizar' ? 'refresh-cw' : 'copy');
      });
      return result;
    };
    const settingsButton = doc.getElementById('ajustesBtn');
    settingsButton?.setAttribute('aria-label', 'Configurações');
    settingsButton?.setAttribute('title', 'Configurações');
    const rollback = doc.getElementById('pilotRollback');
    if (rollback) {
      rollback.className = 'pilot-menu-button';
      rollback.innerHTML = environment.cdbMenuContents('Recuperar', 'historico');
      doc.querySelector('.pilot-home-controls').append(rollback);
    }
    showRoot();
  }

  function installGuide(environment) {
    const doc = environment.document, icon = environment.cdbPilotIcon;
    const dialog = doc.createElement('dialog'); dialog.id = 'pilotMigrationGuide';
    dialog.className = 'pilot-home pilot-guide'; dialog.setAttribute('aria-labelledby', 'pilotGuideTitle');
    dialog.innerHTML = '<div class="pilot-guide-head"><h2 id="pilotGuideTitle" tabindex="-1">Sistema Antigo</h2><button type="button" data-guide="close" aria-label="Fechar">' + icon('fechar') + '</button></div>'
      + '<div class="pilot-guide-body"><p id="pilotGuideProgress" role="status"></p><h3 id="pilotGuideHeading" tabindex="-1"></h3><p id="pilotGuideText"></p><figure><img id="pilotGuideImage"><figcaption>Telas de referência com dados de exemplo. A aparência do Sistema Antigo pode variar.</figcaption></figure></div>'
      + '<div class="pilot-guide-foot"><button type="button" data-guide="back">' + icon('chevron-right') + '<span>Voltar</span></button><button type="button" data-guide="next"><span>Próximo</span>' + icon('chevron-right') + '</button></div>';
    doc.body.append(dialog);
    let index = 0, focus;
    function render() {
      const step = STEPS[index];
      doc.getElementById('pilotGuideProgress').textContent = 'Passo ' + (index + 1) + ' de ' + STEPS.length;
      doc.getElementById('pilotGuideHeading').textContent = step.title;
      doc.getElementById('pilotGuideText').textContent = step.body;
      const image = doc.getElementById('pilotGuideImage'); image.src = 'guia/' + step.image; image.alt = step.alt;
      dialog.querySelector('[data-guide=back]').disabled = index === 0;
      dialog.querySelector('[data-guide=next] span').textContent = index === STEPS.length - 1 ? 'Concluir' : 'Próximo';
      dialog.querySelector('.pilot-guide-body').scrollTop = 0;
    }
    environment.cdbOpenMigrationGuide = function () {
      focus = doc.activeElement; index = 0; render();
      if (!dialog.open) dialog.showModal();
      doc.getElementById('pilotGuideTitle').focus();
    };
    dialog.addEventListener('click', event => {
      const action = event.target.closest('[data-guide]')?.dataset.guide;
      if (action === 'close' || (action === 'next' && index === STEPS.length - 1)) dialog.close();
      else if (action === 'next' || (action === 'back' && index > 0)) {
        index += action === 'next' ? 1 : -1; render(); doc.getElementById('pilotGuideHeading').focus();
      }
    });
    dialog.addEventListener('close', () => { if (focus?.isConnected) focus.focus(); });
    const nativeBack = environment.cdbHandleNativeBack;
    environment.cdbHandleNativeBack = function () { if (dialog.open) { dialog.close(); return true; } return nativeBack?.apply(this, arguments); };
  }
  if (typeof module !== 'undefined' && module.exports) module.exports = { GROUPS, STEPS, installSettings, installGuide };
  if (root.document && root.CDB_CONFIG?.isPilot) { installSettings(root); installGuide(root); }
})(typeof window !== 'undefined' ? window : globalThis);
