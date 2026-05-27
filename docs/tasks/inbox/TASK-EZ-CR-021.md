# TASK-EZ-CR-021 — Adicionar NSubstitute ao EasyStock.Api.IntegrationTests.csproj (fix master)

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-31)
**Prioridade:** P0 (build do master quebrado)
**Esforco:** P (~10 minutos)
**Status:** inbox — **URGENTE, faz primeiro**

## Objetivo

Restaurar o build do master adicionando `<PackageReference Include="NSubstitute" />` ao csproj de integration tests. Master tem 6 erros CS0246 desde commit `92c9b10b`.

## Causa raiz

Commit `92c9b10b feat(TASK-EZ-PEDIDOS-001): green — use case + endpoint GET /pedidos cliente` adicionou o arquivo:

```
EasyStock.Api.IntegrationTests/Storefront/Pedidos/PedidosClienteControllerTests.cs
```

que usa:
```csharp
using NSubstitute;
using NSubstitute.ExceptionExtensions;
```

mas o csproj nao tem PackageReference para NSubstitute.

## Erros atuais

```
EasyStock.Api.IntegrationTests/Storefront/Pedidos/PedidosClienteControllerTests.cs(13,7):
  error CS0246: O nome do tipo ou do namespace "NSubstitute" nao pode ser encontrado
EasyStock.Api.IntegrationTests/Storefront/Pedidos/PedidosClienteControllerTests.cs(14,7):
  error CS0246: O nome do tipo ou do namespace "NSubstitute" nao pode ser encontrado
```
(6 ocorrencias no total — duas linhas `using` + uso em 4 lugares no metodo)

## Escopo

- [EasyStock.Api.IntegrationTests/EasyStock.Api.IntegrationTests.csproj](../../../EasyStock.Api.IntegrationTests/EasyStock.Api.IntegrationTests.csproj)

## Fix

Adicionar ao `<ItemGroup>` de `PackageReference`:

```xml
<PackageReference Include="NSubstitute" Version="5.3.0" />
<PackageReference Include="NSubstitute.Analyzers.CSharp" Version="1.0.17" />
```

Versao 5.3.0 é a estavel mais recente em 2026-05-27. Verificar com:
```powershell
dotnet list 'C:\easy\EasyStok\EasyStock.Application.Tests\EasyStock.Application.Tests.csproj' package | Select-String NSubstitute
```

(Outros test projects que ja usam NSubstitute para alinhar versao.)

## Definicao de Pronto

- [ ] `<PackageReference Include="NSubstitute" Version="..." />` adicionado ao csproj
- [ ] `dotnet restore EasyStok.sln` ok
- [ ] `dotnet build EasyStok.sln --nologo` verde (0 erros)
- [ ] Commit direto em master (hotfix < 5 arquivos, CLAUDE.md v2.1 R1 permite) ou via PR formal — alinhar com Felipe
- [ ] Push + (se PR) merge admin-squash

## Riscos

- Versao diferente do NSubstitute pode causar conflito com outros test projects — alinhar via Directory.Packages.props se ja existe
- Pode haver outro problema no arquivo `.cs` (mock setup invalido) que so aparece depois do package adicionado

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-31)
- Commit causador: `92c9b10b feat(TASK-EZ-PEDIDOS-001): green`
