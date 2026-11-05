'use strict';
// Carrega a PWA de verdade (index.html e os scripts locais, na ordem do HTML)
// num vm com canvas real (@napi-rs/canvas). O simulador chama as mesmas
// funções que o app chama ao imprimir, então os bytes são os do app.
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const { createCanvas, GlobalFonts } = require('@napi-rs/canvas');

const FONTES = path.join(__dirname, '..', 'fontes');
let fontesProntas = false;
function registrarFontes() {
  if (fontesProntas) return;
  // A Manrope do APK é variável e o Skia usa o peso padrão (200). As instâncias
  // estáticas reproduzem o peso que o WebView aplica.
  for (const w of [400, 500, 600, 700, 800]) GlobalFonts.registerFromPath(path.join(FONTES, `Manrope-${w}.ttf`), 'Manrope');
  GlobalFonts.registerFromPath(path.join(FONTES, 'Fraunces-700.ttf'), 'Fraunces');
  fontesProntas = true;
}

function elementoFalso(id) {
  return {
    id, value: '', textContent: '', innerHTML: '', style: {}, dataset: {}, checked: false, options: [],
    classList: { add() {}, remove() {}, toggle() {}, contains() { return false; } },
    addEventListener() {}, removeEventListener() {}, focus() {}, click() {},
    filhos: [], appendChild(f) { this.filhos.push(f); return f; }, removeChild() {}, querySelector() { return null; }, querySelectorAll() { return []; },
    setAttribute() {}, getAttribute() { return null; }, removeAttribute() {},
    closest() { return null; }, contains() { return false; }
  };
}

// O @napi-rs/canvas 0.1.65 no Node 24 derruba o V8 ao finalizar canvas
// (contagem de memória externa negativa). O simulador vive pouco, então
// mantém os canvas vivos e o GC não os finaliza.
const VIVOS = [];
function novoCanvas() {
  const c = createCanvas(300, 150);
  c.style = {};
  // A prévia marca o canvas com role e aria-label.
  c.setAttribute = function () {};
  c.getAttribute = () => null;
  VIVOS.push(c);
  return c;
}

function scripts(html, dir) {
  const out = [];
  const re = /<script\b([^>]*)>([\s\S]*?)<\/script>/gi;
  let m;
  while ((m = re.exec(html)) !== null) {
    const attrs = m[1] || '';
    if (/\btype\s*=\s*(['"])module\1/i.test(attrs)) continue;
    const src = attrs.match(/\bsrc\s*=\s*(['"])([^'"]+)\1/i);
    if (src) {
      if (/^(?:https?:)?\/\//i.test(src[2]) || src[2].startsWith('data:')) continue;
      const arq = path.resolve(dir, src[2].split('?')[0].split('#')[0]);
      if (fs.existsSync(arq)) out.push({ body: fs.readFileSync(arq, 'utf8'), nome: path.basename(arq) });
      continue;
    }
    out.push({ body: m[2], nome: null });
  }
  return out;
}

// pwaDir: pasta com index.html (o EasyStock.Api/wwwroot/pwa do worktree ou
// uma cópia do master). Devolve { sb, rodar(codigo), storage }.
function carregarPwa(pwaDir) {
  registrarFontes();
  const html = fs.readFileSync(path.join(pwaDir, 'index.html'), 'utf8');
  const storage = {};
  const porId = {};
  const elemento = id => (porId[id] = porId[id] || elementoFalso(id));
  const localStorage = {
    getItem: k => (Object.prototype.hasOwnProperty.call(storage, k) ? storage[k] : null),
    setItem: (k, v) => { storage[k] = String(v); },
    removeItem: k => { delete storage[k]; },
    clear: () => { for (const k in storage) delete storage[k]; },
    get length() { return Object.keys(storage).length; },
    key: i => Object.keys(storage)[i] || null
  };
  const document = {
    documentElement: { setAttribute() {}, getAttribute() { return null; }, removeAttribute() {}, classList: { add() {}, remove() {}, toggle() {}, contains: () => false }, style: {} },
    body: { classList: { add() {}, remove() {}, toggle() {}, contains: () => false }, appendChild() {}, removeChild() {}, style: {}, addEventListener() {}, removeEventListener() {} },
    head: { appendChild() {}, removeChild() {} },
    fonts: { load: () => Promise.resolve([]), ready: Promise.resolve(), check: () => true },
    // Um elemento por id: o simulador lê o que a tela anexou (a prévia).
    getElementById: id => elemento(id),
    querySelector: () => null,
    querySelectorAll: () => [],
    createElement: tag => (String(tag).toLowerCase() === 'canvas' ? novoCanvas() : elementoFalso('virtual')),
    createElementNS: () => elementoFalso('virtual'),
    addEventListener() {}, removeEventListener() {},
    cookie: '', visibilityState: 'visible', hidden: false
  };
  const sb = {
    console: { log() {}, info() {}, warn() {}, error() {}, debug() {} },
    setTimeout, clearTimeout, setInterval, clearInterval, queueMicrotask,
    TextEncoder, TextDecoder, URL: Object.assign(function () {}, { createObjectURL: () => 'blob://sim', revokeObjectURL() {} }),
    document, localStorage, sessionStorage: { getItem: () => null, setItem() {}, removeItem() {} },
    navigator: { onLine: true, userAgent: 'sim-impressora', vibrate() {}, clipboard: { writeText: async () => {} } },
    location: { hostname: 'localhost', pathname: '/', href: 'http://localhost/', reload() {} },
    fetch: () => Promise.reject(new Error('sem rede no simulador')),
    alert() {}, confirm: () => true, prompt: () => null,
    requestAnimationFrame: cb => setTimeout(cb, 0), cancelAnimationFrame: id => clearTimeout(id),
    Image: class { constructor() { setTimeout(() => this.onerror && this.onerror(new Error('sem imagem')), 0); } },
    HTMLElement: class {}, HTMLCanvasElement: class {},
    CustomEvent: class { constructor(t, o) { this.type = t; this.detail = (o && o.detail) || null; } },
    Event: class { constructor(t) { this.type = t; } },
    Blob: class { constructor(p) { this.size = (p || []).reduce((s, x) => s + ((x && x.length) || 0), 0); } },
    FileReader: class { readAsDataURL() { setTimeout(() => this.onload && this.onload({ target: { result: 'data:;base64,' } }), 0); } },
    addEventListener() {}, removeEventListener() {}, dispatchEvent() {},
    matchMedia: () => ({ matches: false, addEventListener() {}, removeEventListener() {}, addListener() {}, removeListener() {} }),
    innerWidth: 360, innerHeight: 740, devicePixelRatio: 2,
    scrollTo() {}, focus() {}, blur() {}, print() {}, open: () => null, close() {}
  };
  sb.window = sb; sb.self = sb; sb.globalThis = sb;
  vm.createContext(sb);
  const erros = [];
  for (const s of scripts(html, pwaDir)) {
    try { vm.runInContext(s.body, sb, { timeout: 10000, filename: s.nome || 'index.html' }); }
    catch (e) { erros.push((s.nome || 'inline') + ': ' + (e && e.message)); }
  }
  const rodar = codigo => vm.runInContext(codigo, sb, { timeout: 20000, filename: 'sim' });
  for (const f of ['etiquetasTspl', 'etiquetasFromBatch', 'escPosEtiquetaProducao']) {
    if (rodar(`typeof ${f}`) !== 'function') throw new Error(`PWA em ${pwaDir} sem ${f}(): ${erros.join(' | ')}`);
  }
  return { sb, rodar, storage, erros, elemento };
}

module.exports = { carregarPwa, registrarFontes, createCanvas };
