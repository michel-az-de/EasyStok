'use strict';
// #3114 (B9, P1): o APK de homologação instala ao lado do oficial e não tem caminho para a produção.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { execFileSync, spawnSync } = require('node:child_process');
const root = path.resolve(__dirname, '..');
const app = path.join(root, 'android/app');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
// Host com ou sem esquema. A lista de recusa do Ambiente.java usa ponto escapado e não é endereço.
const PRODUCAO = /(?:[a-z0-9-]+\.)*(?:easystok\.online|casadababa\.com)/gi;
// As origens oficiais nativas ficam no flavor oficial do build.gradle, fora dos fontes comuns.
const ONDE_PODE = new Set(['scripts/ambiente.cjs']);

function fontes(dir) {
  return fs.readdirSync(path.join(root, dir), { withFileTypes: true }).flatMap(entry => {
    const relative = dir + '/' + entry.name;
    return entry.isDirectory() ? fontes(relative) : /\.(java|cjs|js)$/.test(entry.name) ? [relative] : [];
  });
}

// O que vai dentro do APK: fontes nativas, scripts do piloto e o empacotador do conteúdo web com o que ele carrega.
// Scripts de publicação (ota-push, signed-notice) falam com a produção de propósito e não entram no APK.
function empacotador() {
  const copia = read('scripts/copy-web.cjs');
  return ['scripts/copy-web.cjs', ...[...copia.matchAll(/require\('\.\/([\w.-]+\.cjs)'\)/g)].map(match => 'scripts/' + match[1])];
}

test('endereço de produção não aparece nos fontes comuns do APK, nativo e web', () => {
  const vistos = fontes('android/app/src/main/java').concat(empacotador(),
    fs.readdirSync(root).filter(name => /^pilot.*\.js$/.test(name)));
  assert.ok(vistos.some(file => file.endsWith('SessionHttpClient.java')), 'varredura precisa alcançar o login nativo');
  assert.ok(vistos.includes('scripts/ota-contract.cjs'), 'varredura precisa seguir o que o empacotador carrega');
  const fixos = vistos.filter(file => !ONDE_PODE.has(file)).flatMap(file => read(file).split('\n')
    .flatMap((line, index) => (line.match(PRODUCAO) || []).map(host => `${file}:${index + 1} fixa ${host}`)));
  assert.deepEqual(fixos, []);
});

test('variante de homologação tem outro pacote, nome e ícone, sem Firebase; a oficial não muda', () => {
  const gradle = read('android/app/build.gradle');
  const defaultConfig = gradle.match(/defaultConfig \{([\s\S]*?)\n    \}/)[1];
  assert.match(defaultConfig, /applicationId "com\.casadababa\.piloto"/);
  assert.doesNotMatch(defaultConfig, /firebase|buildConfigField/i, 'Firebase e origens ficam por variante');
  const flavors = gradle.match(/productFlavors \{([\s\S]*?)\n    \}/);
  assert.ok(flavors, 'build.gradle precisa declarar productFlavors');
  const oficial = flavors[1].match(/oficial \{([\s\S]*?)\n        \}/)[1];
  const homologacao = flavors[1].match(/homologacao \{([\s\S]*?)\n        \}/)[1];
  assert.doesNotMatch(oficial, /applicationId/, 'o oficial segue com.casadababa.piloto');
  for (const [campo, origem] of [['API', 'https://api.easystok.online'], ['APP', 'https://app.easystok.online'], ['DOWNLOADS', 'https://easystok.online']])
    assert.ok(oficial.includes(`'CDB_${campo}_ORIGIN', '"${origem}"'`), 'oficial mantém ' + origem);
  assert.match(oficial, /firebaseResources\.each/);
  assert.match(homologacao, /applicationIdSuffix "\.homologacao"/);
  assert.doesNotMatch(homologacao, /firebase|easystok|casadababa/i, 'homologação sem Firebase e sem origem da produção');
  assert.ok(gradle.includes("beforeVariants(selector().withName('homologacaoRelease')) { it.enable = false }"), 'homologação nunca assina com a chave oficial');
  assert.ok(gradle.includes("file('src/main/assets/public/config.js')"), 'Gradle confere o conteúdo web da variante');
  const valor = (file, name) => (read(file).match(new RegExp(`name="${name}">([^<]+)<`)) || [])[1];
  const main = 'android/app/src/main/res/values/', hml = 'android/app/src/homologacao/res/values/';
  assert.equal(valor(main + 'strings.xml', 'app_name'), 'Casa da Baba Piloto');
  assert.equal(valor(hml + 'strings.xml', 'app_name'), 'Casa da Baba Homologação');
  // O launcher corta nomes longos: "Casa da Baba Piloto" e "Casa da Baba Homologação" viram os dois "Casa da ...".
  assert.equal(valor(hml + 'strings.xml', 'title_activity_main'), 'Homologação');
  const fundo = valor(hml + 'ic_launcher_background.xml', 'ic_launcher_background');
  assert.ok(fundo && fundo !== valor(main + 'ic_launcher_background.xml', 'ic_launcher_background'), 'ícone da homologação precisa de fundo próprio');
  // O PNG oficial tem fundo branco opaco: no tamanho oficial ele cobre a máscara e esconde a cor do fundo.
  const marca = file => read(file).match(/<item android:width="([\d.]+)dp" android:height="([\d.]+)dp" android:gravity="center">\s*<bitmap android:src="@drawable\/casa_da_baba_logo"/).slice(1).map(Number);
  const [lo, ao] = marca('android/app/src/main/res/drawable/casa_da_baba_foreground.xml');
  const [lh, ah] = marca('android/app/src/homologacao/res/drawable/casa_da_baba_foreground.xml');
  assert.ok(Math.hypot(lh, ah) / 2 < 36, 'marca da homologação cabe dentro do círculo visível de 72dp, com fundo em volta');
  assert.ok(lh < lo && Math.abs(lh / ah - lo / ao) < 0.0001, 'mesma marca, menor e sem distorção');
  assert.ok(!fs.existsSync(path.join(app, 'src/oficial')), 'o oficial não ganha recurso próprio');
});

test('conteúdo web da homologação recusa produção e desliga a atualização', () => {
  const { configWeb } = require('../scripts/ambiente.cjs');
  const oficial = configWeb({});
  assert.equal(oficial.ambiente, 'oficial');
  assert.equal(oficial.apiBaseUrl, 'https://api.easystok.online');
  assert.match(oficial.updateManifestUrl, /^https:\/\/easystok\.online\/downloads\/casa-da-baba\/latest\.json$/);
  for (const apiBaseUrl of [undefined, '', 'http://api.teste.example', 'https://api.easystok.online', 'https://API.EasyStok.Online',
    'https://easystok.online', 'https://x.app.easystok.online', 'https://casadababa.com', 'https://www.casadababa.com/', 'https://u@api.teste.example',
    // Ponto final do nome absoluto resolve para o mesmo servidor da produção.
    'https://api.easystok.online.', 'https://API.EASYSTOK.ONLINE.', 'https://api.easystok.online%2e', 'https://casadababa.com.']) {
    assert.throws(() => configWeb({ ambiente: 'homologacao', apiBaseUrl }), /homologação/i, String(apiBaseUrl));
  }
  const hml = configWeb({ ambiente: 'homologacao', apiBaseUrl: 'https://api-teste.example/' });
  assert.deepEqual(hml, { ambiente: 'homologacao', apiBaseUrl: 'https://api-teste.example', updateManifestUrl: '', updatePageUrl: '' });
  assert.throws(() => configWeb({ ambiente: 'producao' }));
});

test('build confere origens e conteúdo web pela regra testada do ambiente.cjs', () => {
  const gradle = read('android/app/build.gradle');
  const pronto = gradle.slice(gradle.indexOf('gradle.taskGraph.whenReady'));
  assert.match(pronto, /file\('\.\.\/\.\.\/scripts\/ambiente\.cjs'\)\.path, 'conferir'/, 'Gradle delega a regra ao ambiente.cjs');
  assert.match(pronto, /exitValue\(\) != 0\) throw new GradleException/, 'recusa do ambiente.cjs derruba o build');
  assert.doesNotMatch(gradle, /easystok\\\.online|casadababa\\\.com/, 'Gradle sem regra própria de origem');
  assert.doesNotMatch(pronto, /JsonSlurper|config\.ambiente/, 'Gradle sem regra própria de conteúdo web');
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'cdb-conferir-'));
  try {
    const { configWeb } = require('../scripts/ambiente.cjs');
    const gravar = (nome, config) => { const file = path.join(tmp, nome); fs.writeFileSync(file, 'window.CDB_CONFIG = ' + JSON.stringify(config) + ';\n'); return file; };
    const api = 'https://api-teste.example', app = 'https://console-teste.example';
    const oficial = gravar('oficial.js', configWeb({})), hml = gravar('hml.js', configWeb({ ambiente: 'homologacao', apiBaseUrl: api }));
    const conferir = (...args) => spawnSync(process.execPath, [path.join(root, 'scripts/ambiente.cjs'), 'conferir', ...args], { encoding: 'utf8' });
    assert.equal(conferir('oficial', oficial).status, 0);
    assert.equal(conferir('homologacao', hml, api, app).status, 0);
    for (const [motivo, args] of [
      ['conteúdo oficial no build de homologação', ['homologacao', oficial, api, app]],
      ['conteúdo de homologação no build oficial', ['oficial', hml]],
      ['API do -P diferente da do conteúdo web', ['homologacao', hml, 'https://outra-api.example', app]],
      ['-P de API da produção', ['homologacao', hml, 'https://api.easystok.online', app]],
      ['-P de app da produção', ['homologacao', hml, api, 'https://app.easystok.online']],
      ['-P da produção com ponto final', ['homologacao', hml, api, 'https://app.easystok.online.']],
      ['-P sem HTTPS', ['homologacao', hml, api, 'http://console-teste.example']],
      ['-P com barra final', ['homologacao', hml, api, app + '/']],
      ['-P ausente', ['homologacao', hml, api, '']],
      ['conteúdo web ausente', ['homologacao', path.join(tmp, 'nada.js'), api, app]],
      ['ambiente desconhecido', ['producao', oficial]]
    ]) {
      const resultado = conferir(...args);
      assert.notEqual(resultado.status, 0, motivo);
      assert.ok(resultado.stderr.trim(), motivo + ': recusa com motivo');
    }
  } finally { fs.rmSync(tmp, { recursive: true, force: true }); }
});

test('empacotador real gera conteúdo de homologação sem nenhum host da produção', () => {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'cdb-homologacao-'));
  try {
    const fixture = path.join(tmp, 'casa-da-baba-mobile/apk');
    const copy = (from, to) => { fs.mkdirSync(path.dirname(to), { recursive: true }); fs.cpSync(from, to, { recursive: true }); };
    for (const name of ['package.json', 'assets', ...fs.readdirSync(root).filter(name => /^pilot.*\.(js|css)$/.test(name)), ...empacotador()])
      copy(path.join(root, name), path.join(fixture, name));
    copy(path.resolve(root, '../../EasyStock.Api/wwwroot/pwa'), path.join(tmp, 'EasyStock.Api/wwwroot/pwa'));
    copy(path.resolve(root, '../../EasyStock.Console/src/assets/casa-da-baba.png'), path.join(tmp, 'EasyStock.Console/src/assets/casa-da-baba.png'));
    const empacotar = env => execFileSync(process.execPath, [path.join(fixture, 'scripts/copy-web.cjs')], { env: { ...process.env, ...env }, stdio: 'pipe' });
    const web = path.join(fixture, 'web');
    const config = () => JSON.parse(fs.readFileSync(path.join(web, 'config.js'), 'utf8').replace(/^window\.CDB_CONFIG = /, '').replace(/;\s*$/, ''));
    empacotar({ CDB_AMBIENTE: 'oficial', CDB_API_BASE_URL: '' });
    assert.deepEqual([config().ambiente, config().apiBaseUrl], ['oficial', 'https://api.easystok.online']);
    assert.throws(() => empacotar({ CDB_AMBIENTE: 'homologacao', CDB_API_BASE_URL: '' }), /homologação/);
    empacotar({ CDB_AMBIENTE: 'homologacao', CDB_API_BASE_URL: 'https://api-teste.example' });
    assert.deepEqual([config().ambiente, config().apiBaseUrl, config().updateManifestUrl], ['homologacao', 'https://api-teste.example', '']);
    const arquivos = dir => fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => entry.isDirectory() ? arquivos(path.join(dir, entry.name)) : [path.join(dir, entry.name)]);
    const vistos = arquivos(web);
    assert.ok(vistos.some(file => file.endsWith('index.html')) && vistos.length > 5, 'empacotador gerou o conteúdo');
    const achados = vistos.flatMap(file => (fs.readFileSync(file, 'latin1').match(PRODUCAO) || []).map(host => path.relative(web, file) + ' contém ' + host));
    assert.deepEqual(achados, []);
  } finally { fs.rmSync(tmp, { recursive: true, force: true }); }
});

test('aviso de APK novo não roda sem canal de atualização', () => {
  const pilot = read('pilot.js');
  const inicio = pilot.indexOf('async function checkUpdate');
  const guarda = pilot.indexOf('if (!config.updateManifestUrl)', inicio);
  assert.ok(guarda > inicio && guarda < pilot.indexOf('checkingUpdate = true;', inicio), 'checkUpdate precisa sair antes de buscar o manifesto');
  const { validateManifest } = require('../pilot.js');
  const manifesto = { schemaVersion: 1, appId: 'com.casadababa.piloto', versionCode: 2, version: '0.1.1', sha256: 'a'.repeat(64),
    url: 'https://easystok.online/downloads/casa-da-baba/casa-da-baba-2.apk' };
  assert.throws(() => validateManifest(manifesto, { appId: 'com.casadababa.piloto', versionCode: 1, updatePageUrl: '' }));
});
