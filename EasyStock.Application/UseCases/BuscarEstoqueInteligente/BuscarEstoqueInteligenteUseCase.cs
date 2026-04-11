using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;

namespace EasyStock.Application.UseCases.BuscarEstoqueInteligente
{
    public enum TipoResultadoBuscaInteligente
    {
        Produto,
        Variacao,
        ItemEstoque,
        Fornecedor
    }

    public sealed record BuscarEstoqueInteligenteQuery(Guid EmpresaId, string Termo, int Limite = 50);

    public sealed record ResultadoBuscaInteligente(
        TipoResultadoBuscaInteligente Tipo,
        Guid Id,
        Guid ProdutoId,
        Guid? ProdutoVariacaoId,
        string Titulo,
        string? Subtitulo,
        string ChaveExibicao,
        int Score,
        string? Sku = null,
        int? QuantidadeAtual = null,
        string? Status = null,
        string? FornecedorNome = null,
        string? Loja = null);

    public class BuscarEstoqueInteligenteUseCase(
        IProdutoRepository produtoRepository,
        IProdutoVariacaoRepository produtoVariacaoRepository,
        IItemEstoqueRepository itemEstoqueRepository,
        IFornecedorRepository fornecedorRepository)
    {
        public async Task<IReadOnlyCollection<ResultadoBuscaInteligente>> ExecuteAsync(BuscarEstoqueInteligenteQuery query)
        {
            if (query.EmpresaId == Guid.Empty) throw new UseCaseValidationException("EmpresaId e obrigatorio.");
            if (string.IsNullOrWhiteSpace(query.Termo)) return [];

            var termo = query.Termo.Trim();
            var tProdutos = produtoRepository.SearchAsync(query.EmpresaId, termo);
            var tVariacoes = produtoVariacaoRepository.SearchAsync(query.EmpresaId, termo);
            var tItens = itemEstoqueRepository.SearchAsync(query.EmpresaId, termo);
            var tFornecedores = fornecedorRepository.SearchAsync(query.EmpresaId, termo);
            await Task.WhenAll(tProdutos, tVariacoes, tItens, tFornecedores);

            var produtos = tProdutos.Result;
            var variacoes = tVariacoes.Result;
            var itens = tItens.Result;
            var fornecedores = tFornecedores.Result;

            var resultados = new List<ResultadoBuscaInteligente>();
            resultados.AddRange(produtos.Select(p => CriarResultadoProduto(p, termo)));
            resultados.AddRange(variacoes.Select(v => CriarResultadoVariacao(v, termo)));
            resultados.AddRange(itens.Select(i => CriarResultadoItemEstoque(i, termo)));
            resultados.AddRange(fornecedores.Select(f => CriarResultadoFornecedor(f, termo)));

            return resultados
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.Titulo)
                .Take(query.Limite)
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
                CalcularScore(termo, produto.Nome, produto.SkuBase?.Value, produto.CodigoBarras, produto.Marca, produto.DescricaoBase),
                Sku: produto.SkuBase?.Value ?? produto.CodigoBarras);

        private static ResultadoBuscaInteligente CriarResultadoVariacao(ProdutoVariacao variacao, string termo) =>
            new(
                TipoResultadoBuscaInteligente.Variacao,
                variacao.Id,
                variacao.ProdutoId,
                variacao.Id,
                variacao.Nome,
                $"{variacao.Cor} {variacao.Tamanho}".Trim(),
                variacao.Sku?.Value ?? variacao.CodigoBarras ?? variacao.Nome,
                CalcularScore(termo, variacao.Nome, variacao.Sku?.Value, variacao.CodigoBarras, variacao.Cor, variacao.Tamanho, variacao.DescricaoComercial),
                Sku: variacao.Sku?.Value ?? variacao.CodigoBarras);

        private static ResultadoBuscaInteligente CriarResultadoItemEstoque(ItemEstoque item, string termo) =>
            new(
                TipoResultadoBuscaInteligente.ItemEstoque,
                item.Id,
                item.ProdutoId,
                item.ProdutoVariacaoId,
                item.CodigoInterno ?? item.VariacaoDescricao ?? "Item de estoque",
                item.DescricaoAnuncio,
                item.ChavePesquisa ?? item.CodigoMarketplace ?? item.CodigoInterno ?? item.Id.ToString(),
                CalcularScore(termo, item.CodigoInterno, item.CodigoMarketplace, item.ChavePesquisa, item.VariacaoDescricao, item.DescricaoAnuncio, item.Cor, item.Tamanho),
                Sku: item.CodigoInterno ?? item.CodigoMarketplace,
                QuantidadeAtual: item.QuantidadeAtual?.Value,
                Status: item.Status.ToString(),
                FornecedorNome: item.FornecedorNome);

        private static ResultadoBuscaInteligente CriarResultadoFornecedor(EasyStock.Domain.Entities.Fornecedor fornecedor, string termo) =>
            new(
                TipoResultadoBuscaInteligente.Fornecedor,
                fornecedor.Id,
                fornecedor.Id,
                null,
                fornecedor.Nome,
                fornecedor.Email ?? fornecedor.Documento,
                fornecedor.Documento ?? fornecedor.Email ?? fornecedor.Nome,
                CalcularScore(termo, fornecedor.Nome, fornecedor.Documento, fornecedor.Email, fornecedor.Contato));

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
