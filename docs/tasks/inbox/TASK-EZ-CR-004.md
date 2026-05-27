# TASK-EZ-CR-004 — JwtTokenService fail-fast em config

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-5)
**Prioridade:** P1
**Esforco:** P
**Status:** inbox

## Objetivo

Garantir que `JwtTokenService` falhe rapido (no startup ou construtor) se config JWT essencial estiver ausente ou invalida, em vez de gerar tokens sem `iss`/`aud` claims que falham silenciosamente em outros servicos.

## Problemas

1. `Issuer = configuration["Jwt:Issuer"]` — aceita null, token gerado sem claim `iss`
2. `Audience = configuration["Jwt:Audience"]` — idem para `aud`
3. `SecretKey` faz throw se ausente mas **nao valida length minimo** (HmacSha256 precisa >=32 bytes)
4. Sem politica de rotacao documentada

## Escopo

- [EasyStock.Api/Services/JwtTokenService.cs](../../../EasyStock.Api/Services/JwtTokenService.cs)
- `appsettings.Production.json` (template/exemplo)
- ADR nova ou existente sobre JWT

## Padrao de fix

```csharp
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expiresInSeconds;

    public JwtTokenService(IConfiguration configuration)
    {
        _secretKey = configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("Jwt:SecretKey nao configurado.");
        if (_secretKey.Length < 32)
            throw new InvalidOperationException("Jwt:SecretKey precisa ter pelo menos 32 caracteres (HmacSha256).");

        _issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Jwt:Issuer nao configurado.");
        _audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Jwt:Audience nao configurado.");

        _expiresInSeconds = int.TryParse(configuration["Jwt:ExpirationMinutes"], out var mins)
            ? mins * 60
            : 3600;
    }

    public int ExpiresInSeconds => _expiresInSeconds;

    // ... resto do GerarToken usando _secretKey, _issuer, _audience
}
```

## Definicao de Pronto

- [ ] Construtor faz throw se Jwt:SecretKey/Issuer/Audience ausentes ou invalidos
- [ ] `appsettings.Development.json` tem valores validos (chave generica)
- [ ] `appsettings.Production.json` tem placeholders documentados
- [ ] ADR nova ou atualizacao documentando rotacao de chave (HS256 → considerar RS256 futuro)
- [ ] Unit test: construtor lanca se config ausente
- [ ] Integration test: token gerado tem `iss` e `aud` claims
- [ ] `dotnet build` verde + tests verdes

## Riscos

- Quebra apps que rodam sem Jwt:Issuer/Audience configurados — verificar dev/staging primeiro
- Migration para RS256 (futura) requer coordenacao de servicos consumidores

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-5)
