#!/bin/bash
set -e
export ANDROID_HOME="$HOME/android-sdk"
export PATH="$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/platform-tools:$PATH"

SRC="/mnt/c/rep/EasyStok/casa-da-baba-mobile/apk"
DST="$HOME/apk-build"

echo "==> Copiando projeto pra Linux fs (incluindo node_modules)..."
mkdir -p "$DST"
rsync -a --delete \
  --exclude="android/.gradle" \
  --exclude="android/build" \
  --exclude="android/app/build" \
  --exclude="android/capacitor-cordova-android-plugins/build" \
  "$SRC/" "$DST/"

# CRITICO: copia web/ -> android/.../assets/public/. Sem esse passo o
# Gradle empacota a versao antiga que ficou em assets/public — explicava
# por que mudancas visuais "nao apareciam" no APK. Equivalente a
# 'npx cap copy android' mas sem dependencia de npm/node no WSL.
# IMPORTANTE: copia SO os arquivos do app (nao 'rsync --delete' que
# apagaria capacitor.js, cordova.js, plugins/).
echo "==> Sincronizando web/ -> assets/public/ (capacitor copy manual)..."
ASSETS_PUB="$DST/android/app/src/main/assets/public"
mkdir -p "$ASSETS_PUB"
cp -f "$DST/web/index.html"     "$ASSETS_PUB/index.html"
cp -f "$DST/web/sync.js"        "$ASSETS_PUB/sync.js"
cp -f "$DST/web/manifest.json"  "$ASSETS_PUB/manifest.json"
[ -f "$DST/web/sw.js" ]          && cp -f "$DST/web/sw.js"         "$ASSETS_PUB/sw.js"
[ -f "$DST/web/qrcode.min.js" ]  && cp -f "$DST/web/qrcode.min.js" "$ASSETS_PUB/qrcode.min.js"
[ -f "$DST/web/config.js" ]      && cp -f "$DST/web/config.js"     "$ASSETS_PUB/config.js"
[ -d "$DST/web/icons" ]          && rsync -a "$DST/web/icons/" "$ASSETS_PUB/icons/"
ls -la "$ASSETS_PUB/index.html"

# ASSERCAO #1: garante que web/ e assets/public/ tem o MESMO index.html
# byte a byte. Se falhar, build aborta antes do gradle — evita gastar
# tempo empacotando uma versao errada.
WEB_HASH=$(sha256sum "$DST/web/index.html" | cut -d' ' -f1)
PUB_HASH=$(sha256sum "$ASSETS_PUB/index.html" | cut -d' ' -f1)
if [ "$WEB_HASH" != "$PUB_HASH" ]; then
  echo ""
  echo "==> ERRO: web/index.html != assets/public/index.html"
  echo "    web/    sha256: $WEB_HASH"
  echo "    public/ sha256: $PUB_HASH"
  echo "    Cheque se a copia acima rodou. Build abortado."
  exit 1
fi
echo "==> OK: web/ e assets/public/ sincronizados (sha256 $WEB_HASH)."

# ASSERCAO #1.5: roda testes unitarios da PWA antes de empacotar.
# Falha = build aborta. Cobre logica de pedido, caixa, producao e
# conferencia + helpers de error log e scan mode.
# WSL geralmente nao tem node instalado, mas o PATH do Windows fica
# montado em /mnt/c/... — busca node.exe nas localizacoes comuns.
NODE_BIN=""
if command -v node >/dev/null 2>&1; then
  NODE_BIN="node"
else
  for cand in \
    "/mnt/c/Program Files/nodejs/node.exe" \
    "/mnt/c/Program Files (x86)/nodejs/node.exe" \
    "/mnt/c/Users/$USER/AppData/Roaming/nvm/node.exe" \
    "$(command -v node.exe 2>/dev/null)"; do
    if [ -n "$cand" ] && [ -x "$cand" ]; then NODE_BIN="$cand"; break; fi
  done
fi
if [ -n "$NODE_BIN" ]; then
  echo ""
  echo "==> Rodando testes unitarios (node: $NODE_BIN)..."
  # Se node e o Windows node.exe, paths Linux precisam ser convertidos
  # pra Windows. Detecta pelo .exe no nome.
  if [[ "$NODE_BIN" == *.exe ]] && command -v wslpath >/dev/null 2>&1; then
    TEST_RUN="$(wslpath -w "$DST/tests/run.js")"
    PWA_FILE="$(wslpath -w "$DST/web/index.html")"
  else
    TEST_RUN="$DST/tests/run.js"
    PWA_FILE="$DST/web/index.html"
  fi
  if PWA_HTML="$PWA_FILE" "$NODE_BIN" "$TEST_RUN"; then
    echo "==> OK: testes unitarios passaram."
  else
    echo ""
    echo "==> ERRO: testes unitarios FALHARAM. Build abortado."
    echo "    Corrija os testes em casa-da-baba-mobile/apk/tests/ ou a logica"
    echo "    em wwwroot/pwa/index.html antes de gerar o APK."
    exit 1
  fi
else
  echo "==> AVISO: node nao encontrado (WSL ou Windows) — testes pulados."
  echo "    Instale Node 18+ ou rode 'sudo apt install nodejs' no WSL."
fi

cd "$DST/android"
chmod +x ./gradlew

# gradle.properties limpo (sem flags experimentais Windows)
cat > gradle.properties <<'EOF'
org.gradle.jvmargs=-Xmx2048m
android.useAndroidX=true
android.suppressUnsupportedCompileSdk=35
EOF

echo "==> Iniciando build..."
./gradlew assembleDebug 2>&1 | tail -80
echo ""
echo "==> APK final:"
APK_PATH=$(find . -path "*/outputs/apk/debug/*.apk" | head -1)
ls -la "$APK_PATH"

# ASSERCAO #2: extrai index.html DO APK e compara byte-a-byte com a fonte.
# Se diverge, o APK saiu errado mesmo com a copia OK — algo no build do
# gradle empacotou outro arquivo. Falha loud antes de instalar no celular.
echo ""
echo "==> Validando index.html dentro do APK..."
APK_HASH=$(unzip -p "$APK_PATH" assets/public/index.html | sha256sum | cut -d' ' -f1)
SRC_HASH=$(sha256sum "$DST/web/index.html" | cut -d' ' -f1)
if [ "$APK_HASH" != "$SRC_HASH" ]; then
  echo ""
  echo "==> ERRO: index.html dentro do APK NAO bate com a fonte."
  echo "    APK    sha256: $APK_HASH"
  echo "    fonte  sha256: $SRC_HASH"
  echo "    O APK saiu errado. Nao instale."
  exit 1
fi
echo "==> OK: APK contem o index.html correto (sha256 $APK_HASH)."
