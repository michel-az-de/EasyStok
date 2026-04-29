#!/usr/bin/env node
/**
 * Easy Stock Mobile — Test Runner
 *
 * Carrega o index.html da PWA num sandbox vm com stubs minimos
 * (document, localStorage, navigator) e roda asserts sobre as funcoes
 * de logica de negocio. Roda antes do build do APK pra travar regressoes.
 *
 * Uso: node tests/run.js
 */
'use strict';

const fs = require('fs');
const path = require('path');
const vm = require('vm');
const assert = require('assert');

const HTML_PATH = process.env.PWA_HTML
  || path.resolve(__dirname, '../web/index.html');

// ---------- 1. Carrega index.html e extrai scripts inline ----------
const html = fs.readFileSync(HTML_PATH, 'utf8');
const scriptBodies = [];
const re = /<script\b([^>]*)>([\s\S]*?)<\/script>/gi;
let m;
while ((m = re.exec(html)) !== null) {
  const attrs = m[1] || '';
  if (/\bsrc=/.test(attrs)) continue; // skip <script src=...>
  scriptBodies.push(m[2]);
}

// ---------- 2. Sandbox: stubs minimos pra DOM/Storage/Navigator ----------
function makeStubElement(id) {
  const ev = {};
  return {
    id, value: '', textContent: '', innerHTML: '', style: {},
    classList: { add() {}, remove() {}, toggle() {}, contains() { return false; } },
    dataset: {},
    addEventListener(type, fn) { (ev[type] = ev[type] || []).push(fn); },
    removeEventListener() {}, focus() {}, click() {},
    appendChild() {}, querySelector() { return null; }, querySelectorAll() { return []; },
    setAttribute() {}, getAttribute() { return null; },
    closest() { return null; }, contains() { return false; }
  };
}

const storageData = {};
const localStorageStub = {
  getItem(k) { return Object.prototype.hasOwnProperty.call(storageData, k) ? storageData[k] : null; },
  setItem(k, v) { storageData[k] = String(v); },
  removeItem(k) { delete storageData[k]; },
  clear() { for (const k in storageData) delete storageData[k]; },
  get length() { return Object.keys(storageData).length; },
  key(i) { return Object.keys(storageData)[i] || null; }
};

const documentStub = {
  documentElement: {
    setAttribute() {}, getAttribute() { return null; }, removeAttribute() {},
    classList: { add() {}, remove() {}, toggle() {}, contains: () => false },
    style: {}
  },
  body: {
    classList: { add() {}, remove() {}, toggle() {}, contains: () => false },
    appendChild() {}, removeChild() {}, style: {},
    addEventListener() {}, removeEventListener() {}
  },
  head: { appendChild() {}, removeChild() {} },
  getElementById(id) { return makeStubElement(id); },
  querySelector() { return null; },
  querySelectorAll() { return []; },
  createElement() { return makeStubElement('virtual'); },
  createElementNS() { return makeStubElement('virtual'); },
  addEventListener() {}, removeEventListener() {},
  cookie: '',
  visibilityState: 'visible', hidden: false
};

const sandbox = {
  console, setTimeout, clearTimeout, setInterval, clearInterval,
  Date, Math, JSON, parseInt, parseFloat, Number, String, Array, Object,
  RegExp, Error, Promise, Map, Set, Symbol, isNaN, isFinite,
  encodeURIComponent, decodeURIComponent, btoa: s => Buffer.from(s, 'binary').toString('base64'),
  atob: s => Buffer.from(s, 'base64').toString('binary'),
  document: documentStub,
  localStorage: localStorageStub,
  sessionStorage: { ...localStorageStub, _sd: {} },
  navigator: { onLine: true, userAgent: 'node-test', vibrate() {}, share: undefined, clipboard: { writeText: async () => {} } },
  location: { hostname: 'localhost', pathname: '/', href: 'http://localhost/', reload() {} },
  fetch: () => Promise.reject(new Error('fetch nao implementado em testes')),
  alert: () => {}, confirm: () => true, prompt: () => null,
  requestAnimationFrame: cb => setTimeout(cb, 16),
  cancelAnimationFrame: id => clearTimeout(id),
  Image: class { constructor() { setTimeout(() => this.onerror && this.onerror(new Error('no img')), 0); } },
  HTMLElement: class {},
  Capacitor: undefined,
  // Padrao Web — usados por escpos e outras conversoes binarias
  TextEncoder, TextDecoder, Uint8Array, Uint16Array, Int32Array, ArrayBuffer, DataView,
  Blob: class { constructor(parts) { this.size = (parts || []).reduce((s,p)=>s+(p&&p.length||0),0); this.type=''; } },
  URL: { createObjectURL: () => 'blob://stub', revokeObjectURL: () => {} },
  FileReader: class { readAsDataURL() { setTimeout(() => this.onload && this.onload({ target: { result: 'data:;base64,' } }), 0); } },
  qrcode: () => ({ addData() {}, make() {}, getModuleCount: () => 0, isDark: () => false }),
  // Window-level handlers — algumas funcoes do PWA chamam window.addEventListener,
  // window.removeEventListener, window.dispatchEvent etc no escopo top-level.
  // Sem stubs aqui, o script aborta na primeira chamada e funcoes ficam em TDZ.
  addEventListener() {}, removeEventListener() {}, dispatchEvent() {},
  matchMedia: () => ({ matches: false, addEventListener() {}, removeEventListener() {}, addListener() {}, removeListener() {} }),
  innerWidth: 360, innerHeight: 740, devicePixelRatio: 2,
  scrollTo() {}, scrollBy() {}, focus() {}, blur() {},
  print: () => {}, open: () => null, close: () => {},
};
sandbox.window = sandbox;
sandbox.self = sandbox;
sandbox.globalThis = sandbox;

vm.createContext(sandbox);

// ---------- 3. Avalia cada script no sandbox ----------
const errors = [];
for (let i = 0; i < scriptBodies.length; i++) {
  try {
    vm.runInContext(scriptBodies[i], sandbox, { timeout: 5000, filename: `index.html.script[${i}]` });
  } catch (e) {
    // Alguns scripts dependem de plugins de runtime (Capacitor) — skip silencioso.
    // So aborta se NENHUM script carregar.
    errors.push({ i, err: e });
  }
}
if (scriptBodies.length === 0) {
  console.error('Nenhum <script> inline encontrado em index.html');
  process.exit(2);
}

// ---------- 4. Test harness ----------
const tests = [];
function test(name, fn) { tests.push({ name, fn }); }

// Executa codigo no MESMO contexto do script — necessario pra acessar
// variaveis declaradas com let/const no top-level (que nao ficam em
// globalThis no vm sandbox). Usado pra setar arrays como `orders`,
// `cashEntries` etc que estao em closure scope.
function runInSandbox(code, opts) {
  return vm.runInContext(code, sandbox, Object.assign({ timeout: 5000, filename: 'test-inline' }, opts || {}));
}

function reset() {
  // Reseta state global. Precisa ser executado DENTRO do contexto pra
  // resetar as `let` em closure scope.
  storageData['cdb-products']      = '[]';
  storageData['cdb-orders']        = '[]';
  storageData['cdb-clients']       = '[]';
  storageData['cdb-cash']          = '[]';
  storageData['cdb-batches']       = '[]';
  storageData['cdb-cash-closings'] = '[]';
  storageData['cdb-error-log']     = '[]';
  storageData['cdb-prod-scan-mode']= 'both';
  storageData['cdb-empresa-name']  = 'Minha empresa';
  try {
    runInSandbox(`
      try { products    = []; } catch(_) {}
      try { orders      = []; } catch(_) {}
      try { clients     = []; } catch(_) {}
      try { cashEntries = []; } catch(_) {}
      try { batches     = []; } catch(_) {}
      try { cashClosings= []; } catch(_) {}
    `);
  } catch (e) { /* ignore */ }
}

// ---------- 5. Importa testes ----------
const testFiles = fs.readdirSync(__dirname)
  .filter(f => f.endsWith('.test.js'))
  .sort();
for (const f of testFiles) {
  require(path.join(__dirname, f))({ test, sandbox, reset, assert, runInSandbox });
}

// ---------- 6. Roda testes ----------
let pass = 0, fail = 0;
const failures = [];
(async () => {
  for (const t of tests) {
    try {
      reset();
      await t.fn();
      console.log('  \x1b[32m✓\x1b[0m ' + t.name);
      pass++;
    } catch (e) {
      console.log('  \x1b[31m✗\x1b[0m ' + t.name);
      console.log('    \x1b[31m' + (e && e.message ? e.message : e) + '\x1b[0m');
      fail++;
      failures.push({ name: t.name, err: e });
    }
  }
  console.log('');
  console.log(`Resultado: ${pass} passou, ${fail} falhou (de ${tests.length})`);
  if (errors.length) {
    console.log(`(${errors.length} script(s) com erro de carga — esperado pra trechos que dependem de Capacitor)`);
  }
  if (fail > 0) {
    console.log('\nFalhas detalhadas:');
    failures.forEach(f => {
      console.log('  - ' + f.name);
      if (f.err && f.err.stack) console.log('    ' + f.err.stack.split('\n').slice(0,3).join('\n    '));
    });
    process.exit(1);
  }
  process.exit(0);
})();
