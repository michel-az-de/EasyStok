using System.Diagnostics;

namespace EasyStock.Application.UseCases.Analytics.Dia;

public sealed record ObterResumoDiaCommand(Guid EmpresaId, Guid? LojaId) : ICommand;

public sealed record ObterResumoDiaResult(
    int PedidosEntreguesHoje,
    decimal FaturamentoHoje,
    decimal TicketMedioHoje,
    int PedidosPendentes,
    decimal ValorPedidosPendentes,
    bool CaixaAbertaHoje,
    bool CaixaFechadaHoje,
    decimal SaldoCaixaAtual,
    int PixRecebidosHoje,
    decimal ValorPixHoje,
    bool OnboardingCompleto,
    int CategoriasCount,
    int EntradasCount)
{
    public static ObterResumoDiaResult FromDto(ResumoDia r) => new(
        r.PedidosEntreguesHoje, r.FaturamentoHoje, r.TicketMedioHoje,
        r.PedidosPendentes, r.ValorPedidosPendentes,
        r.CaixaAbertaHoje, r.CaixaFechadaHoje, r.SaldoCaixaAtual,
        r.PixRecebidosHoje, r.ValorPixHoje,
        r.OnboardingCompleto,
        r.CategoriasCount,
        r.EntradasCount);
}

public sealed class ObterResumoDiaUseCase(
    IAnalyticsRepository analyticsRepository,
    ILogger<ObterResumoDiaUseCase> logger)
    : IUseCase<ObterResumoDiaCommand, ObterResumoDiaResult>
{
    public async Task<ObterResumoDiaResult> ExecuteAsync(ObterResumoDiaCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var sw = Stopwatch.StartNew();
        var resumo = await analyticsRepository.GetResumoDiaAsync(cmd.EmpresaId, cmd.LojaId);
        sw.Stop();

        logger.LogInformation("Resumo do dia retrieved in {Ms}ms for empresa {EmpresaId}",
            sw.ElapsedMilliseconds, cmd.EmpresaId);

        return ObterResumoDiaResult.FromDto(resumo);
    }
}
