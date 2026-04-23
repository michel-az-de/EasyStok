# INSTALL.md — Instalação manual

Se você não vai usar o Claude Code pra integrar, siga esse passo a passo manualmente. Dura cerca de **30-45 minutos** se o EasyStock já está rodando.

## Pré-requisitos

- EasyStock já rodando localmente com .NET 9
- Acesso ao PostgreSQL do EasyStock (`psql` ou Azure Data Studio ou pgAdmin)
- Um celular com Chrome (Android) ou Safari (iOS) na mesma rede Wi-Fi

---

## Passo 1 — Rodar a migration

Abre o terminal na pasta do projeto, pega a connection string do `appsettings.Development.json` e roda:

```bash
psql "$CONN_STRING" -f casa-da-baba-mobile/backend/Migrations/001_CreateMobileSchema.sql
```

Ou abre o `001_CreateMobileSchema.sql` no pgAdmin / Azure Data Studio e executa direto.

Confere que criou:
```sql
\dt mobile_*
```

Deve aparecer 7 tabelas.

## Passo 2 — Copiar o código C# pro projeto

Do terminal, na raiz do EasyStock:

```bash
# Ajusta "SeuProjetoApi" pro nome real do projeto que tem o Program.cs
mkdir -p SeuProjetoApi/Mobile/{Models,DTOs,Controllers}

cp casa-da-baba-mobile/backend/Models/*.cs        SeuProjetoApi/Mobile/Models/
cp casa-da-baba-mobile/backend/DTOs/*.cs          SeuProjetoApi/Mobile/DTOs/
cp casa-da-baba-mobile/backend/Controllers/*.cs   SeuProjetoApi/Mobile/Controllers/
cp casa-da-baba-mobile/backend/Mobile/*.cs        SeuProjetoApi/Mobile/
```

## Passo 3 — Ajustar namespaces

Abre cada `.cs` copiado e troca:

- `namespace EasyStock.Mobile` → `namespace SeuProjetoApi.Mobile`
- `using EasyStock.Mobile` → `using SeuProjetoApi.Mobile`

No `SyncController.cs`, adicionalmente:
- `ApplicationDbContext` → nome real do DbContext (ex: `EasyStockDbContext`)

VS Code: `Ctrl+Shift+H` faz isso em batch.

## Passo 4 — Registrar no DbContext

Abre o DbContext do EasyStock (tipicamente em `Data/` ou `Infrastructure/`). No método `OnModelCreating`, adiciona no final:

```csharp
protected override void OnModelCreating(ModelBuilder mb)
{
    base.OnModelCreating(mb);
    // ... código existente ...

    mb.RegisterMobileModels();
}
```

Adicione `using SeuProjetoApi.Mobile;` no topo do arquivo.

## Passo 5 — Registrar no Program.cs

No `Program.cs` da API:

```csharp
using SeuProjetoApi.Mobile;

var builder = WebApplication.CreateBuilder(args);

// ... serviços existentes ...
builder.Services.AddMobileModule();

var app = builder.Build();

// ... middleware existente ...
app.UseMobileModule();

app.Run();
```

Se o `Program.cs` já tem `AddControllers()`, `UseCors(...)` e `MapControllers()`, **não duplique**. Em vez disso, só copie a parte de static files do `UseMobileModule` manualmente.

## Passo 6 — Copiar o PWA pra wwwroot

```bash
mkdir -p SeuProjetoApi/wwwroot/pwa
cp -r casa-da-baba-mobile/pwa/* SeuProjetoApi/wwwroot/pwa/
```

Confere que ficou:
```
SeuProjetoApi/wwwroot/pwa/
├── index.html
├── manifest.json
├── sw.js
├── sync.js
└── icons/
```

## Passo 7 — Rodar

```bash
cd SeuProjetoApi
dotnet run
```

Testa no browser do computador:
- `http://localhost:5000/pwa/` — deve abrir o app
- `http://localhost:5000/api/mobile/sync/pull?since=0&deviceId=test` — deve retornar JSON

Se o app abrir, parabéns. Agora falta o celular.

## Passo 8 — Acessar do celular

### Descobrir o IP local do computador

**Windows**: `ipconfig` → procure por "IPv4 Address" no adaptador Wi-Fi
**Mac/Linux**: `ip addr | grep inet` ou `ifconfig`

Exemplo: `192.168.1.10`

### Fazer o ASP.NET escutar em todas as interfaces

Edita `Properties/launchSettings.json`:

```json
"applicationUrl": "http://0.0.0.0:5000"
```

(Ou configura via `builder.WebHost.UseUrls("http://0.0.0.0:5000")` no Program.cs)

### Liberar firewall (Windows)

PowerShell como admin:
```powershell
New-NetFirewallRule -DisplayName "EasyStock Mobile" -Direction Inbound -LocalPort 5000 -Protocol TCP -Action Allow
```

### Abrir no celular

No celular, mesmo Wi-Fi: abre o Chrome e vai pra:
```
http://192.168.1.10:5000/pwa/
```

(Troca o IP pelo seu)

Deve abrir o app.

## Passo 9 — Instalar como PWA no celular

### Android (Chrome)
1. Com o app aberto, toca nos 3 pontinhos do menu
2. "Adicionar à tela inicial" ou "Instalar app"
3. Confirma
4. Aparece ícone "CB" na tela do celular
5. Toca nele, abre fullscreen sem barra de browser

### iOS (Safari)
1. Com o app aberto, toca no botão de compartilhar (quadrado com seta pra cima)
2. "Adicionar à Tela de Início"
3. Confirma
4. Aparece ícone "CB" na tela
5. Toca, abre

> **Importante**: em HTTP (não HTTPS), o service worker não registra. O app roda normalmente, mas não funciona offline. Se quiser offline, leia a seção abaixo.

---

## HTTPS (opcional mas recomendado pra modo offline)

### Opção A — Cloudflare Tunnel (mais simples)

```bash
# Instala o cloudflared
# Roda na pasta do EasyStock enquanto estiver com dotnet run:
cloudflared tunnel --url http://localhost:5000
```

Ele gera uma URL pública HTTPS tipo `https://random-name.trycloudflare.com`. Acessa essa URL no celular. Service worker funciona.

Contra: a URL muda cada vez que reinicia o tunnel.

### Opção B — Deploy no Azure App Service

Felipe já tem Visual Studio Pro via Avanade, provavelmente tem crédito Azure também. Deploy o EasyStock lá, já vem com HTTPS certificado. Acessa sempre pelo mesmo domínio.

### Opção C — Certificado self-signed local

Mais chato, envolve gerar certificado, confiar nele no celular, etc. Só faça se as outras opções não derem.

---

## Verificação final

Abre o app no celular, faz um teste completo:

1. Registra um lote de produção com foto
2. Cria um pedido avulso com item do cardápio
3. Avança o pedido pra "Pronto" (estoque desconta)
4. Marca como "Entregue" (cai no caixa)
5. Volta pro computador e consulta:

```sql
SELECT * FROM mobile_orders ORDER BY created_at DESC LIMIT 5;
SELECT * FROM mobile_batches ORDER BY created_at DESC LIMIT 5;
SELECT id, name, stock FROM mobile_products;
```

Se os dados apareceram, tá funcionando.

---

## Troubleshooting

**"Can't reach the server"** (no celular): firewall tá bloqueando, ou o ASP.NET só escuta em localhost. Ver Passo 8.

**"Error 500 no /sync"**: provavelmente DbContext não está injetado. Confere o Passo 3.

**"Tabelas não existem"**: migration não rodou. Confere o Passo 1.

**"A tela tá em branco"**: abre o DevTools (Chrome: `chrome://inspect` conectado no celular via USB) e vê o erro no console.

**"Service worker not registering"**: é HTTP em vez de HTTPS. Veja seção HTTPS.

**"O pedido não aparece no computador depois"**: o sync pode ter falhado silenciosamente. Abre o console do browser no celular (via `chrome://inspect`) e vê se tem erros de fetch.
