// Renders all code elements (QR + barcodes) in a rendered label root.
// Call after renderEtiqueta() result is inserted into the DOM.
// Depends on: /etiqueta/vendor/qrcode.min.js and /etiqueta/vendor/jsbarcode.min.js

/**
 * @param {HTMLElement} root - the element returned by renderEtiqueta()
 */
export async function renderCodes(root) {
  const codeEls = root.querySelectorAll('[data-etq-type="code"]');
  if (!codeEls.length) return;

  await ensureLibs();

  for (const el of codeEls) {
    const format  = el.getAttribute('data-etq-code-format') ?? 'qr';
    const content = el.getAttribute('data-etq-code-content') ?? '';
    const quiet   = parseFloat(el.getAttribute('data-etq-quiet-zone') ?? '1');

    el.innerHTML = '';

    if (!content || content === '—') {
      el.style.background = '#F0F0F0';
      continue;
    }

    try {
      if (format === 'qr') {
        renderQr(el, content, quiet);
      } else if (format === 'barcode-ean13') {
        renderBarcode(el, content, 'EAN13');
      } else {
        renderBarcode(el, content, 'CODE128');
      }
    } catch (err) {
      console.warn('[codes.js] render error', format, content, err);
      el.style.background = '#FFE0E0';
      el.title = `Erro: ${err.message}`;
    }
  }
}

function renderQr(el, content, quietMm) {
  // qrcode-generator (Kazuhiko Arase) expoe global `qrcode` (lowercase, funcional)
  // typeNumber 0 = auto-deteccao; nivel 'M' = ~15% correcao
  const qr = qrcode(0, 'M');
  qr.addData(content);
  qr.make();
  const modules = qr.getModuleCount();
  const size = Math.min(el.offsetWidth, el.offsetHeight) || 64;
  const cellSize = Math.max(1, Math.floor(size / (modules + 2)));
  // createSvgTag escala perfeitamente em qualquer zoom; margin em modulos
  el.innerHTML = qr.createSvgTag({ cellSize, margin: 1, scalable: true });
  const svg = el.querySelector('svg');
  if (svg) { svg.style.width = '100%'; svg.style.height = '100%'; svg.style.display = 'block'; }
}

function renderBarcode(el, content, format) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.style.width  = '100%';
  svg.style.height = '100%';
  el.appendChild(svg);
  JsBarcode(svg, content, {
    format,
    displayValue: false,
    margin:       0,
    background:   '#fff',
    lineColor:    '#000',
  });
}

let _libsLoaded = false;
async function ensureLibs() {
  if (_libsLoaded) return;
  await Promise.all([
    loadScript('/etiqueta/vendor/qrcode.min.js'),
    loadScript('/etiqueta/vendor/jsbarcode.min.js'),
  ]);
  _libsLoaded = true;
}

function loadScript(src) {
  return new Promise((resolve, reject) => {
    if (document.querySelector(`script[src="${src}"]`)) { resolve(); return; }
    const s = document.createElement('script');
    s.src = src;
    s.onload  = resolve;
    s.onerror = () => reject(new Error(`Failed to load ${src}`));
    document.head.appendChild(s);
  });
}

if (typeof window !== 'undefined') window.renderCodes = renderCodes;
