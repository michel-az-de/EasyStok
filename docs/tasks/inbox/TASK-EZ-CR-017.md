# TASK-EZ-CR-017 — Adicionar .ValidateOnStart() em Configure<T>()

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-21)
**Prioridade:** P3
**Esforco:** P
**Status:** inbox

## Objetivo

Garantir fail-fast no startup quando opcoes de configuracao estao mal-formadas, em vez de descobrir so em runtime.

## Escopo

- [EasyStock.Api/Configuration/ApiServiceCollectionExtensions.cs](../../../EasyStock.Api/Configuration/ApiServiceCollectionExtensions.cs) (linhas ~145-161)
- Todos os `Configure<T>` em `Program.cs` e extensions

## Plano

Identificar todos os `services.Configure<T>(...)` no projeto e migrar para:

```csharp
services.AddOptions<InternalCronJobOptions>()
    .Bind(configuration.GetSection("Notifications:CronJob"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

Adicionar `[Required]`, `[Range]`, `[RegularExpression]` em propriedades das classes `*Options.cs`:

```csharp
public sealed class InternalCronJobOptions
{
    [Required(AllowEmptyStrings = false)]
    public string Token { get; set; } = string.Empty;

    [Required]
    public bool Habilitado { get; set; }
}
```

## Definicao de Pronto

- [ ] Todos `Configure<T>` virados em `AddOptions<T>().ValidateDataAnnotations().ValidateOnStart()`
- [ ] `*Options.cs` com data annotations apropriadas
- [ ] App falha no startup com mensagem clara se config invalida (testar removendo Jwt:SecretKey)
- [ ] `dotnet build` verde
- [ ] PR mergeado

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-21)
