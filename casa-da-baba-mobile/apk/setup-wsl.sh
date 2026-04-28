#!/bin/bash
set -e
cd ~
mkdir -p android-sdk/cmdline-tools
cd android-sdk/cmdline-tools
if [ ! -d latest ]; then
  echo "Baixando cmdline-tools..."
  wget -q https://dl.google.com/android/repository/commandlinetools-linux-11076708_latest.zip -O cmdline.zip
  unzip -q cmdline.zip
  mv cmdline-tools latest
  rm cmdline.zip
fi
export ANDROID_HOME="$HOME/android-sdk"
export PATH="$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/platform-tools:$PATH"
yes | sdkmanager --licenses >/dev/null 2>&1 || true
echo "Instalando build-tools, platforms, platform-tools..."
sdkmanager "platform-tools" "platforms;android-35" "build-tools;35.0.0" 2>&1 | tail -5
echo "ANDROID_HOME=$ANDROID_HOME"
ls "$ANDROID_HOME"
