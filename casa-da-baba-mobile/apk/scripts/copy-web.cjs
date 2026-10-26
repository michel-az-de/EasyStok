'use strict';
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const source = path.resolve(root, '..', '..', 'EasyStock.Api', 'wwwroot', 'pwa');
const target = path.join(root, 'web');
const version = require('../package.json');
const apiBaseUrl = process.env.CDB_API_BASE_URL || 'https://api.easystok.online';
if (new URL(apiBaseUrl).protocol !== 'https:') throw new Error('A API do piloto precisa de HTTPS.');
for (const name of ['pilot.js', 'pilot.css']) {
  if (!fs.existsSync(path.join(root, name))) throw new Error('Arquivo obrigatório ausente: ' + name);
}
if (!fs.existsSync(path.join(source, 'index.html'))) throw new Error('PWA fonte ausente.');
if (path.dirname(target) !== root || path.basename(target) !== 'web') throw new Error('Destino inválido.');
fs.rmSync(target, { recursive: true, force: true });
fs.cpSync(source, target, { recursive: true });
for (const name of ['pilot.js', 'pilot.css']) fs.copyFileSync(path.join(root, name), path.join(target, name));
const config = {
  apiBaseUrl: apiBaseUrl.replace(/\/+$/, ''),
  isPilot: true,
  appId: 'com.casadababa.piloto',
  version: version.version,
  pilotVersion: version.version,
  versionCode: version.versionCode,
  updateManifestUrl: 'https://easystok.online/downloads/casa-da-baba/latest.json',
  updatePageUrl: 'https://easystok.online/downloads/casa-da-baba/'
};
fs.writeFileSync(path.join(target, 'config.js'), 'window.CDB_CONFIG = ' + JSON.stringify(config) + ';\n');
const htmlPath = path.join(target, 'index.html');
let html = fs.readFileSync(htmlPath, 'utf8');
const syncScript = /<script\s+src=["']sync\.js[^"']*["']\s*><\/script>/i;
if (!syncScript.test(html)) throw new Error('Ponto de carga sync.js não encontrado.');
html = html.replace(/<script\s+src=["']config\.js[^"']*["']\s*><\/script>\s*/gi, '');
html = html.replace(syncScript, '<script src="config.js"></script>\n$&');
html = html.replace('</head>', '<link rel="stylesheet" href="pilot.css">\n</head>');
html = html.replace('</body>', '<script src="pilot.js"></script>\n</body>');
html = html.replaceAll('__APP_VERSION__', 'piloto-' + version.version)
  .replaceAll('"__APP_RELEASE_NOTES__"', JSON.stringify('Piloto integrado ao EasyStok'));
fs.writeFileSync(htmlPath, html);
fs.rmSync(path.join(target, 'sw.js'), { force: true });
console.log('PWA empacotada: piloto ' + version.version);
