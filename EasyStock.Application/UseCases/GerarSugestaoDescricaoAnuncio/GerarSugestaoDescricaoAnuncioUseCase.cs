using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;

namespace EasyStock.Application.UseCases.GerarSugestaoDescricaoAnuncio
{
    public sealed record GerarSugestaoDescricaoAnuncioCommand(
        Guid ProdutoId,
        Guid? ProdutoVariacaoId,
        string? InstrucoesComplementares);

    public sealed record GerarSugestaoDescricaoAnuncioResult(
        Guid ProdutoId,
        Guid? ProdutoVariacaoId,
        string DescricaoGerada);

    public class GerarSugestaoDescricaoAnuncioUseCase(
        IProdutoRepository produtoRepository,
        IProdutoVariacaoRepository produtoVariacaoRepository,
        IGeradorDescricaoAnuncio geradorDescricaoAnuncio,
        IUnitOfWork unitOfWork)
    {
        public async Task<GerarSugestaoDescricaoAnuncioResult> ExecuteAsync(GerarSugestaoDescricaoAnuncioCommand command)
        {
            var produto = await produtoRepository.GetByIdAsync(command.ProdutoId)
                ?? throw new UseCaseValidationException("Produto nao encontrado.");

            ProdutoVariacao? variacao = null;
            if (command.ProdutoVariacaoId.HasValue)
            {
                variacao = await produtoVariacaoRepository.GetByIdAsync(command.ProdutoVariacaoId.Value)
                    ?? throw new UseCaseValidationException("Variacao de produto nao encontrada.");

                if (variacao.ProdutoId != produto.Id)
                    throw new UseCaseValidationException("A variacao informada nao pertence ao produto.");
            }

            var descricao = await geradorDescricaoAnuncio.GerarAsync(produto, variacao, null, command.InstrucoesComplementares);
            produto.SugestaoDescricaoAnuncio = descricao;
            produto.AlteradoEm = DateTime.UtcNow;

            await produtoRepository.UpdateAsync(produto);
            await unitOfWork.CommitAsync();

            return new GerarSugestaoDescricaoAnuncioResult(produto.Id, variacao?.Id, descricao);
        }
    }
}
