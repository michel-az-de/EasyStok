using EasyStock.Application.Ports.Output;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre;

/// <summary>Encaminha para <see cref="EasyStockDbContext.SetMobileTenantContext"/> — mesmo mecanismo do módulo Mobile, reaproveitado para o webhook do WhatsApp (S03), que também não tem claim JWT.</summary>
public sealed class TenantContextAccessor(EasyStockDbContext dbContext) : ITenantContextAccessor, EasyStock.Application.Ports.Output.Ai.ITenantAtual
{
    public void SetCurrentTenant(Guid empresaId) => dbContext.SetMobileTenantContext(empresaId);

    /// <summary>#3064: o tenant que o filtro e a RLS usam agora (override do webhook/job, senão a claim; vazio sem nenhum).</summary>
    public Guid EmpresaId => dbContext.CurrentTenantId;
}
