# Ambiente local automático (`scripts/local-env/`)

Terceiro ambiente do EasyStok, ao lado do sandbox (VM Azure) e da prod (Fly): roda
**API (+PWA), Web e Admin** na sua máquina via `dotnet watch` (hot reload). Sobe sozinho
**a cada `git push`** (hook `.husky/pre-push`).

## Topologia desta máquina

- **Apps** (API/Web/Admin): `dotnet` do **Windows**, hot reload nativo. É o que o `up.ps1` lança.
- **Postgres**: container **`pg-easystok`** rodando no **Docker do WSL2**, exposto em `localhost:5432`
  (db `easystok_demo`, user/pass `easystok`). O `up.ps1` **só detecta** a 5432 — quem sobe/para o
  Postgres é você, no WSL. O script não gerencia Docker (que não existe no PowerShell do Windows).

## Portas

| Serviço | URL | Observação |
|---|---|---|
| API + PWA | https://localhost:7039 | Swagger `/swagger` · PWA `/pwa/` · health `/health` |
| Web (MVC) | https://localhost:7010 | login em `/auth/login` |
| Admin | https://localhost:7002 | `/` redireciona para `/Auth/Login` |
| Postgres | localhost:5432 | `pg-easystok` (WSL) · db `easystok_demo` · `easystok/easystok` |

## Pré-requisitos (uma vez)

1. **.NET 9 SDK** no Windows.
2. **Postgres de pé** no WSL: `wsl -e docker start pg-easystok` (já costuma ficar ligado).
3. **Dev cert HTTPS confiável** (os apps sobem em `https://localhost`):
   ```powershell
   dotnet dev-certs https --trust
   ```

## Uso

```powershell
# Sobe/garante tudo de pé e imprime as URLs (aguarda a API responder):
powershell scripts/local-env/up.ps1

# Modo hook (usado pelo pre-push): lança o que faltar e retorna na hora:
powershell scripts/local-env/up.ps1 -Ensure

# Encerra os 3 apps (o Postgres do WSL fica intacto):
powershell scripts/local-env/down.ps1
```

> Use `powershell` (Windows PowerShell 5.1, presente por padrão). `pwsh` (PowerShell 7) também serve, se instalado.

## Como funciona

- **Idempotente por porta**: se a porta do serviço já está em `LISTEN`, o `up.ps1`
  considera que está de pé e não relança. Por isso `dotnet watch` fica vivo entre
  pushes e o hot reload mantém o código atualizado sozinho.
- Cada app sobe **destacado** (`Start-Process`), com `--no-launch-profile` +
  `ASPNETCORE_URLS` (portas fixas, sem abrir browser) e `DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1`
  (rude edits reiniciam sem pedir input).
- **Env vars injetadas** (precedência sobre `appsettings`): connection string do Postgres
  (`easystok_demo`) e `Jwt__SecretKey` para a API; `ApiBaseUrl`/`EasyStockWebUrl` para o Admin
  (cujo `appsettings.json` aponta para 7000/7001 inexistentes). Connection string e JWT podem ser
  sobrescritos por env var já presente no shell — ex.: para usar outro banco/porta.
- **Logs e PIDs** ficam em `.build/local-env/` (fora do git): `<svc>.out.log`, `<svc>.err.log`, `<svc>.pid`.

## O hook `pre-push`

`.husky/pre-push` chama `up.ps1 -Ensure`. É **non-blocking e non-fatal**: lança os processos
e retorna; se algo falhar (Postgres parado, build quebrado), o push **não trava** — o erro
fica nos logs. Logo, **não há motivo para `git push --no-verify`** por causa deste hook.

## Credenciais

O superadmin de dev (acesso ao Admin) é semeado por
[`EasyStock.Api/Data/SuperAdminSeed.cs`](../../EasyStock.Api/Data/SuperAdminSeed.cs). Para
fixar e-mail/senha, exporte antes de subir:

```powershell
$env:SEED_SUPERADMIN_EMAIL = 'voce@exemplo.com'
$env:SEED_SUPERADMIN_PASSWORD = 'UmaSenhaForte12+'
powershell scripts/local-env/up.ps1
```

## Troubleshooting

| Sintoma | Causa / correção |
|---|---|
| Apps não abrem em `https://` | Dev cert: `dotnet dev-certs https --trust` |
| API não conecta no banco | Postgres parado — `wsl -e docker start pg-easystok` e rode `up.ps1` de novo |
| Quero outro banco/porta | `$env:ConnectionStrings__DefaultConnection = '...'` antes do `up.ps1` |
| Porta ocupada | Outro processo na 7039/7010/7002 — rode `down.ps1` ou libere a porta |
| Quero ver o que a API logou | `.build/local-env/api.out.log` e `api.err.log` |
| 1ª subida demora | A API aplica as migrations no startup (~1 min). Acompanhe pelo log. |
| Preciso de HTTP em vez de HTTPS | Os apps também escutam em 5280/5128/5002 (http). |
