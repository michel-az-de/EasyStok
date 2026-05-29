namespace EasyStock.Application.UseCases.BuscarEstoqueInteligente
{
    public enum TipoResultadoBuscaInteligente
    {
        Produto,
        Variacao,
        ItemEstoque,
        Fornecedor,
        Pedido,
        Entrada,
        Saida,
        Loja,
        Usuario
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
        decimal? QuantidadeAtual = null,
        string? Status = null,
        string? FornecedorNome = null,
        string? Loja = null);

    public class BuscarEstoqueInteligenteUseCase(
        IProdutoRepository produtoRepository,
        IProdutoVariacaoRepository produtoVariacaoRepository,
        IItemEstoqueRepository itemEstoqueRepository,
        IFornecedorRepository fornecedorRepository,
        IPedidoFornecedorRepository pedidoRepository,
        ILojaRepository lojaRepository,
        IUsuarioRepository usuarioRepository,
        IMovimentacaoEstoqueRepository movimentacaoRepository)
    {
        public async Task<IReadOnlyCollection<ResultadoBuscaInteligente>> ExecuteAsync(BuscarEstoqueInteligenteQuery query)
        {
            UseCaseGuards.EnsureEmpresaId(query.EmpresaId);
            if (string.IsNullOrWhiteSpace(query.Termo)) return [];

            var termo = query.Termo.Trim();

            // Busca sequencial — DbContext nao e thread-safe para queries paralelas
            var produtos = await produtoRepository.SearchAsync(query.EmpresaId, termo);
            var variacoes = await produtoVariacaoRepository.SearchAsync(query.EmpresaId, termo);
            var itens = await itemEstoqueRepository.SearchAsync(query.EmpresaId, termo);
            var fornecedores = await fornecedorRepository.SearchAsync(query.EmpresaId, termo);
            var pedidos = await pedidoRepository.SearchAsync(query.EmpresaId, termo);
            var lojas = await lojaRepository.SearchAsync(query.EmpresaId, termo);
            var usuarios = await usuarioRepository.SearchAsync(query.EmpresaId, termo);
            var movimentacoes = await movimentacaoRepository.SearchAsync(query.EmpresaId, termo);

            var resultados = new List<ResultadoBuscaInteligente>();
            resultados.AddRange(produtos.Select(p => CriarResultadoProduto(p, termo)));
            resultados.AddRange(variacoes.Select(v => CriarResultadoVariacao(v, termo)));
            resultados.AddRange(itens.Select(i => CriarResultadoItemEstoque(i, termo)));
            resultados.AddRange(fornecedores.Select(f => CriarResultadoFornecedor(f, termo)));
            resultados.AddRange(pedidos.Select(p => CriarResultadoPedido(p, termo)));
            resultados.AddRange(lojas.Select(l => CriarResultadoLoja(l, termo)));
            resultados.AddRange(usuarios.Select(u => CriarResultadoUsuario(u, termo)));
            resultados.AddRange(movimentacoes.Select(m => CriarResultadoMovimentacao(m, termo)));

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

        private static ResultadoBuscaInteligente CriarResultadoPedido(PedidoFornecedor pedido, string termo) =>
            new(
                TipoResultadoBuscaInteligente.Pedido,
                pedido.Id,
                pedido.Id,
                null,
                $"Pedido {pedido.DataPedido:dd/MM/yyyy}",
                pedido.Fornecedor?.Nome ?? pedido.Observacoes,
                pedido.Tracking ?? pedido.Id.ToString()[..8],
                CalcularScore(termo, pedido.Observacoes, pedido.Tracking, pedido.Canal, pedido.Fornecedor?.Nome),
                Status: pedido.Status.ToString(),
                FornecedorNome: pedido.Fornecedor?.Nome);

        private static ResultadoBuscaInteligente CriarResultadoLoja(Domain.Entities.Loja loja, string termo) =>
            new(
                TipoResultadoBuscaInteligente.Loja,
                loja.Id,
                loja.Id,
                null,
                loja.Nome,
                loja.Endereco ?? loja.Documento,
                loja.Nome,
                CalcularScore(termo, loja.Nome, loja.Documento, loja.Endereco),
                Status: loja.Ativa ? "Ativa" : "Inativa");

        private static ResultadoBuscaInteligente CriarResultadoUsuario(Usuario usuario, string termo) =>
            new(
                TipoResultadoBuscaInteligente.Usuario,
                usuario.Id,
                usuario.Id,
                null,
                usuario.Nome,
                usuario.Email,
                usuario.Email,
                CalcularScore(termo, usuario.Nome, usuario.Email),
                Status: usuario.Ativo ? "Ativo" : "Inativo");

        private static ResultadoBuscaInteligente CriarResultadoMovimentacao(MovimentacaoEstoque mov, string termo)
        {
            var isEntrada = mov.Natureza.ToString().Contains("Entrada", StringComparison.OrdinalIgnoreCase);
            return new(
                isEntrada ? TipoResultadoBuscaInteligente.Entrada : TipoResultadoBuscaInteligente.Saida,
                mov.Id,
                mov.ProdutoId,
                mov.ProdutoVariacaoId,
                $"{(isEntrada ? "Entrada" : "Saida")} — {mov.Produto?.Nome ?? "Produto"}",
                $"{mov.Quantidade?.Value ?? 0} un. em {mov.DataMovimentacao:dd/MM/yyyy}",
                mov.DocumentoReferencia ?? mov.Descricao ?? mov.Id.ToString()[..8],
                CalcularScore(termo, mov.Descricao, mov.DocumentoReferencia, mov.Produto?.Nome),
                QuantidadeAtual: mov.Quantidade?.Value);
        }

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
