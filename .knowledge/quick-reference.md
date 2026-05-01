# Quick Reference — EasyStock

## Build & test
```bash
dotnet build EasyStock.sln
dotnet test EasyStock.sln                                 # 457 testes
dotnet test EasyStock.Application.Tests                   # só Application
dotnet test --filter "FullyQualifiedName~Pedido"          # filtro por nome
```

## Run local
```bash
dotnet run --project EasyStock.Api                        # API (5xxx)
dotnet run --project EasyStock.Web                        # Web MVC
dotnet run --project EasyStock.Admin                      # Admin Razor Pages
```

## EF migrations
```bash
dotnet ef migrations add NomeDaMigration \
  --project EasyStock.Infra.Postgre \
  --startup-project EasyStock.Api

dotnet ef database update \
  --project EasyStock.Infra.Postgre \
  --startup-project EasyStock.Api

dotnet ef migrations remove \
  --project EasyStock.Infra.Postgre \
  --startup-project EasyStock.Api
```

## Postgres local
```bash
psql -h localhost -U postgres -d easystock
\dt                                                       # listar tabelas
SELECT version();
```

## Git workflow
```bash
git status
git diff
git add -A && git commit -m "tipo(escopo): msg"
git log --oneline -20
git push
```

## Tunnel local pra HTTPS público (cloudflared)
```bash
~/bin/cloudflared.exe tunnel --url http://localhost:5000
```

## Knowledge base maintenance
```bash
bash .knowledge/update.sh                                 # regen recent-evolution.md
```

## Docker (se for testar imagem Cloud Run)
```bash
docker build -f Dockerfile.cloudrun.api -t easystock-api .
docker run -p 8080:8080 easystock-api
```

## Endpoints úteis em dev
- API Swagger: `http://localhost:5000/swagger`
- Web: `http://localhost:5001`
- Admin: `http://localhost:5002/admin`
- Diagnóstico (auth): `/diagnostico/saude`
