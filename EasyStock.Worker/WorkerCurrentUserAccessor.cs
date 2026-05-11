using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Enums;

namespace EasyStock.Worker;

/// <summary>
/// ICurrentUserAccessor pro Worker — sem JWT/HTTP, opera como SuperAdmin pra bypass do
/// filter global multi-tenant em EasyStockDbContext. Sem isso, CurrentTenantId=Guid.Empty
/// + IsSuperAdmin=false zerariam toda query do pipeline (SLA monitor, notificacoes,
/// outbox legacy). Registrado em Program.cs antes do AddEasyStockPostgreInfrastructure.
/// </summary>
internal sealed class WorkerCurrentUserAccessor : ICurrentUserAccessor
{
    public Guid EmpresaId => Guid.Empty;
    public bool IsAuthenticated => true;
    public Guid UsuarioId => Guid.Empty;
    public NivelAcesso Nivel => NivelAcesso.SuperAdmin;
    public bool TemPermissao(Permissao permissao) => true;
}
