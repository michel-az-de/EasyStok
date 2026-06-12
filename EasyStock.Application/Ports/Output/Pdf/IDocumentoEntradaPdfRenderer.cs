namespace EasyStock.Application.Ports.Output.Pdf;

/// <summary>
/// Dados (já resolvidos) para renderizar os documentos de uma entrada de estoque:
/// a etiqueta do lote e a Nota de Entrada. Sem dependência de entidade — a camada
/// Application monta a partir de ItemEstoque + Empresa e o renderer só desenha.
/// </summary>
public sealed record DocumentoEntradaPdfData(
    string EmpresaNome,
    string? EmpresaDocumento,
    string ProdutoNome,
    string? ProdutoSku,
    decimal Quantidade,
    string? UnidadeLabel,
    decimal CustoUnitario,
    decimal Total,
    string? LoteCodigo,
    DateTime? Validade,
    DateTime DataEntrada,
    string? FornecedorNome,
    string? Observacoes,
    string NumeroDocumento,
    string QrConteudo);

/// <summary>
/// Renderer dos PDFs da entrada de estoque (etiqueta + Nota de Entrada), com QRCode.
/// Implementação em <c>EasyStock.Infra.Async/Pdf/DocumentoEntradaPdfRenderer.cs</c>
/// (QuestPDF + QRCoder). Stateless e threadsafe — pode ser Singleton no DI.
/// </summary>
public interface IDocumentoEntradaPdfRenderer
{
    /// <summary>Etiqueta do lote (rótulo compacto com QRCode do código do lote).</summary>
    Task<byte[]> RenderEtiquetaAsync(DocumentoEntradaPdfData data, CancellationToken ct = default);

    /// <summary>Nota de Entrada (documento A4 com emissor, item, totais e QRCode).</summary>
    Task<byte[]> RenderNotaAsync(DocumentoEntradaPdfData data, CancellationToken ct = default);
}
