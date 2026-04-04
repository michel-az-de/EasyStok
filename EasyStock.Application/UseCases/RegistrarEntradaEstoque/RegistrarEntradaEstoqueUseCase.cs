using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Specifications;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.RegistrarEntradaEstoque
{
    public sealed record RegistrarEntradaEstoqueCommand(
        Guid EmpresaId,
        Guid ProdutoId,
        Guid? ProdutoVariacaoId,
        int Quantidade,
        decimal CustoUnitario,
        decimal? PrecoVendaSugerido,
        DateTime DataEntrada,
        NaturezaMovimentacaoEstoque Natureza,
        string? CodigoInterno,
        string? CodigoLote,
        string? CodigoMarketplace,
        string? VariacaoDescricao,
        string? Cor,
        string? Tamanho,
        string? FornecedorNome,
        DateTime? Validade,
        string? Observacoes,
        string? DescricaoAnuncio,
        string? DocumentoReferencia,
        DimensoesInput? DimensoesReais,
        string? InstrucoesGeracaoDescricao);

    public sealed record RegistrarEntradaEstoqueResult(
        Guid ItemEstoqueId,
        Guid MovimentacaoId,
        string? DescricaoAnuncio,
        string ChavePesquisa);

    public class RegistrarEntradaEstoqueUseCase(
        IProdutoRepository produtoRepository,
        IProdutoVariacaoRepository produtoVariacaoRepository,
        IItemEstoqueRepository itemEstoqueRepository,
        IMovimentacaoEstoqueRepository movimentacaoEstoqueRepository,
        IUnitOfWork unitOfWork,
        ILogger<RegistrarEntradaEstoqueUseCase> logger,
        IGeradorDescricaoAnuncio? geradorDescricaoAnuncio = null)
    {
        public async Task<RegistrarEntradaEstoqueResult> ExecuteAsync(RegistrarEntradaEstoqueCommand command)
        {
            logger.LogInformation("Registrando entrada de estoque. ProdutoId: {ProdutoId}, Quantidade: {Quantidade}", command.ProdutoId, command.Quantidade);

            if (command.EmpresaId == Guid.Empty) throw new UseCaseValidationException("EmpresaId e obrigatorio.");
            if (command.Quantidade <= 0) throw new QuantidadeInvalidaException(command.Quantidade);

            var produto = await produtoRepository.GetByIdAsync(command.ProdutoId)
                ?? throw new UseCaseValidationException("Produto nao encontrado.");

            if (produto.EmpresaId != command.EmpresaId)
                throw new UseCaseValidationException("O produto informado nao pertence a empresa.");

            if (!new ProdutoAtivoSpecification().EhSatisfeitaPor(produto))
                throw new ProdutoInativoException(produto.Id);

            ProdutoVariacao? variacao = null;
            if (command.ProdutoVariacaoId.HasValue)
            {
                variacao = await produtoVariacaoRepository.GetByIdAsync(command.ProdutoVariacaoId.Value)
                    ?? throw new UseCaseValidationException("Variacao de produto nao encontrada.");

                if (variacao.ProdutoId != produto.Id)
                    throw new UseCaseValidationException("A variacao informada nao pertence ao produto.");

                if (variacao.EmpresaId != command.EmpresaId)
                    throw new UseCaseValidationException("A variacao informada nao pertence a empresa.");

                if (!variacao.Ativa)
                    throw new UseCaseValidationException("A variacao informada esta inativa.");
            }

            var quantidade = Quantidade.From(command.Quantidade);
            var descricaoAnuncio = await ResolverDescricaoAnuncioAsync(command, produto, variacao);
            var agora = DateTime.UtcNow;

            var item = ItemEstoque.CriarParaEntrada(
                Guid.NewGuid(),
                command.EmpresaId,
                produto,
                variacao,
                quantidade,
                Dinheiro.FromDecimal(command.CustoUnitario),
                command.PrecoVendaSugerido.HasValue ? Dinheiro.FromDecimal(command.PrecoVendaSugerido.Value) : null,
                command.DataEntrada,
                command.CodigoInterno,
                string.IsNullOrWhiteSpace(command.CodigoLote) ? null : CodigoLote.From(command.CodigoLote),
                command.CodigoMarketplace,
                command.VariacaoDescricao,
                command.Cor,
                command.Tamanho,
                descricaoAnuncio,
                command.DimensoesReais.ToValueObjectOrNull(),
                command.FornecedorNome,
                command.Validade.HasValue ? Validade.From(command.Validade.Value) : null,
                command.Observacoes,
                agora);

            var movimentacao = MovimentacaoEstoque.CriarEntrada(
                Guid.NewGuid(),
                command.EmpresaId,
                item,
                command.Natureza,
                quantidade,
                item.CustoUnitario,
                command.DataEntrada,
                descricaoAnuncio,
                command.DocumentoReferencia,
                agora);

            await itemEstoqueRepository.AddAsync(item);
            await movimentacaoEstoqueRepository.AddAsync(movimentacao);
            await unitOfWork.CommitAsync();

            logger.LogInformation("Entrada de estoque registrada com sucesso. ItemEstoqueId: {ItemEstoqueId}, MovimentacaoId: {MovimentacaoId}", item.Id, movimentacao.Id);

            return new RegistrarEntradaEstoqueResult(item.Id, movimentacao.Id, descricaoAnuncio, item.ChavePesquisa ?? string.Empty);
        }

        private async Task<string?> ResolverDescricaoAnuncioAsync(RegistrarEntradaEstoqueCommand command, Produto produto, ProdutoVariacao? variacao)
        {
            if (!string.IsNullOrWhiteSpace(command.DescricaoAnuncio))
                return command.DescricaoAnuncio.Trim();

            if (geradorDescricaoAnuncio is null)
                return produto.SugestaoDescricaoAnuncio;

            return await geradorDescricaoAnuncio.GerarAsync(produto, variacao, null, command.InstrucoesGeracaoDescricao);
        }
    }
}
