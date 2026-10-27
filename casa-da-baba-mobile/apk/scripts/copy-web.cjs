'use strict';
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const source = path.resolve(root, '..', '..', 'EasyStock.Api', 'wwwroot', 'pwa');
const target = path.join(root, 'web');
const fonts = path.join(root, 'assets', 'fonts');
const version = require('../package.json');
const contract = require('./ota-contract.cjs');
const { adaptAppMarkup } = require('./app-markup.cjs');
const bundleVersion = process.env.CDB_BUNDLE_VERSION || version.version;
if (!/^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$/.test(bundleVersion)) throw new Error('Versão do conteúdo inválida.');
const apiBaseUrl = process.env.CDB_API_BASE_URL || 'https://api.easystok.online';
if (new URL(apiBaseUrl).protocol !== 'https:') throw new Error('A API do piloto precisa de HTTPS.');
for (const name of ['pilot.js', 'pilot-ui.js', 'pilot.css']) {
  if (!fs.existsSync(path.join(root, name))) throw new Error('Arquivo obrigatório ausente: ' + name);
}
if (!fs.existsSync(path.join(source, 'index.html'))) throw new Error('PWA fonte ausente.');
for (const name of ['fonts.css', 'Manrope.ttf', 'Fraunces.ttf', 'Fraunces-Italic.ttf', 'Manrope-OFL.txt', 'Fraunces-OFL.txt']) {
  if (!fs.existsSync(path.join(fonts, name))) throw new Error('Fonte local obrigatória ausente: ' + name);
}
if (path.dirname(target) !== root || path.basename(target) !== 'web') throw new Error('Destino inválido.');
fs.rmSync(target, { recursive: true, force: true });
fs.cpSync(source, target, { recursive: true });
fs.cpSync(fonts, path.join(target, 'fonts'), { recursive: true });
for (const name of ['pilot.js', 'pilot-ui.js', 'pilot.css']) fs.copyFileSync(path.join(root, name), path.join(target, name));
fs.copyFileSync(path.resolve(root, '../../EasyStock.Console/src/assets/casa-da-baba.png'), path.join(target, 'casa-da-baba.png'));
const config = {
  apiBaseUrl: apiBaseUrl.replace(/\/+$/, ''),
  isPilot: true,
  appId: contract.APP,
  version: version.version,
  pilotVersion: version.version,
  versionCode: version.versionCode,
  bundleVersion,
  dataSchemaVersion: contract.DATA_SCHEMA,
  updateManifestUrl: 'https://easystok.online/downloads/casa-da-baba/latest.json',
  updatePageUrl: 'https://easystok.online/downloads/casa-da-baba/'
};
fs.writeFileSync(path.join(target, 'config.js'), 'window.CDB_CONFIG = ' + JSON.stringify(config) + ';\n');
fs.writeFileSync(path.join(target, 'bundle.json'), JSON.stringify({ schemaVersion: 1, appId: config.appId, channel: contract.CHANNEL, bundleVersion, nativeVersionCode: contract.NATIVE_VERSION_CODE, dataSchemaVersion: contract.DATA_SCHEMA }));
const htmlPath = path.join(target, 'index.html');
let html = adaptAppMarkup(fs.readFileSync(htmlPath, 'utf8'));
// Mantém as mesmas famílias/eixos sem depender da rede para abrir o APK.
let localFontsLinked = false;
html = html.replace(/<link\b[^>]*href=["']https:\/\/fonts\.(?:googleapis\.com|gstatic\.com)[^"']*["'][^>]*>\s*/gi, () => {
  if (localFontsLinked) return '';
  localFontsLinked = true;
  return '<link rel="stylesheet" href="fonts/fonts.css">\n';
});
if (!localFontsLinked) throw new Error('Ponto de carga das fontes não encontrado.');
const syncScript = /<script\s+src=["']sync\.js[^"']*["']\s*><\/script>/i;
if (!syncScript.test(html)) throw new Error('Ponto de carga sync.js não encontrado.');
html = html.replace(/<script\s+src=["']config\.js[^"']*["']\s*><\/script>\s*/gi, '');
// A configuração precisa existir antes do onboarding inline do PWA.
html = html.replace('<head>', '<head>\n<script src="config.js"></script>');
html = html.replace('<title>Easy Stock Mobile</title>', '<title>Casa da Baba Piloto</title>');
html = html.replace('<h2>Registrar lote</h2>', '<h2>Produção</h2>');
html = html.replace('Selecione os itens produzidos, revise validade e gere etiquetas em seguida.', 'Informe a quantidade produzida. Depois, confira o lote e imprima as etiquetas.');
html = html.replace('O app costuma se atualizar sozinho ao reabrir. Se precisar, force a verificação manual.', 'O conteúdo é preparado automaticamente e aplicado na próxima abertura. Mudanças nativas exigem confirmar a instalação no Android. Não desinstale o app.');
html = html.replace('</head>', '<link rel="stylesheet" href="pilot.css">\n</head>');
html = html.replace('</body>', '<script src="pilot.js"></script>\n<script src="pilot-ui.js"></script>\n</body>');
html = html.replaceAll('__APP_VERSION__', 'piloto-' + version.version)
  .replaceAll('"__APP_RELEASE_NOTES__"', JSON.stringify('Piloto integrado ao EasyStok'));
// WebViews e previews podem conservar scripts da versão anterior após atualizar.
html = html.replace(/((?:src|href)=['"])([^'"<>]+\.(?:js|css)(?:\?[^'"<>]*)?)(['"])/gi, (match, before, asset, after) => {
  if (/^(?:https?:)?\/\//i.test(asset)) return match;
  return before + asset + (asset.includes('?') ? '&' : '?') + 'pilot=' + encodeURIComponent(bundleVersion) + after;
});
fs.writeFileSync(htmlPath, html);
fs.rmSync(path.join(target, 'sw.js'), { force: true });
console.log('PWA empacotada: piloto ' + version.version);
