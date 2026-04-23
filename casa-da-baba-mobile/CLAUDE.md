# CLAUDE.md — Casa da Baba Mobile Integration

Você está sendo executado numa pasta que contém a solução **EasyStock** (.NET 9 + PostgreSQL) ao lado do pacote `casa-da-baba-mobile/` que precisa ser integrado. Sua missão é fazer a integração completa e deixar o app rodando.

## Contexto

O **Casa da Baba Mobile** é um PWA (Progressive Web App) que serve como ferramenta operacional de cozinha pra o Felipe e a Thati. Ele vai rodar instalado no celular deles, sincronizando com o backend do EasyStock via REST. Não vai pra Play Store, é uso pessoal / da casa.

Stack do Felipe (já existente):
- **EasyStock**: .NET 9, Entity Framework Core, Azure PostgreSQL
- **Arquitetura**: modular monolith

Stack que você vai adicionar:
- **PWA**: HTML/CSS/JS vanilla puro, service worker, manifest
- **Backend**: 1 controller (`SyncController`) + 5 entidades + 1 módulo de registro
- **Banco**: 7 tabelas novas (prefixadas com `mobile_`)

## Princípios de integração

1. **Respeite a arquitetura existente do EasyStock**. Não reorganize o projeto. Se houver convenções (nomes de DbContext, namespaces, padrões de injeção), siga-as.
2. **Os arquivos em `backend/` são drop-in mas precisam de ajuste ao contexto**. Substitua `ApplicationDbContext` e `EasyStock.Mobile.*` pelos nomes reais do projeto.
3. **Tabelas prefixadas com `mobile_`** evitam colisão com schema existente.
4. **Não inicialize um git repo novo**, não rode `dotnet new`, não sobrescreva `Program.cs`, `appsettings.json` ou o `.csproj` existente. Só adicione trechos necessários.
5. **Pergunte ao Felipe se algo estiver ambíguo** ao invés de chutar.

---

## Passo a passo de integração

### Passo 1 — Reconhecimento do projeto

Antes de qualquer mudança, mapeie o que existe:

```bash
# Rode cada um e registre o resultado
find . -name "*.sln" -not -path "./casa-da-baba-mobile/*"
find . -name "*.csproj" -not -path "./casa-da-baba-mobile/*"
find . -name "Program.cs" -not -path "./casa-da-baba-mobile/*"
find . -name "*DbContext.cs" -not -path "./casa-da-baba-mobile/*"
find . -name "appsettings*.json" -not -path "./casa-da-baba-mobile/*"
```

Identifique:
- **Projeto principal da API** (onde tem `Program.cs` com `WebApplication.CreateBuilder`)
- **Namespace raiz do projeto** (ex: `EasyStock.Api`, `EasyStock.Web`)
- **Nome do DbContext** (ex: `EasyStockDbContext`, `AppDbContext`)
- **String de conexão** (em `appsettings.json`, procure por `"DefaultConnection"` ou similar)
- **Como as migrations são gerenciadas** (EF Core migrations ou SQL scripts)

Crie mentalmente um mapeamento:
```
ApplicationDbContext  ->  [DbContext real do EasyStock]
EasyStock.Mobile      ->  [namespace escolhido, ex: EasyStock.Api.Mobile]
```

### Passo 2 — Copiar arquivos C# pro projeto

Copie o conteúdo de `casa-da-baba-mobile/backend/` pro projeto principal da API:

```
casa-da-baba-mobile/backend/Models/*.cs      →  [ProjetoApi]/Mobile/Models/
casa-da-baba-mobile/backend/DTOs/*.cs        →  [ProjetoApi]/Mobile/DTOs/
casa-da-baba-mobile/backend/Controllers/*.cs →  [ProjetoApi]/Mobile/Controllers/
casa-da-baba-mobile/backend/Mobile/*.cs      →  [ProjetoApi]/Mobile/
```

Após copiar, **rode um find-and-replace em todos os arquivos copiados**:
- `namespace EasyStock.Mobile` → `namespace [RaizDoProjeto].Mobile`
- `using EasyStock.Mobile` → `using [RaizDoProjeto].Mobile`
- `ApplicationDbContext` → nome real do DbContext (em `SyncController.cs`)

### Passo 3 — Registrar DbSets no DbContext existente

Abra o DbContext do EasyStock e adicione ao método `OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    // ... código existente ...

    // Módulo Casa da Baba Mobile
    modelBuilder.RegisterMobileModels();
}
```

Isso registra as 7 entidades (Product, Client, Order, OrderItem, Batch, BatchItem, CashEntry) com suas relações e cascades.

**Não adicione DbSet<X> explicitamente no DbContext** — o `RegisterMobileModels()` faz isso via `Entity<T>()`. Se o projeto exigir DbSets nomeados (para queries com `_db.Products`), adicione manualmente:

```csharp
public DbSet<Product> MobileProducts => Set<Product>();
// ... etc
```

### Passo 4 — Registrar o módulo no Program.cs

Abra o `Program.cs` do projeto da API. Adicione nos lugares apropriados:

```csharp
using EasyStock.Mobile; // ou o namespace escolhido no Passo 2

// ... na seção de services (antes de builder.Build()) ...
builder.Services.AddMobileModule();

var app = builder.Build();

// ... depois de app.Build() e antes de app.Run() ...
app.UseMobileModule();
```

Se o `Program.cs` já tem `AddControllers()`, `UseCors()` ou `MapControllers()`, o `AddMobileModule()` e `UseMobileModule()` vão duplicar isso. Nesse caso, NÃO chame os métodos de extensão. Em vez disso:

- Garanta que o `AddCors` existente permita o PWA (adicione origem ou use política ampla em dev)
- Registre manualmente os static files do PWA (ver código de `MobileModule.UseMobileModule`)

### Passo 5 — Rodar a migration SQL

O arquivo `backend/Migrations/001_CreateMobileSchema.sql` cria todas as tabelas.

Opção A — **rodar SQL direto** (mais rápido, se não tem EF migrations):
```bash
# Pegue a connection string do appsettings e rode:
psql "$CONN_STRING" -f casa-da-baba-mobile/backend/Migrations/001_CreateMobileSchema.sql
```

Opção B — **gerar migration EF Core** (se o projeto usa EF migrations):
```bash
cd [ProjetoApi]
dotnet ef migrations add AddMobileSchema
dotnet ef database update
```

O SQL já traz um `INSERT ... ON CONFLICT DO NOTHING` com o catálogo inicial (7 produtos do cardápio). Se escolher EF Core, o seed pode ser adicionado com `HasData` no `OnModelCreating` ou simplesmente rodando o trecho de INSERT separadamente.

### Passo 6 — Copiar o PWA pra wwwroot

```bash
mkdir -p [ProjetoApi]/wwwroot/pwa
cp -r casa-da-baba-mobile/pwa/* [ProjetoApi]/wwwroot/pwa/
```

O `UseMobileModule` já configura o `UseStaticFiles` apontando pra essa pasta.

### Passo 7 — Testar

```bash
cd [ProjetoApi]
dotnet run
```

Abra no browser:
- `http://localhost:5000/pwa/` → deve mostrar o app
- `http://localhost:5000/app` → deve redirecionar pro PWA
- `curl http://localhost:5000/api/mobile/sync/pull?since=0&deviceId=test` → deve retornar JSON

Se der erro 500 no sync, cheque:
- DbContext injetado corretamente
- Migration rodou (tabelas existem no banco)
- Connection string aponta pro banco certo

### Passo 8 — Configurar pra acesso do celular

O Felipe vai acessar do celular dele na mesma rede Wi-Fi de casa. Você precisa:

**A)** Fazer o ASP.NET escutar em todas as interfaces (não só `localhost`):

No `Program.cs` ou em `launchSettings.json`:
```json
"applicationUrl": "http://0.0.0.0:5000;https://0.0.0.0:5001"
```

**B)** Liberar a porta no firewall (Windows):
```powershell
New-NetFirewallRule -DisplayName "EasyStock 5000" -Direction Inbound -LocalPort 5000 -Protocol TCP -Action Allow
```

**C)** Descobrir o IP local do computador:
```bash
# Linux/Mac
ip addr | grep inet
# Windows
ipconfig
```

Diga ao Felipe: "Abre no celular o link `http://SEU-IP:5000/pwa/` e toque em Adicionar à tela inicial."

> **Importante sobre HTTPS e service worker**: service workers só funcionam em HTTPS OU em `localhost`. Acessando por IP local via HTTP, o service worker NÃO registra — o app ainda roda, mas sem cache offline e sem "instalação" completa como PWA. Para ter a experiência full, duas opções:
>
> 1. Configurar um certificado self-signed e aceitar no celular (chato mas funciona)
> 2. Rodar com Cloudflare Tunnel ou ngrok, que expõe o localhost via HTTPS pública
> 3. Rodar o EasyStock no Azure (App Service já vem com HTTPS)

Informe o Felipe qual opção você recomenda e por quê.

### Passo 9 — Relatório final

Ao terminar, forneça ao Felipe:

1. **URL do PWA** (ex: `http://192.168.1.10:5000/pwa/`)
2. **Como instalar no celular**: abrir URL no Chrome Android → menu 3 pontos → "Adicionar à tela inicial". No iOS: Safari → botão compartilhar → "Adicionar à Tela de Início"
3. **Confirmação de que os endpoints respondem**: rode `curl` nos dois endpoints e mostre o resultado
4. **Nome das tabelas criadas no banco**: liste com `\dt mobile_*` no psql
5. **Caveats**: qualquer coisa que você teve que adaptar ou que ficou em TODO

---

## Endpoints criados

Veja `API-CONTRACT.md` pra detalhes completos. Resumo:

| Método | Rota | Função |
|---|---|---|
| POST | `/api/mobile/sync` | Recebe mutations do PWA |
| GET  | `/api/mobile/sync/pull?since=<ms>&deviceId=<id>` | Retorna mudanças feitas por outros devices |

---

## Troubleshooting

**"DbContext não consegue resolver"**  
→ O `SyncController` usa `ApplicationDbContext` como placeholder. Substitua pelo nome real (Passo 2).

**"Erro de migração: tabela já existe"**  
→ O SQL usa `CREATE TABLE IF NOT EXISTS`. Rodar duas vezes é seguro. Mas se teve crash no meio, rode `DROP TABLE mobile_xxx CASCADE` pra limpar.

**"Service worker não registra"**  
→ É esperado em HTTP. Veja Passo 8. App ainda funciona, só sem cache offline.

**"CORS bloqueado"**  
→ Aconteceu se o PWA está num domínio diferente do backend. Ajuste o policy em `MobileModule.AddMobileModule`.

**"Estoque ficou negativo"**  
→ É comportamento esperado. O app permite pedidos mesmo sem estoque suficiente (porque na prática o Felipe sabe que vai produzir). O backend replica isso. Se quiser bloquear, ajuste `ApplyStockRule` em `SyncController`.

---

## O que NÃO fazer

- Não crie autenticação/login (uso pessoal, rede local, não precisa)
- Não adicione Identity, JWT, OAuth — seria overkill
- Não coloque o app na Play Store (PWA é melhor pro caso de uso)
- Não implemente real-time sync (WebSockets, SignalR) — sync sob demanda já resolve
- Não tente ser clever com conflict resolution além do last-write-wins

---

## Se precisar de ajuda

- Docs do Felipe sobre EasyStock: provavelmente em `README.md` na raiz
- Preferências dele: direto ao ponto, sem vírgulas sobrando, sem travessões, respostas em português brasileiro
- Se ele pediu pra "rodar" ou "deixar pronto", termine com os 5 itens do Passo 9
