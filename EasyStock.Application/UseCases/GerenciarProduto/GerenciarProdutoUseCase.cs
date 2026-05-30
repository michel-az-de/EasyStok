namespace EasyStock.Application.UseCases.GerenciarProduto;

public sealed record AtualizarProdutoCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid ProdutoId,
    [property: Required] Guid CategoriaId,
    Guid? SubcategoriaId,
    [property: Required] string Nome,
    string? DescricaoBase,
    string? Marca,
    TipoProduto Tipo,
    string? SkuBase,
    string? CodigoBarras,
    bool ControlaValidade,
    DimensoesInput? Dimensoes,
    decimal? CustoReferencia,
    decimal? PrecoReferencia,
    decimal? MargemEstimada,
    string? AtributosJson,
    StatusProduto Status,
    IReadOnlyCollection<ProdutoCaracteristicaInput>? Caracteristicas,
    IReadOnlyCollection<ProdutoEmbalagemInput>? Embalagens,
    IReadOnlyCollection<ProdutoVariacaoInput>? Variacoes,
    Guid UsuarioId = default,
    string? Motivo = null,
    string? Observacao = null,
    string? ObservacaoInterna = null,
    // C2 (RDC 727/2022): default Avulso para nao quebrar callers existentes.
    TipoEmbalagem TipoEmbalagem = TipoEmbalagem.Avulso);

public sealed record ProdutoDetalheResult(
    Guid ProdutoId,
    Guid EmpresaId,
    Guid CategoriaId,
    Guid? SubcategoriaId,
    string Nome,
    string? DescricaoBase,
    string? Marca,
    TipoProduto Tipo,
    string? SkuBase,
    string? CodigoBarras,
    bool ControlaValidade,
    StatusProduto Status,
    decimal? CustoReferencia,
    decimal? PrecoReferencia,
    decimal? MargemEstimada,
    DimensoesDetalheResult? Dimensoes,
    decimal QuantidadeTotalEstoque,
    DateTime? UltimaEntradaEm,
    IReadOnlyCollection<ProdutoFotoResult> Fotos,
    IReadOnlyCollection<ProdutoVariacaoDetalheResult> Variacoes,
    IReadOnlyCollection<ProdutoCaracteristicaDetalheResult> Caracteristicas,
    IReadOnlyCollection<ProdutoEmbalagemDetalheResult> Embalagens,
    Guid? CriadoPor = null,
    Guid? AlteradoPor = null,
    string? CriadoPorNome = null,
    string? AlteradoPorNome = null,
    string? ObservacaoInterna = null,
    DateTime? CriadoEm = null,
    DateTime? AlteradoEm = null,
    int? QuantidadeMinima = null,
    int? QuantidadeCritica = null,
    // C2 (RDC 727/2022): "Avulso" (default) | "Embalado".
    TipoEmbalagem TipoEmbalagem = TipoEmbalagem.Avulso,
    // Ficha tecnica nutricional (JSON serializado via ProdutoFichaTecnica VO).
    string? AtributosJson = null);

public sealed record DimensoesDetalheResult(
    decimal Peso,
    decimal Largura,
    decimal Altura,
    decimal Comprimento);

public sealed record ProdutoVariacaoDetalheResult(
    Guid VariacaoId,
    string Nome,
    string? Cor,
    string? Tamanho,
    string? DescricaoComercial,
    string? Sku,
    string? CodigoBarras,
    bool Ativa,
    decimal QuantidadeEmEstoque,
    DateTime? UltimaEntradaEm);

public sealed record ProdutoCaracteristicaDetalheResult(
    Guid CaracteristicaId,
    string Nome,
    string? Descricao,
    int? QuantidadeReferencia,
    string? VariacaoPadrao,
    Guid? VariacaoId,
    int OrdemExibicao);

public sealed record ProdutoEmbalagemDetalheResult(
    Guid EmbalagemId,
    string Nome,
    string? Descricao,
    DimensoesDetalheResult? Dimensoes,
    bool Padrao);

public sealed record ProdutoHistoricoItemResult(
    Guid MovimentacaoId,
    TipoMovimentacaoEstoque Tipo,
    string Natureza,
    decimal Quantidade,
    decimal? ValorTotal,
    DateTime DataMovimentacao,
    Guid? ItemEstoqueId,
    string? DocumentoReferencia,
    string? Observacoes);

public sealed record ProdutoEstatisticasResult(
    Guid ProdutoId,
    decimal QuantidadeEmEstoque,
    decimal? MargemRealPercentual,
    decimal Velocidade30Dias,
    int? PrevisaoZeramentoDias,
    decimal Velocidade60Dias,
    decimal Velocidade90Dias,
    IReadOnlyCollection<SazonalidadeMensalResult> SazonalidadeMensal);

public sealed record SazonalidadeMensalResult(
    int Ano,
    int Mes,
    int TotalSaidas,
    decimal ValorTotal);

public sealed record ProdutoFotoResult(
    Guid FotoId,
    string Url,
    DateTime CriadoEm);

internal sealed record ProdutoFotoMetadata(
    Guid FotoId,
    string Url,
    string StorageKey,
    DateTime CriadoEm);

public sealed class GerenciarProdutoUseCase(
    IProdutoRepository produtoRepository,
    ICategoriaRepository categoriaRepository,
    IProdutoVariacaoRepository produtoVariacaoRepository,
    IProdutoCaracteristicaRepository caracteristicaRepository,
    IProdutoEmbalagemRepository embalagemRepository,
    IItemEstoqueRepository itemEstoqueRepository,
    IMovimentacaoEstoqueRepository movimentacaoEstoqueRepository,
    IUnitOfWork unitOfWork,
    Comandos.AtualizarLimiaresProdutoUseCase atualizarLimiaresUseCase,
    Comandos.RemoverProdutoUseCase removerUseCase,
    Comandos.RestaurarProdutoUseCase restaurarUseCase,
    Comandos.ReordenarFotosProdutoUseCase reordenarFotosUseCase,
    Queries.ObterHistoricoProdutoUseCase obterHistoricoUseCase,
    Queries.ObterEstatisticasProdutoUseCase obterEstatisticasUseCase,
    Queries.ObterDetalheProdutoUseCase obterDetalheUseCase,
    Comandos.AtualizarProdutoUseCase atualizarUseCase,
    ICacheService? cacheService = null,
    IProdutoAlteracaoRepository? alteracaoRepository = null,
    IUsuarioRepository? usuarioRepository = null,
    IPedidoRepository? pedidoRepository = null)
{
    // F9b facade: delega para Comandos.AtualizarProdutoUseCase (o maior — 239 LoC originais).
    public Task AtualizarAsync(AtualizarProdutoCommand command) => atualizarUseCase.ExecuteAsync(command);

    // F9 facade: delega para Comandos.AtualizarLimiaresProdutoUseCase.
    public Task AtualizarLimiaresAsync(Guid empresaId, Guid produtoId, int? quantidadeMinima, int? quantidadeCritica)
        => atualizarLimiaresUseCase.ExecuteAsync(empresaId, produtoId, quantidadeMinima, quantidadeCritica);

    // F9 facade: delega para Comandos.RemoverProdutoUseCase / RestaurarProdutoUseCase.
    public Task RemoverAsync(Guid empresaId, Guid produtoId, Guid usuarioId = default)
        => removerUseCase.ExecuteAsync(empresaId, produtoId, usuarioId);

    public Task RestaurarAsync(Guid empresaId, Guid produtoId, Guid usuarioId = default)
        => restaurarUseCase.ExecuteAsync(empresaId, produtoId, usuarioId);

    // F9b facade: delega para Queries.ObterDetalheProdutoUseCase.
    public Task<ProdutoDetalheResult> ObterDetalheAsync(Guid empresaId, Guid produtoId)
        => obterDetalheUseCase.ExecuteAsync(empresaId, produtoId);

    // F9b facade: delega para Queries.ObterHistoricoProdutoUseCase.
    public Task<IReadOnlyCollection<ProdutoHistoricoItemResult>> ObterHistoricoAsync(Guid empresaId, Guid produtoId)
        => obterHistoricoUseCase.ExecuteAsync(empresaId, produtoId);

    // F9b facade: delega para Queries.ObterEstatisticasProdutoUseCase.
    public Task<ProdutoEstatisticasResult> ObterEstatisticasAsync(Guid empresaId, Guid produtoId)
        => obterEstatisticasUseCase.ExecuteAsync(empresaId, produtoId);

    // F9 facade: delega para Comandos.ReordenarFotosProdutoUseCase.
    public Task ReordenarFotosAsync(Guid empresaId, Guid produtoId, Guid[] novaOrdem)
        => reordenarFotosUseCase.ExecuteAsync(empresaId, produtoId, novaOrdem);

    private static string? Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
