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
  let html = fs.readFileSync(htmlPath, 'utf8');
  // Idempotente: se config.js ja referenciado, nao mexe (build incremental).
  if (/<script[^>]+src=["']config\.js/i.test(html)) {
    console.log('[copy-web] config.js ja injetado (idempotente)');
    return;
  }
  // Match flexivel: aceita cache-buster `?v=...` que o workflow CI injeta
  // (rewrite-cache-version.ps1) — antes a regex era literal e quebrava
  // silenciosamente, deixando o config.js orfao no APK.
  const re = /<script\s+src=["']sync\.js[^"']*["']\s*><\/script>/i;
  if (!re.test(html)) {
    console.error('[copy-web] ERRO: nao encontrei <script src="sync.js..."> em index.html');
    console.error('[copy-web]   config.js NAO sera carregado pelo HTML - build invalido.');
    process.exit(1);
  }
  html = html.replace(re, '<script src="config.js"></script>\n  $&');
  fs.writeFileSync(htmlPath, html, 'utf8');
  console.log('[copy-web] injetou <script src="config.js"> antes de sync.js');
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
  // Opcionalmente injeta forcedPairingCode/forcedPairingLabel (lido de env vars
  // PAIRING_CODE/PAIRING_LABEL) — usado pelo workflow build-casadababa-release
  // pra que o APK pareie sozinho no primeiro boot, sem operador digitar codigo.
  // Em build normal (sem env vars) o objeto fica vazio nesse campo e o sync.js
  // ignora — comportamento idêntico ao da PWA producao.
  const configPath = path.join(DST, 'config.js');
  const cfg = {
    apiBaseUrl: apiBaseUrl
  };
  if (process.env.PAIRING_CODE) {
    cfg.forcedPairingCode = String(process.env.PAIRING_CODE).trim();
    cfg.forcedPairingLabel = String(process.env.PAIRING_LABEL || 'Casa da Baba (auto-pair)');
    console.log('[copy-web] forcedPairingCode injetado (length=' + cfg.forcedPairingCode.length + ')');
  }
  // PROVISIONING_SECRET: token de longa vida bate com AppProvisioning:Secret
  // no server (3 env vars no server setam empresa/loja default). APK pareia
  // sozinho via POST /api/mobile/devices/pair-auto, sem expirar como o pairing
  // code de 6 digitos. Recomendado pra APK distribuido pre-configurado.
  if (process.env.PROVISIONING_SECRET) {
    cfg.forcedProvisioningSecret = String(process.env.PROVISIONING_SECRET).trim();
    cfg.forcedProvisioningLabel = String(process.env.PROVISIONING_LABEL || 'Casa da Baba (provisioned)');
    console.log('[copy-web] forcedProvisioningSecret injetado (length=' + cfg.forcedProvisioningSecret.length + ')');
  }
  fs.writeFileSync(configPath, 'window.CDB_CONFIG = ' + JSON.stringify(cfg) + ';\n', 'utf8');
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
