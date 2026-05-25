using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Pdf;

/// <summary>
/// Renderer de PDF de uma <see cref="Fatura"/> — abstracao para que a camada
/// Application/Api dispare a geracao sem depender de detalhes da lib (QuestPDF).
///
/// <para>
/// Implementacao em <c>EasyStock.Infra.Async/Pdf/FaturaPdfRenderer.cs</c>.
/// Layout fiscal-friendly (cabecalho emissor, dados faturado, tabela itens,
/// totais, formas de pagamento, observacoes, rodape com hash) — preparado
/// para futura emissao de NFS-e quando o adapter fiscal estiver pronto.
/// </para>
/// </summary>
public interface IFaturaPdfRenderer
{
    /// <summary>
    /// Renderiza a fatura para um buffer PDF em memoria. Idempotente —
    /// dado o mesmo input produz o mesmo byte[] (importante para snapshot
    /// tests). Threadsafe.
    /// </summary>
    Task<byte[]> RenderAsync(Fatura fatura, CancellationToken ct = default);
}
