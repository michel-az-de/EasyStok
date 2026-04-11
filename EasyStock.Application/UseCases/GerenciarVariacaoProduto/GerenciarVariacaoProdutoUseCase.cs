using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.GerenciarVariacaoProduto;

public sealed record CriarVariacaoProdutoCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid ProdutoId,
    [property: Required] string Nome,
    string? Cor,
    string? Tamanho,
    string? DescricaoComercial,
    string? Sku,
    string? CodigoBarras,
    string? AtributosJson,
    DimensoesInput? DimensoesPadrao,
    bool Ativa = true);

public sealed record AtualizarVariacaoProdutoCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid ProdutoId,
    [property: Required] Guid VariacaoId,
    [property: Required] string Nome,
    string? Cor,
    string? Tamanho,
    string? DescricaoComercial,
    string? Sku,
    string? CodigoBarras,
    string? AtributosJson,
    DimensoesInput? DimensoesPadrao,
    bool Ativa = true);

public sealed record VariacaoProdutoResult(
    Guid VariacaoId,
    Guid ProdutoId,
    string Nome,
    string? Cor,
    string? Tamanho,
    string? Sku,
    bool Ativa);

public sealed class GerenciarVariacaoProdutoUseCase(
    IProdutoRepository produtoRepository,
    IProdutoVariacaoRepository produtoVariacaoRepository,
    IItemEstoqueRepository itemEstoqueRepository,
    IUnitOfWork unitOfWork,
    ILogger<GerenciarVariacaoProdutoUseCase> logger)
{
    public async Task<VariacaoProdutoResult> CriarAsync(CriarVariacaoProdutoCommand command)
    {
        logger.LogInformation("Criando variacao do produto {ProdutoId}. EmpresaId: {EmpresaId}.", command.ProdutoId, command.EmpresaId);

        var produto = await ValidarProdutoAtivoAsync(command.EmpresaId, command.ProdutoId);
        await ValidarSkuAsync(command.EmpresaId, command.Sku, null);

        var agora = DateTime.UtcNow;
        var variacao = new ProdutoVariacao
        {
            Id = Guid.NewGuid(),
            EmpresaId = command.EmpresaId,
            ProdutoId = produto.Id,
            Nome = command.Nome.Trim(),
            Cor = Normalizar(command.Cor),
            Tamanho = Normalizar(command.Tamanho),
            DescricaoComercial = Normalizar(command.DescricaoComercial),
            Sku = string.IsNullOrWhiteSpace(command.Sku) ? null : CodigoSku.From(command.Sku.Trim()),
            CodigoBarras = Normalizar(command.CodigoBarras),
            AtributosJson = command.AtributosJson,
            DimensoesPadrao = command.DimensoesPadrao.ToValueObjectOrNull(),
            Ativa = command.Ativa,
            CriadoEm = agora,
            AlteradoEm = agora
        };

        await produtoVariacaoRepository.InsertAsync(variacao);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Variacao {VariacaoId} criada para produto {ProdutoId}.", variacao.Id, command.ProdutoId);

        return Map(variacao);
    }

    public async Task<VariacaoProdutoResult> AtualizarAsync(AtualizarVariacaoProdutoCommand command)
    {
        logger.LogInformation("Atualizando variacao {VariacaoId} do produto {ProdutoId}.", command.VariacaoId, command.ProdutoId);

        _ = await ValidarProdutoAtivoAsync(command.EmpresaId, command.ProdutoId);

        var variacao = await produtoVariacaoRepository.GetByIdAsync(command.EmpresaId, command.ProdutoId, command.VariacaoId)
            ?? throw new UseCaseValidationException("Variacao nao encontrada.");

        await ValidarSkuAsync(command.EmpresaId, command.Sku, command.VariacaoId);

        variacao.Nome = command.Nome.Trim();
        variacao.Cor = Normalizar(command.Cor);
        variacao.Tamanho = Normalizar(command.Tamanho);
        variacao.DescricaoComercial = Normalizar(command.DescricaoComercial);
        variacao.Sku = string.IsNullOrWhiteSpace(command.Sku) ? null : CodigoSku.From(command.Sku.Trim());
        variacao.CodigoBarras = Normalizar(command.CodigoBarras);
        variacao.AtributosJson = command.AtributosJson;
        variacao.DimensoesPadrao = command.DimensoesPadrao.ToValueObjectOrNull();
        variacao.Ativa = command.Ativa;
        variacao.AlteradoEm = DateTime.UtcNow;

        await produtoVariacaoRepository.UpdateAsync(variacao);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Variacao {VariacaoId} atualizada com sucesso.", command.VariacaoId);

        return Map(variacao);
    }

    public async Task RemoverAsync(Guid empresaId, Guid produtoId, Guid variacaoId)
    {
        logger.LogInformation("Removendo variacao {VariacaoId} do produto {ProdutoId}. EmpresaId: {EmpresaId}.", variacaoId, produtoId, empresaId);

        _ = await ValidarProdutoAtivoAsync(empresaId, produtoId);

        var variacao = await produtoVariacaoRepository.GetByIdAsync(empresaId, produtoId, variacaoId)
            ?? throw new UseCaseValidationException("Variacao nao encontrada.");

        if (await itemEstoqueRepository.ExisteEstoqueDaVariacaoAsync(empresaId, produtoId, variacaoId))
            throw new UseCaseValidationException("Nao e permitido inativar variacao com estoque disponivel.");

        variacao.Ativa = false;
        variacao.AlteradoEm = DateTime.UtcNow;
        await produtoVariacaoRepository.UpdateAsync(variacao);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Variacao {VariacaoId} inativada com sucesso.", variacaoId);
    }

    private async Task<Produto> ValidarProdutoAtivoAsync(Guid empresaId, Guid produtoId)
    {
        if (empresaId == Guid.Empty) throw new UseCaseValidationException("EmpresaId é obrigatório.");
        if (produtoId == Guid.Empty) throw new UseCaseValidationException("ProdutoId é obrigatório.");

        var produto = await produtoRepository.GetByIdAsync(empresaId, produtoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        if (produto.Status != StatusProduto.Ativo)
            throw new ProdutoInativoException(produtoId);

        return produto;
    }

    private async Task ValidarSkuAsync(Guid empresaId, string? sku, Guid? ignoreVariacaoId)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return;

        sku = sku.Trim();
        if (await produtoRepository.ExistsSkuBaseAsync(empresaId, sku) ||
            await produtoVariacaoRepository.ExistsSkuAsync(empresaId, sku, ignoreVariacaoId))
        {
            throw new UseCaseValidationException("SKU duplicado para esta empresa.");
        }
    }

    private static VariacaoProdutoResult Map(ProdutoVariacao variacao) =>
        new(
            variacao.Id,
            variacao.ProdutoId,
            variacao.Nome,
            variacao.Cor,
            variacao.Tamanho,
            variacao.Sku?.Value,
            variacao.Ativa);

    private static string? Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
