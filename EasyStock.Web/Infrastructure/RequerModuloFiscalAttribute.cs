using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace EasyStock.Web.Infrastructure;

/// <summary>
/// Gate do módulo fiscal (issue #770). Aplicado na classe do controller, faz TODAS
/// as actions responderem 404 quando <c>Features:FiscalHabilitado</c> está off — sem
/// meia-emissão nem tela que leva a beco sem saída. Fiscal está FORA do escopo v1.0
/// (docs/plan/v1.0/SCOPE.md) enquanto a homologação FocusNFe não conclui; o backend
/// (issue #558) permanece intacto. Reativar = <c>Features:FiscalHabilitado=true</c>.
///
/// Lê a flag do container por request (RequestServices) em vez de injetar no ctor,
/// para não acoplar o gate à construção de nenhum controller específico.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequerModuloFiscalAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var features = context.HttpContext.RequestServices.GetRequiredService<IOptions<FeaturesOptions>>();
        if (!features.Value.FiscalHabilitado)
        {
            context.Result = new NotFoundResult();
            return;
        }

        await next();
    }
}
