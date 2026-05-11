# Casa da Baba — APK Android (via Capacitor)

Wrapper Capacitor que empacota o PWA (`EasyStock.Api/wwwroot/pwa/`) como APK
nativo Android. Permite instalar no celular do Felipe sem passar por "Add to
Home Screen" (embora isso também funcione — veja **Alternativa simples** no
fim).

## Estado atual

- ✅ Projeto Capacitor configurado (`package.json`, `capacitor.config.json`).
- ✅ Platform Android adicionada (`android/` gerado).
- ✅ **Build via WSL funciona** na máquina Avanade — ver [BUILD-WSL.md](BUILD-WSL.md).
- ⚠️  Build nativo Windows corporativo (Avanade) falha com
    `java.io.IOException: Unable to establish loopback connection` — firewall
    corporativo bloqueia sockets Unix-domain do Gradle. **Solução: WSL** (abaixo).
- ⚠️  GitHub Actions suspenso por billing — não usar até destravar.

## Formas de gerar APK

### 1) WSL — Windows Subsystem for Linux (recomendado na máquina Avanade)

**Ver guia completo: [BUILD-WSL.md](BUILD-WSL.md)**

Resumo rápido (depois do setup inicial):
```powershell
# No PowerShell, pasta apk/:
node scripts/copy-web.js "https://easystok-api.onrender.com"
wsl bash /mnt/c/rep/EasyStok/casa-da-baba-mobile/apk/build-wsl.sh
# APK em: \\wsl$\Ubuntu\home\<user>\apk-build\android\app\build\outputs\apk\debug\app-debug.apk
```

### 2) GitHub Actions (quando billing for destravado)

Workflow `.github/workflows/build-casadababa-release.yml` — disparo manual:
1. **Actions** → **Build Casa da Baba APK (release + auto-pair)** → **Run workflow**.
2. Informe `apiBaseUrl` (padrão: `https://easystok-api.onrender.com`).
3. (Opcional) `pairingCode` de 6 dígitos do Admin → Dispositivos.
4. Baixe o APK em **Artifacts**.

### 3) Build local (máquina pessoal / Linux / Mac sem firewall corporativo)

Requisitos:
- **JDK 21** (ou 17; 21 recomendado)
- **Node.js 18+**
- **Android SDK** com `platforms;android-34`, `build-tools;34.0.0`,
  `platform-tools`. Via `sdkmanager` ou Android Studio.

Variáveis de ambiente:
```
JAVA_HOME=<path-do-jdk>
ANDROID_HOME=<path-do-sdk>
ANDROID_SDK_ROOT=<path-do-sdk>
PATH=%JAVA_HOME%/bin;%PATH%
```

Comandos:
```bash
cd casa-da-baba-mobile/apk
npm install
node scripts/copy-web.js ""            # vazio = mesmo host; ou passe a URL do backend
npx cap sync android
cd android
./gradlew assembleDebug --no-daemon    # Windows: .\gradlew.bat assembleDebug
# APK gerado em: android/app/build/outputs/apk/debug/app-debug.apk
```

### 3) PWABuilder.com (via URL HTTPS pública)

Se o EasyStock estiver deployado em HTTPS público:

1. Abra https://www.pwabuilder.com/
2. Cole a URL do PWA (ex.: `https://easystock.exemplo.com/pwa/`)
3. Clique em **Package for Stores** → **Android**
4. Baixe o APK (ou AAB) gerado.

Não funciona com `localhost` ou IP local.

## Configuração da URL da API dentro do APK

O APK roda num WebView local (offline-first). Precisa saber qual é a URL do
backend EasyStock para sincronizar.

O script `scripts/copy-web.js` aceita um argumento:
```bash
node scripts/copy-web.js "http://192.168.0.10:5280"   # IP local da máquina do EasyStock
node scripts/copy-web.js "https://easystock.exemplo.com"  # produção
node scripts/copy-web.js ""                            # vazio = mesmo host que serve o PWA
```

Ele gera `web/config.js` com `window.CDB_CONFIG = { apiBaseUrl: '...' }` que
o `sync.js` lê na inicialização. Para mudar a URL depois do APK gerado,
precisa rebuildar.

## Estrutura

```
apk/
├── package.json              # Dependências Capacitor
├── capacitor.config.json     # Config do wrapper (appId, appName, webDir)
├── scripts/
│   └── copy-web.js           # Copia PWA de ../EasyStock.Api/wwwroot/pwa → ./web
├── web/                      # Arquivos copiados do PWA (gerado, não versionado)
└── android/                  # Projeto Android nativo (gerado por `cap add android`)
    └── app/build/outputs/apk/debug/app-debug.apk   # Saída do build
```

## Alternativa simples: "Add to Home Screen"

Se você não precisa de um APK empacotado real (só instalação no celular), a
forma mais rápida é:

1. Rodar o EasyStock na sua rede local.
2. No Chrome Android: abrir `http://<ip-do-pc>:5280/pwa/` → menu 3 pontos
   → **Adicionar à tela inicial**.
3. No iOS Safari: botão compartilhar → **Adicionar à Tela de Início**.

O app abre em fullscreen, aparece na home do celular com o ícone, funciona
offline (service worker cacheia tudo). A experiência é ~95% igual à de um
APK; a única diferença é não poder publicar na Play Store.

## Limitações conhecidas

- **Sem HTTPS em localhost**: o service worker só registra em HTTPS ou
  `localhost`. Quando o Felipe acessa por IP local via HTTP, o SW não roda —
  mas o APK do Capacitor não usa SW (tudo já está empacotado dentro do APK),
  então não é problema para a versão APK.
- **API key desabilitada**: atualmente `SyncController` está com
  `[AllowAnonymous]`. Reativar quando o sistema estiver em produção exposta.
  Ver `EasyStock.Api/Mobile/Security/MobileApiKeyAttribute.cs`.
- **Atualização do app**: para atualizar o APK depois, rebuildar e
  desinstalar/reinstalar no celular. A fila de sync do `localStorage` não é
  perdida (persiste por chave de app no Android).
