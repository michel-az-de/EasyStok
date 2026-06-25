#!/bin/bash
# Build APK Casa da Baba release-assinado via WSL.
#
# Pre-requisitos no WSL Ubuntu:
#   - Java 17 (`apt install -y openjdk-17-jdk`)
#   - Android SDK em ~/android-sdk (instalado por `setup-wsl.sh`)
#   - Node sera instalado pelo script se faltar
#
# Keystore (gerado uma vez, fora do repo):
#   /mnt/c/rep/keystores/casadababa-release.jks
#   /mnt/c/rep/keystores/casadababa-release.README.txt  (linha "Senha ...: <pass>")
#
# Outputs:
#   /mnt/c/rep/keystores/builds/casadababa-release-<timestamp>.apk
#
# Variaveis de ambiente opcionais:
#   CDB_KEYSTORE      path do .jks (default: /mnt/c/rep/keystores/casadababa-release.jks)
#   CDB_KS_README     path do README com senha (default: ao lado do .jks)
#   CDB_API_BASE_URL  URL da API EasyStok (default: https://api.20.230.185.203.sslip.io = VM Azure canonica)
#   CDB_OUT_DIR       pasta destino do APK (default: /mnt/c/rep/keystores/builds)
#   PAIRING_CODE      opcional: codigo de 6 digitos pra auto-pair no primeiro boot
#
# Uso:
#   wsl bash -lc "/mnt/c/rep/EasyStok/casa-da-baba-mobile/apk/build-release-wsl.sh"
#   # ou com pre-pair:
#   wsl bash -lc "PAIRING_CODE=123456 /mnt/c/rep/EasyStok/casa-da-baba-mobile/apk/build-release-wsl.sh"

set -euo pipefail

# ---- Paths --------------------------------------------------------------
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# casa-da-baba-mobile/apk/build-release-wsl.sh -> repo root e dois niveis acima
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PWA_SRC="$REPO_ROOT/EasyStock.Api/wwwroot/pwa"
SRC="$SCRIPT_DIR"
DST="$HOME/apk-build-release"

CDB_KEYSTORE="${CDB_KEYSTORE:-/mnt/c/rep/keystores/casadababa-release.jks}"
CDB_KS_README="${CDB_KS_README:-/mnt/c/rep/keystores/casadababa-release.README.txt}"
CDB_API_BASE_URL="${CDB_API_BASE_URL:-https://api.20.230.185.203.sslip.io}"
CDB_OUT_DIR="${CDB_OUT_DIR:-/mnt/c/rep/keystores/builds}"
KS_ALIAS="${CDB_KS_ALIAS:-casadababa}"

export ANDROID_HOME="$HOME/android-sdk"
export PATH="$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/platform-tools:$ANDROID_HOME/build-tools/35.0.0:$PATH"

mkdir -p "$CDB_OUT_DIR"

# ---- Pre-flight ---------------------------------------------------------
[ -d "$PWA_SRC" ]      || { echo "PWA fonte nao existe: $PWA_SRC"; exit 1; }
[ -f "$CDB_KEYSTORE" ] || { echo "Keystore nao existe: $CDB_KEYSTORE"; exit 1; }
[ -f "$CDB_KS_README" ] || { echo "README do keystore nao existe: $CDB_KS_README"; exit 1; }

KS_PASS=$(grep -oP "Senha .*: \K.+" "$CDB_KS_README" | tr -d '\n\r')
[ -n "$KS_PASS" ] || { echo "Senha vazia no README"; exit 1; }

echo "==> Java + Android SDK..."
java -version 2>&1 | head -1
ls "$ANDROID_HOME/platforms/" 2>&1 | head -3

if ! command -v node >/dev/null 2>&1; then
  echo "==> Instalando Node via apt..."
  apt-get update -qq
  DEBIAN_FRONTEND=noninteractive apt-get install -y -qq nodejs npm >/dev/null
fi
echo "node: $(node --version)  npm: $(npm --version)"

# ---- Copia projeto pra Linux fs (rsync sem node_modules) ----------------
echo "==> Copiando projeto pra $DST (rsync)..."
mkdir -p "$DST"
rsync -a --delete \
  --exclude="android/.gradle" \
  --exclude="android/build" \
  --exclude="android/app/build" \
  --exclude="android/capacitor-cordova-android-plugins/build" \
  --exclude="node_modules" \
  --exclude=".gradle" \
  --exclude="web" \
  "$SRC/" "$DST/"

# ---- npm install --------------------------------------------------------
echo "==> npm install (Capacitor + plugins)..."
cd "$DST"
npm install --no-audit --no-fund --silent 2>&1 | tail -5 || true
[ -d "$DST/node_modules/@capacitor/cli" ] || { echo "npm install falhou"; exit 1; }

# ---- Copia PWA -> web/ + gera config.js ---------------------------------
echo "==> Copiando PWA + gerando config.js (apiBaseUrl=$CDB_API_BASE_URL)..."
rm -rf "$DST/web"
mkdir -p "$DST/web"
rsync -a "$PWA_SRC/" "$DST/web/"

# config.js: window.CDB_CONFIG carrega antes do sync.js
if [ -n "${PAIRING_CODE:-}" ]; then
  echo "::add-mask::$PAIRING_CODE" 2>/dev/null || true
  cat > "$DST/web/config.js" <<EOF
window.CDB_CONFIG = {"apiBaseUrl":"$CDB_API_BASE_URL","forcedPairingCode":"$PAIRING_CODE","forcedPairingLabel":"Casa da Baba (auto-pair)"};
EOF
  echo "==> forcedPairingCode injetado (length=${#PAIRING_CODE})"
else
  cat > "$DST/web/config.js" <<EOF
window.CDB_CONFIG = {"apiBaseUrl":"$CDB_API_BASE_URL"};
EOF
fi

if grep -q '<script src="sync.js"></script>' "$DST/web/index.html" && \
   ! grep -q 'config.js' "$DST/web/index.html"; then
  sed -i 's|<script src="sync.js"></script>|<script src="config.js"></script>\n<script src="sync.js"></script>|' "$DST/web/index.html"
fi

STAMP_VER="apk-$(date +%Y%m%d-%H%M%S)"
sed -i "s|__APP_VERSION__|$STAMP_VER|g" "$DST/web/index.html"
sed -i 's|"__APP_RELEASE_NOTES__"|"Bundle empacotado no APK"|g' "$DST/web/index.html"
# SW nao faz sentido em WebView local (Capacitor)
rm -f "$DST/web/sw.js"

# ---- Capacitor sync (gera capacitor-cordova-android-plugins/) -----------
echo "==> npx cap sync android..."
npx cap sync android 2>&1 | tail -10

# ---- Sincroniza web/ -> assets/public/ ----------------------------------
echo "==> Sincronizando web/ -> android/app/src/main/assets/public/..."
ASSETS_PUB="$DST/android/app/src/main/assets/public"
mkdir -p "$ASSETS_PUB"
cp -f "$DST/web/index.html"     "$ASSETS_PUB/index.html"
cp -f "$DST/web/sync.js"        "$ASSETS_PUB/sync.js"
cp -f "$DST/web/manifest.json"  "$ASSETS_PUB/manifest.json"
[ -f "$DST/web/qrcode.min.js" ] && cp -f "$DST/web/qrcode.min.js" "$ASSETS_PUB/qrcode.min.js"
[ -f "$DST/web/config.js" ]     && cp -f "$DST/web/config.js"     "$ASSETS_PUB/config.js"
[ -d "$DST/web/icons" ]         && rsync -a "$DST/web/icons/" "$ASSETS_PUB/icons/"

WEB_HASH=$(sha256sum "$DST/web/index.html" | cut -d' ' -f1)
PUB_HASH=$(sha256sum "$ASSETS_PUB/index.html" | cut -d' ' -f1)
[ "$WEB_HASH" = "$PUB_HASH" ] || { echo "ERRO: index.html web/ vs public/ divergem"; exit 1; }
echo "==> OK web sha256 $WEB_HASH"

# ---- Gradle assembleRelease com signing ---------------------------------
cd "$DST/android"
sed -i 's/\r$//' gradlew    # gradlew vem com CRLF do Windows
chmod +x ./gradlew

cat > gradle.properties <<'EOF'
org.gradle.jvmargs=-Xmx2048m
android.useAndroidX=true
android.suppressUnsupportedCompileSdk=35
EOF

echo "==> assembleRelease com signing (~3-5min)..."
./gradlew assembleRelease --no-daemon \
  -Pandroid.injected.signing.store.file="$CDB_KEYSTORE" \
  -Pandroid.injected.signing.store.password="$KS_PASS" \
  -Pandroid.injected.signing.key.alias="$KS_ALIAS" \
  -Pandroid.injected.signing.key.password="$KS_PASS" \
  2>&1 | tail -40

APK_PATH=$(find . -path "*/outputs/apk/release/*.apk" | head -1)
[ -n "$APK_PATH" ] || { echo "APK nao encontrado"; exit 1; }

echo "==> Verificando assinatura..."
"$ANDROID_HOME/build-tools/35.0.0/apksigner" verify --print-certs "$APK_PATH" 2>&1 | head -5

STAMP=$(date +%Y%m%d-%H%M%S)
OUT="$CDB_OUT_DIR/casadababa-release-$STAMP.apk"
cp "$APK_PATH" "$OUT"
SIZE=$(stat -c '%s' "$OUT")
SIZE_MB=$(awk "BEGIN{printf \"%.1f\", $SIZE/1024/1024}")
WIN_PATH=$(echo "$OUT" | sed 's|/mnt/c|C:|; s|/|\\|g')
echo ""
echo "============================================================"
echo "  APK FINAL: $OUT"
echo "  Windows: $WIN_PATH"
echo "  Tamanho: $SIZE_MB MB"
echo "============================================================"
