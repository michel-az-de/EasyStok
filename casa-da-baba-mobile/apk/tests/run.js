#!/usr/bin/env node
/**
 * Easy Stock Mobile — Test Runner
 *
 * Carrega o index.html da PWA num sandbox vm com stubs minimos
 * (document, localStorage, navigator) e roda asserts sobre as funcoes
 * de logica de negocio. Roda antes do build do APK pra travar regressoes.
 *
 * Uso: node tests/run.js
 *
 * #390 (integridade do runner): nao mascara mais skip/erro de carga como
 * verde. Erro de execucao de modulo LOCAL critico (sync.js etc) = falha dura;
 * inline opcional = logado com indice+mensagem. Boot de window.cdbSync e'
 * aguardado por poll (teto 5s) e a sua ausencia FALHA o run. `skip()` e'
 * contado e visivel (nao mais `return` silencioso que contava como pass).
 */
'use strict';

const fs = require('fs');
const path = require('path');
const vm = require('vm');
const assert = require('assert');

const HTML_PATH = process.env.PWA_HTML
  || path.resolve(__dirname, '../web/index.html');

// ---------- 1. Carrega index.html e extrai scripts inline + externos locais ----------
// Scripts inline rodam diretamente. Scripts externos com src local (ex: sync.js,
// qrcode.min.js) sao carregados via fs e injetados na mesma ordem que aparecem
// no HTML — necessario pra testar sync.js (que esta em arquivo separado e
// expõe window.cdbSync). URLs externas e absolutas sao puladas (silenciosas).
const html = fs.readFileSync(HTML_PATH, 'utf8');
const HTML_DIR = path.dirname(HTML_PATH);
// Cada item: { body, src } — src e' o caminho relativo (modulo externo local)
// ou null (script inline). Usado pra classificar erro de carga depois.
const scriptBodies = [];
// #390: erros de carga/execucao com origem, pra classificar modulo-local-critico
// (falha dura) vs inline opcional (so loga, sem rotular "Capacitor" sem evidencia).
const loadErrors = [];
// #390: <script type="module"> (ESM com import/export) nao roda em vm.runInContext.
// Pulamos explicitamente e logamos — em vez de estourar ENOENT/"import statement".
const moduleSkips = [];
const re = /<script\b([^>]*)>([\s\S]*?)<\/script>/gi;
let m;
while ((m = re.exec(html)) !== null) {
  const attrs = m[1] || '';
  if (/\btype\s*=\s*(['"])module\1/i.test(attrs)) {
    const sm = attrs.match(/\bsrc\s*=\s*(['"])([^'"]+)\1/i);
    moduleSkips.push(sm ? sm[2] : 'inline (module)');
    continue;
  }
  const srcMatch = attrs.match(/\bsrc\s*=\s*(['"])([^'"]+)\1/i);
  if (srcMatch) {
    const src = srcMatch[2];
    // Pula URLs absolutas e protocolo-relativas — nao baixamos rede.
    if (/^(?:https?:)?\/\//i.test(src) || src.startsWith('data:')) continue;
    // Resolve relativo ao diretorio do index.html (strip query string).
    const cleanPath = src.split('?')[0].split('#')[0];
    const fullPath = path.resolve(HTML_DIR, cleanPath);
    try {
      scriptBodies.push({ body: fs.readFileSync(fullPath, 'utf8'), src: cleanPath });
    } catch (e) {
      // #390: src local ausente vira erro REGISTRADO (modulo critico falha o run).
      loadErrors.push({ src: cleanPath, inline: false, critical: false, phase: 'read', message: e && e.message });
    }
    continue;
  }
  scriptBodies.push({ body: m[2], src: null });
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
    setAttribute() {}, getAttribute() { return null; }, removeAttribute() {},
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
// #390: classifica falha de EXECUCAO. Modulo local critico (sync.js etc) que
// estoura = erro DURO (falha o run la embaixo). Inline que estoura = logado com
// indice + mensagem — sem rotular "esperado Capacitor" sem evidencia.
const CRITICAL_LOCAL_MODULES = ['sync.js', 'queue-store.js', 'photo-store.js', 'upload-photos.js', 'push-subscribe.js'];
for (let i = 0; i < scriptBodies.length; i++) {
  const { body, src } = scriptBodies[i];
  try {
    vm.runInContext(body, sandbox, { timeout: 5000, filename: src || `index.html.script[${i}]` });
  } catch (e) {
    const base = src ? src.split('/').pop() : null;
    const critical = !!(base && CRITICAL_LOCAL_MODULES.includes(base));
    loadErrors.push({ src: src || `inline[${i}]`, inline: !src, critical, phase: 'exec', message: e && e.message });
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

// #390: skip CONTADO e VISIVEL. Substitui o `return` silencioso (que contava
// como pass). Teste chama skip('motivo') -> aparece no resultado como ⊘.
function skip(reason) { const e = new Error(reason || 'skip'); e.__skip = true; throw e; }

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
  require(path.join(__dirname, f))({ test, sandbox, reset, assert, runInSandbox, skip });
}

// ---------- 6. Roda testes ----------
let pass = 0, fail = 0, skipped = 0;
const failures = [];
(async () => {
  // #390: poll-until-ready em vez de espera fixa de 600ms. sync.js cria
  // window.cdbSync num setTimeout interno; esperamos ate existir (teto 5s).
  // Se nao bootar, o run FALHA la embaixo (nao deixa a suite de sync
  // "passar" silenciosamente via `if(!cdbSync())return`).
  const BOOT_DEADLINE_MS = 5000, BOOT_STEP_MS = 25;
  let bootWaited = 0;
  while (!(sandbox.window && sandbox.window.cdbSync) && bootWaited < BOOT_DEADLINE_MS) {
    await new Promise(r => setTimeout(r, BOOT_STEP_MS));
    bootWaited += BOOT_STEP_MS;
  }
  for (const t of tests) {
    try {
      reset();
      await t.fn();
      console.log('  \x1b[32m✓\x1b[0m ' + t.name);
      pass++;
    } catch (e) {
      if (e && e.__skip) {
        console.log('  \x1b[33m⊘\x1b[0m ' + t.name + ' \x1b[33m(skip: ' + (e.message || '') + ')\x1b[0m');
        skipped++;
        continue;
      }
      console.log('  \x1b[31m✗\x1b[0m ' + t.name);
      console.log('    \x1b[31m' + (e && e.message ? e.message : e) + '\x1b[0m');
      fail++;
      failures.push({ name: t.name, err: e });
    }
  }
  console.log('');
  console.log(`Resultado: ${pass} passou, ${fail} falhou, ${skipped} skip (de ${tests.length})`);

  const bootOk = !!(sandbox.window && sandbox.window.cdbSync);
  const criticalLoad = loadErrors.filter(e => e.critical);

  if (loadErrors.length) {
    console.log(`\n${loadErrors.length} script(s) com erro de carga:`);
    loadErrors.forEach(e => {
      const tag = e.critical ? 'CRITICO modulo-local' : (e.inline ? 'inline' : 'src-local');
      console.log(`  - [${tag}] ${e.src} (${e.phase}): ${e.message}`);
    });
  }
  if (moduleSkips.length) {
    console.log(`\n${moduleSkips.length} script(s) type=module pulados (vm nao executa ESM): ${moduleSkips.join(', ')}`);
  }
  if (skipped > 0) {
    console.log(`\n${skipped} teste(s) SKIP (visiveis acima) — NAO contam como pass.`);
  }
  if (!bootOk) {
    console.error('\nERRO DURO: window.cdbSync NAO bootou em ' + BOOT_DEADLINE_MS + 'ms. A suite de '
      + 'sync depende dele; falha o run em vez de deixar os testes pularem silenciosamente.');
  }
  if (criticalLoad.length > 0) {
    console.error(`\nERRO DURO: ${criticalLoad.length} modulo(s) local(is) critico(s) falharam ao carregar (ver acima).`);
  }
  if (fail > 0) {
    console.log('\nFalhas detalhadas:');
    failures.forEach(f => {
      console.log('  - ' + f.name);
      if (f.err && f.err.stack) console.log('    ' + f.err.stack.split('\n').slice(0,3).join('\n    '));
    });
  }
  if (fail > 0 || !bootOk || criticalLoad.length > 0) {
    process.exit(1);
  }
  process.exit(0);
})();
