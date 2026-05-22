import { renderEtiqueta } from './render.js';
import { renderCodes }   from './codes.js';

// ── State ────────────────────────────────────────────────────────────────────
let _payload    = null;   // EtiquetaRenderPayload from API
let _templates  = [];     // EtiquetaTemplateListItem[]
let _loteId     = null;
let _empresaId  = null;
let _selectedTemplate = null;  // { origem, id, nome, layoutJson }
let _offlineMode = false;
let _abortCtrl  = null;   // aborts in-flight loads when overlay closes

const _FALLBACK_TEMPLATES = [
  { Id:'sys-identificacao', Origem:'Sistema', Nome:'Identificação', Descricao:'Compacta. Logo + nome + lote + QR.', IsDefault:true, LayoutJson:JSON.stringify({v:1,size:{preset:"80x40mm",w_mm:80,h_mm:40,orientation:"horizontal"},elements:[{id:"logo",type:"image",asset:"system:lockup-easystok",x_mm:2,y_mm:2,w_mm:10,h_mm:5,locked:false},{id:"nome",type:"text",content:"{produto.nome}",x_mm:14,y_mm:2,w_mm:44,h_mm:8,font:"sans",size_pt:14,weight:700,align:"left",overflow:"shrink-then-ellipsis"},{id:"marca",type:"text",content:"{produto.marca}",x_mm:14,y_mm:11,w_mm:44,h_mm:5,font:"sans",size_pt:9,weight:400,align:"left",overflow:"shrink-then-ellipsis"},{id:"lote",type:"text",content:"LOT {lote.codigo}",x_mm:2,y_mm:26,w_mm:56,h_mm:4,font:"mono",size_pt:9,weight:700,align:"left",overflow:"clip"},{id:"val",type:"text",content:"VAL {lote.validadeEm:dd/MM/yyyy}",x_mm:2,y_mm:31,w_mm:56,h_mm:5,font:"mono",size_pt:9,weight:400,align:"left",overflow:"clip"},{id:"qr",type:"code",format:"qr",content:"{etiqueta.codigo}",x_mm:60,y_mm:4,w_mm:18,h_mm:18,quiet_zone_mm:1},{id:"seq",type:"text",content:"{etiqueta.sequencial}",x_mm:60,y_mm:22,w_mm:18,h_mm:5,font:"mono",size_pt:8,weight:400,align:"center",overflow:"clip"},{id:"footer",type:"text",content:"@easystok",x_mm:62,y_mm:35,w_mm:16,h_mm:3,font:"sans",size_pt:6,weight:400,align:"center",color:"ink-500",locked:false}]}) },
  { Id:'sys-nutricional', Origem:'Sistema', Nome:'Com tabela nutricional', Descricao:'Identificação + tabela ANVISA-friendly.', IsDefault:false, LayoutJson:JSON.stringify({v:1,size:{preset:"80x40mm",w_mm:80,h_mm:40,orientation:"horizontal"},elements:[{id:"logo",type:"image",asset:"system:logo-easystok",x_mm:2,y_mm:2,w_mm:6,h_mm:6,locked:false},{id:"nome",type:"text",content:"{produto.nome}",x_mm:12,y_mm:2,w_mm:40,h_mm:7,font:"sans",size_pt:11,weight:700,align:"left",overflow:"shrink-then-ellipsis"},{id:"lote",type:"text",content:"LOT {lote.codigo}",x_mm:12,y_mm:10,w_mm:40,h_mm:4,font:"mono",size_pt:8,weight:700,align:"left",overflow:"clip"},{id:"val",type:"text",content:"VAL {lote.validadeEm:dd/MM/yyyy}",x_mm:12,y_mm:15,w_mm:40,h_mm:4,font:"mono",size_pt:8,weight:400,align:"left",overflow:"clip"},{id:"nutri",type:"nutritional-table",x_mm:2,y_mm:20,w_mm:52,h_mm:16,size_pt_min:6,size_pt_max:8},{id:"qr",type:"code",format:"qr",content:"{etiqueta.codigo}",x_mm:60,y_mm:2,w_mm:18,h_mm:18,quiet_zone_mm:1},{id:"empresa",type:"text",content:"{empresa.nome}",x_mm:56,y_mm:22,w_mm:22,h_mm:4,font:"sans",size_pt:7,weight:400,align:"center",overflow:"shrink-then-ellipsis"},{id:"footer",type:"text",content:"@easystok",x_mm:62,y_mm:35,w_mm:16,h_mm:3,font:"sans",size_pt:6,weight:400,align:"center",color:"ink-500",locked:false}]}) },
  { Id:'sys-refeicao', Origem:'Sistema', Nome:'Refeição completa', Descricao:'Nutrição + alérgenos. Refeições prontas.', IsDefault:false, LayoutJson:JSON.stringify({v:1,size:{preset:"80x40mm",w_mm:80,h_mm:40,orientation:"horizontal"},elements:[{id:"logo",type:"image",asset:"system:logo-easystok",x_mm:2,y_mm:2,w_mm:6,h_mm:6,locked:false},{id:"nome",type:"text",content:"{produto.nome}",x_mm:12,y_mm:2,w_mm:40,h_mm:6,font:"sans",size_pt:11,weight:700,align:"left",overflow:"shrink-then-ellipsis"},{id:"val",type:"text",content:"VAL {lote.validadeEm:dd/MM/yyyy}",x_mm:12,y_mm:9,w_mm:40,h_mm:4,font:"mono",size_pt:8,weight:700,align:"left",overflow:"clip"},{id:"nutri",type:"nutritional-table",x_mm:2,y_mm:14,w_mm:46,h_mm:16},{id:"alergenos",type:"alergenos-pills",x_mm:2,y_mm:31,w_mm:54,h_mm:4},{id:"qr",type:"code",format:"qr",content:"{etiqueta.codigo}",x_mm:60,y_mm:2,w_mm:18,h_mm:18,quiet_zone_mm:1},{id:"lote",type:"text",content:"LOT {lote.codigo}",x_mm:56,y_mm:22,w_mm:22,h_mm:4,font:"mono",size_pt:7,weight:400,align:"center",overflow:"clip"},{id:"empresa",type:"text",content:"{empresa.nome}",x_mm:56,y_mm:27,w_mm:22,h_mm:4,font:"sans",size_pt:7,weight:400,align:"center",overflow:"shrink-then-ellipsis"},{id:"footer",type:"text",content:"@easystok",x_mm:62,y_mm:35,w_mm:16,h_mm:3,font:"sans",size_pt:6,weight:400,align:"center",color:"ink-500",locked:false}]}) },
];

// ── Public API (called from index.html inline handlers + lote card) ──────────

window.etqAbrirImprimir = async function(loteId, empresaId) {
  if (_abortCtrl) _abortCtrl.abort();
  _abortCtrl = new AbortController();
  _loteId    = loteId;
  _empresaId = empresaId;
  _payload   = null;

  const overlay = document.getElementById('etq-imprimir-overlay');
  overlay.style.display = 'flex';

  _setStatus('Carregando…');
  try {
    await Promise.all([_loadTemplates(_abortCtrl.signal), _loadPayload(_abortCtrl.signal)]);
    _populateTemplateSel();
    _updateCounts();
    await _atualizarPreview();
    _setStatus('');
  } catch (err) {
    if (err.name !== 'AbortError') _setStatus('Erro ao carregar: ' + err.message);
  }
};

window.etqImpFechar = function() {
  if (_abortCtrl) { _abortCtrl.abort(); _abortCtrl = null; }
  document.getElementById('etq-imprimir-overlay').style.display = 'none';
  _payload = null;
};

window.etqImpAtualizarPreview = async function() {
  await _atualizarPreview();
};

window.etqImpRangeChange = function() {
  _updateCounts();
};

window.etqImpImprimir = async function() {
  const ids = _getSelectedIds();
  if (!ids.length) { _setStatus('Nenhuma etiqueta selecionada.'); return; }

  const template = _selectedTemplate;
  if (!template) { _setStatus('Selecione um modelo.'); return; }

  // Modal: >100 etiquetas
  if (ids.length > 100) {
    await _confirm({
      title: `Imprimir ${ids.length} etiquetas?`,
      body:  'O navegador pode travar em volumes acima de 100. Baixar PDF é mais estável.',
      btns: [
        { label: 'Baixar PDF',         action: () => etqImpBaixarPdf() },
        { label: 'Imprimir mesmo assim', action: () => _executarImpressao(ids, template), primary: true },
        { label: 'Cancelar',           action: null },
      ]
    });
    return;
  }

  // Modal: etiquetas já impressas com mesmo modelo
  const jaImpressas = ids.filter(id => {
    const e = _payload.Etiquetas.find(x => x.Id === id);
    return e && (e.Status === 'impressa' || e.Status === 'enviada_impressao');
  });
  if (jaImpressas.length > 0 && !_temSnapshotDivergente(ids, template)) {
    await _confirm({
      title: 'Reimprimir etiquetas?',
      body:  `${jaImpressas.length} dessas etiquetas já foram impressas antes.`,
      btns: [
        { label: 'Imprimir mesmo assim',  action: () => _executarImpressao(ids, template, true), primary: true },
        { label: 'Imprimir só pendentes', action: () => _executarImpressao(ids.filter(id => {
            const e = _payload.Etiquetas.find(x => x.Id === id);
            return e && e.Status === 'pendente';
          }), template) },
        { label: 'Cancelar', action: null },
      ]
    });
    return;
  }

  // Modal: modelo diferente do snapshot
  if (_temSnapshotDivergente(ids, template)) {
    const primeiraImpressao = _payload.Etiquetas.find(e => ids.includes(e.Id) && e.LayoutSnapshotMeta);
    const nomeAnterior = _snapshotNome(primeiraImpressao?.LayoutSnapshotMeta);
    await _confirm({
      title: 'Reimprimir com modelo diferente?',
      body:  `Lote impresso com '${nomeAnterior}'. Reimprimir com '${template.nome}' cria inconsistência visual entre etiquetas do mesmo lote.`,
      btns: [
        { label: `Continuar com '${template.nome}'`, action: () => _executarImpressao(ids, template, true), primary: true },
        { label: `Manter '${nomeAnterior}'`,         action: null },
        { label: 'Cancelar',                          action: null },
      ]
    });
    return;
  }

  await _executarImpressao(ids, template);
};

window.etqImpBaixarPdf = function() {
  if (!_loteId || !_selectedTemplate) return;
  const ids = _getSelectedIds();
  if (!ids.length) { _setStatus('Nenhuma etiqueta selecionada.'); return; }
  // Sem render PDF no servidor: imprime via navegador (use "Salvar como PDF" no diálogo).
  _executarImpressao(ids, _selectedTemplate);
};

window.etqAbrirModelos = async function() {
  document.getElementById('etq-modelos-overlay').style.display = 'flex';
  await _carregarModelos();
};

window.etqModelosFechar = function() {
  document.getElementById('etq-modelos-overlay').style.display = 'none';
};

window.etqModelosAba = function(aba) {
  const btnSis = document.getElementById('etq-tab-sistema');
  const btnEmp = document.getElementById('etq-tab-empresa');
  btnSis.style.borderBottomColor = aba === 'sistema' ? 'var(--navy-700,#1A2E5A)' : 'transparent';
  btnEmp.style.borderBottomColor = aba === 'empresa' ? 'var(--navy-700,#1A2E5A)' : 'transparent';
  btnSis.setAttribute('aria-selected', aba === 'sistema' ? 'true' : 'false');
  btnEmp.setAttribute('aria-selected', aba === 'empresa' ? 'true' : 'false');
  _renderModeloLista(aba);
};

window.etqModelosCriar = function() {
  if (_offlineMode) {
    alert('Criar modelos personalizados requer conexão com o servidor.');
    return;
  }
  window.etqAbrirEditor && window.etqAbrirEditor(null);
};

// ── Internal helpers ─────────────────────────────────────────────────────────

async function _loadTemplates(signal) {
  try {
    const res = await _apiFetch(`/api/etiquetas/templates?empresaId=${_empresaId}`, signal);
    _templates = res.data ?? [];
    _offlineMode = false;
  } catch (err) {
    if (err.name === 'AbortError') throw err;
    _templates = _FALLBACK_TEMPLATES;
    _offlineMode = true;
  }
}

async function _loadPayload(signal) {
  const t = _selectedTemplate;
  let url = `/api/lotes/${_loteId}/etiquetas/render?empresaId=${_empresaId}`;
  if (t) url += `&templateOrigem=${t.origem}&templateId=${t.id}`;
  const res = await _apiFetch(url, signal);
  _payload = res.data;
}

function _populateTemplateSel() {
  const sel = document.getElementById('etq-imp-template-sel');
  sel.innerHTML = '';
  for (const t of _templates) {
    const opt = document.createElement('option');
    opt.value = JSON.stringify({ origem: t.Origem, id: t.Id, nome: t.Nome, layoutJson: t.LayoutJson });
    opt.textContent = (t.IsDefault ? '★ ' : '') + (t.Origem === 'Sistema' ? `[Pronto] ${t.Nome}` : t.Nome);
    if (t.IsDefault) opt.selected = true;
    sel.appendChild(opt);
  }
  _syncSelectedTemplate();
}

function _syncSelectedTemplate() {
  const sel = document.getElementById('etq-imp-template-sel');
  if (!sel.value) return;
  try { _selectedTemplate = JSON.parse(sel.value); } catch { _selectedTemplate = null; }
}

function _updateCounts() {
  if (!_payload) return;
  const etiquetas = _payload.Etiquetas ?? [];
  const todas     = etiquetas.length;
  const pendentes = etiquetas.filter(e => e.Status === 'pendente').length;
  document.getElementById('etq-imp-count-todas').textContent    = todas;
  document.getElementById('etq-imp-count-pendentes').textContent = pendentes;

  const semFicha = _payload.ProdutosSemFicha ?? [];
  const divSF = document.getElementById('etq-imp-sem-ficha');
  if (semFicha.length > 0) {
    document.getElementById('etq-imp-sem-ficha-count').textContent = `${semFicha.length} produto(s)`;
    divSF.style.display = 'block';
  } else {
    divSF.style.display = 'none';
  }
}

function _getSelectedIds() {
  if (!_payload) return [];
  const etiquetas = _payload.Etiquetas ?? [];
  const range = document.querySelector('input[name="etq-range"]:checked')?.value ?? 'todas';
  if (range === 'pendentes') return etiquetas.filter(e => e.Status === 'pendente').map(e => e.Id);
  if (range === 'intervalo') {
    const from = parseInt(document.getElementById('etq-imp-from').value) || 1;
    const to   = parseInt(document.getElementById('etq-imp-to').value)   || etiquetas.length;
    return etiquetas.filter(e => e.Sequencial >= from && e.Sequencial <= to).map(e => e.Id);
  }
  return etiquetas.map(e => e.Id);
}

async function _atualizarPreview() {
  _syncSelectedTemplate();
  if (!_payload || !_selectedTemplate) return;

  // Reload payload with selected template
  try {
    const t = _selectedTemplate;
    const url = `/api/lotes/${_loteId}/etiquetas/render?empresaId=${_empresaId}&templateOrigem=${t.origem}&templateId=${t.id}`;
    const res = await _apiFetch(url);
    _payload = res.data;
  } catch { /* keep existing */ }

  const area = document.getElementById('etq-imp-preview-area');
  area.innerHTML = '';

  if (!_payload?.Etiquetas?.length) return;

  const primeira = _payload.Etiquetas[0];
  let layout;
  try { layout = JSON.parse(_selectedTemplate.layoutJson); } catch { return; }

  const dados = _etiquetaToDados(primeira);
  const el = renderEtiqueta(layout, dados);
  area.appendChild(el);
  await renderCodes(area);

  const chip = document.getElementById('etq-imp-preview-chip');
  chip.textContent = `Etiqueta 1 de ${_payload.Etiquetas.length}. As demais seguem o mesmo layout.`;

  _updateCounts();
  _atualizarBannerPreDeploy();
  document.getElementById('etq-imp-title').textContent = `Imprimir etiquetas — Lote ${_payload.Etiquetas[0]?.LoteCodigo ?? ''}`;
}

function _etiquetaToDados(e) {
  return {
    produto:  e.Produto,
    etiqueta: { Codigo: e.Codigo, Sequencial: e.Sequencial },
    lote:     { Codigo: e.LoteCodigo, ValidadeEm: e.LoteValidadeEm, CriadoEm: e.LoteCriadoEm },
    empresa:  _payload?.Empresa,
  };
}

async function _executarImpressao(ids, template, overwrite = false) {
  if (!ids.length) return;
  _setStatus('Enviando para impressão…');

  // Montar documento de impressão
  let layout;
  try { layout = JSON.parse(template.layoutJson); } catch { _setStatus('Layout inválido.'); return; }

  const printWin = window.open('', '_blank', 'width=800,height=600');
  if (!printWin) {
    // BUG #28: feedback claro tambem fora do status inline pra desktop/sem hardware.
    _setStatus('Pop-up bloqueado. Permita pop-ups e tente novamente.');
    _showToastEtq('Pop-up bloqueado — habilite pop-ups e tente de novo.');
    return;
  }

  printWin.document.write(_buildPrintHtml(ids, layout));
  printWin.document.close();
  _showToastEtq(`Etiquetas enviadas (${ids.length}). Verifique a janela de impressao.`);

  // Marcar como enviada_impressao
  try {
    const headers = { 'Content-Type': 'application/json' };
    if (overwrite) headers['X-Overwrite-Snapshot'] = 'true';
    await fetch(`/api/lotes/${_loteId}/etiquetas/marcar-impressas?empresaId=${_empresaId}`, {
      method: 'POST',
      headers,
      credentials: 'include',
      body: JSON.stringify({
        ids,
        layoutJson: template.layoutJson,
        layoutMeta: { origem: template.origem, id: template.id, nome: template.nome },
        status: 'enviada_impressao',
      }),
    });
  } catch { /* non-blocking */ }

  _setStatusHtml(`${_esc(String(ids.length))} etiquetas enviadas para impressão. ` +
             '<a href="#" onclick="etqMarcarImpressas();return false">Marcar como impressas →</a>');
}

function _buildPrintHtml(ids, layout) {
  if (!_payload) return '';
  const etiquetas = _payload.Etiquetas.filter(e => ids.includes(e.Id));
  const wMm = layout.size?.w_mm ?? 80;
  const hMm = layout.size?.h_mm ?? 40;

  const labelHtmls = etiquetas.map((e, i) => {
    const dados = _etiquetaToDados(e);
    const el = renderEtiqueta(layout, dados, { forPrint: i === 0 });
    return `<section class="etq-page" style="page-break-after:always">${el.outerHTML}</section>`;
  }).join('');

  return `<!DOCTYPE html><html><head><meta charset="utf-8">
<title>Etiquetas — ${_payload.Etiquetas[0]?.LoteCodigo ?? ''}</title>
<link rel="stylesheet" href="/etiqueta/etiqueta.css">
<style>
  @page { size: ${wMm}mm ${hMm}mm; margin: 0; }
  body  { margin: 0; padding: 0; }
  .etq-page:last-child { page-break-after: avoid; break-after: avoid; }
</style>
</head><body>
${labelHtmls}
<script src="/etiqueta/vendor/qrcode.min.js"><\/script>
<script src="/etiqueta/vendor/jsbarcode.min.js"><\/script>
<script type="module">
  import { renderCodes } from '/etiqueta/codes.js';
  (async () => { for (const r of document.querySelectorAll('.etq-label')) { try { await renderCodes(r); } catch(e) { console.warn('[etq] renderCodes:', e); } } })();
  window.addEventListener('load', () => setTimeout(() => window.print(), 500));
<\/script>
</body></html>`;
}

function _temSnapshotDivergente(ids, template) {
  if (!_payload) return false;
  return _payload.Etiquetas.some(e => {
    if (!ids.includes(e.Id)) return false;
    if (!e.LayoutSnapshotMeta) return false;
    try {
      const meta = JSON.parse(e.LayoutSnapshotMeta);
      return meta.id !== template.id;
    } catch { return false; }
  });
}

function _snapshotNome(metaJson) {
  try { return JSON.parse(metaJson).nome; } catch { return 'modelo anterior'; }
}

window.etqMarcarImpressas = async function() {
  const ids = _getSelectedIds();
  if (!ids.length || !_selectedTemplate) return;
  try {
    await fetch(`/api/lotes/${_loteId}/etiquetas/marcar-impressas?empresaId=${_empresaId}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Overwrite-Snapshot': 'true' },
      credentials: 'include',
      body: JSON.stringify({
        ids,
        layoutJson: _selectedTemplate.layoutJson,
        layoutMeta: { origem: _selectedTemplate.origem, id: _selectedTemplate.id, nome: _selectedTemplate.nome },
        status: 'impressa',
      }),
    });
    _invalidarCacheRender();
    _setStatus(`${ids.length} etiquetas marcadas como impressas.`);
    await _loadPayload();
    _updateCounts();
    _renderStatusBadges();
  } catch (err) {
    _setStatus('Erro: ' + err.message);
  }
};

window.etqReverterPendente = async function() {
  const ids = _getSelectedIds();
  if (!ids.length || !_selectedTemplate) return;
  try {
    await fetch(`/api/lotes/${_loteId}/etiquetas/marcar-impressas?empresaId=${_empresaId}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({
        ids,
        layoutJson: _selectedTemplate.layoutJson,
        layoutMeta: { origem: _selectedTemplate.origem, id: _selectedTemplate.id, nome: _selectedTemplate.nome },
        status: 'pendente',
      }),
    });
    _invalidarCacheRender();
    _setStatus(`${ids.length} etiquetas voltaram para pendente.`);
    await _loadPayload();
    _updateCounts();
    _renderStatusBadges();
  } catch (err) {
    _setStatus('Erro: ' + err.message);
  }
};

function _invalidarCacheRender() {
  try {
    const url = `/api/lotes/${_loteId}/etiquetas/render?empresaId=${_empresaId}`;
    if (navigator.serviceWorker?.controller) {
      navigator.serviceWorker.controller.postMessage({ type: 'CACHE_DELETE', url });
    }
  } catch { /* non-blocking */ }
}

function _renderStatusBadges() {
  if (!_payload) return;
  const area = document.getElementById('etq-imp-status-badges');
  if (!area) return;
  const counts = { pendente: 0, enviada_impressao: 0, impressa: 0 };
  for (const e of _payload.Etiquetas) {
    const s = e.Status ?? 'pendente';
    if (s in counts) counts[s]++;
  }
  const labels = { pendente: 'Pendentes', enviada_impressao: 'Enviadas', impressa: 'Impressas' };
  const cls    = { pendente: 'etq-status--pendente', enviada_impressao: 'etq-status--enviada_impressao', impressa: 'etq-status--impressa' };
  area.innerHTML = Object.entries(counts)
    .filter(([, n]) => n > 0)
    .map(([s, n]) => `<span class="etq-status ${cls[s]}">${labels[s]}: ${n}</span>`)
    .join(' ');
}

// ── Modelos ──────────────────────────────────────────────────────────────────

let _modelosAba = 'sistema';

async function _carregarModelos() {
  const lista = document.getElementById('etq-modelos-lista');
  lista.innerHTML = '<div class="etq-caption" style="padding:12px">Carregando…</div>';
  await _loadTemplates();
  const empresa = _templates.filter(t => t.Origem === 'Empresa');
  document.getElementById('etq-tab-empresa-count').textContent = empresa.length;
  const criarBtn = document.getElementById('etq-modelos-criar-btn');
  if (criarBtn) criarBtn.style.display = _offlineMode ? 'none' : '';
  _renderModeloLista(_modelosAba);
}

function _renderModeloLista(aba) {
  _modelosAba = aba;
  const lista = document.getElementById('etq-modelos-lista');
  const items = _templates.filter(t => (aba === 'sistema' ? t.Origem === 'Sistema' : t.Origem === 'Empresa'));

  if (!items.length) {
    lista.innerHTML = `<div style="text-align:center;padding:32px 16px">
      <div style="font-size:32px;margin-bottom:8px;opacity:0.4">🏷️</div>
      <div class="etq-section-lbl" style="margin-bottom:4px">${aba === 'empresa' ? 'Nenhum modelo personalizado' : 'Nenhum modelo do sistema'}</div>
      <div class="etq-caption">${aba === 'empresa'
        ? (_offlineMode
          ? 'Conecte ao servidor para duplicar modelos prontos e criar os seus.'
          : 'Duplique um modelo pronto na aba anterior para começar.')
        : 'Modelos prontos aparecerão aqui.'}</div>
    </div>`;
    return;
  }

  lista.innerHTML = items.map(t => {
    const eId = _esc(t.Id);
    const eNome = _esc(t.Nome);
    const eOrigem = _esc(t.Origem);
    const eDesc = t.Descricao ? _esc(t.Descricao) : '';
    const features = _getFeatureTags(t);
    return `
    <div class="etq-tpl-card">
      <div class="etq-tpl-card-thumb" id="thumb-${eId}" onclick="etqModeloPreview('${eId}')" title="Toque para ampliar">
        <span class="etq-tpl-card-thumb-placeholder">⊞</span>
      </div>
      <div style="padding:10px 12px 12px">
        <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap">
          <span class="etq-section-lbl">${eNome}</span>
          ${t.IsDefault ? '<span class="etq-status etq-status--impressa">Padrão</span>' : ''}
        </div>
        ${eDesc ? `<div class="etq-caption" style="margin-top:2px">${eDesc}</div>` : ''}
        <div class="etq-tpl-features">${features}</div>
        ${_temNutricional(t)
          ? '<div class="etq-disclaimer" style="margin-top:6px">ℹ️ Confira a RDC 727 antes de usar em embalagem de venda.</div>'
          : ''}
        <div style="display:flex;flex-wrap:wrap;gap:6px;margin-top:8px">
          <button class="etq-btn-secondary" style="font-size:12px;padding:4px 10px" onclick="etqModeloPreview('${eId}')">Visualizar</button>
          ${aba === 'sistema' ? `
            ${_offlineMode ? '' : `<button class="etq-btn-secondary" style="font-size:12px;padding:4px 10px" onclick="etqModeloDefinirPadrao('${eOrigem}','${eId}')">Definir como padrão</button>`}
            ${_offlineMode ? '' : `<button class="etq-btn-secondary" style="font-size:12px;padding:4px 10px" onclick="etqModeloDuplicar('${eId}','${eNome}')">Duplicar</button>`}
          ` : `
            <button class="etq-btn-secondary" style="font-size:12px;padding:4px 10px" onclick="window.etqAbrirEditor && etqAbrirEditor('${eId}')">Editar</button>
            <button class="etq-btn-secondary" style="font-size:12px;padding:4px 10px" onclick="etqModeloDefinirPadrao('${eOrigem}','${eId}')">Definir como padrão</button>
            <button class="etq-btn-secondary" style="font-size:12px;padding:4px 10px;color:var(--crit-600,#DC2626)" onclick="etqModeloExcluir('${eId}','${eNome}')">Excluir</button>
          `}
        </div>
      </div>
    </div>`;
  }).join('');

  for (const t of items) {
    _renderThumbnail(t);
  }
}

function _getFeatureTags(t) {
  const tags = [];
  try {
    const layout = JSON.parse(t.LayoutJson);
    const els = layout.elements || [];
    if (els.some(e => e.type === 'code')) tags.push('<span class="etq-tpl-feat etq-tpl-feat--qr">QR / Código</span>');
    if (els.some(e => e.type === 'nutritional-table')) tags.push('<span class="etq-tpl-feat etq-tpl-feat--nutri">Nutricional</span>');
    if (els.some(e => e.type === 'alergenos-pills')) tags.push('<span class="etq-tpl-feat etq-tpl-feat--alerg">Alérgenos</span>');
    if (els.some(e => e.type === 'image' && e.asset && e.asset.startsWith('system:'))) tags.push('<span class="etq-tpl-feat etq-tpl-feat--brand">EasyStok</span>');
    const size = layout.size;
    if (size) tags.push(`<span class="etq-tpl-feat" style="color:var(--ink-500,#6B7480);background:var(--ink-50,#F5F7FA)">${size.w_mm}×${size.h_mm}mm</span>`);
  } catch {}
  return tags.join('');
}

window.etqModeloPreview = function(id) {
  const t = _templates.find(x => x.Id === id);
  if (!t) return;
  try {
    const layout = JSON.parse(t.LayoutJson);
    const dados = { produto: { Nome: 'Bolo de Cenoura', Marca: 'Casa da Babá', FichaKcal: 250, FichaCarbsG: 30, FichaProteinaG: 12, FichaGorduraG: 8, FichaGorduraSaturadaG: 3, FichaFibrasG: 2, FichaSodioMg: 400, FichaPorcaoG: 100, FichaAlergenos: ['gluten','lactose'] }, etiqueta: { Codigo: 'ETQ-001', Sequencial: 1 }, lote: { Codigo: 'LOT-260514-001', ValidadeEm: new Date(Date.now() + 7*86400000).toISOString(), CriadoEm: new Date().toISOString() }, empresa: { Nome: 'Casa da Babá', LogoUrl: null } };
    const el = renderEtiqueta(layout, dados);
    const wrap = document.getElementById('etq-tpl-preview-wrap');
    wrap.innerHTML = '';
    wrap.appendChild(el);
    renderCodes(wrap);
    document.getElementById('etq-tpl-preview-name').textContent = t.Nome;
    document.getElementById('etq-tpl-preview').classList.add('open');
  } catch (err) { alert('Erro ao visualizar: ' + err.message); }
};

async function _renderThumbnail(t) {
  const wrap = document.getElementById(`thumb-${t.Id}`);
  if (!wrap) return;
  try {
    const layout = JSON.parse(t.LayoutJson);
    const wMm = layout.size?.w_mm ?? 80;
    const hMm = layout.size?.h_mm ?? 40;
    const wPx = wMm * 3.7795;
    const hPx = hMm * 3.7795;
    const containerW = wrap.clientWidth || 320;
    const containerH = 120;
    const scale = Math.min((containerW - 16) / wPx, (containerH - 8) / hPx);
    const scaledW = wPx * scale;

    const dados = { produto: { Nome: 'Bolo de Cenoura', Marca: 'Casa da Babá', FichaKcal: 250, FichaCarbsG: 30, FichaProteinaG: 12, FichaGorduraG: 8, FichaGorduraSaturadaG: 3, FichaFibrasG: 2, FichaSodioMg: 400, FichaPorcaoG: 100, FichaAlergenos: ['gluten','lactose'] }, etiqueta: { Codigo: 'ETQ-001', Sequencial: 1 }, lote: { Codigo: 'LOT-260514-001', ValidadeEm: new Date(Date.now() + 7*86400000).toISOString(), CriadoEm: new Date().toISOString() }, empresa: { Nome: 'Casa da Babá', LogoUrl: null } };

    const el = renderEtiqueta(layout, dados);
    el.style.transform = `scale(${scale})`;
    el.style.transformOrigin = 'top left';
    el.style.position = 'absolute';
    el.style.top = '4px';
    el.style.left = `${Math.max(8, (containerW - scaledW) / 2)}px`;
    wrap.innerHTML = '';
    wrap.appendChild(el);
    await renderCodes(wrap);
  } catch { /* keep placeholder */ }
}

window.etqModeloDefinirPadrao = async function(origem, id) {
  try {
    await fetch(`/api/etiquetas/templates/${origem}/${id}/set-default?empresaId=${_empresaId}`, {
      method: 'POST', credentials: 'include',
    });
    await _carregarModelos();
    _showToastEtq('Modelo definido como padrão.');
  } catch (err) { _showToastEtq('Erro: ' + err.message); }
};

window.etqModeloDuplicar = async function(idBase, nomeBase) {
  const nomeCopia = `${nomeBase} (cópia)`;
  try {
    const tBase = _templates.find(t => t.Id === idBase);
    if (!tBase) return;
    const res = await fetch(`/api/etiquetas/templates?empresaId=${_empresaId}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ empresaId: _empresaId, nome: nomeCopia, layoutJson: tBase.LayoutJson, baseSistemaId: tBase.Id }),
    });
    if (!res.ok) throw new Error('Falha ao duplicar.');
    _showToastEtq(`Modelo duplicado: '${nomeCopia}'.`);
    await _carregarModelos();
    etqModelosAba('empresa');
  } catch (err) { _showToastEtq('Erro: ' + err.message); }
};

window.etqModeloExcluir = async function(id, nome) {
  // Count snapshots first (API returns snapshotsExistentes)
  await _confirm({
    title: `Excluir o modelo '${nome}'?`,
    body:  'Esta ação não pode ser desfeita.',
    btns: [
      { label: 'Excluir', primary: true, danger: true, action: async () => {
        try {
          await fetch(`/api/etiquetas/templates/${id}?empresaId=${_empresaId}`, { method: 'DELETE', credentials: 'include' });
          _showToastEtq('Modelo excluído.');
          await _carregarModelos();
        } catch (err) { _showToastEtq('Erro: ' + err.message); }
      }},
      { label: 'Cancelar', action: null },
    ],
  });
};

// ── Confirm modal helper ──────────────────────────────────────────────────────

function _confirm({ title, body, btns }) {
  return new Promise(resolve => {
    const modal = document.getElementById('etq-confirm-modal');
    document.getElementById('etq-confirm-title').textContent = title;
    document.getElementById('etq-confirm-body').textContent  = body;
    const btnsEl = document.getElementById('etq-confirm-btns');
    btnsEl.innerHTML = '';
    for (const btn of btns) {
      const b = document.createElement('button');
      b.textContent = btn.label;
      b.className   = btn.primary ? 'etq-btn-primary' : 'etq-btn-secondary';
      if (btn.danger) b.style.background = 'var(--crit-600,#DC2626)';
      b.onclick = async () => {
        modal.style.display = 'none';
        if (btn.action) await btn.action();
        resolve();
      };
      btnsEl.appendChild(b);
    }
    modal.style.display = 'flex';
  });
}

// ── Utilities ─────────────────────────────────────────────────────────────────

function _temNutricional(t) {
  try {
    const layout = JSON.parse(t.LayoutJson ?? '{}');
    return (layout.elements ?? []).some(e => e.type === 'nutritional-table');
  } catch { return false; }
}

function _detectarPreDeploy(etiquetas) {
  // Lote pré-deploy: alguma etiqueta já foi processada (impressa/conferida/etc.)
  // mas não tem snapshot — foi gerada antes do novo sistema de modelos.
  const statusProcessado = new Set(['impressa', 'conferida', 'divergente', 'consumida']);
  return etiquetas.some(e => statusProcessado.has(e.Status) && !e.LayoutSnapshotJson);
}

function _atualizarBannerPreDeploy() {
  const banner = document.getElementById('etq-imp-predeploy-banner');
  if (!banner || !_payload) return;
  banner.hidden = !_detectarPreDeploy(_payload.Etiquetas ?? []);
}

function _esc(s) {
  const d = document.createElement('div');
  d.textContent = s;
  return d.innerHTML;
}

function _setStatus(text) {
  const el = document.getElementById('etq-imp-status-msg');
  if (el) el.textContent = text;
}

function _setStatusHtml(trustedHtml) {
  const el = document.getElementById('etq-imp-status-msg');
  if (el) el.innerHTML = trustedHtml;
}

function _showToastEtq(msg) {
  if (typeof showToast === 'function') showToast(msg);
  else console.log('[etq]', msg);
}

async function _apiFetch(url, signal) {
  const res = await fetch(url, { credentials: 'include', signal });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json();
}
