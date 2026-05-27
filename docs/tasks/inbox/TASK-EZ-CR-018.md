# TASK-EZ-CR-018 — Verificar NU1903 SharpCompress + remover NoWarn se patch existir

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-26)
**Prioridade:** P3
**Esforco:** P
**Status:** inbox

## Objetivo

Validar se o pacote SharpCompress ainda tem vulnerabilidade NU1903 ou se ja existe patch. Remover `<NoWarn>NU1903</NoWarn>` do `EasyStock.Api.csproj` quando seguro.

## Escopo

- [EasyStock.Api/EasyStock.Api.csproj](../../../EasyStock.Api/EasyStock.Api.csproj) (linhas ~12-14)

## Plano

```powershell
dotnet list 'C:\easy\EasyStok\EasyStock.Api\EasyStock.Api.csproj' package --vulnerable
```

Se SharpCompress nao aparece mais (ou tem patch disponivel):
1. Atualizar versao do SharpCompress (ou da dependencia transitive que traz ele)
2. Remover `<NoWarn>NU1903</NoWarn>`
3. `dotnet build` deve continuar verde

Se ainda vulneravel:
- Documentar em `docs/dev/incidentes/` com data e contexto
- Criar issue para revisitar em 1 mes

## Definicao de Pronto

- [ ] `dotnet list package --vulnerable` documentado em comentario do PR
- [ ] Se patch existe: pacote atualizado + `NoWarn NU1903` removido
- [ ] Se nao: criar issue + atualizar comentario do csproj com data
- [ ] `dotnet build` verde
- [ ] PR mergeado

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-26)
