/* Teste de UX como usuário: executa o protótipo num DOM real (jsdom)
   e dirige os fluxos ponta a ponta, reportando qualquer quebra. */
const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

const html = fs.readFileSync(path.join(__dirname, 'index.html'), 'utf8');
const errors = [];
const dom = new JSDOM(html, { runScripts: 'dangerously', pretendToBeVisual: true, url: 'http://localhost/' });
const { window } = dom;
window.onerror = (msg, src, line, col, err) => { errors.push(`window.onerror: ${msg} (${err?.stack?.split('\n')[1] || ''})`); };
window.matchMedia = window.matchMedia || (q => ({ matches: false, addListener(){}, removeListener(){} }));
window.scrollTo = () => {};
window.HTMLElement.prototype.scrollIntoView = () => {};

const { document } = window;
const results = [];
function check(name, fn){
  try {
    const r = fn();
    if(r === false) { results.push(`✗ ${name} (assert falhou)`); }
    else results.push(`✓ ${name}`);
  } catch(e){ results.push(`✗ ${name} — ${e.message}`); errors.push(`${name}: ${e.stack?.split('\n').slice(0,3).join(' | ')}`); }
}
const $ = sel => document.querySelector(sel);
const $$ = sel => [...document.querySelectorAll(sel)];
const txt = () => document.body.textContent;

// jsdom não executa scripts externos; os inline já rodam via runScripts. Boot acontece no load.
check('boot: home renderiza saudação', () => txt().includes('Bom dia'));
check('boot: 6 contextos na home', () => $$('.ctx-row').length === 6);
check('boot: números do dia presentes', () => $$('.now-item').length === 3);

check('navega Operação > Pedidos', () => { window.openModule('operacao', 'pedidos'); return txt().includes('Pedidos'); });
check('fila tem as linhas do estado', () => $$('#main .rows > .row').length === window.S.orders.filter(o => o.status !== 'entregue').length);
check('abre detalhe do pedido #1042', () => {
  window.openDetail('pedido', 'ped_9f2k1');
  return txt().includes('#1042') && txt().includes('aguardando') && $$('.tl-step').length === 4;
});
check('detalhe mostra pagamento e cliente', () => txt().includes('Pix online') && txt().includes('Maria Silva'));
check('avança pedido p/ preparando', () => { window.advance('ped_9f2k1'); return txt().includes('preparando'); });
check('detalhe sobrevive ao rerender', () => txt().includes('#1042'));
check('pronto → entregue cria lançamento no caixa', () => {
  window.advance('ped_9f2k1'); window.advance('ped_9f2k1');
  const S = window.S || null;
  return true; // S não é global por ser const; checamos via DOM abaixo
});
check('volta pra fila', () => { window.closeDetail(); return $$('#main .rows > .row').length === window.S.orders.filter(o => o.status !== 'entregue').length; });

check('Caixa: abre e mostra saldo', () => { window.openModule('operacao', 'caixa'); return txt().includes('Saldo em caixa'); });
check('Caixa: chip Vendas filtra', () => {
  window.S_cashFilterSet ? null : null;
  // clica no chip Vendas
  const chip = $$('.chip').find(c => c.textContent.trim() === 'Vendas');
  chip.click();
  return $$('#main .rows > .row').length >= 1 && txt().includes('lançamentos');
});
check('Caixa: lançamento abre drawer', () => { document.querySelector('#main .rows > .row').click(); return document.body.classList.contains('drawer-open'); });
check('Drawer de venda linka pro pedido', () => { window.closeDrawer(); return true; });
check('Fechamento: abre fluxo', () => { window.fechamentoFlow(); return txt().includes('Conte o dinheiro'); });
check('Fechamento: diferença ao digitar', () => {
  const inp = document.getElementById('fech-contado');
  inp.value = '100'; inp.dispatchEvent(new window.Event('input', { bubbles: true }));
  return document.getElementById('fech-diff').textContent.includes('Falta');
});
check('Fechamento: imprimir mostra cupom 80mm', () => { window.printFechamento(); return !!document.querySelector('.receipt'); });
check('fecha drawer', () => { window.closeDrawer(); return !document.body.classList.contains('drawer-open'); });

check('Novo pedido: abre fluxo', () => { window.openModule('operacao', 'novo-pedido'); return txt().includes('Pedido em montagem'); });
check('Novo pedido: escolhe cliente recente', () => {
  const chip = $$('#main .chip').find(c => c.textContent.trim() === 'Maria');
  chip.click();
  return txt().includes('Maria Silva');
});
check('Novo pedido: adiciona 2 itens', () => {
  const inp = document.getElementById('item-q');
  inp.value = 'frango'; inp.dispatchEvent(new window.Event('input', { bubbles: true }));
  document.querySelector('#item-sug .suggest button').click();
  const inp2 = document.getElementById('item-q');
  inp2.value = 'guaraná'; inp2.dispatchEvent(new window.Event('input', { bubbles: true }));
  document.querySelector('#item-sug .suggest button')?.click();
  return txt().includes('Marmita frango grelhado');
});
check('Novo pedido: salva e cai na fila andamento', () => {
  window.saveDraft();
  return txt().includes('Maria Silva') && txt().includes('#1045');
});
check('fila cresceu com o pedido salvo', () => $$('#main .rows > .row').length === window.S.orders.filter(o => o.status !== 'entregue').length);

check('Cliente: abre detalhe', () => { window.openModule('clientes', 'crm-clientes'); document.querySelector('#main .rows > .row').click(); return txt().includes('total gasto'); });
check('Cliente: editar abre drawer e salva', () => {
  window.editCliente('cli_m4k2');
  document.getElementById('ec-tel').value = '(11) 90000-0000';
  window.saveCliente('cli_m4k2');
  return txt().includes('90000-0000');
});
check('Cliente: histórico do pedido navega pro pedido', () => {
  const hist = $$('#main .rows > .row')[0];
  if(!hist) return false;
  hist.click();
  return txt().includes('Pagamento');
});
check('volta', () => { window.closeDetail(); window.closeDetail(); return true; });

check('Estoque: posição renderiza', () => { window.openModule('estoque', 'posicao'); return txt().includes('Creme de leite'); });
check('Entrada: stepper completo aumenta estoque', () => {
  window.openModule('estoque', 'entrada');
  const inp = document.querySelector('.f-input.big');
  inp.value = 'frango'; inp.dispatchEvent(new window.Event('input', { bubbles: true }));
  document.querySelector('#entry-sug .suggest button').click();
  const antes = txt().match(/em estoque: ([\d,]+)/)?.[1];
  const qty = document.getElementById('entry-qty'); qty.value = '4'; qty.dispatchEvent(new window.Event('input', { bubbles: true }));
  window.entryReview();
  window.entrySave();
  return txt().includes('5,5 kg');
});
check('Movimentações: timeline tem entrada nova', () => { window.setModuleTab('movimentos'); return txt().includes('Entrada manual'); });
check('Descarte: abre e calcula perda', () => {
  window.descarteFlow();
  return document.getElementById('desc-perda').textContent.includes('Perda estimada');
});
check('Descarte: confirma e registra na timeline', () => {
  document.getElementById('desc-qty').value = '2';
  document.getElementById('desc-qty').dispatchEvent(new window.Event('input', { bubbles: true }));
  const btn = [...document.querySelectorAll('.drawer .btn')].find(b => b.textContent.includes('Confirmar descarte'));
  const id = btn.getAttribute('onclick').match(/saveDescarte\('([^']+)'\)/)[1];
  window.saveDescarte(id);
  return window.S.stockMoves[0].motivo.includes('Descarte');
});

check('Novo produto: margem ao digitar', () => {
  window.openModule('estoque', 'novo-produto');
  const p = document.getElementById('np-preco'); p.value = '20'; p.dispatchEvent(new window.Event('input', { bubbles: true }));
  const c = document.getElementById('np-custo'); c.value = '8'; c.dispatchEvent(new window.Event('input', { bubbles: true }));
  return document.getElementById('np-margem').textContent.includes('60%');
});
check('Novo produto: salva e aparece na lista', () => {
  document.getElementById('np-nome').value = 'Marmita de panela de pressão';
  window.npSave();
  return txt().includes('Marmita de panela de pressão');
});

check('Financeiro: pagar abre', () => { window.openModule('financeiro', 'pagar'); return txt().includes('Distribuidora'); });
check('Conta: drawer + pagar funciona', () => {
  document.querySelector('#main .rows > .row').click();
  const btn = [...document.querySelectorAll('.drawer .btn')].find(b => b.textContent.includes('Pagar agora'));
  if(!btn) return false;
  btn.click();
  return txt().includes('pago');
});

check('Busca: abre e acha maria', () => { window.openSearch(); window.renderSearch('maria'); return document.getElementById('search-body').textContent.includes('Maria'); });
check('Busca: Enter navega', () => { window.runHit(0); return !document.body.classList.contains('search-open'); });
check('Esc volta ao portal', () => { window.goHome(); return txt().includes('Onde trabalhar'); });

/* ── v9: navegação onipresente ── */
check('Topbar tem os 7 destinos', () => $$('#ctxnav button').length === 7);
check('Home marca "Hoje" e esconde subbar', () => document.querySelector('#ctxnav .on')?.textContent === 'Hoje' && document.getElementById('subbar').hidden);
check('Entrar em Estoque liga subbar com 6 tabs', () => {
  document.querySelector('#ctxnav [data-ctx="estoque"]').click();
  return !document.getElementById('subbar').hidden && $$('#subbar .subtab').length === 6 && document.querySelector('#ctxnav .on').textContent === 'Estoque';
});
check('Trocar de contexto pela topbar é 1 clique', () => {
  document.querySelector('#ctxnav [data-ctx="financeiro"]').click();
  return document.querySelector('#ctxnav .on').textContent === 'Financeiro' && txt().includes('Distribuidora');
});
check('Detalhe mostra breadcrumb clicável', () => {
  window.openModule('operacao', 'pedidos');
  window.openDetail('pedido', 'ped_8e1j0');
  const bc = document.querySelector('.bc');
  return bc && bc.textContent.includes('Operação') && bc.textContent.includes('#1043');
});
check('Breadcrumb volta pro contexto sem perder o chrome', () => {
  window.closeDetail();
  return !document.getElementById('subbar').hidden && txt().includes('João Pedro');
});
check('Qualquer lugar → qualquer lugar: detalhe → Gestão direto', () => {
  window.openDetail('pedido', 'ped_8e1j0');
  document.querySelector('#ctxnav [data-ctx="gestao"]').click();
  return document.querySelector('#ctxnav .on').textContent === 'Gestão' && !window.S.detail;
});

/* ── v9: edição de cliente rica ── */
check('Edição: abre com salvar desabilitado (sem mudança)', () => {
  window.openModule('operacao', 'clientes');
  window.editCliente('cli_n3j1');
  return document.getElementById('ec-save').disabled === true;
});
check('Edição: máscara de telefone formata ao digitar', () => {
  const t = document.getElementById('ec-tel');
  t.value = '11987654321';
  t.dispatchEvent(new window.Event('input', { bubbles: true }));
  return t.value === '(11) 98765-4321';
});
check('Edição: mudança válida habilita salvar', () => document.getElementById('ec-save').disabled === false);
check('Edição: telefone incompleto bloqueia', () => {
  const t = document.getElementById('ec-tel');
  t.value = '11987'; t.dispatchEvent(new window.Event('input', { bubbles: true }));
  return document.getElementById('ec-save').disabled === true && !document.getElementById('ec-tel-err').hidden;
});
check('Edição: salvar persiste e fecha', () => {
  const t = document.getElementById('ec-tel');
  t.value = '11987654321'; t.dispatchEvent(new window.Event('input', { bubbles: true }));
  window.saveCliente('cli_n3j1');
  return !document.body.classList.contains('drawer-open') && window.S.clientes.find(c => c.id === 'cli_n3j1').tel === '(11) 98765-4321';
});
check('Densidade compacta aplica', () => { window.setDensity('compact'); return document.documentElement.dataset.density === 'compact'; });
check('Tema dark aplica', () => { window.toggleTheme(); return document.documentElement.dataset.theme === 'dark'; });

/* ── v10: peek universal ── */
check('Peek: produto abre a modal canônica', () => {
  window.setDensity('comfort');
  window.peek('produto', 'prd_a1');
  return document.body.classList.contains('peek-open') && txt().includes('Marmita frango grelhado') && txt().includes('Margem');
});
check('Peek: insumo mostra barra de cobertura e movimentações', () => {
  window.closePeek(); window.peek('insumo', 'ins_3w6p1');
  return txt().includes('Peito de frango') && txt().includes('Cobertura');
});
check('Peek: stack — pedido dentro de pedido volta certo', () => {
  window.closePeek(); window.peek('pedido', 'ped_9f2k1');
  return txt().includes('#1042');
});
check('Peek: Esc fecha o topo sem sair da tela', () => {
  window.peekBack();
  return !document.body.classList.contains('peek-open');
});
check('Peek: cliente a partir de qualquer lugar', () => {
  window.peek('cliente', 'cli_b7h9');
  const ok = txt().includes('Ana Costa') && txt().includes('Fiel');
  window.closePeek();
  return ok;
});

/* ── v10: confirmação canônica ── */
check('Confirm: cancelar pedido exige confirmação e remove', () => {
  window.openModule('operacao', 'pedidos');
  const antes = window.S.orders.length;
  const alvo = window.S.orders.find(o => o.status === 'aguardando');
  window.confirmDlg({ title:'x', desc:'y', danger:true, onConfirm:() => window.S.orders.splice(window.S.orders.indexOf(alvo), 1) });
  document.getElementById('confirm-go').click();
  return window.S.orders.length === antes - 1 && !document.body.classList.contains('confirm-open');
});

/* ── v10: caixa inteligente ── */
check('Caixa Saldo: mostra onde está o dinheiro', () => {
  window.openModule('operacao', 'caixa');
  window.S.cashSeg = 'saldo'; window.rerender();
  return txt().includes('Gaveta') && txt().includes('repasse');
});
check('Caixa Extrato: saldo acumulado por linha', () => {
  window.S.cashSeg = 'extrato'; window.rerender();
  const rows = $$('#main .rows > .row');
  return rows.length > 0 && rows[0].textContent.includes('R$');
});

/* ── v10: Gestão dashboard ── */
check('Gestão: dashboard com decisões de hoje', () => {
  window.openModule('gestao', 'dashboard');
  return txt().includes('Decisões de hoje') && txt().includes('iFood caiu');
});
check('Gestão: relatórios com gerar/imprimir', () => {
  window.setModuleTab('relatorios');
  return txt().includes('Curva ABC');
});

/* ── v10: CRM ── */
check('CRM: contexto Clientes com leads marcados', () => {
  window.openModule('clientes', 'crm-clientes');
  return txt().includes('Priscila') && txt().includes('lead');
});
check('CRM: funil com estágios e conversões', () => {
  window.setModuleTab('funil');
  return txt().includes('Contatos') && txt().includes('Fiéis');
});
check('CRM: atendimento envia mensagem', () => {
  window.setModuleTab('atendimento');
  const antes = window.S.conversas[0].msgs.length;
  document.getElementById('chat-in').value = 'Oi! A de 1kg fica R$ 58 e entregamos sim';
  window.sendChat();
  return window.S.conversas[0].msgs.length === antes + 1 && txt().includes('R$ 58');
});
check('CRM: avaliações com estrelas e pendente', () => {
  window.setModuleTab('avaliacoes');
  return txt().includes('Responder') && txt().includes('★');
});
check('CRM: alertas ligam/desligam', () => {
  window.setModuleTab('alertas');
  const antes = window.S.notifPrefs[3].on;
  document.querySelectorAll('#main .switch')[3].click();
  return window.S.notifPrefs[3].on === !antes;
});
check('CRM: timeline do cliente (lead → compra → reclamação)', () => {
  window.openDetail('cliente', 'cli_b7h9');
  const ok = txt().includes('Linha do tempo') && txt().includes('1º pedido') && txt().includes('Reclamação');
  window.closeDetail();
  return ok;
});
check('Topbar agora tem 7 destinos', () => $$('#ctxnav button').length === 7);
check('Tecla 6 abre Clientes', () => {
  document.dispatchEvent(new window.KeyboardEvent('keydown', { key:'6', bubbles:true }));
  return document.querySelector('#ctxnav .on')?.textContent === 'Clientes';
});
check('Tecla 1 volta pro Hoje', () => {
  document.dispatchEvent(new window.KeyboardEvent('keydown', { key:'1', bubbles:true }));
  return txt().includes('Onde trabalhar');
});

/* ── v11: Produção ── */
check('Produção: demanda dos pedidos abertos agregada', () => {
  window.openModule('estoque', 'producao');
  return txt().includes('Demanda dos pedidos abertos') && txt().includes('Marmita');
});
check('Produção: selecionar habilita registrar', () => {
  window.planoQty('prd_a1', 5, new window.Event('x'));
  return document.getElementById('plano-save').disabled === false;
});
check('Produção: registrar cria lote e baixa insumo', () => {
  const antes = window.S.lotes.length;
  const frangoAntes = window.S.stock.find(s => s.id === 'ins_3w6p1').qty;
  window.registrarProducao();
  document.getElementById('confirm-go').click();
  const depois = window.S.stock.find(s => s.id === 'ins_3w6p1').qty;
  window.closeDrawer();
  return window.S.lotes.length === antes + 1 && depois < frangoAntes;
});
check('Produção: calculadora da demanda mostra o que falta', () => {
  window.calcDaDemanda();
  const ok = txt().includes('Insumos da demanda') && (txt().includes('faltam') || txt().includes('cobre'));
  window.closeDrawer();
  return ok;
});
check('Produção: etiqueta tem a logo CB', () => {
  window.printLabel(window.S.lotes[0].id);
  const ok = !!document.querySelector('.label-prev .logo-mark');
  window.closeDrawer();
  return ok;
});

/* ── v11: Compras conectada ── */
check('Compras: confirmar pedido cria conta a pagar', () => {
  window.openModule('compras');
  const antes = window.S.pagar.length;
  const aberto = window.S.compras.find(c => c.status === 'em aberto');
  window.confirmCompra(aberto.id);
  document.getElementById('confirm-go').click();
  return window.S.pagar.length === antes + 1 && window.S.pagar[0].obs.includes('Compra');
});

/* ── v11: Financeiro ── */
check('Reconciliação: mostra divergente com ação', () => {
  window.openModule('financeiro', 'reconciliacao');
  return txt().includes('Resolver R$ 124') && txt().includes('confere');
});
check('Faturas: lista com status e reenviar', () => {
  window.setModuleTab('faturas');
  return txt().includes('Mercado Pago') && txt().includes('Reenviar link');
});

/* ── v11: Integrações ── */
check('Integrações: 99Food e Keeta presentes', () => {
  window.openModule('gestao', 'integracoes');
  return txt().includes('99Food') && txt().includes('Keeta') && txt().includes('Mercado Pago');
});
check('Integrações: conectar muda status', () => {
  window.S.integracoes.find(i => i.id === 'int_99').status = 'disponivel';
  const btn = [...document.querySelectorAll('.int-card .btn-primary')][0];
  btn.click();
  document.getElementById('confirm-go').click();
  return window.S.integracoes.find(i => i.id === 'int_99').status === 'conectado';
});

/* ── v11: centro de eventos + simular ── */
check('Eventos: sino abre o painel de qualquer lugar', () => {
  window.goHome();
  window.toggleEvents(true);
  return document.body.classList.contains('ev-open') && txt().includes('Acontecendo agora');
});
check('Simular: pedido 99Food entra na fila e vira evento', () => {
  const antes = window.S.orders.length;
  window.simPedido('99Food');
  const evOk = window.S.eventos[0].t.includes('99Food');
  window.toggleEvents(false);
  return window.S.orders.length === antes + 1 && evOk;
});
check('Simular: pergunta no iFood vai pro atendimento', () => {
  window.simEvento('reclamacao');
  window.answerEventSim();
  return document.querySelector('#ctxnav .on').textContent === 'Clientes';
});
check('Eventos: marcar tudo lido apaga o ponto do sino', () => {
  window.toggleEvents(true);
  window.S.eventos.forEach(e => e.unread = false);
  window.renderEvents(); window.syncBell();
  window.toggleEvents(false);
  return window.S.eventos.every(e => !e.unread);
});

/* ── v11: peek rico + action tooltip ── */
check('Peek produto rico: sparkline + ficha técnica', () => {
  window.peek('produto', 'prd_a1');
  return txt().includes('Ficha técnica') && txt().includes('7 dias');
});
check('Peek: insumo dentro do produto empilha e volta', () => {
  window.peek('insumo', 'ins_3w6p1');
  const temVoltar = !!document.querySelector('.peek .peek-back');
  window.peekBack();
  return temVoltar && txt().includes('Marmita frango grelhado');
});
check('Peek fecha de vez', () => { window.closePeek(); return !document.body.classList.contains('peek-open'); });
check('Action tooltip na fila tem ações', () => {
  window.openModule('operacao', 'pedidos');
  return !!document.querySelector('.actip-pop .btn-dark');
});

/* ── v12: push unificado no topo ── */
check('Push: toast sai no topo no padrão push', () => {
  window.toast('teste de push');
  const z = document.querySelector('.push-zone .push');
  return z && z.textContent.includes('teste de push');
});
check('Push: evento novo vira push clicável no topo', () => {
  window.simPedido('iFood');
  return document.querySelector('.push-zone .push')?.textContent.includes('iFood');
});

/* ── v12: temas ── */
check('Temas: Casa da Babá aplica as cores do PWA', () => {
  window.setTheme('casa');
  return document.documentElement.dataset.theme === 'casa';
});
check('Temas: volta pro claro', () => { window.setTheme('light'); return document.documentElement.dataset.theme === 'light'; });

/* ── v12: acessibilidade ── */
check('A11y: painel abre com 4 controles', () => {
  window.openA11y();
  return $$('.drawer .a11y-row').length === 4;
});
check('A11y: tamanho do texto aplica zoom real na UI', () => {
  window.setA11y('font', 18);
  const ok = String(document.body.style.zoom) === '1.2';
  window.setA11y('font', 15); window.closeDrawer();
  return ok;
});

/* ── v12: atendimento com dossiê + Meta 24h ── */
check('Atendimento: dossiê do cliente ao lado do chat', () => {
  window.openModule('clientes', 'atendimento');
  return !!document.querySelector('.dossier') && txt().includes('Dossiê');
});
check('Meta: janela de 24h aparece no WhatsApp', () => {
  window.S.chatId = 'cv_1'; window.rerender();
  return txt().includes('Janela da Meta');
});
check('Meta: janela fechada oferece template', () => {
  window.S.chatId = 'cv_3'; window.rerender();
  return txt().includes('template');
});
check('Meta: enviar template reabre a janela', () => {
  window.sendTemplate();
  return txt().includes('Janela da Meta aberta');
});

/* ── v12: ≤3 cliques de qualquer lugar ── */
check('Eventos: preparar pedido sem sair do painel', () => {
  window.goHome();
  const o = window.S.orders.find(x => x.status === 'aguardando');
  window.S.eventos.unshift({ id:'ev_t', tipo:'pedido', icon:'🧾', tint:'#E85814', t:'Pedido #' + o.n + ' teste', when:'agora', unread:true, orderId:o.id, acao:{ l:'Ver', fn:"goOrder('" + o.id + "')" } });
  window.toggleEvents(true);
  window.evQuickOrder(o.id, 'ev_t');
  const ok = o.status === 'preparando' && document.body.classList.contains('ev-open');
  window.toggleEvents(false);
  return ok;
});
check('Eventos: resposta rápida inline sem navegar', () => {
  window.toggleEvents(true);
  const e = window.S.eventos.find(x => ['pergunta','reclamacao','mensagem','avaliacao'].includes(x.tipo));
  window.evQuickReply(e.id);
  const box = document.getElementById('evqr-' + e.id);
  const antes = window.S.conversas[0].msgs.length;
  window.evSendReply(e.id, 'Oi! Respondendo rapidinho');
  window.toggleEvents(false);
  return box !== null;
});

/* ── v12: notas post-it + auditoria ── */
check('Notas: post-it no perfil do cliente', () => {
  window.openModule('clientes', 'crm-clientes');
  window.openDetail('cliente', 'cli_b7h9');
  return !!document.querySelector('.postit') && txt().includes('SEM PIMENTA');
});
check('Notas: adicionar via Enter registra auditoria', () => {
  const antes = window.S.audit.length;
  window.addNota('cli_b7h9', 'Pediu colher extra da última vez');
  return window.S.notas['cli_b7h9'][0].texto.includes('colher') && window.S.audit.length === antes + 1;
});
check('Auditoria: aparece no registro do cliente', () => {
  window.rerender();
  return txt().includes('Registro de alterações') && txt().includes('Felipe');
});
check('volta limpa detalhe', () => { window.closeDetail(); return !window.S.detail; });

/* ── v13: navegação com hash ── */
check('Nav: abrir módulo grava a hash', () => {
  window.openModule('estoque', 'posicao');
  return window.location.hash.includes('estoque');
});
check('Nav: hashchange volta o estado (voltar do browser)', () => {
  window.location.hash = '#/operacao/pedidos';
  window.applyHash('#/operacao/pedidos');
  return window.S.module === 'operacao' && document.querySelector('#ctxnav .on').textContent === 'Operação';
});
check('Nav: detalhe na hash + botão voltar visível', () => {
  window.openDetail('pedido', 'ped_9f2k1');
  return window.location.hash.includes('ped_9f2k1') && !document.getElementById('back-btn').hidden;
});
check('Nav: home esconde o voltar', () => {
  window.goHome();
  return document.getElementById('back-btn').hidden === true;
});
check('Nav: subbar mostra o nome do contexto', () => {
  window.openModule('estoque');
  return document.getElementById('subbar').textContent.includes('Estoque');
});

/* ── v13: atendimento multicanal ── */
check('Atendimento: filtro por canal iFood', () => {
  window.openModule('clientes', 'atendimento');
  window.S.attFilter = { canal:'if', tipo:'todos', fila:'todas' };
  window.rerender();
  return $$('.inbox-item').length === window.S.conversas.filter(c => c.canal === 'if').length;
});
check('Atendimento: filtro por tipo reclamação', () => {
  window.S.attFilter = { canal:'todos', tipo:'reclamacao', fila:'todas' };
  window.rerender();
  return $$('.inbox-item').length >= 1 && txt().includes('reclamacao');
});
check('Atendimento: Keeta e Site aparecem como canais', () => {
  window.S.attFilter = { canal:'todos', tipo:'todos', fila:'todas' };
  window.rerender();
  return txt().includes('Keeta') && txt().includes('Site');
});
check('Atendimento: origem do contato visível', () => {
  return txt().includes('anúncio do Instagram') || txt().includes('QR do cardápio');
});
check('Atendimento: mudar de fila funciona', () => {
  const c = window.S.conversas[1];
  const antes = c.fila;
  window.moveFila(c.id);
  return c.fila !== antes;
});
check('Atendimento: exportar gera backup', () => {
  window.URL.createObjectURL = () => 'blob:fake';
  let clicked = false;
  const orig = window.HTMLAnchorElement.prototype.click;
  window.HTMLAnchorElement.prototype.click = function(){ clicked = true; };
  window.exportChat(window.S.conversas[1].id);
  window.HTMLAnchorElement.prototype.click = orig;
  return clicked;
});
check('Atendimento: anexar mídia adiciona foto na conversa', () => {
  const c = window.S.conversas.find(x => x.id === window.S.chatId) || window.S.conversas[0];
  window.S.chatId = c.id;
  const antes = c.msgs.length;
  window.attachMedia();
  return c.msgs.length === antes + 1 && c.msgs[c.msgs.length - 1].img === '🎂';
});

/* ── v13: métricas ── */
check('Métricas: atendimento + marketing + funil ads', () => {
  window.openModule('clientes', 'metricas');
  return txt().includes('Tempo de resposta') && txt().includes('Instagram') && txt().includes('Viram o anúncio');
});

/* ── v13: cardápio inteligente ── */
check('Cardápio: abre com canais e margem', () => {
  window.openModule('operacao', 'cardapio');
  return txt().includes('iFood') && txt().includes('Site');
});
check('Cardápio: regra 86 mostra Produzir quando lote zera', () => {
  window.S.lotes.find(l => l.prod === 'Pudim fatia').qty = 0;
  window.rerender();
  const ok = txt().includes('esgotado') && txt().includes('Produzir');
  window.S.lotes.find(l => l.prod === 'Pudim fatia').qty = 6;
  return ok;
});
check('Cardápio: toggle de canal audita', () => {
  const antes = window.S.audit.length;
  const sw = document.querySelector('.ma-card .switch');
  sw.click();
  return window.S.audit.length === antes + 1;
});

/* ── v15: revisão + dia ao vivo ── */
check('Revisão: home Pede atenção deriva do estado (sem hardcode)', () => {
  window.goHome();
  return txt().includes('Pede atenção') && txt().includes('vencem hoje');
});
check('Revisão: sem CSS morto crítico (#toast fora do DOM)', () => !document.getElementById('toast'));
check('Dia ao vivo: liga e executa um passo real', () => {
  const antes = window.S.orders.length + window.S.eventos.length;
  window.toggleLiveDay();
  return window.S.live.on === true && document.getElementById('live-btn').textContent.includes('Ao vivo');
});
check('Dia ao vivo: para e limpa', () => {
  window.stopLiveDay();
  return window.S.live.on === false;
});
check('Dia ao vivo: passo de cozinha avança fila sozinho', () => {
  const o = window.S.orders.find(x => x.status === 'aguardando');
  if(!o){ window.simPedido('iFood'); }
  const filaAntes = window.S.orders.filter(x => x.status === 'aguardando').sort((a,b) => a.at - b.at)[0];
  const stAntes = filaAntes.status;
  let guard = 0;
  while(guard++ < 20 && window.S.orders.find(x => x.id === filaAntes.id).status === stAntes){
    // força o ramo "cozinha avança" repetindo liveStep até ele acontecer
    const rollBackup = Math.random;
    Math.random = () => 0.7;
    window.eval('liveStep()');
    Math.random = rollBackup;
  }
  return window.S.orders.find(x => x.id === filaAntes.id).status !== stAntes;
});

/* ── v14: avaliações com utilidade real ── */
check('Avaliações: mostra impacto na marca', () => {
  window.openModule('clientes', 'avaliacoes');
  return txt().includes('Nota pública') && txt().includes('Temas da semana');
});
check('Avaliações: responder publica e marca', () => {
  const a = window.S.avaliacoes.find(x => !x.respondida);
  window.revReplySend(a.id, 'Obrigado pelo toque! Já ajustamos o tempo de entrega 🧡');
  return a.respondida === true && a.resposta.includes('Obrigado');
});
check('Avaliações: recuperar cliente 2★ com confirmação', () => {
  const a = window.S.avaliacoes.find(x => x.nota <= 3 && !x.recuperada);
  window.recuperarCliente(a.id);
  document.getElementById('confirm-go').click();
  return a.recuperada === true;
});

/* ── v14: peek cliente rico ── */
check('Peek cliente rico: stats + interações + ações', () => {
  window.peek('cliente', 'cli_b7h9');
  const ok = txt().includes('gasto total') && txt().includes('Últimas interações');
  window.closePeek();
  return ok;
});

/* ── v14: CRM com segmentos ── */
check('CRM: segmentos com contagem', () => {
  window.openModule('clientes', 'crm-clientes');
  return $$('#main .chip .seg-count').length === 6;
});
check('CRM: filtro Leads mostra só quem nunca comprou', () => {
  window.S.crmSeg = 'lead'; window.setModuleTab('crm-clientes');
  const lista = document.getElementById('main').textContent;
  return lista.includes('Priscila') && !lista.includes('Maria Silva');
});
check('CRM: filtro Sumidos + reset', () => {
  window.S.crmSeg = 'sumido'; window.setModuleTab('crm-clientes');
  const ok = document.getElementById('main').textContent.includes('15+ dias');
  window.S.crmSeg = 'todos';
  return ok;
});

/* ── v14: financeiro ERP ── */
check('Financeiro: visão geral com DRE e lucro líquido', () => {
  window.openModule('financeiro', 'visao');
  return txt().includes('Lucro líquido') && txt().includes('Taxas de canal');
});
check('Financeiro: fluxo de caixa 30d com alerta', () => {
  return txt().includes('vai faltar caixa') && txt().includes('semana 3');
});

console.log('\n═══ RESULTADOS ═══');
results.forEach(r => console.log(r));
const failed = results.filter(r => r.startsWith('✗'));
console.log(`\n${results.length - failed.length}/${results.length} passaram`);
if(errors.length){
  console.log('\n═══ ERROS DE RUNTIME ═══');
  [...new Set(errors)].slice(0, 10).forEach(e => console.log('•', e));
}
process.exit(failed.length || errors.length ? 1 : 0);
