# TASK-EZ-CR-010 — Extrair RateLimitingPolicies static class

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-12)
**Prioridade:** P2
**Esforco:** P
**Status:** inbox

## Objetivo

Extrair as ~110 linhas inline de `AddEasyStockRateLimit` em `ApiServiceCollectionExtensions.cs` para uma classe estatica `RateLimitingPolicies` com 1 metodo por politica.

## Escopo

- [EasyStock.Api/Configuration/ApiServiceCollectionExtensions.cs](../../../EasyStock.Api/Configuration/ApiServiceCollectionExtensions.cs) (linhas ~169-280)
- Nova `EasyStock.Api/Configuration/RateLimitingPolicies.cs`

## Plano

```csharp
public static class RateLimitingPolicies
{
    public const string Ai = "ai";
    public const string TicketsPost = "tickets-post";
    public const string PublicRead = "public-read";
    public const string PublicPost = "public-post";
    public const string Signup = "signup";
    public const string Disponibilidade = "disponibilidade";
    // ...

    public static FixedWindowRateLimiterOptions Ai() => new() { ... };
    public static FixedWindowRateLimiterOptions TicketsPost() => new() { ... };
    // ...

    public static void Apply(RateLimiterOptions options)
    {
        options.AddFixedWindowLimiter(Ai, Ai());
        options.AddFixedWindowLimiter(TicketsPost, TicketsPost());
        // ...
    }
}
```

`AddEasyStockRateLimit` fica:
```csharp
public static IServiceCollection AddEasyStockRateLimit(this IServiceCollection services)
{
    services.AddRateLimiter(RateLimitingPolicies.Apply);
    return services;
}
```

## Definicao de Pronto

- [ ] `RateLimitingPolicies.cs` criado com todas as 8 politicas
- [ ] `AddEasyStockRateLimit` com <20 linhas
- [ ] `dotnet build` verde
- [ ] Integration test valida que cada politica aplica corretamente
- [ ] ADR opcional documentando limites
- [ ] PR mergeado

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-12)
