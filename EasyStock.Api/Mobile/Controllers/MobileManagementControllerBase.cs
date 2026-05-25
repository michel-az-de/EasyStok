using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Base para controllers Mobile chamados pelo painel WEB (gestão).
/// Diferente de SyncController/DevicePairingController, esses são acessados
/// pelo gestor autenticado por cookie e operam dados de mobile_*.
///
/// O guard de tenant aqui é CRÍTICO: sem ele, qualquer usuário autenticado
/// poderia listar/promover/linkar/revogar registros de outras empresas
/// passando ?empresaId=&lt;outra&gt;. Reaproveita a lógica de
/// <see cref="EasyStock.Api.Http.EasyStockControllerBase.TryResolveEmpresaId"/>:
/// SuperAdmin precisa informar empresaId; demais usuários têm o empresaId
/// resolvido do <see cref="ICurrentUserAccessor"/> e qualquer divergência
/// com a query é Forbid.
/// </summary>
public abstract class MobileManagementControllerBase(ICurrentUserAccessor currentUser) : ControllerBase
{
    protected ICurrentUserAccessor CurrentUser { get; } = currentUser;

    /// <summary>
    /// Resolve o EmpresaId efetivo da request mobile-management. Retorna
    /// false (com IActionResult de erro em <paramref name="error"/>) quando
    /// não foi possível resolver — caller deve `return error!;`.
    /// </summary>
    protected bool TryResolveEmpresaId(Guid? requestedEmpresaId, out Guid empresaId, out IActionResult? error)
    {
        error = null;
        empresaId = requestedEmpresaId.GetValueOrDefault();

        if (CurrentUser.Nivel == NivelAcesso.SuperAdmin)
        {
            if (empresaId == Guid.Empty)
            {
                error = BadRequest(new { error = "empresaId obrigatório (SuperAdmin)" });
                return false;
            }
            return true;
        }

        if (CurrentUser.EmpresaId != Guid.Empty)
        {
            if (requestedEmpresaId.HasValue && requestedEmpresaId.Value != Guid.Empty
                && requestedEmpresaId.Value != CurrentUser.EmpresaId)
            {
                // Tentou operar em empresa que não é a do usuário.
                error = Forbid();
                empresaId = Guid.Empty;
                return false;
            }
            empresaId = CurrentUser.EmpresaId;
            return true;
        }

        if (empresaId == Guid.Empty)
        {
            error = BadRequest(new { error = "empresaId obrigatório" });
            return false;
        }
        return true;
    }
}
