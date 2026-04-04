using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Specifications;
using EasyStock.Domain.ValueObjects;

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
        IGeradorDescricaoAnuncio? geradorDescricaoAnuncio = null)
    {
        public async Task<RegistrarEntradaEstoqueResult> ExecuteAsync(RegistrarEntradaEstoqueCommand command)
        {
            if (command.EmpresaId == Guid.Empty) throw new UseCaseValidationException("EmpresaId e obrigatorio.");
            if (command.Quantidade <= 0) throw new QuantidadeInvalidaException(command.Quantidade);

            var produto = await produtoRepository.GetByIdAsync(command.ProdutoId)
                ?? throw new UseCaseValidationException("Produto nao encontrado.");

            if (!new ProdutoAtivoSpecification().EhSatisfeitaPor(produto))
                throw new ProdutoInativoException(produto.Id);

            ProdutoVariacao? variacao = null;
            if (command.ProdutoVariacaoId.HasValue)
            {
                variacao = await produtoVariacaoRepository.GetByIdAsync(command.ProdutoVariacaoId.Value)
                    ?? throw new UseCaseValidationException("Variacao de produto nao encontrada.");

                if (variacao.ProdutoId != produto.Id)
                    throw new UseCaseValidationException("A variacao informada nao pertence ao produto.");
            }

            var quantidade = Quantidade.From(command.Quantidade);
            var descricaoAnuncio = await ResolverDescricaoAnuncioAsync(command, produto, variacao);
            var agora = DateTime.UtcNow;

            var item = new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = command.EmpresaId,
                ProdutoId = produto.Id,
                ProdutoVariacaoId = variacao?.Id,
                CodigoInterno = command.CodigoInterno?.Trim(),
                CodigoLote = string.IsNullOrWhiteSpace(command.CodigoLote) ? null : CodigoLote.From(command.CodigoLote),
                CodigoMarketplace = command.CodigoMarketplace?.Trim(),
                ChavePesquisa = MontarChavePesquisa(produto, variacao, command.CodigoInterno, command.CodigoMarketplace, command.CodigoLote),
                VariacaoDescricao = command.VariacaoDescricao?.Trim() ?? variacao?.DescricaoComercial ?? variacao?.Nome,
                Cor = command.Cor?.Trim() ?? variacao?.Cor,
                Tamanho = command.Tamanho?.Trim() ?? variacao?.Tamanho,
                DescricaoAnuncio = descricaoAnuncio,
                DimensoesReais = command.DimensoesReais.ToValueObjectOrNull() ?? variacao?.DimensoesPadrao ?? produto.Dimensoes,
                FornecedorNome = command.FornecedorNome?.Trim(),
                QuantidadeInicial = quantidade,
                QuantidadeAtual = quantidade,
                CustoUnitario = Dinheiro.FromDecimal(command.CustoUnitario),
                PrecoVendaSugerido = command.PrecoVendaSugerido.HasValue ? Dinheiro.FromDecimal(command.PrecoVendaSugerido.Value) : produto.PrecoReferencia,
                EntradaEm = command.DataEntrada,
                ValidadeEm = command.Validade.HasValue ? Validade.From(command.Validade.Value) : null,
                UltimaMovimentacaoEm = command.DataEntrada,
                Status = StatusItemEstoque.Ativo,
                Observacoes = command.Observacoes?.Trim(),
                CriadoEm = agora,
                AlteradoEm = agora
            };

            var movimentacao = new MovimentacaoEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = command.EmpresaId,
                ItemEstoqueId = item.Id,
                ProdutoId = produto.Id,
                ProdutoVariacaoId = variacao?.Id,
                Tipo = TipoMovimentacaoEstoque.Entrada,
                Natureza = command.Natureza,
                Quantidade = quantidade,
                ValorUnitario = item.CustoUnitario,
                ValorTotal = Dinheiro.FromDecimal(item.CustoUnitario.Valor * quantidade.Value),
                DataMovimentacao = command.DataEntrada,
                Descricao = descricaoAnuncio,
                DocumentoReferencia = command.DocumentoReferencia?.Trim(),
                CriadoEm = agora
            };

            await itemEstoqueRepository.AddAsync(item);
            await movimentacaoEstoqueRepository.AddAsync(movimentacao);
            await unitOfWork.CommitAsync();

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

        private static string MontarChavePesquisa(Produto produto, ProdutoVariacao? variacao, string? codigoInterno, string? codigoMarketplace, string? codigoLote)
        {
            var partes = new[]
            {
                codigoInterno,
                variacao?.Sku?.Value,
                produto.SkuBase?.Value,
                codigoMarketplace,
                codigoLote,
                produto.Nome,
                variacao?.Nome,
                variacao?.Cor,
                variacao?.Tamanho,
                produto.CodigoBarras,
                variacao?.CodigoBarras
            };

            return string.Join(" ", partes.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        }
    }
}
