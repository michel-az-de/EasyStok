# TASK-EZ-CR-019 — Cap LRU no cache de reflection do ValidateEmpresaIdAttribute

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-30)
**Prioridade:** P3
**Esforco:** P
**Status:** inbox

## Objetivo

Limitar o tamanho do `ConcurrentDictionary` que cacheia reflection expressions no `ValidateEmpresaIdAttribute`, para evitar leak leve em apps long-running.

## Escopo

[EasyStock.Api/Http/ValidateEmpresaIdAttribute.cs:63-68](../../../EasyStock.Api/Http/ValidateEmpresaIdAttribute.cs)

## Problema

`ConcurrentDictionary<Type, Func<object, Guid?>>` cresce indefinidamente conforme novos tipos com `EmpresaId` aparecem. Em apps long-running com geracao dinamica de tipos (raro mas possivel), vira memory leak.

## Plano

Opcoes (ordenadas por simplicidade):

### Opcao A — Size cap simples (limpa tudo)
```csharp
private const int MaxCacheSize = 1000;
private static readonly ConcurrentDictionary<Type, Func<object, Guid?>> _cache = new();

private static Func<object, Guid?> GetAccessor(Type type)
{
    if (_cache.Count >= MaxCacheSize)
        _cache.Clear(); // ou implementar LRU real
    return _cache.GetOrAdd(type, BuildAccessor);
}
```

### Opcao B — LRU via `MemoryCache`
```csharp
private static readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 1000 });
```

### Opcao C — Aceitar como esta + documentar
- Adicionar comentario explicando que tipos com `EmpresaId` sao finitos (definidos no Domain)
- Marcar como wontfix se a quantidade de tipos for trivialmente pequena (verificar via reflection)

## Recomendacao

**Opcao C** primeiro: contar tipos com `EmpresaId` via reflection no Domain.

```powershell
Grep "public Guid\? EmpresaId|public Guid EmpresaId" EasyStock.Domain --include='*.cs' --count
```

Se for <50 tipos, e overkill. Documentar com comentario.

## Definicao de Pronto

- [ ] Quantidade de tipos com `EmpresaId` contada (anexar resultado ao PR)
- [ ] Se <50: comentario adicionado documentando limite teorico
- [ ] Se >=50: implementar Opcao A ou B
- [ ] `dotnet build` verde
- [ ] PR mergeado

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-30)
