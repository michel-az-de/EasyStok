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
    IReadOnlyCollection<ProdutoVariacaoInput>? Variacoes);

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
    int QuantidadeTotalEstoque,
    DateTime? UltimaEntradaEm,
    IReadOnlyCollection<ProdutoFotoResult> Fotos,
    IReadOnlyCollection<ProdutoVariacaoDetalheResult> Variacoes,
    IReadOnlyCollection<ProdutoCaracteristicaDetalheResult> Caracteristicas,
    IReadOnlyCollection<ProdutoEmbalagemDetalheResult> Embalagens);

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
    int QuantidadeEmEstoque,
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
    IProdutoCaracteristicaRepository caracteristicaRepository,
    IProdutoEmbalagemRepository embalagemRepository,
    IItemEstoqueRepository itemEstoqueRepository,
    IMovimentacaoEstoqueRepository movimentacaoEstoqueRepository,
    IUnitOfWork unitOfWork,
    ICacheService? cacheService = null)
{
    public async Task AtualizarAsync(AtualizarProdutoCommand command)
    {
        if (command.EmpresaId == Guid.Empty) throw new UseCaseValidationException("EmpresaId é obrigatório.");
        if (command.ProdutoId == Guid.Empty) throw new UseCaseValidationException("ProdutoId é obrigatório.");
        if (command.CategoriaId == Guid.Empty) throw new UseCaseValidationException("CategoriaId é obrigatório.");
        if (string.IsNullOrWhiteSpace(command.Nome)) throw new UseCaseValidationException("Nome do produto é obrigatório.");

        var produto = await produtoRepository.GetByIdAsync(command.EmpresaId, command.ProdutoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        var categoria = await categoriaRepository.GetByIdAsync(command.CategoriaId)
            ?? throw new UseCaseValidationException("Categoria nao encontrada.");

        if (categoria.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("A categoria informada nao pertence a empresa.");

        if (command.SubcategoriaId.HasValue)
        {
            var subcategoria = await categoriaRepository.GetByIdAsync(command.SubcategoriaId.Value)
                ?? throw new UseCaseValidationException("Subcategoria nao encontrada.");
            if (subcategoria.EmpresaId != command.EmpresaId)
                throw new UseCaseValidationException("A subcategoria informada nao pertence a empresa.");
            if (subcategoria.CategoriaPaiId != command.CategoriaId)
                throw new UseCaseValidationException("A subcategoria nao pertence a categoria informada.");
        }

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
        produto.SubcategoriaId = command.SubcategoriaId;
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

        try
        {
            await produtoRepository.UpdateAsync(produto);

            // Replace caracteristicas — batch delete + re-insert
            if (command.Caracteristicas is not null)
            {
                await caracteristicaRepository.DeleteByProdutoAsync(command.EmpresaId, command.ProdutoId);

                var agora = DateTime.UtcNow;
                foreach (var input in command.Caracteristicas)
                {
                    await caracteristicaRepository.InsertAsync(new ProdutoCaracteristica
                    {
                        Id = Guid.NewGuid(),
                        EmpresaId = command.EmpresaId,
                        ProdutoId = command.ProdutoId,
                        Nome = input.Nome.Trim(),
                        Descricao = input.Descricao?.Trim(),
                        QuantidadeReferencia = input.QuantidadeReferencia,
                        VariacaoPadrao = input.VariacaoPadrao?.Trim(),
                        OrdemExibicao = input.OrdemExibicao,
                        CriadoEm = agora,
                        AlteradoEm = agora
                    });
                }
            }

            // Replace embalagens — batch delete + re-insert
            if (command.Embalagens is not null)
            {
                if (command.Embalagens.Count(e => e.Padrao) > 1)
                    throw new UseCaseValidationException("Somente uma embalagem pode ser marcada como padrao.");

                await embalagemRepository.DeleteByProdutoAsync(command.EmpresaId, command.ProdutoId);

                var agora = DateTime.UtcNow;
                foreach (var input in command.Embalagens)
                {
                    await embalagemRepository.InsertAsync(new ProdutoEmbalagem
                    {
                        Id = Guid.NewGuid(),
                        EmpresaId = command.EmpresaId,
                        ProdutoId = command.ProdutoId,
                        Nome = input.Nome.Trim(),
                        Descricao = input.Descricao?.Trim(),
                        Dimensoes = input.Dimensoes.ToValueObjectOrNull(),
                        Padrao = input.Padrao,
                        CriadoEm = agora,
                        AlteradoEm = agora
                    });
                }
            }

            // Replace variacoes — batch delete + re-insert
            if (command.Variacoes is not null)
            {
                await produtoVariacaoRepository.DeleteByProdutoAsync(command.EmpresaId, command.ProdutoId);

                var agora = DateTime.UtcNow;
                foreach (var variacao in command.Variacoes)
                {
                    await produtoVariacaoRepository.InsertAsync(new ProdutoVariacao
                    {
                        Id = Guid.NewGuid(),
                        EmpresaId = command.EmpresaId,
                        ProdutoId = command.ProdutoId,
                        Nome = variacao.Nome.Trim(),
                        Cor = variacao.Cor?.Trim(),
                        Tamanho = variacao.Tamanho?.Trim(),
                        DescricaoComercial = variacao.DescricaoComercial?.Trim(),
                        Sku = string.IsNullOrWhiteSpace(variacao.Sku) ? null : CodigoSku.From(variacao.Sku),
                        CodigoBarras = variacao.CodigoBarras?.Trim(),
                        AtributosJson = variacao.AtributosJson,
                        DimensoesPadrao = variacao.DimensoesPadrao.ToValueObjectOrNull(),
                        Ativa = variacao.Ativa,
                        CriadoEm = agora,
                        AlteradoEm = agora
                    });
                }
            }

            await unitOfWork.CommitAsync();
        }
        catch (UseCaseValidationException)
        {
            throw; // deixa subir sem logar como erro inesperado
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Erro ao atualizar produto {command.ProdutoId}: {ex.Message}", ex);
        }

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
                    variacao.DescricaoComercial,
                    variacao.Sku?.Value,
                    variacao.CodigoBarras,
                    variacao.Ativa,
                    itensDaVariacao.Sum(i => i.QuantidadeAtual.Value),
                    itensDaVariacao.OrderByDescending(i => i.EntradaEm).Select(i => (DateTime?)i.EntradaEm).FirstOrDefault());
            })
            .ToArray();

        var caracteristicasResult = (produto.Caracteristicas ?? [])
            .OrderBy(c => c.OrdemExibicao)
            .Select(c => new ProdutoCaracteristicaDetalheResult(
                c.Id, c.Nome, c.Descricao, c.QuantidadeReferencia, c.VariacaoPadrao, c.VariacaoId, c.OrdemExibicao))
            .ToArray();

        var embalagensResult = (produto.Embalagens ?? [])
            .Select(e => new ProdutoEmbalagemDetalheResult(
                e.Id, e.Nome, e.Descricao,
                e.Dimensoes is null ? null : new DimensoesDetalheResult(e.Dimensoes.Peso, e.Dimensoes.Largura, e.Dimensoes.Altura, e.Dimensoes.Comprimento),
                e.Padrao))
            .ToArray();

        return new ProdutoDetalheResult(
            produto.Id,
            produto.EmpresaId,
            produto.CategoriaId,
            produto.SubcategoriaId,
            produto.Nome,
            produto.DescricaoBase,
            produto.Marca,
            produto.Tipo,
            produto.SkuBase?.Value,
            produto.CodigoBarras,
            produto.ControlaValidade,
            produto.Status,
            produto.CustoReferencia?.Valor,
            produto.PrecoReferencia?.Valor,
            produto.MargemEstimada,
            produto.Dimensoes is null ? null : new DimensoesDetalheResult(produto.Dimensoes.Peso, produto.Dimensoes.Largura, produto.Dimensoes.Altura, produto.Dimensoes.Comprimento),
            itens.Sum(i => i.QuantidadeAtual.Value),
            itens.OrderByDescending(i => i.EntradaEm).Select(i => (DateTime?)i.EntradaEm).FirstOrDefault(),
            fotos.Select(f => new ProdutoFotoResult(f.FotoId, f.Url, f.CriadoEm)).ToArray(),
            variacoesResult,
            caracteristicasResult,
            embalagensResult);
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
