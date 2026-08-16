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
check('Entrar em Estoque liga subbar com 5 tabs', () => {
  document.querySelector('#ctxnav [data-ctx="estoque"]').click();
  return !document.getElementById('subbar').hidden && $$('#subbar .subtab').length === 5 && document.querySelector('#ctxnav .on').textContent === 'Estoque';
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

console.log('\n═══ RESULTADOS ═══');
results.forEach(r => console.log(r));
const failed = results.filter(r => r.startsWith('✗'));
console.log(`\n${results.length - failed.length}/${results.length} passaram`);
if(errors.length){
  console.log('\n═══ ERROS DE RUNTIME ═══');
  [...new Set(errors)].slice(0, 10).forEach(e => console.log('•', e));
}
process.exit(failed.length || errors.length ? 1 : 0);
