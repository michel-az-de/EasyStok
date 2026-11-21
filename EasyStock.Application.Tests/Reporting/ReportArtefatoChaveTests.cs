using System.Globalization;
using EasyStock.Application.Reporting;
using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Tests.Reporting;

/// <summary>
/// #2788: a chave B-15 tem uma fonte só, usada pelo Worker que envia e pelo watchdog que apaga o envio interrompido.
/// </summary>
public sealed class ReportArtefatoChaveTests
{
    private static string Mes(ReportRun run) =>
        run.EnqueuedAt.UtcDateTime.ToString("yyyy'/'MM", CultureInfo.InvariantCulture);

    [Fact]
    public void Run_do_tenant_fica_na_pasta_da_empresa_pelo_mes_do_enfileiramento()
    {
        var empresa = Guid.NewGuid();
        var run = ReportRun.Enqueue(empresa, Guid.NewGuid(), "vendas.por-periodo", ReportCategoria.Vendas,
            ReportContexto.Tenant, "{}", "hash", ReportFormat.Xlsx, "1.0.0", TimeSpan.FromDays(7));

        ReportArtefato.Chave(run).Should().Be($"tenants/{empresa}/reports/{Mes(run)}/{run.Id}.xlsx");
    }

    [Fact]
    public void Run_do_admin_fica_na_pasta_do_admin()
    {
        var run = ReportRun.Enqueue(Guid.Empty, Guid.NewGuid(), "vendas.por-periodo", ReportCategoria.Vendas,
            ReportContexto.AdminSaaS, "{}", "hash", ReportFormat.Csv, "1.0.0", TimeSpan.FromDays(7),
            motivoExecucao: "auditoria");

        ReportArtefato.Chave(run).Should().Be($"admin/reports/{Mes(run)}/{run.Id}.csv");
    }
}
