using EasyStock.Application.Ports.Output.Pdf;

namespace EasyStock.Application.UseCases.DocumentoEntrada
{
    public sealed record GerarDocumentoEntradaQuery(Guid EmpresaId, Guid ItemEstoqueId, TipoDocumentoEntrada Tipo);

    public enum TipoDocumentoEntrada { Etiqueta, Nota }

    public sealed record DocumentoEntradaPdfResult(byte[] Bytes, string ContentType, string FileName);

    /// <summary>
    /// Gera o PDF (etiqueta do lote ou Nota de Entrada) de um item de estoque entrado.
    /// Carrega ItemEstoque (com produto) + Empresa e delega o desenho ao renderer.
    /// Retorna null quando o item não existe / não pertence à empresa (anti-enumeration).
    /// </summary>
    public class GerarDocumentoEntradaUseCase(
        IItemEstoqueRepository itemEstoqueRepository,
        IEmpresaRepository empresaRepository,
        IDocumentoEntradaPdfRenderer renderer)
    {
        public async Task<DocumentoEntradaPdfResult?> ExecuteAsync(GerarDocumentoEntradaQuery query, CancellationToken ct = default)
        {
            UseCaseGuards.EnsureEmpresaId(query.EmpresaId);

            var item = await itemEstoqueRepository.GetItemComProdutoAsync(query.EmpresaId, query.ItemEstoqueId);
            if (item is null) return null;

            var empresa = await empresaRepository.GetByIdAsync(query.EmpresaId);

            var quantidade = item.QuantidadeAtual.Value;
            var custo = (decimal)item.CustoUnitario;
            var lote = item.CodigoLote?.Value;
            var numero = item.Id.ToString("N")[..8].ToUpperInvariant();

            var data = new DocumentoEntradaPdfData(
                EmpresaNome: empresa?.Nome ?? "EasyStok",
                EmpresaDocumento: empresa?.Documento,
                ProdutoNome: item.Produto?.Nome ?? "Produto",
                ProdutoSku: item.Produto?.SkuBase?.Value,
                Quantidade: quantidade,
                UnidadeLabel: item.Produto?.UnidadeMedidaBase.ToString(),
                CustoUnitario: custo,
                Total: quantidade * custo,
                LoteCodigo: lote,
                Validade: item.ValidadeEm?.DataValidade,
                DataEntrada: item.EntradaEm,
                FornecedorNome: item.FornecedorNome,
                Observacoes: item.Observacoes,
                NumeroDocumento: numero,
                // QR aponta para o lote (rastreável em /lotes); sem lote, o id do item.
                QrConteudo: lote ?? item.Id.ToString());

            var bytes = query.Tipo == TipoDocumentoEntrada.Etiqueta
                ? await renderer.RenderEtiquetaAsync(data, ct)
                : await renderer.RenderNotaAsync(data, ct);

            var nome = query.Tipo == TipoDocumentoEntrada.Etiqueta
                ? $"etiqueta-{numero}.pdf"
                : $"nota-entrada-{numero}.pdf";

            return new DocumentoEntradaPdfResult(bytes, "application/pdf", nome);
        }
    }
}
