(function (root) {
  'use strict';

  // Ações do APK: uma palavra e um único ícone por significado.
  const COMMANDS = {
    Atualizar: 'refresh-cw', Sincronizar: 'refresh-cw', Conectar: 'link',
    Salvar: 'check', Confirmar: 'check', Etiquetar: 'printer', Imprimir: 'printer',
    Reimprimir: 'printer', Cadastrar: 'plus', Criar: 'plus', Adicionar: 'plus',
    Editar: 'lapis', Excluir: 'lixeira', Descartar: 'lixeira',
    Arquivar: 'archive', Recuperar: 'historico', Desfazer: 'historico',
    Cancelar: 'fechar', Fechar: 'fechar', Remover: 'fechar',
    Voltar: 'chevron-right', Limpar: 'fechar',
    Cobrar: 'ficha', Preparar: 'producao', Produzir: 'producao',
    Finalizar: 'check', Entregar: 'check', Detalhes: 'ficha', Histórico: 'historico',
    Copiar: 'copy', Compartilhar: 'enviar', Enviar: 'enviar',
    Exportar: 'download', Baixar: 'download', Importar: 'download',
    Ajustar: 'configuracoes', Configurar: 'configuracoes', Ativar: 'bell',
    Guia: 'ajuda', Buscar: 'search', Trocar: 'refresh-cw', Continuar: 'chevron-right',
    Reabrir: 'historico', Reverter: 'historico', Mesclar: 'producao',
    Agendar: 'relogio', Retroativo: 'historico', Carregar: 'chevron-right',
    Opções: 'ellipsis', Pronto: 'check', Ajustes: 'configuracoes',
    Indicadores: 'ficha', Testar: 'printer', Pular: 'chevron-right',
    Escolher: 'plus', Repetir: 'refresh-cw',
    'Sincronizando…': 'refresh-cw', 'Enviando…': 'enviar'
  };
  const SHORT_LABELS = {
    'Novo pedido': 'Adicionar', '+ Novo pedido': 'Adicionar',
    'Cadastrar produto': 'Cadastrar', 'Cadastrar cliente': 'Cadastrar',
    'Criar e usar': 'Etiquetar', 'Criar e adicionar': 'Adicionar',
    'Salvar produto': 'Salvar', 'Salvar pedido': 'Salvar', 'Salvar PNG': 'Salvar',
    'Confirmar e abrir etiquetas': 'Etiquetar', 'Conferir produção': 'Produzir',
    'Iniciar preparo': 'Preparar', 'Marcar pronto': 'Finalizar', 'Conferir entrega': 'Entregar',
    'Preparar cobrança': 'Cobrar', 'Cancelar pedido': 'Cancelar',
    'Imprimir etiqueta': 'Imprimir', 'Imprimir etiquetas': 'Imprimir',
    'Excluir lote': 'Excluir', 'Recuperar lote': 'Recuperar',
    'Compartilhar no WhatsApp': 'Compartilhar', 'Copiar mensagem toda': 'Copiar',
    'Conectar produção': 'Conectar', 'Alterar conexão': 'Conectar', 'Reconectar produção': 'Conectar',
    'Verificar atualização': 'Atualizar', 'Atualizar APK': 'Atualizar',
    'Atualizar avisos': 'Atualizar', 'Configurar notificações': 'Configurar',
    'Ativar avisos neste aparelho': 'Ativar', 'Copiar identificação': 'Copiar',
    'Confirmar origem e sincronizar': 'Sincronizar', 'Desfazer importação': 'Desfazer',
    'Enviar primeira migração': 'Sincronizar', 'Voltar à versão anterior': 'Recuperar',
    'Ver passo a passo da migração': 'Guia', '← Voltar ao histórico': 'Voltar',
    'Pedido pronto': 'Pronto', 'Mais ações': 'Opções', 'Mais opções do produto': 'Opções',
    'Ajustar etiqueta': 'Ajustes', 'Ver indicadores': 'Indicadores',
    'QR + barras': 'Ambos', 'Só barras': 'Barras', 'Só QR': 'QR',
    '+ Produção': 'Produzir', 'Carregar mais': 'Carregar',
    'Produzir agora': 'Produzir', 'Ajustar pedido': 'Ajustar',
    'Continuar mesmo assim (estoque ficara negativo)': 'Continuar',
    'Adicionar item do cardápio': 'Adicionar', '+ Adicionar item do cardápio': 'Adicionar',
    'Pedido já realizado? (cadastrar retroativo)': 'Retroativo',
    'Agendar para depois? (entrega futura)': 'Agendar', '+ Cliente': 'Cadastrar',
    'Mesclar lote': 'Mesclar', 'Salvar ajuste': 'Salvar', 'Descartar lote': 'Descartar',
    'Importar backup': 'Importar', 'Exportar backup': 'Exportar',
    'Configurar impressora': 'Configurar', 'Pular tour': 'Pular', 'Criar pedido': 'Adicionar',
    'Buscar impressora': 'Buscar', 'Testar impressão': 'Testar',
    'Exportar backup (JSON)': 'Exportar', 'Restaurar de arquivo': 'Importar',
    'Verificar atualizações agora': 'Atualizar', 'Abrir diagnóstico': 'Detalhes',
    '+ Novo produto': 'Cadastrar', 'Escolher imagem': 'Escolher',
    'Exportar histórico JSON': 'Exportar', 'Restaurar do celular (auto)': 'Recuperar',
    'Tentar de novo': 'Repetir', 'Sem código': 'Nenhum', '7 dias': '7d', '30 dias': '30d',
    'Retomar migração': 'Sincronizar', 'Enviando primeira migração…': 'Enviando…',
    'Conferindo e sincronizando…': 'Sincronizando…'
  };
  const ALLOWED_SCREENS = new Set(['home', 'producao', 'pedidos', 'finalizados', 'historico', 'clientes']);
  const EXCLUDED = [
    '.nav-btn[data-nav="home"]', '.nav-btn[data-nav="more"]', '.nav-btn[data-nav="conferencia"]',
    '[data-screen="home"]', '[data-screen="caixa"]', '[data-screen="compras"]',
    '[data-screen="conferencia"]', '[data-screen="estoque"]', '[data-screen="suporte"]',
    '[data-ptab="calculadora"]', '#prodDemand', '#moreMenu', '#globalSearchBtn',
    '.k-confer-badge', '.batch-progress', '.batch-unit-row',
    '#histFilter [data-f="cash"]', '#histFilter [data-f="settings"]', '#histFilter [data-f="backup"]',
    '[onclick*="openConfer("]', '[onclick*="openConferProducao("]',
    '[onclick*="openCestaCalc"]', '[onclick*="go(\'caixa\')"]',
    '[onclick*="go(\'compras\')"]', '[onclick*="go(\'estoque\')"]',
    '[onclick*="go(\'conferencia\')"]', '[onclick*="openCash"]',
    '[onclick*="resetAll"]', '[onclick*="cestaCalcAdicionar"]',
    '[onclick*="factoryReset"]', '[onclick*="purgeOldData"]', '[onclick*="clearTestData"]',
    '[onclick*="loadPresentationMode"]', '[onclick*="clearPresentationMode"]'
  ].join(',');

  function commandFor(text, handler = '') {
    const clean = text.trim().replace(/\s+/g, ' ').replace(/^[^\p{L}\p{N}+←]+/u, '').trim();
    const raw = clean.replace(/\s+/g, '');
    const label = SHORT_LABELS[clean]
      || (/^Conferir\d+unidades?$/.test(raw) ? 'Produzir' : '')
      || (/^SalvarR\$/.test(raw) ? 'Salvar' : '')
      || clean;
    if (/advanceOrder\(/.test(handler)) return { label: label === 'Entregar' ? 'Entregar' : label === 'Finalizar' ? 'Finalizar' : 'Preparar', icon: COMMANDS[label] || 'producao' };
    return COMMANDS[label] ? { label, icon: COMMANDS[label] } : SHORT_LABELS[clean] ? { label, icon: '' } : null;
  }

  function installProductionOrdersUI(environment) {
    const doc = environment.document;
    const icon = environment.cdbPilotIcon;
    function refresh() {
      doc.querySelectorAll(EXCLUDED).forEach(node => {
        if (!node.classList.contains('pilot-out-of-scope')) node.classList.add('pilot-out-of-scope');
      });
      doc.querySelectorAll('button, .k-more-actions > summary, .pilot-product-options > summary, .pilot-label-options > summary').forEach(button => {
        if (button.matches('.nav-btn, .screen-tab:not(.pilot-new-order):not(.pilot-orders-back), .pilot-card, .pilot-bell, .pilot-topline button, #pilotHomeButton, .qty-btn, .emoji-btn, .etq-format-chip') || button.querySelector('input,select,[id]')) return;
        const command = commandFor(button.textContent, button.getAttribute('onclick') || '');
        if (!command) return;
        if (button.matches('.msg-template-btn')) command.icon = '';
        const markup = (command.icon ? icon(command.icon) : '') + '<span class="pilot-command-label">' + command.label + '</span>';
        if (button.dataset.command === command.label && button.textContent.trim() === command.label
            && button.querySelector('.pilot-command-label')
            && (!command.icon || button.querySelector('svg[data-pilot-icon]')?.getAttribute('data-pilot-icon') === command.icon)) return;
        button.innerHTML = markup;
        button.classList.add('pilot-command');
        button.dataset.command = command.label;
        if (!button.hasAttribute('aria-label') || button.dataset.commandAria === 'true') {
          button.setAttribute('aria-label', command.label);
          button.dataset.commandAria = 'true';
        }
      });
    }
    const navigate = environment.go;
    environment.go = function (screen, options) {
      const target = ALLOWED_SCREENS.has(screen) ? screen : 'producao';
      const result = navigate.call(this, target, options);
      if (target !== 'home') doc.querySelectorAll('.nav-btn').forEach(button => {
        const active = button.dataset.nav === (['pedidos', 'finalizados', 'clientes'].includes(target) ? 'pedidos' : 'producao');
        button.classList.toggle('active', active);
        if (active) button.setAttribute('aria-current', 'page');
        else button.removeAttribute('aria-current');
      });
      return result;
    };
    const productionTab = environment.goProducaoTab;
    environment.goProducaoTab = function (tab) {
      return productionTab.call(this, tab === 'lotes' ? 'lotes' : 'catalogo');
    };
    // O histórico é subordinado à produção. Os IDs antigos continuam disponíveis
    // para inicialização e importação do PWA, sem expor os módulos excluídos.
    const tabs = doc.getElementById('producaoTabs');
    if (tabs) {
      const history = doc.createElement('button');
      history.className = 'screen-tab';
      history.type = 'button';
      history.textContent = 'Histórico';
      history.addEventListener('click', () => environment.go('historico'));
      tabs.append(history);
    }
    const productionTitle = doc.getElementById('productionConfirmTitle');
    if (productionTitle) productionTitle.textContent = 'Lote';
    doc.querySelectorAll('button.cobranca-close, button.modal-close, button.etq-close').forEach(button => {
      button.innerHTML = icon('fechar');
      button.setAttribute('aria-label', 'Fechar');
      button.classList.add('pilot-icon-command');
    });
    doc.querySelectorAll('#scanModeOptions .scan-mode-card').forEach(button => {
      const label = button.querySelector('strong');
      if (label) label.textContent = { both: 'Ambos', barcode: 'Barras', qr: 'QR', none: 'Nenhum' }[button.dataset.mode];
    });
    doc.querySelectorAll('#ajustesModal .ajustes-tabs button').forEach(button => {
      button.textContent = { loja: 'Geral', eq: 'Etiquetas', manut: 'Backup' }[button.dataset.group];
    });
    doc.querySelectorAll('[onclick*="factoryReset"], #themeToggleAjustes').forEach(button => button.closest('.ajustes-section')?.classList.add('pilot-out-of-scope'));
    refresh();
    if (environment.MutationObserver) {
      let scheduled = false;
      new environment.MutationObserver(() => {
        if (scheduled) return;
        scheduled = true;
        environment.requestAnimationFrame(() => { scheduled = false; refresh(); });
      }).observe(doc.body, { childList: true, subtree: true, characterData: true });
    }
    return { refresh };
  }
  if (typeof module !== 'undefined' && module.exports) module.exports = { commandFor, COMMANDS, ALLOWED_SCREENS, installProductionOrdersUI };
  if (root.document && root.CDB_CONFIG?.isPilot) installProductionOrdersUI(root);
})(typeof window !== 'undefined' ? window : globalThis);
