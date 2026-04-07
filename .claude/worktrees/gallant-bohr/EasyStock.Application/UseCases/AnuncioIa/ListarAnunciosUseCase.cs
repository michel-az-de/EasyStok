using EasyStock.Application.Ports.Output.Persistence;

namespace EasyStock.Application.UseCases.AnuncioIa
{
    public sealed record ListarAnunciosQuery(Guid EmpresaId, Guid ProdutoId);

    public sealed record AnuncioIaResult(
        Guid Id,
        Guid ProdutoId,
        Guid? ProdutoVariacaoId,
        string Titulo,
        string Conteudo,
        string? InstrucoesUsadas,
        int TokensConsumidos,
        DateTime CriadoEm);

    public class ListarAnunciosUseCase(IAnuncioIaRepository anuncioIaRepository)
    {
        public async Task<IReadOnlyList<AnuncioIaResult>> ExecuteAsync(ListarAnunciosQuery query)
        {
            var anuncios = await anuncioIaRepository.GetByProdutoAsync(query.EmpresaId, query.ProdutoId);

            return anuncios
                .Select(a => new AnuncioIaResult(
                    a.Id,
                    a.ProdutoId,
                    a.ProdutoVariacaoId,
                    a.Titulo,
                    a.Conteudo,
                    a.InstrucoesUsadas,
                    a.TokensConsumidos,
                    a.CriadoEm))
                .ToList();
        }
    }
}
