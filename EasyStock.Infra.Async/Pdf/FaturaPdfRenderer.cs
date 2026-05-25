using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace EasyStock.Infra.Async.Pdf;

/// <summary>
/// Implementacao de <see cref="IFaturaPdfRenderer"/> usando QuestPDF.
///
/// <para>
/// QuestPDF e <see href="https://www.questpdf.com/license/">Community MIT</see>
/// para faturamento anual abaixo de US$1M (suficiente para early-stage SaaS).
/// Acima disso, exige licenca paga. Configurar via
/// <see cref="QuestPDF.Settings.License"/> antes do primeiro uso.
/// </para>
///
/// <para>
/// O renderer e threadsafe e stateless — o estado da fatura entra via
/// <see cref="FaturaTemplate"/>. Pode ser registrado como Singleton no DI.
/// </para>
/// </summary>
public sealed class FaturaPdfRenderer : IFaturaPdfRenderer
{
    static FaturaPdfRenderer()
    {
        // Community license — auto-aceita para faturamento &lt; US$1M/ano. Empresas
        // que ultrapassarem precisam configurar QuestPDF.Settings.License = LicenseType.Professional
        // e adquirir licenca paga.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public Task<byte[]> RenderAsync(Fatura fatura, CancellationToken ct = default)
    {
        if (fatura is null) throw new ArgumentNullException(nameof(fatura));
        ct.ThrowIfCancellationRequested();

        var doc = new FaturaTemplate(fatura);
        var bytes = doc.GeneratePdf();
        return Task.FromResult(bytes);
    }
}
