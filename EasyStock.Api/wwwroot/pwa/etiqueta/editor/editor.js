/**
 * EasyStok Label Editor — F5
 * Vanilla JS, Pointer Events, no deps beyond render.js + codes.js
 */
import { renderEtiqueta } from '/etiqueta/render.js';
import { renderCodes }   from '/etiqueta/codes.js';

// ── Constants ──────────────────────────────────────────────────────────────
const MM = 3.7795275591; // px per mm at 96dpi
const SNAP = 1;          // snap grid in mm
const MIN_MM = 4;        // minimum element size

const PRESETS = [
    { label: '80×40mm (padrão)', w: 80,  h: 40  },
    { label: '40×40mm (rolo 40mm)', w: 40, h: 40 },
    { label: '60×40mm',          w: 60,  h: 40  },
    { label: '100×50mm',         w: 100, h: 50  },
    { label: 'A4 grade 3×8',     w: 210, h: 297 },
    { label: 'Personalizado…',   w: null, h: null },
];

const ZOOM_LEVELS = [0.5, 0.75, 1, 1.5, 2];

const FIRST_RUN_KEY  = 'etiqueta_editor_intro_v1';
const FIRST_RUN_STEPS = [
    { title: 'Arraste elementos',        body: 'Da lateral esquerda para a etiqueta. Clique num elemento posicionado para editá-lo.' },
    { title: 'Use variáveis',            body: 'Em campos de texto, escreva { para inserir dados do produto, lote ou etiqueta.' },
    { title: 'Códigos e branding',       body: 'QR, código de barras e logo são elementos. Apague o que não quiser. O @easystok também pode sair.' },
    { title: 'Veja como vai imprimir',   body: 'O preview considera o tamanho real em mm e impressão preto-e-branco.' },
];

const VAR_COMPLETIONS = [
    { v: '{produto.nome}',                   d: 'Nome do produto' },
    { v: '{produto.marca}',                  d: 'Marca' },
    { v: '{produto.ficha.kcal}',             d: 'Calorias (kcal)' },
    { v: '{produto.ficha.carbs_g}',          d: 'Carboidratos (g)' },
    { v: '{produto.ficha.proteina_g}',       d: 'Proteínas (g)' },
    { v: '{produto.ficha.gordura_g}',        d: 'Gorduras totais (g)' },
    { v: '{produto.ficha.gordura_saturada_g}', d: 'Gorduras sat. (g)' },
    { v: '{produto.ficha.fibras_g}',         d: 'Fibras (g)' },
    { v: '{produto.ficha.sodio_mg}',         d: 'Sódio (mg)' },
    { v: '{produto.ficha.porcao_g}',         d: 'Porção (g)' },
    { v: '{etiqueta.codigo}',                d: 'Código da etiqueta' },
    { v: '{etiqueta.sequencial}',            d: 'Número sequencial' },
    { v: '{lote.codigo}',                    d: 'Código do lote' },
    { v: '{lote.validadeEm:dd/MM/yyyy}',     d: 'Validade (data)' },
    { v: '{lote.criadoEm:dd/MM HH:mm}',     d: 'Data de produção' },
    { v: '{empresa.nome}',                   d: 'Nome da empresa' },
];

const ELEM_TYPES = [
    { type: 'text',              label: 'Texto',             icon: '𝗧',  maxCount: Infinity },
    { type: 'image',             label: 'Logo / Imagem',     icon: '⊞',  maxCount: Infinity },
    { type: 'code',              label: 'Código (QR/barras)',icon: '▦',  maxCount: 2 },
    { type: 'nutritional-table', label: 'Tabela nutricional',icon: '≡',  maxCount: 1 },
    { type: 'preparo-text',      label: 'Modo de preparo',   icon: '♨',  maxCount: 1 },
    { type: 'alergenos-pills',   label: 'Alérgenos',         icon: '⚠',  maxCount: 1 },
    { type: 'divider',           label: 'Divisor',           icon: '—',  maxCount: Infinity },
];

const FIXTURE_DADOS = {
    produto: {
        Nome: 'Nome do Produto', Marca: 'Marca Exemplo',
        FichaAlergenos: ['gluten', 'lactose'],
        AtributosJson: JSON.stringify({
            nutricional: { porcao_g: 100, kcal: 250, carbs_g: 30, proteina_g: 12,
                gordura_g: 8, gordura_saturada_g: 3, fibras_g: 2, sodio_mg: 400 },
            modo_preparo: 'Aquecer em banho-maria por 5 minutos.',
        })
    },
    etiqueta: { Codigo: 'ETQ-001', Sequencial: 1 },
    lote:     { Codigo: 'LOT-260514-001', ValidadeEm: new Date().toISOString(), CriadoEm: new Date().toISOString() },
    empresa:  { Nome: 'EasyStok', LogoUrl: null },
};

// ── State ──────────────────────────────────────────────────────────────────
let _templateId   = null;
let _layout       = null;   // { v, size, elements[] }
let _selectedId   = null;
let _zoom         = 1;
let _unsaved      = false;
let _offline      = false;
let _dragSrc      = null;   // sidebar drag ghost
let _ptrState     = null;   // active pointer gesture
let _acVisible    = false;
let _acIndex      = -1;
let _firstRunStep = 0;
let _empresaId    = '';

// ── DOM refs (populated in init) ──────────────────────────────────────────
let _overlay, _canvas, _canvasWrap, _propsPanel, _liveRegion,
    _firstRunEl, _confirmEl, _acEl, _valOverlay;

// ── Public API ─────────────────────────────────────────────────────────────
export function etqEditorAbrir(templateId, layoutJson, empresaId) {
    _templateId = templateId;
    _empresaId  = empresaId ?? '';
    _layout     = _migrateLayout(JSON.parse(layoutJson ?? '{"v":1,"size":{"w_mm":80,"h_mm":40},"elements":[]}'));
    _selectedId = null;
    _unsaved    = false;
    _zoom       = 1;

    _overlay.style.display = '';
    _overlay.classList.add('open');
    _resetCanvas();
    _renderSidebar();
    _renderCanvas();
    _renderProps();
    _updateToolbar();
    _updateSidebarCounts();
    _setUnsaved(false);

    if (!localStorage.getItem(FIRST_RUN_KEY)) {
        _firstRunStep = 0;
        _showFirstRun();
    }
}
window.etqEditorAbrir = etqEditorAbrir;

// Called by imprimir.js "Editar" button — fetches latest layoutJson then opens editor
window.etqAbrirEditor = async function(templateId) {
    try {
        const empresaId = window._EMPRESA_ID ?? '';
        const res = await fetch(`/api/etiquetas/templates/Empresa/${templateId}?empresaId=${empresaId}`, { credentials: 'include' });
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const { data } = await res.json();
        etqEditorAbrir(templateId, data.LayoutJson, empresaId);
    } catch(e) {
        alert('Erro ao abrir editor: ' + e.message);
    }
};

export function etqEditorFechar() {
    if (_unsaved) {
        _confirm({ title: 'Descartar alterações?', body: 'Você tem alterações não salvas.',
            btns: [
                { label: 'Descartar', cls: 'crit', action: () => { _unsaved = false; _doClose(); }},
                { label: 'Continuar editando', cls: 'sec', action: null },
            ]
        });
        return;
    }
    _doClose();
}
window.etqEditorFechar = etqEditorFechar;

function _doClose() { _overlay.classList.remove('open'); }

// ── Initialization ─────────────────────────────────────────────────────────
export function initEditor() {
    _overlay    = document.getElementById('etq-editor-overlay');
    if (!_overlay) return;

    _canvas     = _overlay.querySelector('.etq-ed-canvas');
    _canvasWrap = _overlay.querySelector('.etq-ed-canvas-wrap');
    _propsPanel = _overlay.querySelector('.etq-ed-props');
    _liveRegion = document.getElementById('etq-ed-live');
    _firstRunEl = _overlay.querySelector('.etq-ed-firstrun');
    _confirmEl  = _overlay.querySelector('.etq-ed-confirm');
    _acEl       = document.getElementById('etq-ed-autocomplete');
    _valOverlay = _overlay.querySelector('.etq-ed-val-overlay');

    _buildToolbar();
    _buildSidebar();
    _bindCanvas();
    _bindKeyboard();
    _bindOffline();
    _bindFirstRun();
    _bindConfirm();
}
window.initEditor = initEditor;

// ── Layout migration ───────────────────────────────────────────────────────
function _migrateLayout(l) {
    if (!l.v) l.v = 1;
    if (!l.size) l.size = { w_mm: 80, h_mm: 40 };
    if (!l.elements) l.elements = [];
    return l;
}

// ── Canvas setup ───────────────────────────────────────────────────────────
function _resetCanvas() {
    const w = (_layout.size.w_mm ?? 80) * MM * _zoom;
    const h = (_layout.size.h_mm ?? 40) * MM * _zoom;
    _canvas.style.width  = w + 'px';
    _canvas.style.height = h + 'px';

    // safe area inset 2mm
    const s = 2 * MM * _zoom;
    let safe = _canvas.querySelector('.etq-ed-safe-area');
    if (!safe) { safe = document.createElement('div'); safe.className = 'etq-ed-safe-area'; _canvas.appendChild(safe); }
    safe.style.cssText = `top:${s}px;left:${s}px;right:${s}px;bottom:${s}px;`;

    let grid = _canvas.querySelector('.etq-ed-grid');
    if (!grid) { grid = document.createElement('div'); grid.className = 'etq-ed-grid'; _canvas.appendChild(grid); }
    grid.style.backgroundSize = `${4 * _zoom}px ${4 * _zoom}px`;
}

// ── Canvas render ──────────────────────────────────────────────────────────
function _renderCanvas() {
    // Remove existing element nodes (keep overlays)
    _canvas.querySelectorAll('.etq-ed-el').forEach(n => n.remove());
    for (const el of _layout.elements) _appendElement(el);
    _updateValidation();
}

function _appendElement(el) {
    const node = document.createElement('div');
    node.className = 'etq-ed-el';
    node.dataset.id = el.id;
    node.dataset.type = el.type;
    _positionNode(node, el);
    if (el.locked) { node.classList.add('locked'); node.innerHTML = '<span class="lock-icon">🔒</span>'; }

    // Visual content
    const inner = document.createElement('div');
    inner.style.cssText = 'position:absolute;inset:0;overflow:hidden;pointer-events:none;font-size:10px;color:#3A4350;';
    inner.textContent = _elemPreviewText(el);
    node.appendChild(inner);

    _canvas.appendChild(node);

    if (el.id === _selectedId) node.classList.add('selected');

    // Pointer events for move
    node.addEventListener('pointerdown', e => _onElPointerDown(e, el.id, 'move'));
}

function _positionNode(node, el) {
    node.style.left   = (el.x_mm * MM * _zoom) + 'px';
    node.style.top    = (el.y_mm * MM * _zoom) + 'px';
    node.style.width  = (el.w_mm * MM * _zoom) + 'px';
    node.style.height = (el.h_mm * MM * _zoom) + 'px';
}

function _elemPreviewText(el) {
    switch (el.type) {
        case 'text':              return el.content ?? '{texto}';
        case 'image':             return el.asset ?? 'Imagem';
        case 'code':              return el.format?.toUpperCase() ?? 'Código';
        case 'nutritional-table': return 'Tabela nutricional';
        case 'preparo-text':      return 'Modo de preparo';
        case 'alergenos-pills':   return 'Alérgenos';
        case 'divider':           return '—————';
        default:                  return el.type;
    }
}

function _updateNodePosition(id) {
    const el   = _findEl(id);
    const node = _canvas.querySelector(`[data-id="${id}"]`);
    if (el && node) _positionNode(node, el);
}

// ── Sidebar ────────────────────────────────────────────────────────────────
function _buildSidebar() {
    const sb = _overlay.querySelector('.etq-ed-sidebar');
    sb.innerHTML = '<div class="etq-ed-sidebar-title">Elementos</div>';

    for (const def of ELEM_TYPES) {
        const chip = document.createElement('div');
        chip.className = 'etq-ed-elem-chip';
        chip.dataset.type = def.type;
        chip.draggable = true;
        chip.innerHTML = `<span class="chip-icon">${def.icon}</span><span class="chip-label">${def.label}</span>`;
        chip.title = def.maxCount === 1 ? 'Apenas 1 por modelo.' : def.maxCount === 2 ? 'Máximo 2 por modelo.' : '';

        chip.addEventListener('dragstart', e => {
            _dragSrc = def.type;
            e.dataTransfer.effectAllowed = 'copy';
            e.dataTransfer.setData('text/plain', def.type);
        });
        chip.addEventListener('dragend', () => { _dragSrc = null; });

        // Touch-based drag (PWA on mobile via pointer events)
        chip.addEventListener('pointerdown', e => {
            if (e.pointerType === 'touch') _onSidebarTouchDrag(e, def.type);
        });

        sb.appendChild(chip);
    }
}

function _renderSidebar() { /* counts updated separately */ _updateSidebarCounts(); }

function _updateSidebarCounts() {
    const sb = _overlay.querySelector('.etq-ed-sidebar');
    for (const def of ELEM_TYPES) {
        const count = (_layout?.elements ?? []).filter(e => e.type === def.type).length;
        const chip  = sb.querySelector(`[data-type="${def.type}"]`);
        if (!chip) continue;
        const disabled = count >= def.maxCount;
        chip.classList.toggle('disabled', disabled);
        if (disabled) {
            const tip = def.maxCount === 1
                ? 'Já existe um elemento desse tipo. Cada modelo aceita 1 só.'
                : `Máximo ${def.maxCount} códigos por modelo.`;
            chip.title = tip;
        }
    }
}

// ── Canvas drop target ─────────────────────────────────────────────────────
function _bindCanvas() {
    _canvas.addEventListener('dragover', e => { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; });
    _canvas.addEventListener('drop', e => {
        e.preventDefault();
        const type = e.dataTransfer.getData('text/plain') || _dragSrc;
        if (!type) return;
        const rect = _canvas.getBoundingClientRect();
        const x_mm = Math.max(0, _snapMm((e.clientX - rect.left) / _zoom / MM));
        const y_mm = Math.max(0, _snapMm((e.clientY - rect.top)  / _zoom / MM));
        _addElement(type, x_mm, y_mm);
    });

    _canvas.addEventListener('click', e => {
        if (e.target === _canvas || e.target.classList.contains('etq-ed-grid') ||
            e.target.classList.contains('etq-ed-safe-area')) {
            _select(null);
        }
    });
}

// ── Touch drag from sidebar (pointer events) ───────────────────────────────
function _onSidebarTouchDrag(e, type) {
    e.preventDefault();
    const pointerId = e.pointerId;
    const chip      = e.currentTarget;
    chip.setPointerCapture(pointerId);

    let ghost = null;

    const onMove = ev => {
        if (ev.pointerId !== pointerId) return;
        if (!ghost) {
            ghost = document.createElement('div');
            ghost.style.cssText = 'position:fixed;pointer-events:none;z-index:99999;padding:6px 12px;background:#1A2E5A;color:#fff;border-radius:6px;font-size:12px;';
            ghost.textContent = type;
            document.body.appendChild(ghost);
        }
        ghost.style.left = (ev.clientX + 10) + 'px';
        ghost.style.top  = (ev.clientY - 16) + 'px';
    };

    const onUp = ev => {
        if (ev.pointerId !== pointerId) return;
        chip.removeEventListener('pointermove', onMove);
        chip.removeEventListener('pointerup', onUp);
        ghost?.remove();

        const rect = _canvas.getBoundingClientRect();
        if (ev.clientX >= rect.left && ev.clientX <= rect.right &&
            ev.clientY >= rect.top  && ev.clientY <= rect.bottom) {
            const x_mm = _snapMm((ev.clientX - rect.left) / _zoom / MM);
            const y_mm = _snapMm((ev.clientY - rect.top)  / _zoom / MM);
            _addElement(type, x_mm, y_mm);
        }
    };

    chip.addEventListener('pointermove', onMove);
    chip.addEventListener('pointerup', onUp);
}

// ── Element add ────────────────────────────────────────────────────────────
function _addElement(type, x_mm, y_mm) {
    const def   = ELEM_TYPES.find(d => d.type === type);
    if (!def) return;
    const count = _layout.elements.filter(e => e.type === type).length;
    if (count >= def.maxCount) return;

    const id  = type + '-' + Date.now();
    const w   = _layout.size.w_mm;
    const h   = _layout.size.h_mm;
    const el  = _defaultElement(type, id, Math.min(x_mm, w - 10), Math.min(y_mm, h - 6));
    _layout.elements.push(el);
    _appendElement(el);
    _select(el.id);
    _updateSidebarCounts();
    _setUnsaved(true);
    _updateValidation();
    _announce(`${def.label} adicionado.`);
}

function _defaultElement(type, id, x, y) {
    const base = { id, type, x_mm: x, y_mm: y, locked: false };
    switch (type) {
        case 'text':              return { ...base, content: 'Texto', w_mm: 30, h_mm: 6, font: 'sans', size_pt: 10, weight: 400, align: 'left', overflow: 'shrink-then-ellipsis' };
        case 'image':             return { ...base, asset: 'system:logo-easystok', w_mm: 12, h_mm: 6 };
        case 'code':              return { ...base, format: 'qr', content: '{etiqueta.codigo}', w_mm: 20, h_mm: 20, quiet_zone_mm: 1 };
        case 'nutritional-table': return { ...base, w_mm: 50, h_mm: 16, size_pt_min: 6, size_pt_max: 8 };
        case 'preparo-text':      return { ...base, w_mm: 50, h_mm: 8 };
        case 'alergenos-pills':   return { ...base, w_mm: 50, h_mm: 6 };
        case 'divider':           return { ...base, w_mm: 60, h_mm: 2, stroke_pt: 0.5 };
        default:                  return { ...base, w_mm: 20, h_mm: 6 };
    }
}

// ── Selection ──────────────────────────────────────────────────────────────
function _select(id) {
    _selectedId = id;
    _canvas.querySelectorAll('.etq-ed-el').forEach(n => {
        n.classList.toggle('selected', n.dataset.id === id);
    });
    _removeHandles();
    if (id) {
        const node = _canvas.querySelector(`[data-id="${id}"]`);
        if (node) _addHandles(node, id);
    }
    _renderProps();
}

// ── Resize handles ─────────────────────────────────────────────────────────
function _addHandles(node, id) {
    const el = _findEl(id);
    if (!el) return;
    for (const dir of ['nw','n','ne','w','e','sw','s','se']) {
        const h = document.createElement('div');
        h.className = 'etq-ed-handle';
        h.dataset.dir = dir;
        h.addEventListener('pointerdown', e => _onElPointerDown(e, id, 'resize', dir));
        node.appendChild(h);
    }
}

function _removeHandles() {
    _canvas.querySelectorAll('.etq-ed-handle').forEach(h => h.remove());
}

// ── Pointer Events (move + resize) ────────────────────────────────────────
function _onElPointerDown(e, id, mode, dir) {
    e.stopPropagation();
    const el = _findEl(id);
    if (!el) return;

    if (mode === 'move' && el.locked) {
        _announce('Elemento bloqueado. Destrave em Propriedades.');
        return;
    }

    _select(id);
    e.currentTarget.setPointerCapture(e.pointerId);

    const startX  = e.clientX;
    const startY  = e.clientY;
    const origEl  = { ...el };

    _ptrState = { id, mode, dir, startX, startY, origEl };

    const onMove = ev => {
        if (!_ptrState) return;
        const dx = (ev.clientX - startX) / _zoom / MM;
        const dy = (ev.clientY - startY) / _zoom / MM;
        _applyGesture(el, origEl, dx, dy, mode, dir);
        _updateNodePosition(id);
        _setUnsaved(true);
        _updateValidation();
    };

    const onUp = ev => {
        ev.currentTarget.removeEventListener('pointermove', onMove);
        ev.currentTarget.removeEventListener('pointerup', onUp);
        _ptrState = null;
        _removeHandles();
        const node = _canvas.querySelector(`[data-id="${id}"]`);
        if (node) _addHandles(node, id);
        _renderProps();
        _announce(`Elemento em ${el.x_mm.toFixed(1)}mm, ${el.y_mm.toFixed(1)}mm`);
    };

    e.currentTarget.addEventListener('pointermove', onMove);
    e.currentTarget.addEventListener('pointerup', onUp);
}

function _applyGesture(el, orig, dx, dy, mode, dir) {
    const w = _layout.size.w_mm;
    const h = _layout.size.h_mm;

    if (mode === 'move') {
        el.x_mm = _snapMm(Math.max(0, Math.min(orig.x_mm + dx, w - orig.w_mm)));
        el.y_mm = _snapMm(Math.max(0, Math.min(orig.y_mm + dy, h - orig.h_mm)));
        return;
    }

    // resize
    let x = orig.x_mm, y = orig.y_mm, rw = orig.w_mm, rh = orig.h_mm;
    if (dir.includes('e')) rw = Math.max(MIN_MM, _snapMm(orig.w_mm + dx));
    if (dir.includes('s')) rh = Math.max(MIN_MM, _snapMm(orig.h_mm + dy));
    if (dir.includes('w')) { const d = _snapMm(dx); x = Math.max(0, orig.x_mm + d); rw = Math.max(MIN_MM, orig.w_mm - d); }
    if (dir.includes('n')) { const d = _snapMm(dy); y = Math.max(0, orig.y_mm + d); rh = Math.max(MIN_MM, orig.h_mm - d); }
    el.x_mm = x; el.y_mm = y; el.w_mm = rw; el.h_mm = rh;
}

function _snapMm(v) { return Math.round(v / SNAP) * SNAP; }

// ── Validation ─────────────────────────────────────────────────────────────
function _updateValidation() {
    if (!_layout) return;
    const msgs    = [];
    const w       = _layout.size.w_mm;
    const h       = _layout.size.h_mm;

    _canvas.querySelectorAll('.etq-ed-el').forEach(node => {
        node.classList.remove('has-error', 'has-warn');
    });

    for (const el of _layout.elements) {
        const node   = _canvas.querySelector(`[data-id="${el.id}"]`);
        const errors = [];
        const warns  = [];

        if (el.x_mm + el.w_mm > w + 0.1) errors.push(`${el.id}: ultrapassa a largura.`);
        if (el.y_mm + el.h_mm > h + 0.1) errors.push(`${el.id}: ultrapassa a altura.`);

        if (el.type === 'code' && el.format === 'qr' && (el.w_mm < 18 || el.h_mm < 18))
            errors.push(`QR mínimo 18×18mm.`);
        if (el.type === 'code' && el.format?.startsWith('barcode') && (el.w_mm < 30 || el.h_mm < 10))
            errors.push(`Código de barras mínimo 30×10mm.`);
        if (el.type === 'text' && el.size_pt && el.size_pt < 7 && el.y_mm < h - 5)
            warns.push(`Texto menor que 7pt pode não imprimir em térmica 203dpi.`);

        if (errors.length && node) node.classList.add('has-error');
        else if (warns.length && node) node.classList.add('has-warn');

        msgs.push(...errors.map(m => ({ m, cls: 'error' })), ...warns.map(m => ({ m, cls: 'warn' })));
    }

    // overlap check
    for (let i = 0; i < _layout.elements.length; i++) {
        for (let j = i + 1; j < _layout.elements.length; j++) {
            const a = _layout.elements[i], b = _layout.elements[j];
            if (_overlaps(a, b)) {
                const na = _canvas.querySelector(`[data-id="${a.id}"]`);
                const nb = _canvas.querySelector(`[data-id="${b.id}"]`);
                if (na && !na.classList.contains('has-error')) na.classList.add('has-warn');
                if (nb && !nb.classList.contains('has-error')) nb.classList.add('has-warn');
                msgs.push({ m: `Sobreposição entre '${a.id}' e '${b.id}'. Pode ser intencional.`, cls: 'warn' });
            }
        }
    }

    _valOverlay.innerHTML = msgs.slice(0, 5).map(({ m, cls }) =>
        `<div class="etq-ed-val-msg ${cls}">${m}</div>`).join('');
}

function _overlaps(a, b) {
    return a.x_mm < b.x_mm + b.w_mm && a.x_mm + a.w_mm > b.x_mm &&
           a.y_mm < b.y_mm + b.h_mm && a.y_mm + a.h_mm > b.y_mm;
}

// ── Properties panel ──────────────────────────────────────────────────────
function _renderProps() {
    const el = _selectedId ? _findEl(_selectedId) : null;
    if (!el) {
        _propsPanel.innerHTML = '<div class="etq-ed-props-empty">Selecione um elemento no layout para editar.</div>';
        return;
    }

    const form = document.createElement('div');
    form.className = 'etq-ed-props-form';

    // Locked
    if (el.locked) {
        const btn = document.createElement('button');
        btn.className = 'etq-ed-destravar-btn';
        btn.textContent = '🔒 Destravar elemento';
        btn.onclick = () => { el.locked = false; const n = _canvas.querySelector(`[data-id="${el.id}"]`); if(n) { n.classList.remove('locked'); n.querySelector('.lock-icon')?.remove(); } _renderProps(); _setUnsaved(true); };
        form.appendChild(btn);
    }

    // Position & size
    form.appendChild(_makeGroup('Posição', [
        _makeRow([
            _makeField('X (mm)', 'number', el.x_mm.toFixed(1), v => { el.x_mm = _snapMm(+v); _updateNodePosition(el.id); _updateValidation(); _setUnsaved(true); }),
            _makeField('Y (mm)', 'number', el.y_mm.toFixed(1), v => { el.y_mm = _snapMm(+v); _updateNodePosition(el.id); _updateValidation(); _setUnsaved(true); }),
        ]),
        _makeRow([
            _makeField('L (mm)', 'number', el.w_mm.toFixed(1), v => { el.w_mm = Math.max(MIN_MM, _snapMm(+v)); _updateNodePosition(el.id); _updateValidation(); _setUnsaved(true); }),
            _makeField('A (mm)', 'number', el.h_mm.toFixed(1), v => { el.h_mm = Math.max(MIN_MM, _snapMm(+v)); _updateNodePosition(el.id); _updateValidation(); _setUnsaved(true); }),
        ]),
    ]));

    // Type-specific fields
    switch (el.type) {
        case 'text':              _buildTextProps(form, el);    break;
        case 'image':             _buildImageProps(form, el);   break;
        case 'code':              _buildCodeProps(form, el);    break;
        case 'nutritional-table': _buildNutriProps(form, el);   break;
        case 'divider':           _buildDividerProps(form, el); break;
        default: break;
    }

    // Delete button
    const delBtn = document.createElement('button');
    delBtn.className = 'etq-ed-delete-btn';
    delBtn.textContent = 'Remover elemento';
    delBtn.onclick = () => _deleteSelected();
    form.appendChild(delBtn);

    _propsPanel.innerHTML = '';
    _propsPanel.appendChild(form);
}

function _buildTextProps(form, el) {
    const contentGrp = document.createElement('div');
    contentGrp.className = 'etq-ed-prop-group';
    const lbl = document.createElement('div'); lbl.className = 'etq-ed-prop-label'; lbl.textContent = 'Conteúdo';
    const ta  = document.createElement('textarea');
    ta.value = el.content ?? '';
    ta.placeholder = 'Escreva { para variáveis';
    ta.rows = 3;
    ta.oninput = () => {
        el.content = ta.value;
        _updateNodeContent(el.id, ta.value);
        _setUnsaved(true);
        _showAutoComplete(ta);
    };
    ta.onkeydown = e => _onAcKeyDown(e, ta);
    ta.onblur   = () => setTimeout(_hideAutoComplete, 150);
    const helper = document.createElement('div'); helper.className = 'etq-ed-prop-label'; helper.style.cssText = 'color:#6B7480;font-weight:400;'; helper.textContent = 'Comece com { para variáveis.';
    contentGrp.append(lbl, ta, helper);
    form.appendChild(contentGrp);

    form.appendChild(_makeGroup('Fonte', [
        _makeRow([
            _makeSelect('Família', [['sans','Inter (sans)'],['display','Fraunces (display)'],['mono','JetBrains Mono']], el.font ?? 'sans',
                v => { el.font = v; _setUnsaved(true); }),
            _makeField('Tamanho (pt)', 'number', el.size_pt ?? 10, v => { el.size_pt = +v; _validateFontWarn(form, el); _setUnsaved(true); }),
        ]),
        _makeRow([
            _makeSelect('Peso', [['400','Regular'],['600','Semibold'],['700','Bold']], String(el.weight ?? 400),
                v => { el.weight = +v; _setUnsaved(true); }),
            _makeSelect('Alinhamento', [['left','Esquerda'],['center','Centro'],['right','Direita']], el.align ?? 'left',
                v => { el.align = v; _setUnsaved(true); }),
        ]),
    ]));

    _validateFontWarn(form, el);
}

function _validateFontWarn(form, el) {
    form.querySelectorAll('.font-warn').forEach(n => n.remove());
    if ((el.size_pt ?? 10) < 7 && el.y_mm < (_layout.size.h_mm - 5)) {
        const w = document.createElement('div'); w.className = 'etq-ed-prop-warn font-warn';
        w.textContent = 'Texto menor que 7pt pode não imprimir em térmica 203dpi.';
        form.appendChild(w);
    }
}

function _buildImageProps(form, el) {
    form.appendChild(_makeGroup('Imagem', [
        _makeSelect('Imagem', [
            ['system:lockup-easystok','Lockup EasyStok (logo + nome)'],
            ['system:logo-easystok','Ícone EasyStok (só logo)'],
            ['loja:logo','Logo da minha empresa'],
        ], el.asset ?? 'system:logo-easystok', v => { el.asset = v; _setUnsaved(true); _updateNodeContent(el.id, v); }),
    ]));
}

function _buildCodeProps(form, el) {
    form.appendChild(_makeGroup('Código', [
        _makeSelect('Formato', [['qr','QR Code'],['barcode-code128','Code 128'],['barcode-ean13','EAN-13']], el.format ?? 'qr',
            v => { el.format = v; _setUnsaved(true); _updateValidation(); }),
    ]));
    const contentGrp = document.createElement('div');
    contentGrp.className = 'etq-ed-prop-group';
    const lbl = document.createElement('div'); lbl.className = 'etq-ed-prop-label'; lbl.textContent = 'Conteúdo';
    const ta  = document.createElement('textarea'); ta.rows = 2;
    ta.value = el.content ?? '{etiqueta.codigo}';
    ta.oninput = () => { el.content = ta.value; _setUnsaved(true); _showAutoComplete(ta); };
    ta.onkeydown = e => _onAcKeyDown(e, ta);
    ta.onblur   = () => setTimeout(_hideAutoComplete, 150);
    contentGrp.append(lbl, ta);
    form.appendChild(contentGrp);
}

function _buildNutriProps(form, el) {
    form.appendChild(_makeGroup('Tamanho da fonte', [
        _makeRow([
            _makeField('Mín (pt)', 'number', el.size_pt_min ?? 6, v => { el.size_pt_min = +v; _setUnsaved(true); }),
            _makeField('Máx (pt)', 'number', el.size_pt_max ?? 8, v => { el.size_pt_max = +v; _setUnsaved(true); }),
        ]),
    ]));
}

function _buildDividerProps(form, el) {
    form.appendChild(_makeGroup('Espessura', [
        _makeField('Stroke (pt)', 'number', el.stroke_pt ?? 0.5, v => { el.stroke_pt = +v; _setUnsaved(true); }),
    ]));
}

function _updateNodeContent(id, text) {
    const node = _canvas.querySelector(`[data-id="${id}"] div`);
    if (node) node.textContent = text;
}

// ── Props helpers ─────────────────────────────────────────────────────────
function _makeGroup(label, children) {
    const g = document.createElement('div'); g.className = 'etq-ed-prop-group';
    const l = document.createElement('div'); l.className = 'etq-ed-prop-label'; l.textContent = label;
    g.appendChild(l);
    children.forEach(c => g.appendChild(c));
    return g;
}

function _makeRow(children) {
    const r = document.createElement('div'); r.className = 'etq-ed-prop-row';
    children.forEach(c => r.appendChild(c));
    return r;
}

function _makeField(label, type, value, onChange) {
    const wrap = document.createElement('div'); wrap.style.flex = '1';
    const lbl  = document.createElement('div'); lbl.className = 'etq-ed-prop-label'; lbl.textContent = label;
    const inp  = document.createElement('input'); inp.type = type; inp.value = value;
    if (type === 'number') { inp.step = '0.5'; inp.min = '0'; }
    inp.oninput = () => onChange(inp.value);
    wrap.append(lbl, inp);
    return wrap;
}

function _makeSelect(label, options, value, onChange) {
    const wrap = document.createElement('div'); wrap.style.flex = '1';
    const lbl  = document.createElement('div'); lbl.className = 'etq-ed-prop-label'; lbl.textContent = label;
    const sel  = document.createElement('select');
    options.forEach(([v, t]) => { const o = document.createElement('option'); o.value = v; o.textContent = t; if (v === value) o.selected = true; sel.appendChild(o); });
    sel.onchange = () => onChange(sel.value);
    wrap.append(lbl, sel);
    return wrap;
}

// ── Toolbar ────────────────────────────────────────────────────────────────
function _buildToolbar() {
    const tb = _overlay.querySelector('.etq-ed-toolbar');
    tb.innerHTML = '';

    // Close btn
    const closeBtn = document.createElement('button');
    closeBtn.className = 'etq-ed-tb-btn'; closeBtn.textContent = '← Sair';
    closeBtn.onclick = etqEditorFechar;
    tb.appendChild(closeBtn);

    tb.appendChild(_makeSep());

    // Name field
    const nameInp = document.createElement('input');
    nameInp.type = 'text'; nameInp.className = 'etq-ed-name-field etq-ed-tb-name'; nameInp.placeholder = 'Nome do modelo (obrigatório)';
    nameInp.oninput = () => { _layout._nome = nameInp.value; _setUnsaved(true); };
    tb.appendChild(nameInp);

    const unsavedSpan = document.createElement('span');
    unsavedSpan.className = 'etq-ed-unsaved'; unsavedSpan.textContent = 'Alterações não salvas';
    tb.appendChild(unsavedSpan);

    tb.appendChild(_makeSep());

    // Preset selector
    const presetSel = document.createElement('select');
    presetSel.className = 'etq-ed-tb-sel';
    PRESETS.forEach((p, i) => { const o = document.createElement('option'); o.value = i; o.textContent = p.label; presetSel.appendChild(o); });
    presetSel.onchange = () => {
        const p = PRESETS[+presetSel.value];
        if (p.w) { _layout.size.w_mm = p.w; _layout.size.h_mm = p.h; _resetCanvas(); _renderCanvas(); _setUnsaved(true); }
        else {
            const w = parseFloat(prompt('Largura (mm):', '80') || '80');
            const h = parseFloat(prompt('Altura (mm):',  '40') || '40');
            if (w > 0 && h > 0) { _layout.size.w_mm = w; _layout.size.h_mm = h; _resetCanvas(); _renderCanvas(); _setUnsaved(true); }
        }
    };
    tb.appendChild(presetSel);

    // Orientation
    const orientBtn = document.createElement('button');
    orientBtn.className = 'etq-ed-tb-btn etq-ed-tb-orient'; orientBtn.textContent = '↔ Horizontal';
    orientBtn.onclick = () => {
        const s = _layout.size;
        [s.w_mm, s.h_mm] = [s.h_mm, s.w_mm];
        s.orientation = s.w_mm >= s.h_mm ? 'horizontal' : 'vertical';
        orientBtn.textContent = s.orientation === 'horizontal' ? '↔ Horizontal' : '↕ Vertical';
        _resetCanvas(); _renderCanvas(); _setUnsaved(true);
    };
    tb.appendChild(orientBtn);

    tb.appendChild(_makeSep());

    // Zoom
    const zoomSel = document.createElement('select');
    zoomSel.className = 'etq-ed-tb-sel';
    ZOOM_LEVELS.forEach(z => { const o = document.createElement('option'); o.value = z; o.textContent = Math.round(z * 100) + '%'; zoomSel.appendChild(o); });
    const fitOpt = document.createElement('option'); fitOpt.value = 'fit'; fitOpt.textContent = 'Ajustar'; zoomSel.appendChild(fitOpt);
    zoomSel.value = '1';
    zoomSel.onchange = () => {
        if (zoomSel.value === 'fit') {
            const area = _overlay.querySelector('.etq-ed-canvas-area');
            const maxW = area.clientWidth  - 48;
            const maxH = area.clientHeight - 48;
            _zoom = Math.min(maxW / (_layout.size.w_mm * MM), maxH / (_layout.size.h_mm * MM), 2);
        } else {
            _zoom = +zoomSel.value;
        }
        _resetCanvas(); _renderCanvas(); _removeHandles();
        if (_selectedId) { const n = _canvas.querySelector(`[data-id="${_selectedId}"]`); if(n) _addHandles(n, _selectedId); }
    };
    tb.appendChild(zoomSel);

    tb.appendChild(_makeSep());

    // Preview btn
    const prevBtn = document.createElement('button');
    prevBtn.className = 'etq-ed-tb-btn'; prevBtn.textContent = 'Visualizar prévia';
    prevBtn.onclick = _openPreview;
    tb.appendChild(prevBtn);

    // Save btn
    const saveBtn = document.createElement('button');
    saveBtn.className = 'etq-ed-tb-btn primary etq-ed-tb-save'; saveBtn.textContent = 'Salvar modelo';
    saveBtn.onclick = _save;
    tb.appendChild(saveBtn);

    // Offline banner
    const offlineBanner = document.createElement('span');
    offlineBanner.className = 'etq-ed-offline-banner etq-ed-offline-ind'; offlineBanner.textContent = 'Sem conexão. Salve quando voltar.';
    tb.appendChild(offlineBanner);
}

function _makeSep() { const s = document.createElement('div'); s.className = 'etq-ed-toolbar-sep'; return s; }

function _updateToolbar() {
    const nameInp = _overlay.querySelector('.etq-ed-tb-name');
    if (nameInp) nameInp.value = _layout?._nome ?? '';
}

function _setUnsaved(v) {
    _unsaved = v;
    const span = _overlay?.querySelector('.etq-ed-unsaved');
    if (span) span.classList.toggle('show', v);
}

// ── Save ───────────────────────────────────────────────────────────────────
async function _save() {
    const nome = (_overlay.querySelector('.etq-ed-tb-name')?.value ?? '').trim();
    if (!nome) { alert('O nome do modelo é obrigatório.'); return; }

    const errors = _getValidationErrors();
    if (errors.length) { alert(`Há ${errors.length} elemento(s) com problemas:\n${errors.join('\n')}`); return; }

    if (_offline) { alert('Sem conexão. Salve quando voltar.'); return; }

    const layoutJson = JSON.stringify({ v: _layout.v, size: _layout.size, elements: _layout.elements });
    try {
        const res = await fetch(`/api/etiquetas/templates/${_templateId}?empresaId=${_empresaId}`, {
            method: 'PUT',
            credentials: 'include',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ nome, layoutJson }),
        });
        if (res.status === 409) { alert('Outro usuário modificou este modelo. Recarregue a página e tente novamente.'); return; }
        if (!res.ok) throw new Error('HTTP ' + res.status);
        _setUnsaved(false);
        _announce('Modelo salvo.');
        _toast('Modelo salvo.');
    } catch (e) {
        alert('Erro ao salvar: ' + e.message);
    }
}

function _getValidationErrors() {
    const msgs = [];
    const w = _layout.size.w_mm, h = _layout.size.h_mm;
    for (const el of _layout.elements) {
        if (el.x_mm + el.w_mm > w + 0.1) msgs.push(`'${el.id}': ultrapassa a largura.`);
        if (el.y_mm + el.h_mm > h + 0.1) msgs.push(`'${el.id}': ultrapassa a altura.`);
        if (el.type === 'code' && el.format === 'qr' && (el.w_mm < 18 || el.h_mm < 18))
            msgs.push(`QR mínimo 18×18mm.`);
    }
    return msgs;
}

// ── Preview window ─────────────────────────────────────────────────────────
async function _openPreview() {
    const el     = renderEtiqueta(_layout, FIXTURE_DADOS, { forPrint: false });
    const wPx    = _layout.size.w_mm * MM;
    const hPx    = _layout.size.h_mm * MM;
    const win    = window.open('', '_blank', `width=${wPx+40},height=${hPx+80}`);
    win.document.write(`<!DOCTYPE html><html><head><meta charset="utf-8"><title>Prévia</title>
<link rel="stylesheet" href="/etiqueta/etiqueta.css">
<style>body{display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0;background:#ECEFF5}</style>
</head><body>${el.outerHTML}
<script src="/etiqueta/vendor/qrcode.min.js"><\/script>
<script src="/etiqueta/vendor/jsbarcode.min.js"><\/script>
<script type="module">import{renderCodes}from'/etiqueta/codes.js';renderCodes(document.body);<\/script>
</body></html>`);
    win.document.close();
}

// ── Variable autocomplete ──────────────────────────────────────────────────
function _showAutoComplete(ta) {
    const val   = ta.value;
    const caret = ta.selectionStart;
    const before = val.slice(0, caret);
    const match  = before.match(/\{([^}]*)$/);

    if (!match) { _hideAutoComplete(); return; }

    const query   = match[1].toLowerCase();
    const results = VAR_COMPLETIONS.filter(c => c.v.includes(query) || c.d.toLowerCase().includes(query));
    if (!results.length) { _hideAutoComplete(); return; }

    _acEl.innerHTML = results.map((c, i) =>
        `<div class="etq-ed-ac-item" data-idx="${i}"><code>${c.v}</code><div class="ac-desc">${c.d}</div></div>`
    ).join('');

    _acEl.querySelectorAll('.etq-ed-ac-item').forEach(item => {
        item.onmousedown = e => { e.preventDefault(); _applyCompletion(ta, match, results[+item.dataset.idx].v); };
    });

    const rect  = ta.getBoundingClientRect();
    _acEl.style.cssText = `display:block;top:${rect.bottom + window.scrollY + 2}px;left:${rect.left + window.scrollX}px;`;
    _acVisible = true;
    _acIndex   = -1;
}

function _hideAutoComplete() { if (_acEl) _acEl.style.display = 'none'; _acVisible = false; _acIndex = -1; }

function _onAcKeyDown(e, ta) {
    if (!_acVisible) return;
    const items = _acEl.querySelectorAll('.etq-ed-ac-item');
    if (e.key === 'ArrowDown') { e.preventDefault(); _acIndex = Math.min(_acIndex + 1, items.length - 1); _highlightAc(items); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); _acIndex = Math.max(_acIndex - 1, 0); _highlightAc(items); }
    else if (e.key === 'Enter' || e.key === 'Tab') {
        if (_acIndex >= 0) {
            e.preventDefault();
            const val   = ta.value;
            const caret = ta.selectionStart;
            const before = val.slice(0, caret);
            const match  = before.match(/\{([^}]*)$/);
            if (match) _applyCompletion(ta, match, VAR_COMPLETIONS.filter(c => c.v.includes(match[1].toLowerCase()))[_acIndex]?.v);
        }
    } else if (e.key === 'Escape') { _hideAutoComplete(); }
}

function _highlightAc(items) { items.forEach((it, i) => it.classList.toggle('active', i === _acIndex)); }

function _applyCompletion(ta, match, variable) {
    if (!variable) return;
    const caret   = ta.selectionStart;
    const before  = ta.value.slice(0, caret);
    const after   = ta.value.slice(caret);
    const prefix  = before.slice(0, before.length - match[0].length);
    ta.value = prefix + variable + after;
    const pos = (prefix + variable).length;
    ta.setSelectionRange(pos, pos);
    ta.dispatchEvent(new Event('input'));
    _hideAutoComplete();
}

// ── Keyboard shortcuts ─────────────────────────────────────────────────────
function _bindKeyboard() {
    document.addEventListener('keydown', e => {
        if (!_overlay?.classList.contains('open')) return;
        const tag = document.activeElement?.tagName;
        const inInput = tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';

        if ((e.ctrlKey || e.metaKey) && e.key === 's') { e.preventDefault(); _save(); return; }
        if (e.key === 'Escape') { if (_acVisible) { _hideAutoComplete(); } else { etqEditorFechar(); } return; }

        if (inInput) return;

        if (e.key === 'Delete' || e.key === 'Backspace') { e.preventDefault(); _deleteSelected(); return; }

        if (_selectedId) {
            const el   = _findEl(_selectedId);
            const step = e.shiftKey ? 0.5 : 1;
            if (el) {
                let moved = true;
                if (e.key === 'ArrowLeft')  el.x_mm = Math.max(0, el.x_mm - step);
                else if (e.key === 'ArrowRight') el.x_mm = Math.min(_layout.size.w_mm - el.w_mm, el.x_mm + step);
                else if (e.key === 'ArrowUp')   el.y_mm = Math.max(0, el.y_mm - step);
                else if (e.key === 'ArrowDown') el.y_mm = Math.min(_layout.size.h_mm - el.h_mm, el.y_mm + step);
                else moved = false;
                if (moved) {
                    e.preventDefault();
                    _updateNodePosition(_selectedId);
                    _setUnsaved(true);
                    _renderProps();
                    _announce(`${el.x_mm.toFixed(1)}mm, ${el.y_mm.toFixed(1)}mm`);
                }
            }
        }
    });
}

function _deleteSelected() {
    if (!_selectedId) return;
    const idx = _layout.elements.findIndex(e => e.id === _selectedId);
    if (idx === -1) return;
    const type = _layout.elements[idx].type;
    _layout.elements.splice(idx, 1);
    _canvas.querySelector(`[data-id="${_selectedId}"]`)?.remove();
    _announce('Elemento removido.');
    _selectedId = null;
    _removeHandles();
    _renderProps();
    _updateSidebarCounts();
    _setUnsaved(true);
    _updateValidation();
}

// ── Offline detection ─────────────────────────────────────────────────────
function _bindOffline() {
    window.addEventListener('offline', () => _setOffline(true));
    window.addEventListener('online',  () => _setOffline(false));
}

function _setOffline(v) {
    _offline = v;
    const banner = _overlay?.querySelector('.etq-ed-offline-ind');
    if (banner) banner.classList.toggle('show', v);
}

// ── First-run overlay ─────────────────────────────────────────────────────
function _bindFirstRun() {
    if (!_firstRunEl) return;
    const nextBtn = _firstRunEl.querySelector('.etq-ed-firstrun-next');
    const skipBtn = _firstRunEl.querySelector('.etq-ed-firstrun-skip');
    if (nextBtn) nextBtn.onclick = () => {
        _firstRunStep++;
        if (_firstRunStep >= FIRST_RUN_STEPS.length) { _hideFirstRun(); } else { _renderFirstRun(); }
    };
    if (skipBtn) skipBtn.onclick = _hideFirstRun;
}

function _showFirstRun() {
    if (!_firstRunEl) return;
    _renderFirstRun();
    _firstRunEl.style.display = 'flex';
}

function _renderFirstRun() {
    const step = FIRST_RUN_STEPS[_firstRunStep];
    const stepEl  = _firstRunEl.querySelector('.etq-ed-firstrun-step');
    const titleEl = _firstRunEl.querySelector('.etq-ed-firstrun-title');
    const bodyEl  = _firstRunEl.querySelector('.etq-ed-firstrun-body');
    const dotsEl  = _firstRunEl.querySelector('.etq-ed-firstrun-dots');
    const nextBtn = _firstRunEl.querySelector('.etq-ed-firstrun-next');

    if (stepEl)  stepEl.textContent  = `${_firstRunStep + 1} de ${FIRST_RUN_STEPS.length}`;
    if (titleEl) titleEl.textContent = step.title;
    if (bodyEl)  bodyEl.textContent  = step.body;
    if (dotsEl)  dotsEl.innerHTML = FIRST_RUN_STEPS.map((_, i) =>
        `<div class="etq-ed-firstrun-dot${i === _firstRunStep ? ' active' : ''}"></div>`).join('');
    if (nextBtn) nextBtn.textContent = _firstRunStep < FIRST_RUN_STEPS.length - 1 ? 'Próximo →' : 'Entendi, vamos lá';
}

function _hideFirstRun() {
    localStorage.setItem(FIRST_RUN_KEY, '1');
    if (_firstRunEl) _firstRunEl.style.display = 'none';
}

// ── Confirm dialog ─────────────────────────────────────────────────────────
function _bindConfirm() {}

function _confirm({ title, body, btns }) {
    if (!_confirmEl) return;
    _confirmEl.querySelector('.etq-ed-confirm-title').textContent = title;
    _confirmEl.querySelector('.etq-ed-confirm-body').textContent  = body;
    const btnsEl = _confirmEl.querySelector('.etq-ed-confirm-btns');
    btnsEl.innerHTML = '';
    _confirmEl.classList.add('open');
    for (const btn of btns) {
        const b = document.createElement('button');
        b.textContent = btn.label;
        b.className   = btn.cls;
        b.onclick     = () => { _confirmEl.classList.remove('open'); if (btn.action) btn.action(); };
        btnsEl.appendChild(b);
    }
}

// ── Helpers ────────────────────────────────────────────────────────────────
function _findEl(id) { return _layout?.elements.find(e => e.id === id); }

function _announce(msg) {
    if (!_liveRegion) return;
    _liveRegion.textContent = '';
    requestAnimationFrame(() => { _liveRegion.textContent = msg; });
}

function _toast(msg) {
    const t = document.createElement('div');
    t.textContent = msg;
    t.style.cssText = 'position:fixed;bottom:20px;left:50%;transform:translateX(-50%);background:#1A2E5A;color:#fff;padding:10px 16px;border-radius:8px;font-size:13px;z-index:99999';
    document.body.appendChild(t);
    setTimeout(() => t.remove(), 3000);
}
