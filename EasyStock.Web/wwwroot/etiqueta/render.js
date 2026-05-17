import { resolveVariables } from './variables.js';
import { migrateLayoutJson } from './migrate.js';

// MM to px at 96dpi: 1mm = 3.7795px
const MM = 3.7795275591;

const FONT_MAP = {
  display: "'Fraunces', 'Georgia', serif",
  sans:    "'Inter', 'Helvetica Neue', Arial, sans-serif",
  mono:    "'JetBrains Mono', 'Fira Mono', 'Courier New', monospace",
};

/**
 * Render a single label as an HTMLElement.
 * @param {Object} layout   - parsed LayoutJson (v=1)
 * @param {Object} dados    - { produto, etiqueta, lote, empresa }
 * @param {Object} [opts]
 * @param {boolean} [opts.forPrint=false] - inject @page rule when true
 * @returns {HTMLElement}
 */
export function renderEtiqueta(layout, dados, opts = {}) {
  layout = migrateLayoutJson(layout);
  const { size, elements } = layout;
  const wPx = size.w_mm * MM;
  const hPx = size.h_mm * MM;

  const root = document.createElement('div');
  root.className = 'etq-label';
  root.setAttribute('data-etq-size', size.preset ?? `${size.w_mm}x${size.h_mm}mm`);
  Object.assign(root.style, {
    position:   'relative',
    width:      `${wPx}px`,
    height:     `${hPx}px`,
    overflow:   'hidden',
    background: '#fff',
    boxSizing:  'border-box',
  });

  for (const el of (elements ?? [])) {
    const node = renderElement(el, dados, size);
    if (node) root.appendChild(node);
  }

  if (opts.forPrint) injectPageRule(size);

  return root;
}

function renderElement(el, dados, size) {
  switch (el.type) {
    case 'text':              return renderText(el, dados);
    case 'image':             return renderImage(el, dados);
    case 'code':              return renderCode(el, dados);
    case 'nutritional-table': return renderNutritional(el, dados);
    case 'preparo-text':      return renderPreparo(el, dados);
    case 'alergenos-pills':   return renderAlergenos(el, dados);
    case 'divider':           return renderDivider(el);
    default:                  return null;
  }
}

function base(el, extraClass = '') {
  const div = document.createElement('div');
  div.className = `etq-el etq-${el.type}${extraClass ? ' ' + extraClass : ''}`;
  div.setAttribute('data-etq-id', el.id);
  div.setAttribute('data-etq-type', el.type);
  Object.assign(div.style, {
    position: 'absolute',
    left:     `${el.x_mm * MM}px`,
    top:      `${el.y_mm * MM}px`,
    width:    `${el.w_mm * MM}px`,
    height:   `${el.h_mm * MM}px`,
    boxSizing: 'border-box',
    overflow: 'hidden',
  });
  return div;
}

function renderText(el, dados) {
  const div = base(el);
  const raw = resolveVariables(el.content ?? '', dados);

  // Split on {etq-val-prefix} pattern — "VAL 15/05/2026"
  const text = raw.startsWith('VAL ') || raw.startsWith('LOT ')
    ? buildPrefixSpan(raw)
    : document.createTextNode(raw);

  div.setAttribute('data-etq-text', raw);
  Object.assign(div.style, {
    fontFamily:   FONT_MAP[el.font ?? 'sans'] ?? FONT_MAP.sans,
    fontSize:     `${el.size_pt ?? 10}pt`,
    fontWeight:   el.weight ?? 400,
    textAlign:    el.align ?? 'left',
    color:        resolveColor(el.color ?? 'ink-900', false),
    lineHeight:   '1.15',
    display:      'flex',
    alignItems:   'center',
  });

  if (el.overflow === 'shrink-then-ellipsis') {
    div.style.overflow   = 'hidden';
    div.style.textOverflow = 'ellipsis';
    div.style.whiteSpace  = 'nowrap';
    div.style.display     = 'block';
    div.style.lineHeight  = '1.15';
    scheduleShrink(div, el.size_pt ?? 10);
  }

  if (typeof text === 'string') div.textContent = raw;
  else div.appendChild(text);

  return div;
}

function buildPrefixSpan(raw) {
  const spaceIdx = raw.indexOf(' ');
  const frag = document.createDocumentFragment();
  const prefix = document.createElement('span');
  prefix.className = 'etq-val-prefix';
  prefix.textContent = raw.slice(0, spaceIdx);
  frag.appendChild(prefix);
  frag.appendChild(document.createTextNode(raw.slice(spaceIdx)));
  return frag;
}

function scheduleShrink(div, basePt) {
  requestAnimationFrame(() => {
    let pt = basePt;
    while (pt > 6 && div.scrollWidth > div.offsetWidth) {
      pt -= 0.5;
      div.style.fontSize = `${pt}pt`;
    }
  });
}

function renderImage(el, dados) {
  const div = base(el);
  const assetUrl = resolveAsset(el.asset, dados);

  if (!assetUrl) {
    // Placeholder cinza ink-300
    Object.assign(div.style, {
      background:   '#C8CFD8',
      display:      'flex',
      alignItems:   'center',
      justifyContent: 'center',
    });
    if ((el.h_mm ?? 0) >= 5) {
      const lbl = document.createElement('span');
      lbl.textContent = 'Logo';
      lbl.style.cssText = 'font-size:6pt;color:#6B7480;font-family:sans-serif';
      div.appendChild(lbl);
    }
    return div;
  }

  const img = document.createElement('img');
  img.src = assetUrl;
  img.alt = '';
  img.style.cssText = 'width:100%;height:100%;object-fit:contain;display:block';
  img.setAttribute('data-etq-asset', el.asset ?? '');
  div.appendChild(img);
  return div;
}

function resolveAsset(asset, dados) {
  if (!asset) return null;
  if (asset === 'system:logo-easystok')    return '/etiqueta/assets/logo-easystok.svg';
  if (asset === 'system:lockup-easystok') return '/etiqueta/assets/lockup-easystok.svg';
  if (asset === 'loja:logo')              return dados?.empresa?.LogoUrl ?? null;
  return null;
}

function renderCode(el, dados) {
  const div = base(el);
  const content = resolveVariables(el.content ?? '', dados);
  div.setAttribute('data-etq-code-format', el.format ?? 'qr');

  // Codes rendered async after DOM insertion via codes.js
  div.setAttribute('data-etq-code-content', content);
  div.setAttribute('data-etq-quiet-zone', el.quiet_zone_mm ?? 1);
  div.style.display = 'flex';
  div.style.alignItems = 'center';
  div.style.justifyContent = 'center';

  return div;
}

function renderNutritional(el, dados) {
  const div = base(el, 'etq-nutri');
  const p = dados?.produto;

  const rows = [
    ['Porção',                fmt(p?.FichaPorcaoG,           'g')],
    ['Calorias',              fmt(p?.FichaKcal,              'kcal')],
    ['Carboidratos',          fmt(p?.FichaCarbsG,            'g')],
    ['Proteínas',             fmt(p?.FichaProteinaG,         'g')],
    ['Gorduras totais',       fmt(p?.FichaGorduraG,          'g')],
    ['Gorduras saturadas',    fmt(p?.FichaGorduraSaturadaG,  'g')],
    ['Fibras alimentares',    fmt(p?.FichaFibrasG,           'g')],
    ['Sódio',                 fmt(p?.FichaSodioMg,           'mg')],
  ];

  const sizePt = el.size_pt_min ?? 7;
  Object.assign(div.style, {
    fontFamily: FONT_MAP.sans,
    fontSize:   `${sizePt}pt`,
    lineHeight: '1.2',
    overflow:   'hidden',
  });

  const table = document.createElement('table');
  table.style.cssText = 'width:100%;border-collapse:collapse;table-layout:fixed';
  for (const [label, val] of rows) {
    const tr = document.createElement('tr');
    tr.style.borderBottom = '0.3pt solid #000';
    const td1 = document.createElement('td');
    td1.textContent = label;
    td1.style.padding = '0.5pt 0';
    const td2 = document.createElement('td');
    td2.textContent = val;
    td2.style.cssText = 'text-align:right;padding:0.5pt 0;white-space:nowrap';
    tr.appendChild(td1);
    tr.appendChild(td2);
    table.appendChild(tr);
  }

  const hdr = document.createElement('div');
  hdr.textContent = 'INFORMAÇÃO NUTRICIONAL';
  hdr.style.cssText = `font-weight:700;font-size:${sizePt + 0.5}pt;border-bottom:1pt solid #000;padding-bottom:1pt;margin-bottom:1pt`;
  div.appendChild(hdr);
  div.appendChild(table);

  autoFitNutri(div, el.size_pt_min ?? 6, el.size_pt_max ?? 8);
  return div;
}

function autoFitNutri(div, minPt, maxPt) {
  requestAnimationFrame(() => {
    let pt = maxPt;
    div.style.fontSize = `${pt}pt`;
    while (pt > minPt && div.scrollHeight > div.offsetHeight) {
      pt -= 0.25;
      div.style.fontSize = `${pt}pt`;
    }
  });
}

function renderPreparo(el, dados) {
  const div = base(el, 'etq-preparo');
  const txt = dados?.produto?.FichaModoPreparo ?? '—';
  div.style.cssText += `;font-family:${FONT_MAP.sans};font-size:7pt;overflow:hidden`;
  const lbl = document.createElement('strong');
  lbl.textContent = 'Preparo: ';
  lbl.style.fontSize = '6pt';
  const body = document.createElement('span');
  body.textContent = txt;
  div.appendChild(lbl);
  div.appendChild(body);
  return div;
}

function renderAlergenos(el, dados) {
  const div = base(el, 'etq-alergenos');
  const lista = dados?.produto?.FichaAlergenos ?? [];
  div.style.cssText += ';display:flex;flex-wrap:wrap;gap:1px;align-items:center;overflow:hidden';

  const LABELS = {
    gluten:      'Glúten', lactose: 'Lactose', ovo: 'Ovo',
    soja:        'Soja',   amendoim: 'Amendoim', castanhas: 'Castanhas',
    peixe:       'Peixe',  crustaceos: 'Crustáceos', outros: 'Outros',
  };

  for (const al of lista) {
    const pill = document.createElement('span');
    pill.className = 'etq-al-pill';
    pill.textContent = LABELS[al] ?? al;
    pill.style.cssText = 'border:0.5pt solid #000;border-radius:999pt;padding:0 2pt;font-size:6pt;white-space:nowrap;font-family:sans-serif';
    div.appendChild(pill);
  }

  if (lista.length === 0) {
    div.style.display = 'none';
  }
  return div;
}

function renderDivider(el) {
  const div = base(el, 'etq-divider');
  const stroke = el.stroke_pt ?? 0.5;
  const line = document.createElement('div');
  line.style.cssText = `position:absolute;top:50%;left:0;right:0;height:${stroke}pt;background:#000;transform:translateY(-50%)`;
  div.appendChild(line);
  return div;
}

// ── Utilities ────────────────────────────────────────────────────────────────

function fmt(val, unit) {
  if (val === null || val === undefined) return '—';
  return `${val}${unit}`;
}

function resolveColor(token, forPrint) {
  if (forPrint) return '#000';
  const COLOR_MAP = {
    'ink-900': '#1A1F26', 'ink-700': '#3A4350', 'ink-600': '#4D5665',
    'ink-500': '#6B7480', 'ink-300': '#C8CFD8', 'ink-100': '#ECEFF5',
    'orange-600': '#E85814', 'navy-700': '#1A2E5A',
  };
  return COLOR_MAP[token] ?? token;
}

function injectPageRule(size) {
  const id = 'etq-page-rule';
  if (document.getElementById(id)) return;
  const style = document.createElement('style');
  style.id = id;
  style.textContent = `@page { size: ${size.w_mm}mm ${size.h_mm}mm; margin: 0; }`;
  document.head.appendChild(style);
}

// Global expose for Razor ViewComponent
if (typeof window !== 'undefined') window.renderEtiqueta = renderEtiqueta;
