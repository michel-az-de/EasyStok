# TASK-EZ-CR-002 — CancellationToken em ~293 action methods (ADR-0013)

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-2)
**Prioridade:** P1
**Esforco:** G (varios PRs por subdominio)
**Status:** inbox

## Objetivo

Adicionar `CancellationToken ct` como parametro em todas as ~293 action methods `async Task<IActionResult>` que ainda nao tem, e propagar ate `useCase.ExecuteAsync(input, ct)`. Cumpre ADR-0013.

## Numeros (medidos)

- `async Task<IActionResult>` em `EasyStock.Api/Controllers/`: **430 ocorrencias em 76 arquivos**
- `CancellationToken` em `EasyStock.Api/Controllers/`: **137 ocorrencias em 37 arquivos**
- Gap real: ~293 actions (varia porque algumas mencoes de CT sao variaveis locais)

## Escopo

Dividir em PRs por subdominio:

| PR | Subdominio | Aprox. actions |
|---|---|---|
| 1 | Admin (controllers `Admin*`) | ~80 |
| 2 | Business (Categoria, Cliente, Fornecedor, Produto, Pedido, Venda, ...) | ~80 |
| 3 | Mobile (`Mobile/Controllers/*`) | ~42 |
| 4 | Webhooks + Internal + Public | ~15 |
| 5 | Diagnostico + Auth + Storefront | ~30 |

**Out of scope:**
- Use cases (Application) — outra task se houver gap la
- Background services (sao IHostedService, ja recebem stoppingToken)

## Padrao de fix

```csharp
// ANTES
[HttpGet]
public async Task<IActionResult> Listar([FromQuery] FilterDto filter)
{
    var result = await _useCase.ExecuteAsync(filter);
    return Ok(result);
}

// DEPOIS
[HttpGet]
public async Task<IActionResult> Listar([FromQuery] FilterDto filter, CancellationToken ct)
{
    var result = await _useCase.ExecuteAsync(filter, ct);
    return Ok(result);
}
```

ASP.NET Core liga automaticamente o `CancellationToken` ao `HttpContext.RequestAborted`.

## Definicao de Pronto (DoD)

- [ ] Sweep completado: todos os actions `async Task<IActionResult>` recebem `CancellationToken ct`
- [ ] CT propagado para `useCase.ExecuteAsync(input, ct)` (nao usar `default` ou `CancellationToken.None`)
- [ ] `dotnet build` verde
- [ ] Architecture test garante que use cases recebem CT
- [ ] Sem regressao em testes existentes
- [ ] Pelo menos 1 integration test demonstra cancelamento end-to-end (cliente desconecta → query no DB cancela)

## Estrategia de execucao

- **Sweep semi-automatizado:** Roslyn analyzer ou regex `public async Task<IActionResult>\s+(\w+)\(([^)]*)\)` → adicionar `, CancellationToken ct` se nao houver
- Validar manualmente cada PR antes de merge
- Considerar criar `[CancellableEndpoint]` analyzer custom para enforce em PRs futuros

## Riscos

- Use cases que nao aceitam CT (verificar primeiro — pode bloquear)
- Testes que chamam controller direto precisam passar `CancellationToken.None`
- Performance regressao se DB driver nao suporta cancelamento (Npgsql suporta)

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-2)
- `docs/adr/0013-cancellation-token-iusecase.md`
