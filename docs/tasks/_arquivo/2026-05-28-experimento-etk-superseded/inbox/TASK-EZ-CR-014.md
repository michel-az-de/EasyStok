# TASK-EZ-CR-014 — Substituir CryptographicEquals custom por stdlib

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-18)
**Prioridade:** P2
**Esforco:** P
**Status:** inbox

## Objetivo

Substituir impl home-rolled de comparacao tempo-constante por `CryptographicOperations.FixedTimeEquals` da stdlib do .NET (auditado e testado).

## Escopo

[EasyStock.Api/Authorization/InternalCronJobAuthHandler.cs:95-103](../../../EasyStock.Api/Authorization/InternalCronJobAuthHandler.cs)

## Codigo atual

```csharp
private static bool CryptographicEquals(string a, string b)
{
    if (a.Length != b.Length) return false;
    var diff = 0;
    for (var i = 0; i < a.Length; i++)
        diff |= a[i] ^ b[i];
    return diff == 0;
}
```

## Fix

```csharp
using System.Security.Cryptography;
using System.Text;

// ... no metodo HandleAuthenticateAsync ...
var providedBytes = Encoding.UTF8.GetBytes(providedToken);
var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);

if (providedBytes.Length != expectedBytes.Length ||
    !CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
{
    var remoteIp = Context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    Logger.LogWarning("InternalCronJob: token invalido — IP={RemoteIp}", remoteIp);
    return Task.FromResult(AuthenticateResult.Fail("Token invalido."));
}
```

Remover metodo privado `CryptographicEquals`.

## Definicao de Pronto

- [ ] `CryptographicEquals` removido
- [ ] `CryptographicOperations.FixedTimeEquals` usado
- [ ] `using System.Security.Cryptography;` adicionado
- [ ] Unit tests existentes passam (timing-attack resistance test, se houver)
- [ ] `dotnet build` verde
- [ ] PR mergeado

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-18)
