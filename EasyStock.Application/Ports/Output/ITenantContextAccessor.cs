namespace EasyStock.Application.Ports.Output;

/// <summary>
/// Define o tenant da requisição quando não há claim JWT pra resolver (webhooks, jobs). Mesma
/// necessidade do módulo Mobile (<c>X-Mobile-Api-Key</c>): sem isto, o filtro global de tenant e a
/// RLS do Postgres (ADR-0010) veem <c>CurrentTenantId = Guid.Empty</c> e zeram as linhas —
/// fail-closed silencioso. Escopo é o DbContext da requisição atual (scoped).
/// </summary>
public interface ITenantContextAccessor
{
    void SetCurrentTenant(Guid empresaId);
}
