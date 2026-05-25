using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Faturas.RegistrarPagamentoFatura;

public sealed record RegistrarPagamentoFaturaCommand(
    Guid EmpresaId,
    Guid FaturaId,
    string Metodo,
    decimal Valor,
    string GatewayProvedor = "Manual",
    string? GatewayTransactionId = null,
    string? DadosGatewayJson = null,
    StatusFaturaPagamento StatusInicial = StatusFaturaPagamento.Confirmado,
    Guid? RegistradoPorUserId = null,
    string? RegistradoPorNome = null,
    string? Observacao = null,
    string? OrigemRegistro = "api"
);

public sealed record RegistrarPagamentoFaturaResult(
    Guid PagamentoId,
    Guid FaturaId,
    string StatusFatura,
    decimal TotalPago,
    decimal Pendente
);

public class RegistrarPagamentoFaturaUseCase(
    IFaturaRepository repo,
    IUnitOfWork uow,
    ILogger<RegistrarPagamentoFaturaUseCase> logger)
{
    public async Task<RegistrarPagamentoFaturaResult> ExecuteAsync(
        RegistrarPagamentoFaturaCommand cmd,
        CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.FaturaId, nameof(cmd.FaturaId));
        if (cmd.Valor <= 0m)
            throw new UseCaseValidationException("Valor deve ser maior que zero.");

        var fatura = await repo.GetByIdAsync(cmd.EmpresaId, cmd.FaturaId, ct)
            ?? throw new UseCaseValidationException("Fatura nao encontrada.");

        var pagamento = cmd.StatusInicial == StatusFaturaPagamento.Confirmado
            ? FaturaPagamento.CriarConfirmado(
                fatura.Id, cmd.Metodo, cmd.Valor, cmd.GatewayProvedor, fatura.EmpresaId,
                cmd.GatewayTransactionId, cmd.DadosGatewayJson,
                cmd.RegistradoPorUserId, cmd.RegistradoPorNome, cmd.Observacao)
            : FaturaPagamento.CriarPendente(
                fatura.Id, cmd.Metodo, cmd.Valor, cmd.GatewayProvedor, fatura.EmpresaId,
                cmd.GatewayTransactionId, cmd.DadosGatewayJson);

        try
        {
            fatura.RegistrarPagamento(pagamento);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        // Audit
        var tipoEvento = cmd.StatusInicial == StatusFaturaPagamento.Confirmado
            ? TipoEventoFatura.PagamentoConfirmado
            : TipoEventoFatura.PagamentoRegistrado;
        fatura.Eventos.Add(FaturaEvento.Criar(
            fatura.Id, tipoEvento,
            usuarioId: cmd.RegistradoPorUserId, usuarioNome: cmd.RegistradoPorNome,
            origem: cmd.OrigemRegistro,
            valorDepois: $"+{pagamento.Valor:F2} {fatura.Moeda} via {pagamento.Metodo} ({pagamento.GatewayProvedor})"
        ));

        await repo.UpdateAsync(fatura, ct);
        await uow.CommitAsync();

        logger.LogInformation(
            "Pagamento registrado. FaturaId={FaturaId} Valor={Valor} Status={Status} TotalPago={TotalPago}/{Total}",
            fatura.Id, pagamento.Valor, fatura.Status, fatura.TotalPago, fatura.Total);

        return new RegistrarPagamentoFaturaResult(
            pagamento.Id, fatura.Id, fatura.Status.ToString(),
            fatura.TotalPago, fatura.Pendente);
    }
}
