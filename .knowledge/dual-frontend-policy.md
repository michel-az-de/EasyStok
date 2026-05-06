# Dual Frontend Policy — PWA + MAUI

> Documento canônico. Toda iteração futura que mexer em UI/fluxo do app cliente
> deve seguir esta política. Atualize aqui se a estratégia mudar.
>
> Última revisão: 2026-05-06.

## Por que existem dois frontends

Hoje o EasyStock tem **dois frontends de cliente em produção** que vão coexistir
durante a fase de maturação do MAUI:

| Frontend | Stack | Caso de uso primário | Status |
|---|---|---|---|
| **PWA** | HTML/CSS/JS vanilla + Service Worker | Casa da Babá (uso real, white-label, Felipe + Thati) | Estável, em produção |
| **MAUI Android** | .NET 9 MAUI híbrido (shell nativo + WebView com PWA empacotado) | Produto SaaS multi-tenant E2E que será apresentado a clientes externos | Em maturação (v1.3.1) |

A decisão estratégica é: **manter o PWA estável** enquanto o MAUI ganha
maturidade como produto SaaS. Quando o MAUI estiver provado em campo, podemos
reavaliar deprecar o PWA. Até lá, os dois andam juntos.

## A regra de ouro

```
PWA  ───merge──▶  MAUI
PWA  ◀──NUNCA──   MAUI
```

- Mudança no PWA (`EasyStock.Api/wwwroot/pwa/`) **deve** ser propagada pra cópia
  empacotada do MAUI (`EasyStok.Mobile/Resources/Raw/pwa/`) na mesma demanda.
- Mudança nativa no MAUI (ViewModels, Views XAML, Services C#, AppShell, etc.)
  **não** volta pro PWA. O PWA é a fonte da verdade da experiência web; o MAUI é
  superset que adiciona shell nativo, auth, outbox SQLite e sync por cima.

Quem viola essa direção quebra um dos dois apps em produção. Não tem zona cinza.

## Onde está cada coisa

### PWA (fonte da verdade da UI)

```
EasyStock.Api/wwwroot/pwa/
├── index.html               ← single-page app (~720 KB)
├── sw.js                    ← service worker (cache offline)
├── sync.js                  ← fila de sync com backend
├── manifest.json            ← PWA manifest
├── qrcode.min.js
├── staticwebapp.config.json
└── icons/
    ├── favicon.png
    ├── icon-192.png
    ├── icon-512.png
    └── icon-maskable-512.png
```

Servido pela API ASP.NET em `/pwa/` (configurado no `MobileModule.UseMobileModule`).

Também é a base do projeto **`casa-da-baba-mobile/`** — esse projeto **não tem
mais cópia própria do PWA** (foi removida no commit `39a0e39 chore(repo): remove
copia orfa do PWA + atualiza docs apontando pra fonte real`). Quando precisar
replicar o PWA pra outro contexto Casa da Babá, copie de
`EasyStock.Api/wwwroot/pwa/`.

### MAUI (shell nativo + PWA empacotado)

```
EasyStok.Mobile/
├── EasyStok.Mobile.csproj
├── MauiProgram.cs               ← DI + WebViewHandler.Mapper (configura WebView)
├── App.xaml(.cs) / AppShell.xaml(.cs)
├── Views/                       ← páginas nativas (Login, TenantPicker, Producao, ...)
├── ViewModels/                  ← MVVM (CommunityToolkit.Mvvm)
├── Services/                    ← Auth, Sync, Outbox, EstoqueCache, Theme, ...
├── Storage/                     ← SQLite local + SecureStore
├── Network/                     ← AutenticacaoHandler (HTTP)
├── Platforms/Android/           ← MainActivity, MainApplication
└── Resources/Raw/pwa/           ← ⚠️ CÓPIA do PWA empacotada no APK
    ├── index.html
    ├── sw.js
    ├── sync.js
    ├── manifest.json
    ├── qrcode.min.js
    ├── staticwebapp.config.json
    └── icons/...
```

A página `WebOpsPage` hospeda essa cópia local via WebView (Android). Login,
configurações e câmera permanecem nativos MAUI; a operação (estoque, pedidos,
caixa, produção, etc.) é o PWA empacotado.

Em 2026-05-06 as duas cópias estão **byte-idênticas** (verificado por SHA-256).

## Procedimento obrigatório quando mexer no PWA

Toda demanda que altere qualquer arquivo em `EasyStock.Api/wwwroot/pwa/` precisa,
no **mesmo commit**:

1. Aplicar a mudança em `EasyStock.Api/wwwroot/pwa/<arquivo>`.
2. Copiar o(s) mesmo(s) arquivo(s) pra `EasyStok.Mobile/Resources/Raw/pwa/`.
3. Verificar igualdade byte-a-byte (comando abaixo).
4. Bumpar versão do MAUI no `EasyStok.Mobile.csproj`
   (`ApplicationVersion` +1 e `ApplicationDisplayVersion` patch +1) somente se
   a mudança altera comportamento do app empacotado. Ajuste de copy/css trivial
   pode dispensar bump — usar bom senso.
5. Mensagem de commit deve mencionar que a propagação foi feita. Padrão:
   `feat(pwa): X + propagado pro MAUI` ou `fix(pwa): Y + sync MAUI bundle`.

### Comando único pra propagar

PowerShell (ambiente do Felipe):

```powershell
Copy-Item -Path C:\rep\EasyStok\EasyStock.Api\wwwroot\pwa\* `
          -Destination C:\rep\EasyStok\EasyStok.Mobile\Resources\Raw\pwa\ `
          -Recurse -Force
```

### Verificação obrigatória pós-cópia

```powershell
$api  = 'C:\rep\EasyStok\EasyStock.Api\wwwroot\pwa'
$maui = 'C:\rep\EasyStok\EasyStok.Mobile\Resources\Raw\pwa'
$arquivos = @('index.html','sw.js','sync.js','manifest.json','qrcode.min.js','staticwebapp.config.json')
foreach ($f in $arquivos) {
    $a = (Get-FileHash "$api\$f"  -Algorithm SHA256).Hash
    $b = (Get-FileHash "$maui\$f" -Algorithm SHA256).Hash
    if ($a -ne $b) { Write-Error "DRIFT em $f"; exit 1 }
}
Write-Host 'PWA + MAUI bundle sincronizados'
```

Se o output mostrar DRIFT, **não comite**. Repita o passo 2 e investigue.

## O que NÃO se replica (MAUI → PWA)

Mudanças nas camadas abaixo ficam exclusivas do MAUI e jamais voltam pro PWA:

- Tudo em `EasyStok.Mobile/Views/*.xaml*` (telas nativas: Login, TenantPicker,
  LojaPicker, Suporte, Mais, etc.)
- Tudo em `EasyStok.Mobile/ViewModels/*`
- Tudo em `EasyStok.Mobile/Services/*` (Auth, Sync, Outbox, ThemeService,
  AppIdentity, BootCrashLog, ...)
- Tudo em `EasyStok.Mobile/Storage/*` (SQLite, SecureStore)
- Tudo em `EasyStok.Mobile/Network/*` (AutenticacaoHandler, etc.)
- `MauiProgram.cs`, `AppShell`, `App.xaml`, `Platforms/Android/*`,
  `Controls/*`, `Converters/*`, `Resources/Styles|AppIcon|Splash|Fonts/*`

Qualquer feature que precise existir nos dois lugares **começa no PWA** e segue
o procedimento da seção anterior.

## Casa da Babá

O projeto `casa-da-baba-mobile/` (PWA + backend drop-in) usa o PWA da raiz como
fonte. Não cria cópia local. Quando o PWA muda, o Casa da Babá ganha de graça
ao deployar a API. Ver `casa-da-baba-mobile/CLAUDE.md` Passo 6 e
`casa-da-baba-mobile/README.md`.

Casa da Babá é o ambiente de validação contínua do PWA: se quebrar lá, o Felipe
descobre direto. Por isso o PWA tem que ficar estável enquanto o MAUI matura.

## Checklist de PR que toca PWA

- [ ] Arquivo(s) alterado(s) em `EasyStock.Api/wwwroot/pwa/`
- [ ] Mesmo(s) arquivo(s) copiado(s) pra `EasyStok.Mobile/Resources/Raw/pwa/`
- [ ] Hash SHA-256 confere entre as duas cópias (script acima retorna OK)
- [ ] (Se aplicável) `ApplicationVersion` e `ApplicationDisplayVersion`
      bumpados em `EasyStok.Mobile/EasyStok.Mobile.csproj`
- [ ] Mensagem de commit explicita a propagação MAUI
- [ ] Smoke manual: abrir `http://localhost:5000/pwa/` e validar a mudança
- [ ] Smoke manual MAUI quando o ambiente permitir: `dotnet publish` do
      `EasyStok.Mobile`, sideload do APK, abrir `WebOpsPage` e validar a mesma
      mudança no WebView empacotado

## Checklist de PR que toca só MAUI nativo

- [ ] Nenhum arquivo em `EasyStock.Api/wwwroot/pwa/` foi tocado
- [ ] Nenhum arquivo em `EasyStok.Mobile/Resources/Raw/pwa/` foi tocado
- [ ] (Se aplicável) bump de versão no `.csproj` do MAUI

Se você encostou em `Resources/Raw/pwa/` por engano numa demanda de MAUI nativo,
reverta a mudança e refaça em cima do PWA original.

## Como detectar drift mais tarde

Rode o script de verificação (seção "Verificação obrigatória pós-cópia") em
qualquer momento. Pode virar etapa de CI eventualmente — quando virar, atualize
este doc apontando pro workflow.

## O que faz/não faz parte da política

**Faz parte:**
- Garantia de paridade visual e comportamental entre PWA standalone e PWA
  empacotado no MAUI.
- Direção única do merge (PWA → MAUI).
- Versionamento do MAUI quando o bundle muda.

**Não faz parte (são decisões separadas):**
- Quando deprecar o PWA. Isso é decisão de produto futura, depende do MAUI estar
  provado em campo e da Casa da Babá ter sido migrada (ou não).
- Estratégia de bridge JS↔Native dentro do WebView do MAUI (ver
  `WebOpsPage.xaml.cs` linha 8: "Bridge JS->Native fica para v1.3.1").
- Estratégia de OTA do APK. Já existe (`AndroidPackageFormat=apk` permite
  PackageInstaller self-hosted) mas não é objeto deste doc.

## Quando esta política deixa de valer

Quando uma destas três coisas acontecer, este doc precisa ser revisitado:

1. MAUI for considerado pronto pra substituir o PWA na Casa da Babá → PWA pode
   virar legado e a regra de propagação cai.
2. PWA for descontinuado por decisão de produto → MAUI passa a ter UI nativa
   própria sem WebView.
3. Bridge JS↔Native do MAUI exigir customização do `index.html` que não pode ir
   pro PWA standalone → fork controlado, registrar exceção aqui.

Até qualquer um desses três marcos, a regra `PWA → MAUI` é absoluta.
