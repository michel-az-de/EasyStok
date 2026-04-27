// Copia o PWA (wwwroot/pwa) para apk/web/ e injeta config.js
// apontando para a URL do backend que o APK vai consumir.
//
// Uso: node scripts/copy-web.js [apiBaseUrl]
//   Ex.: node scripts/copy-web.js http://192.168.1.10:5280
//   Padrão: vazio = mesmo host que serve o PWA (útil se empacotar com backend local).
//
// O APK do Felipe vai bater num servidor EasyStock real — então é obrigatório
// passar a URL como argumento (ou editar API_BASE_URL no web/config.js).

'use strict';

const fs = require('fs');
const path = require('path');

const SRC = path.resolve(__dirname, '..', '..', '..', 'EasyStock.Api', 'wwwroot', 'pwa');
const DST = path.resolve(__dirname, '..', 'web');

const apiBaseUrl = (process.argv[2] || '').replace(/\/+$/, ''); // strip trailing slash

function rm(p) {
  if (!fs.existsSync(p)) return;
  const stat = fs.statSync(p);
  if (stat.isDirectory()) {
    for (const entry of fs.readdirSync(p)) rm(path.join(p, entry));
    fs.rmdirSync(p);
  } else {
    fs.unlinkSync(p);
  }
}

function copy(src, dst) {
  const stat = fs.statSync(src);
  if (stat.isDirectory()) {
    if (!fs.existsSync(dst)) fs.mkdirSync(dst, { recursive: true });
    for (const entry of fs.readdirSync(src)) {
      copy(path.join(src, entry), path.join(dst, entry));
    }
  } else {
    fs.copyFileSync(src, dst);
  }
}

function injectConfigBeforeSyncJs(htmlPath) {
  const marker = '<script src="sync.js"></script>';
  const inject = '<script src="config.js"></script>\n' + marker;
  let html = fs.readFileSync(htmlPath, 'utf8');
  if (html.includes(marker) && !html.includes('config.js')) {
    html = html.replace(marker, inject);
    fs.writeFileSync(htmlPath, html, 'utf8');
    console.log('[copy-web] injetou <script src="config.js"> antes de sync.js');
  }
}

function stampVersionInHtml(htmlPath) {
  // Bundle embutido no APK: marca como "apk-X". Quando o LiveUpdater entregar
  // um bundle novo do SWA, o sed do workflow substitui pra "1.0.X" — assim
  // da pra distinguir visualmente bundle empacotado vs bundle baixado.
  // Tambem substitui o placeholder de release notes pra manter JS valido.
  let html = fs.readFileSync(htmlPath, 'utf8');
  let changed = false;
  if (html.includes('__APP_VERSION__')) {
    const stamp = process.env.BUILD_VERSION_CODE
      ? `apk-${process.env.BUILD_VERSION_CODE}`
      : 'apk-local';
    html = html.replace(/__APP_VERSION__/g, stamp);
    console.log('[copy-web] carimbou versao no index.html:', stamp);
    changed = true;
  }
  if (html.includes('"__APP_RELEASE_NOTES__"')) {
    html = html.replace(/"__APP_RELEASE_NOTES__"/g, JSON.stringify('Bundle empacotado no APK'));
    changed = true;
  }
  if (changed) fs.writeFileSync(htmlPath, html, 'utf8');
}

function main() {
  if (!fs.existsSync(SRC)) {
    console.error('[copy-web] Fonte do PWA não encontrada:', SRC);
    process.exit(1);
  }

  console.log('[copy-web] Limpando', DST);
  rm(DST);
  fs.mkdirSync(DST, { recursive: true });

  console.log('[copy-web] Copiando', SRC, '→', DST);
  copy(SRC, DST);

  // config.js: define window.CDB_CONFIG.apiBaseUrl antes do sync.js carregar.
  const configPath = path.join(DST, 'config.js');
  const cfg = `window.CDB_CONFIG = { apiBaseUrl: ${JSON.stringify(apiBaseUrl)} };\n`;
  fs.writeFileSync(configPath, cfg, 'utf8');
  console.log('[copy-web] Gerou', configPath, `(apiBaseUrl=${apiBaseUrl || '""'})`);

  injectConfigBeforeSyncJs(path.join(DST, 'index.html'));
  stampVersionInHtml(path.join(DST, 'index.html'));

  // Service worker não faz sentido dentro do APK (Capacitor usa WebView local,
  // sem HTTPS). Removemos o arquivo e a registração acontece apenas via browser.
  const swPath = path.join(DST, 'sw.js');
  if (fs.existsSync(swPath)) {
    fs.unlinkSync(swPath);
    console.log('[copy-web] Removeu sw.js (não faz sentido dentro do APK)');
  }

  console.log('[copy-web] OK — próximo passo: npm run sync && npm run build-debug');
}

main();
