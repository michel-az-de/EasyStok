using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.GerenciarProduto;

public sealed record AtualizarProdutoCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid ProdutoId,
    [property: Required] Guid CategoriaId,
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
    StatusProduto Status);

public sealed record ProdutoDetalheResult(
    Guid ProdutoId,
    Guid EmpresaId,
    Guid CategoriaId,
    string Nome,
    string? DescricaoBase,
    string? Marca,
    string? SkuBase,
    string? CodigoBarras,
    StatusProduto Status,
    decimal? CustoReferencia,
    decimal? PrecoReferencia,
    decimal? MargemEstimada,
    int QuantidadeTotalEstoque,
    DateTime? UltimaEntradaEm,
    IReadOnlyCollection<ProdutoFotoResult> Fotos,
    IReadOnlyCollection<ProdutoVariacaoDetalheResult> Variacoes);

public sealed record ProdutoVariacaoDetalheResult(
    Guid VariacaoId,
    string Nome,
    string? Cor,
    string? Tamanho,
    string? Sku,
    bool Ativa,
    int QuantidadeEmEstoque,
    DateTime? UltimaEntradaEm);

public sealed record ProdutoHistoricoItemResult(
    Guid MovimentacaoId,
    TipoMovimentacaoEstoque Tipo,
    string Natureza,
    int Quantidade,
    decimal? ValorTotal,
    DateTime DataMovimentacao,
    Guid? ItemEstoqueId,
    string? DocumentoReferencia,
    string? Observacoes);

public sealed record ProdutoEstatisticasResult(
    Guid ProdutoId,
    int QuantidadeEmEstoque,
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
    IItemEstoqueRepository itemEstoqueRepository,
    IMovimentacaoEstoqueRepository movimentacaoEstoqueRepository,
    IUnitOfWork unitOfWork,
    ICacheService? cacheService = null)
{
    public async Task AtualizarAsync(AtualizarProdutoCommand command)
    {
        if (command.EmpresaId == Guid.Empty) throw new UseCaseValidationException("EmpresaId e obrigatorio.");
        if (command.ProdutoId == Guid.Empty) throw new UseCaseValidationException("ProdutoId e obrigatorio.");
        if (command.CategoriaId == Guid.Empty) throw new UseCaseValidationException("CategoriaId e obrigatorio.");
        if (string.IsNullOrWhiteSpace(command.Nome)) throw new UseCaseValidationException("Nome do produto e obrigatorio.");

        var produto = await produtoRepository.GetByIdAsync(command.EmpresaId, command.ProdutoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        var categoria = await categoriaRepository.GetByIdAsync(command.CategoriaId)
            ?? throw new UseCaseValidationException("Categoria nao encontrada.");

        if (categoria.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("A categoria informada nao pertence a empresa.");

        if (!string.IsNullOrWhiteSpace(command.SkuBase))
        {
            var skuBase = command.SkuBase.Trim();
            if (await produtoRepository.ExistsSkuBaseAsync(command.EmpresaId, skuBase, command.ProdutoId) ||
                await produtoVariacaoRepository.ExistsSkuAsync(command.EmpresaId, skuBase))
            {
                throw new UseCaseValidationException("SKU duplicado para esta empresa.");
            }

            produto.SkuBase = CodigoSku.From(skuBase);
        }
        else
        {
            produto.SkuBase = null;
        }

        produto.CategoriaId = command.CategoriaId;
        produto.Nome = command.Nome.Trim();
        produto.DescricaoBase = Normalizar(command.DescricaoBase);
        produto.Marca = Normalizar(command.Marca);
        produto.Tipo = command.Tipo;
        produto.CodigoBarras = Normalizar(command.CodigoBarras);
        produto.ControlaValidade = command.ControlaValidade;
        produto.Dimensoes = command.Dimensoes.ToValueObjectOrNull();
        produto.CustoReferencia = command.CustoReferencia.HasValue ? Dinheiro.FromDecimal(command.CustoReferencia.Value) : null;
        produto.PrecoReferencia = command.PrecoReferencia.HasValue ? Dinheiro.FromDecimal(command.PrecoReferencia.Value) : null;
        produto.MargemEstimada = command.MargemEstimada;
        produto.AtributosJson = command.AtributosJson;
        produto.Status = command.Status;
        produto.AlteradoEm = DateTime.UtcNow;

        await produtoRepository.UpdateAsync(produto);
        await unitOfWork.CommitAsync();

        if (cacheService is not null)
            await cacheService.RemoveAsync(CacheKeys.ProdutoRelacionadas(command.EmpresaId, command.ProdutoId));
    }

    public async Task RemoverAsync(Guid empresaId, Guid produtoId)
    {
        var produto = await produtoRepository.GetByIdAsync(empresaId, produtoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        if (await itemEstoqueRepository.ExisteEstoqueDoProdutoAsync(empresaId, produtoId))
            throw new UseCaseValidationException("Nao e permitido inativar produto com estoque disponivel.");

        produto.Status = StatusProduto.Inativo;
        produto.AlteradoEm = DateTime.UtcNow;

        await produtoRepository.UpdateAsync(produto);
        await unitOfWork.CommitAsync();

        if (cacheService is not null)
            await cacheService.RemoveAsync(CacheKeys.ProdutoRelacionadas(empresaId, produtoId));
    }

    public async Task<ProdutoDetalheResult> ObterDetalheAsync(Guid empresaId, Guid produtoId)
    {
        var produto = await produtoRepository.GetDetalheAsync(empresaId, produtoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        var itens = await itemEstoqueRepository.GetByProdutoAsync(empresaId, produtoId);
        var variacoes = await produtoVariacaoRepository.GetByProdutoAsync(empresaId, produtoId);
        var fotos = DesserializarFotos(produto.FotosJson);

        var variacoesResult = variacoes
            .Select(variacao =>
            {
                var itensDaVariacao = itens.Where(i => i.ProdutoVariacaoId == variacao.Id).ToArray();
                return new ProdutoVariacaoDetalheResult(
                    variacao.Id,
                    variacao.Nome,
                    variacao.Cor,
                    variacao.Tamanho,
                    variacao.Sku?.Value,
                    variacao.Ativa,
                    itensDaVariacao.Sum(i => i.QuantidadeAtual.Value),
                    itensDaVariacao.OrderByDescending(i => i.EntradaEm).Select(i => (DateTime?)i.EntradaEm).FirstOrDefault());
            })
            .ToArray();

        return new ProdutoDetalheResult(
            produto.Id,
            produto.EmpresaId,
            produto.CategoriaId,
            produto.Nome,
            produto.DescricaoBase,
            produto.Marca,
            produto.SkuBase?.Value,
            produto.CodigoBarras,
            produto.Status,
            produto.CustoReferencia?.Valor,
            produto.PrecoReferencia?.Valor,
            produto.MargemEstimada,
            itens.Sum(i => i.QuantidadeAtual.Value),
            itens.OrderByDescending(i => i.EntradaEm).Select(i => (DateTime?)i.EntradaEm).FirstOrDefault(),
            fotos.Select(f => new ProdutoFotoResult(f.FotoId, f.Url, f.CriadoEm)).ToArray(),
            variacoesResult);
    }

    public async Task<IReadOnlyCollection<ProdutoHistoricoItemResult>> ObterHistoricoAsync(Guid empresaId, Guid produtoId)
    {
        _ = await produtoRepository.GetByIdAsync(empresaId, produtoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        var historico = await movimentacaoEstoqueRepository.GetByProdutoAsync(empresaId, produtoId);
        return historico
            .Select(m => new ProdutoHistoricoItemResult(
                m.Id,
                m.Tipo,
                m.Natureza.ToString(),
                m.Quantidade.Value,
                m.ValorTotal?.Valor,
                m.DataMovimentacao,
                m.ItemEstoqueId,
                m.DocumentoReferencia,
                m.Descricao))
            .ToArray();
    }

    public async Task<ProdutoEstatisticasResult> ObterEstatisticasAsync(Guid empresaId, Guid produtoId)
    {
        var produto = await produtoRepository.GetByIdAsync(empresaId, produtoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        var itens = await itemEstoqueRepository.GetByProdutoAsync(empresaId, produtoId);
        var quantidade = itens.Sum(i => i.QuantidadeAtual.Value);
        var custoTotal = itens.Sum(i => i.CustoUnitario.Valor * i.QuantidadeAtual.Value);
        var custoMedio = quantidade > 0 ? (decimal?)(custoTotal / quantidade) : produto.CustoReferencia?.Valor;
        var precoReferencia = produto.PrecoReferencia?.Valor;
        var margemReal = precoReferencia.HasValue && precoReferencia.Value > 0m && custoMedio.HasValue
            ? (decimal?)decimal.Round(((precoReferencia.Value - custoMedio.Value) / precoReferencia.Value) * 100m, 2)
            : null;

        var agora = DateTime.UtcNow;
        var vel30 = await movimentacaoEstoqueRepository.GetTaxaSaidaDiariaAsync(empresaId, produtoId, agora.AddDays(-30), agora);
        var vel60 = await movimentacaoEstoqueRepository.GetTaxaSaidaDiariaAsync(empresaId, produtoId, agora.AddDays(-60), agora);
        var vel90 = await movimentacaoEstoqueRepository.GetTaxaSaidaDiariaAsync(empresaId, produtoId, agora.AddDays(-90), agora);
        var previsao = vel30 <= 0m ? null : (int?)Math.Floor(quantidade / vel30);
        var sazonalidade = await movimentacaoEstoqueRepository.GetAgregacaoMensalAsync(empresaId, produtoId, 12);

        return new ProdutoEstatisticasResult(
            produtoId,
            quantidade,
            margemReal,
            decimal.Round(vel30, 2),
            previsao,
            decimal.Round(vel60, 2),
            decimal.Round(vel90, 2),
            sazonalidade
                .Select(x => new SazonalidadeMensalResult(x.Ano, x.Mes, x.TotalSaidas, x.ValorTotal))
                .ToArray());
    }

    internal static IReadOnlyCollection<ProdutoFotoMetadata> DesserializarFotos(string? fotosJson)
    {
        if (string.IsNullOrWhiteSpace(fotosJson))
            return [];

        return JsonSerializer.Deserialize<List<ProdutoFotoMetadata>>(fotosJson) ?? [];
    }

    internal static string SerializarFotos(IEnumerable<ProdutoFotoMetadata> fotos) =>
        JsonSerializer.Serialize(fotos.OrderByDescending(f => f.CriadoEm));

    private static string? Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
