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
find . -path "*/outputs/apk/debug/*.apk" -exec ls -la {} \;
