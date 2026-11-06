'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const html = fs.readFileSync(path.resolve(__dirname, '../../../EasyStock.Api/wwwroot/pwa/index.html'), 'utf8');
const css = fs.readFileSync(path.join(__dirname, '../pilot.css'), 'utf8');
const res = path.join(__dirname, '../android/app/src/main/res');

function bootTheme({ pilot, systemDark, stored }) {
  const start = html.indexOf('function applyTheme(theme) {');
  const end = html.indexOf('// Tabs do modal Ajustes');
  const boot = html.slice(html.indexOf('// Tema inicial'), html.indexOf('\n', html.indexOf('// Fim do tema inicial')));
  const listeners = [];
  let attribute = null;
  const storage = new Map(stored ? [['cdb-theme', stored]] : []);
  const context = {
    document: {
      documentElement: { setAttribute: (_, value) => { attribute = value; }, removeAttribute: () => { attribute = null; } },
      getElementById: () => null
    },
    localStorage: { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) },
    matchMedia: () => ({ get matches() { return context.systemDark; }, addEventListener: (_, fn) => listeners.push(fn) }),
    systemDark
  };
  context.window = context;
  context.window.CDB_CONFIG = { isPilot: pilot };
  vm.createContext(context);
  vm.runInContext(html.slice(start, end) + '\n' + boot, context);
  return { theme: () => (attribute === 'light' ? 'light' : 'dark'), context, listeners };
}

test('no APK o tema segue o claro ou escuro do Android, ignorando escolha antiga salva', () => {
  assert.equal(bootTheme({ pilot: true, systemDark: true, stored: 'light' }).theme(), 'dark');
  assert.equal(bootTheme({ pilot: true, systemDark: false, stored: 'dark' }).theme(), 'light');
});

test('no APK a troca de tema do Android vale sem reabrir o app', () => {
  const boot = bootTheme({ pilot: true, systemDark: false });
  assert.equal(boot.theme(), 'light');
  boot.context.systemDark = true;
  boot.listeners.forEach(listener => listener());
  assert.equal(boot.theme(), 'dark');
});

test('no PWA a escolha salva continua valendo e o padrão segue escuro', () => {
  assert.equal(bootTheme({ pilot: false, systemDark: false, stored: 'light' }).theme(), 'light');
  assert.equal(bootTheme({ pilot: false, systemDark: false }).theme(), 'dark');
});

// Cores literais só onde não dependem do tema: texto sobre preenchimento da marca,
// sombras/transparências (8 dígitos) e o véu atrás dos diálogos.
const ON_FILL = new Set(['#fff9ee', '#fffaf2', '#fff']);
function declarations() {
  const found = [];
  for (const rule of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    for (const declaration of rule[2].matchAll(/([\w-]+)\s*:\s*([^;]+)/g)) {
      found.push({ selector: rule[1].trim(), property: declaration[1], value: declaration[2] });
    }
  }
  return found;
}

test('cores do pilot usam tokens de tema, para claro e escuro ficarem coerentes', () => {
  const offenders = [];
  for (const { selector, property, value } of declarations()) {
    if (property.startsWith('--') || selector.endsWith('::backdrop')) continue;
    for (const hex of value.match(/#[0-9a-f]{3,8}\b/gi) || []) {
      const color = hex.toLowerCase();
      if (color.length === 9) continue;
      if (property === 'color' && ON_FILL.has(color)) continue;
      offenders.push(selector.slice(-40) + ' ' + property + ': ' + color);
    }
  }
  assert.deepEqual(offenders, []);
});

// Pseudo-elementos decorativos (ponto, indicador) não carregam texto.
test('preenchimentos da marca com texto claro usam tokens fixos, nunca o tom de texto do tema', () => {
  const offenders = declarations()
    .filter(({ selector, property, value }) => !/::(before|after)$/.test(selector) && /^background/.test(property) && /var\(--(basil|accent|gold)\)/.test(value))
    .map(({ selector }) => selector.slice(-60));
  assert.deepEqual(offenders, []);
  assert.match(css, /--fill-basil:\s*#496b56/);
  assert.match(css, /--fill-accent:\s*#a34e35/);
});

test('barras do Android acompanham o tema noturno; a abertura mantém a marca clara', () => {
  const night = fs.readFileSync(path.join(res, 'values-night/styles.xml'), 'utf8');
  assert.doesNotMatch(night, /NoActionBarLaunch/);
  for (const name of ['AppTheme.NoActionBar']) {
    const style = night.match(new RegExp('<style name="' + name.replace('.', '\\.') + '"[\\s\\S]*?</style>'))?.[0];
    assert.ok(style, name);
    assert.match(style, /name="android:statusBarColor">#24221D</i);
    assert.match(style, /name="android:windowLightStatusBar">false</);
    assert.match(style, /name="android:navigationBarColor">#24221D</i);
    assert.match(style, /name="android:windowLightNavigationBar">false</);
  }
});

// Componentes do PWA com texto branco sobre accent/basil: no escuro do piloto
// esses tons viram texto claro, então o APK troca o fundo pelo preenchimento fixo.
test('preenchimentos do PWA com texto branco usam o tom fixo da marca no APK', () => {
  const missing = [];
  for (const rule of html.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    const selector = rule[1].trim().replace(/\s+/g, ' ');
    if (selector.includes('data-theme') || selector.includes('@')) continue;
    const fill = rule[2].match(/background(?:-color)?\s*:\s*var\(--(accent|basil)\)/)?.[1];
    if (!fill || !/(^|[;\s])color\s*:\s*(#fff\b|#ffffff|white)/i.test(rule[2])) continue;
    const override = css.match(new RegExp('body\\.pilot-app :is\\(([^)]*)\\) \\{ background: var\\(--fill-' + fill + '\\)'))?.[1] || '';
    for (const part of selector.split(',').map(s => s.trim())) {
      const own = css.includes(' ' + part + ' { background: var(--fill-' + fill + ')');
      if (!own && !override.split(',').map(s => s.trim()).includes(part)) missing.push(fill + ' ' + part);
    }
  }
  assert.deepEqual(missing, []);
});

// Dentro de :is() a especificidade é a do argumento mais forte; o selo da aba ativa
// perderia o escurecido do PWA. Ele fica numa regra própria só para a aba inativa.
test('selo da aba ativa mantém o escurecido do PWA', () => {
  const list = css.match(/body\.pilot-app :is\(([^)]*)\) \{ background: var\(--fill-accent\)/)?.[1] || '';
  assert.ok(!list.includes('.screen-tab-badge'));
  assert.match(css, /body\.pilot-app \.screen-tab:not\(\.active\) \.screen-tab-badge \{ background: var\(--fill-accent\); \}/);
});

// multiply some com o logo sobre fundo escuro; só faz sentido no tema claro.
test('logo só mistura com o fundo no tema claro', () => {
  for (const line of css.split('\n').filter(l => l.includes('mix-blend-mode: multiply'))) {
    assert.match(line, /^html\[data-theme=light\] /, line.slice(0, 80));
  }
});

// O splash aparece antes de o body ganhar pilot-app, quando --fill-basil ainda não existe.
test('spinner do splash tem cor antes do tema do APK carregar', () => {
  assert.match(css, /#cdbSplash\.pilot-splash \{[^}]*--fill-basil: #496b56;/);
});
