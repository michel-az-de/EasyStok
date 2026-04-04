using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;

namespace EasyStock.Application.UseCases.BuscarEstoqueInteligente
{
    public enum TipoResultadoBuscaInteligente
    {
        Produto,
        Variacao,
        ItemEstoque
    }

    public sealed record BuscarEstoqueInteligenteQuery(Guid EmpresaId, string Termo);

    public sealed record ResultadoBuscaInteligente(
        TipoResultadoBuscaInteligente Tipo,
        Guid Id,
        Guid ProdutoId,
        Guid? ProdutoVariacaoId,
        string Titulo,
        string? Subtitulo,
        string ChaveExibicao,
        int Score);

    public class BuscarEstoqueInteligenteUseCase(
        IProdutoRepository produtoRepository,
        IProdutoVariacaoRepository produtoVariacaoRepository,
        IItemEstoqueRepository itemEstoqueRepository)
    {
        public async Task<IReadOnlyCollection<ResultadoBuscaInteligente>> ExecuteAsync(BuscarEstoqueInteligenteQuery query)
        {
            if (query.EmpresaId == Guid.Empty) throw new UseCaseValidationException("EmpresaId e obrigatorio.");
            if (string.IsNullOrWhiteSpace(query.Termo)) return [];

            var termo = query.Termo.Trim();
            var produtos = await produtoRepository.SearchAsync(query.EmpresaId, termo);
            var variacoes = await produtoVariacaoRepository.SearchAsync(query.EmpresaId, termo);
            var itens = await itemEstoqueRepository.SearchAsync(query.EmpresaId, termo);

            var resultados = new List<ResultadoBuscaInteligente>();
            resultados.AddRange(produtos.Select(p => CriarResultadoProduto(p, termo)));
            resultados.AddRange(variacoes.Select(v => CriarResultadoVariacao(v, termo)));
            resultados.AddRange(itens.Select(i => CriarResultadoItemEstoque(i, termo)));

            return resultados
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.Titulo)
                .ToArray();
        }

        private static ResultadoBuscaInteligente CriarResultadoProduto(Produto produto, string termo) =>
            new(
                TipoResultadoBuscaInteligente.Produto,
                produto.Id,
                produto.Id,
                null,
                produto.Nome,
                produto.Marca,
                produto.SkuBase?.Value ?? produto.CodigoBarras ?? produto.Nome,
                CalcularScore(termo, produto.Nome, produto.SkuBase?.Value, produto.CodigoBarras, produto.Marca, produto.DescricaoBase));

        private static ResultadoBuscaInteligente CriarResultadoVariacao(ProdutoVariacao variacao, string termo) =>
            new(
                TipoResultadoBuscaInteligente.Variacao,
                variacao.Id,
                variacao.ProdutoId,
                variacao.Id,
                variacao.Nome,
                $"{variacao.Cor} {variacao.Tamanho}".Trim(),
                variacao.Sku?.Value ?? variacao.CodigoBarras ?? variacao.Nome,
                CalcularScore(termo, variacao.Nome, variacao.Sku?.Value, variacao.CodigoBarras, variacao.Cor, variacao.Tamanho, variacao.DescricaoComercial));

        private static ResultadoBuscaInteligente CriarResultadoItemEstoque(ItemEstoque item, string termo) =>
            new(
                TipoResultadoBuscaInteligente.ItemEstoque,
                item.Id,
                item.ProdutoId,
                item.ProdutoVariacaoId,
                item.CodigoInterno ?? item.VariacaoDescricao ?? "Item de estoque",
                item.DescricaoAnuncio,
                item.ChavePesquisa ?? item.CodigoMarketplace ?? item.CodigoInterno ?? item.Id.ToString(),
                CalcularScore(termo, item.CodigoInterno, item.CodigoMarketplace, item.ChavePesquisa, item.VariacaoDescricao, item.DescricaoAnuncio, item.Cor, item.Tamanho));

        private static int CalcularScore(string termo, params string?[] candidatos)
        {
            var termoNormalizado = termo.Trim().ToUpperInvariant();
            var score = 0;

            foreach (var candidato in candidatos.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                var valor = candidato!.Trim().ToUpperInvariant();
                if (valor == termoNormalizado) score += 100;
                else if (valor.StartsWith(termoNormalizado)) score += 60;
                else if (valor.Contains(termoNormalizado)) score += 30;
            }

            return score;
        }
    }
}
