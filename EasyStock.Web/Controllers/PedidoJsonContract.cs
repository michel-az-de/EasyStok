using EasyStock.Web.Models.Api;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// Mapeia <see cref="ApiResult{Pedido}"/> → resposta JSON do cockpit (issue #591),
/// espelhando o contrato do <c>CriarJson</c>:
/// <list type="bullet">
///   <item>sucesso → 200 <c>{ success: true, pedido: PedidoRowDto }</c></item>
///   <item>falha → <c>StatusCode(httpStatus &gt; 0 ? httpStatus : 400,
///         { success: false, error: { code, message }, correlationId })</c></item>
/// </list>
/// Extraído como helper puro pra ser testável sem DI/antiforgery (o servidor vence
/// sempre na resposta: o cliente substitui o item otimista pelo <c>pedido</c> do corpo).
/// </summary>
public static class PedidoJsonContract
{
    public static IActionResult From(ApiResult<Pedido> r)
    {
        if (!r.Success || r.Data is null) return Erro(r);
        return new OkObjectResult(new { success = true, pedido = PedidoRowDto.From(r.Data) });
    }

    /// <summary>
    /// Contrato das ações de aprovar/recusar do cockpit (#862): sucesso → 200
    /// <c>{ success: true, pedido: { status } }</c> (o cliente faz <c>Object.assign</c> na
    /// linha, só o status muda); falha → propaga o HttpStatus da API (404/409/422).
    /// </summary>
    public static IActionResult FromAcao(ApiResult<AprovacaoResult> r)
    {
        if (!r.Success || r.Data is null) return Erro(r);
        return new OkObjectResult(new { success = true, pedido = new { status = r.Data.Status } });
    }

    private static ObjectResult Erro<T>(ApiResult<T> r) => new(new
    {
        success = false,
        error = new
        {
            code = r.ErrorCode ?? "API_ERROR",
            message = r.ErrorMessage ?? "Não foi possível processar o pedido."
        },
        correlationId = r.CorrelationId
    })
    { StatusCode = r.HttpStatus > 0 ? r.HttpStatus : 400 };
}
