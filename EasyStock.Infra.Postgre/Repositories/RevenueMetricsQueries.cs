using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Fonte unica de receita do Admin (ADR-0037 adendo, issue #754). Agrega cross-tenant sob
/// <see cref="EasyStockDbContext.UseRowLevelSecurityBypass"/> (mesmo mecanismo de
/// <see cref="FleetOperationQueries"/>): a leitura de receita cross-tenant do SuperAdmin
/// seria truncada ao tenant da sessao pela RLS (role sem BYPASSRLS) sem o bypass.
///
/// <para>
/// As 3 grandezas reusam as portas existentes para nao recriar predicado:
/// <c>SomarPrecoMensalAtivasAsync</c> (contratado) e os 2 metodos novos de fatura
/// (faturado por DataEmissao+Origem; recebido por DataPagamentoTotal). A janela e o mes
/// civil de Brasilia ate agora (parcial), via <see cref="HorarioBrasil"/>.
/// </para>
/// </summary>
public sealed class RevenueMetricsQueries(
    EasyStockDbContext db,
    IAssinaturaEmpresaRepository assinaturaRepo,
    IFaturaRepository faturaRepo) : IRevenueMetricsQueries
{
    public async Task<RevenueSnapshot> ObterAsync(Guid? empresaId = null, CancellationToken ct = default)
    {
        // Leitura de receita cross-tenant (SuperAdmin): abre o RLS p/ nao truncar ao tenant da sessao.
        // Setado ANTES de qualquer query no scope, igual FleetOperationQueries.
        using var _ = db.UseRowLevelSecurityBypass();

        var hoje = HorarioBrasil.Hoje();
        var primeiroDiaMes = new DateOnly(hoje.Year, hoje.Month, 1);
        var mesInicioUtc = HorarioBrasil.InicioRealDoDiaUtc(primeiroDiaMes); // meia-noite BRT do dia 1, em UTC
        var mesFimUtc = DateTime.UtcNow;                                     // mes parcial ate agora

        var contratado = await assinaturaRepo.SomarPrecoMensalAtivasAsync(empresaId, ct);
        var faturado = await faturaRepo.SomarTotalEmitidasPorOrigemAsync(
            mesInicioUtc, mesFimUtc, OrigemFatura.Assinatura, empresaId, ct);
        var recebido = await faturaRepo.SomarRecebidoNoPeriodoAsync(mesInicioUtc, mesFimUtc, empresaId, ct);

        return new RevenueSnapshot(contratado, faturado, recebido, mesInicioUtc, mesFimUtc);
    }
}
