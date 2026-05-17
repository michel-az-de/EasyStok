# Build APK Casa da Babá via WSL

**TL;DR — Por que WSL?**  
Build local no Windows corporativo (Avanade) falha com
`java.io.IOException: Unable to establish loopback connection` — firewall/antivírus
bloqueia os sockets Unix-domain do Gradle.  
**WSL (Windows Subsystem for Linux) contorna o problema sem sair da máquina.**  
GitHub Actions (alternativa) está suspenso por billing. Machine pessoal funciona mas
não é necessária.

---

## Pré-requisitos (uma vez por máquina)

1. **WSL 2 + Ubuntu** instalado (`wsl --install` no PowerShell como admin, reiniciar).
2. **JDK 21 no WSL** — o `setup-wsl.sh` cuida do Android SDK, mas JDK precisa ser instalado
   manualmente:
   ```bash
   sudo apt update && sudo apt install -y openjdk-21-jdk
   java -version   # deve mostrar 21
   ```
3. **Node.js no Windows** (não no WSL) — para o `copy-web.js`. Baixar em nodejs.org ou via
   `nvm`. Confirmar: `node --version` no PowerShell.

---

## Passo 1 — Instalar Android SDK (1x por máquina)

No **PowerShell** (repo raiz):
```powershell
wsl bash /mnt/c/rep/EasyStok/casa-da-baba-mobile/apk/setup-wsl.sh
```

Isso baixa `cmdline-tools` e instala `platforms;android-35`, `build-tools;35.0.0`,
`platform-tools` em `~/android-sdk` dentro do WSL. Leva ~5 min na primeira vez.

---

## Passo 2 — Copiar PWA e injetar URL da API (cada build)

No **PowerShell** (pasta `casa-da-baba-mobile/apk`):
```powershell
cd C:\rep\EasyStok\casa-da-baba-mobile\apk
node scripts/copy-web.js "https://easystok-api.onrender.com"
```

Isso copia `EasyStock.Api/wwwroot/pwa/` → `apk/web/`, gera `web/config.js` com
`window.CDB_CONFIG = { apiBaseUrl: "https://easystok-api.onrender.com" }` e remove
`sw.js` (não funciona em WebView do APK).

Para ambiente local (rede interna), substitua pela URL do servidor:
```powershell
node scripts/copy-web.js "http://192.168.0.10:5280"
```

---

## Passo 3 — Build via WSL (cada build)

No **PowerShell** (qualquer pasta):
```powershell
wsl bash /mnt/c/rep/EasyStok/casa-da-baba-mobile/apk/build-wsl.sh
```

O script faz:
1. `rsync` do projeto pra `~/apk-build` (Linux fs — mais rápido que NTFS)
2. Copia `web/` → `android/.../assets/public/`
3. **Asserção #1**: sha256 de `web/index.html` == `assets/public/index.html`
4. Roda testes unitários da PWA (se Node estiver disponível)
5. `./gradlew assembleDebug` com assinatura `cdb-debug.keystore` (versionado)
6. **Asserção #2**: sha256 do `index.html` dentro do APK == fonte — valida que Gradle
   empacotou a versão correta

Saída esperada ao final:
```
==> OK: APK contem o index.html correto (sha256 abc123...).
==> APK final:
-rw-r--r-- 1 user user 8.2M May 11 ~/apk-build/android/app/build/outputs/apk/debug/app-debug.apk
```

---

## Passo 4 — Copiar APK para Windows

```bash
# Dentro do WSL (ou via PowerShell com o caminho UNC):
cp ~/apk-build/android/app/build/outputs/apk/debug/app-debug.apk /mnt/c/temp/casa-da-baba-debug.apk
```

Ou abrir direto pelo Explorer: `\\wsl$\Ubuntu\home\<user>\apk-build\android\app\build\outputs\apk\debug\`

---

## Instalar no celular Android

1. Copiar `casa-da-baba-debug.apk` pro celular (e-mail, Telegram, USB ou link compartilhado).
2. Abrir o arquivo no celular → Android vai pedir pra **habilitar "Instalar apps de origens desconhecidas"**.
3. O Play Protect pode mostrar aviso ("app não reconhecida") — tocar **"Instalar mesmo assim"**.
4. App instala como **"Casa da Baba"** com ícone próprio.

---

## Pareamento (primeira abertura)

1. No painel admin **EasyStock Admin** (https://easystok-admin.onrender.com) → **Dispositivos** → **Gerar código de pareamento**.
2. No APK → tela de **Diagnóstico** → **Parear dispositivo** → digitar os 6 dígitos.
3. App fica vinculado; sync começa automaticamente.

---

## Troubleshooting

| Sintoma | Causa provável | Solução |
|---------|----------------|---------|
| `JAVA_HOME not set` no WSL | JDK não instalado | `sudo apt install openjdk-21-jdk` |
| `sha256 mismatch` no build | `web/` desatualizado | Rodar Passo 2 de novo |
| `API_BASE_URL vazia` no APK | `copy-web.js` rodou sem URL | `node scripts/copy-web.js "https://..."` |
| Play Protect bloqueia instalação | APK debug-signed | Aceitar "Instalar mesmo assim" |
| Sync retorna 401 no APK | `Mobile__RequireApiKey=true` e APK sem pareamento | Fazer pareamento (passo acima) |
| CORS error no APK | Origin não autorizada | Confirmar que `Cors__AllowedOrigins` inclui `https://localhost` |
