using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.FecharCaixa;

public sealed record FecharCaixaCommand(
    [property: Required] Guid EmpresaId,
    DateOnly? Data = null,
    Guid? LojaId = null,
    string? Observacoes = null,
    Guid? FechadoPorUserId = null,
    [property: MaxLength(120)] string? FechadoPorNome = null);

/// <summary>
/// Fecha o caixa do dia: calcula saldo inicial + vendas + pagamentos +
/// entradas/saídas extras = saldo final, persiste em
/// <see cref="FechamentoCaixa"/> e cria movimento "fechamento" (marcador).
/// Idempotente: se já fechou, retorna o snapshot existente.
/// </summary>
public class FecharCaixaUseCase(
    ICaixaRepository repo,
    IUnitOfWork uow,
    ILogger<FecharCaixaUseCase> logger)
{
    public async Task<FechamentoCaixaResult> ExecuteAsync(FecharCaixaCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        var data = cmd.Data ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var existente = await repo.GetFechamentoDoDiaAsync(cmd.EmpresaId, data, cmd.LojaId);
        if (existente != null) return Map(existente);

        // Calcula totais do dia.
        var movimentos = await repo.GetMovimentosDoDiaAsync(cmd.EmpresaId, data, cmd.LojaId);
        var movList = movimentos.ToList();

        decimal saldoInicial = movList.Where(m => m.Tipo == "abertura").Sum(m => m.Valor);
        decimal totalEntradas = movList.Where(m => m.Tipo == "entrada").Sum(m => m.Valor);
        decimal totalSaidas   = movList.Where(m => m.Tipo == "saida").Sum(m => m.Valor);
        var totalVendas = await repo.GetTotalVendasDoDiaAsync(cmd.EmpresaId, data, cmd.LojaId);
        var totalPagamentosPedidos = await repo.GetTotalPagamentosPedidosDoDiaAsync(cmd.EmpresaId, data, cmd.LojaId);

        var fechamento = FechamentoCaixa.Criar(
            cmd.EmpresaId, data, saldoInicial, totalVendas,
            totalPagamentosPedidos, totalEntradas, totalSaidas, cmd.LojaId);
        fechamento.FechadoPorUserId = cmd.FechadoPorUserId;
        fechamento.FechadoPorNome = cmd.FechadoPorNome;
        fechamento.Observacoes = cmd.Observacoes;

        // Cria movimento "fechamento" como marcador (não move saldo).
        var mov = MovimentoCaixa.Criar(cmd.EmpresaId, "fechamento", 0m,
            data.ToDateTime(TimeOnly.MaxValue), cmd.LojaId);
        mov.Descricao = $"Fechamento {data:yyyy-MM-dd}: saldo final {fechamento.SaldoFinal:F2}";
        mov.RegistradoPorUserId = cmd.FechadoPorUserId;
        mov.RegistradoPorNome = cmd.FechadoPorNome;
        mov.Origem = "web";

        await repo.AddFechamentoAsync(fechamento);
        await repo.AddMovimentoAsync(mov);
        await uow.CommitAsync();

        logger.LogInformation("Caixa {Data} fechado (saldo final={Saldo}, vendas={Vendas}).",
            data, fechamento.SaldoFinal, totalVendas);
        return Map(fechamento);
    }

    internal static FechamentoCaixaResult Map(FechamentoCaixa f) => new(
        f.Id, f.EmpresaId, f.LojaId, f.Data,
        f.SaldoInicial, f.TotalVendas, f.TotalPagamentosPedidos,
        f.TotalEntradasExtras, f.TotalSaidasExtras, f.SaldoFinal,
        f.FechadoPorUserId, f.FechadoPorNome, f.Observacoes, f.FechadoEm);
}
