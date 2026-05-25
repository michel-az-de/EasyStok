using EasyStock.Api.Mobile.Security;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// PDV mobile — venda direta autenticada via device api key.
/// Reutiliza RegistrarSaidaEstoqueUseCase (mesma lógica do POST /api/vendas),
/// mas autenticado por X-Mobile-Api-Key em vez de JWT Bearer.
/// </summary>
[ApiController]
[Route("api/mobile/vendas")]
public class MobileVendasController(
    RegistrarSaidaEstoqueUseCase registrarSaidaUseCase,
    ILogger<MobileVendasController> log) : ControllerBase
{
    public sealed record MobileVendaItemRequest(
        [Required] Guid ItemEstoqueId,
        [Range(1, int.MaxValue)] int Quantidade,
        [Range(0.01, double.MaxValue)] decimal PrecoUnitario,
        string? Descricao = null);

    public sealed record MobileVendaRequest(
        [Required][MinLength(1)] IReadOnlyList<MobileVendaItemRequest> Itens,
        CanalVenda Canal = CanalVenda.LojaPropria,
        string? NumeroNotaFiscal = null,
        string? Observacoes = null,
        DateTime? DataVenda = null);

    [HttpPost]
    [MobileApiKey]
    public async Task<IActionResult> CriarVenda([FromBody] MobileVendaRequest request, CancellationToken ct)
    {
        var device = HttpContext.GetMobileDevice();
        if (device is null) return Unauthorized(new { error = "device não pareado" });

        var agora = DateTime.UtcNow;
        var dataVenda = request.DataVenda?.ToUniversalTime() ?? agora;

        var command = new RegistrarSaidaEstoqueCommand(
            EmpresaId: device.EmpresaId,
            Itens: request.Itens
                .Select(i => new RegistrarSaidaEstoqueItemCommand(
                    i.ItemEstoqueId,
                    i.Quantidade,
                    i.PrecoUnitario,
                    i.Descricao))
                .ToList(),
            DataVenda: dataVenda,
            DataSaida: agora,
            DataEnvio: null,
            NotaFiscal: request.NumeroNotaFiscal,
            Natureza: NaturezaMovimentacaoEstoque.Venda,
            Canal: request.Canal,
            Observacoes: request.Observacoes);

        var result = await registrarSaidaUseCase.ExecuteAsync(command);

        log.LogInformation("Venda PDV mobile criada. Device={DeviceId} EmpresaId={EmpresaId} VendaId={VendaId} Total={Total}",
            device.Id, device.EmpresaId, result.VendaId, result.ValorTotal);

        return Ok(new { result.VendaId, result.ValorTotal, result.Itens });
    }
}
