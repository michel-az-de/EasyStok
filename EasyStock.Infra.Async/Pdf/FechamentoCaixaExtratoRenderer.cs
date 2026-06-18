using EasyStock.Application.Ports.Output.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace EasyStock.Infra.Async.Pdf;

/// <summary>
/// Implementação de <see cref="IFechamentoCaixaExtratoRenderer"/> com QuestPDF (issue #642).
/// Stateless/threadsafe — o estado entra via <see cref="FechamentoCaixaExtratoTemplate"/>.
/// Pode ser registrado como Singleton. QuestPDF Community MIT (faturamento &lt; US$1M),
/// igual a <see cref="FaturaPdfRenderer"/>.
/// </summary>
public sealed class FechamentoCaixaExtratoRenderer : IFechamentoCaixaExtratoRenderer
{
    static FechamentoCaixaExtratoRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public Task<byte[]> RenderAsync(FechamentoCaixaExtratoPdfData data, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ct.ThrowIfCancellationRequested();

        var doc = new FechamentoCaixaExtratoTemplate(data);
        return Task.FromResult(doc.GeneratePdf());
    }
}
