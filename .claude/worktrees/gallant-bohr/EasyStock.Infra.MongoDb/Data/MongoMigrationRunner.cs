using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.Data;

public sealed class MongoMigrationRunner(MongoEasyStockContext context)
{
    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync(context.GetCollection<EasyStock.Domain.Entities.Usuario>(MongoCollectionNames.Usuarios),
            [new CreateIndexModel<EasyStock.Domain.Entities.Usuario>(
                Builders<EasyStock.Domain.Entities.Usuario>.IndexKeys.Ascending(x => x.Email),
                new CreateIndexOptions { Unique = true, Name = "ux_usuarios_email" })],
            cancellationToken);

        await EnsureIndexesAsync(context.GetCollection<EasyStock.Domain.Entities.Produto>(MongoCollectionNames.Produtos),
            [
                new CreateIndexModel<EasyStock.Domain.Entities.Produto>(
                    Builders<EasyStock.Domain.Entities.Produto>.IndexKeys
                        .Ascending(x => x.EmpresaId)
                        .Ascending(x => x.Nome),
                    new CreateIndexOptions { Name = "ix_produtos_empresa_nome" }),
                new CreateIndexModel<EasyStock.Domain.Entities.Produto>(
                    Builders<EasyStock.Domain.Entities.Produto>.IndexKeys
                        .Text(x => x.Nome)
                        .Text(x => x.Marca)
                        .Text(x => x.DescricaoBase)
                        .Text(x => x.CodigoBarras),
                    new CreateIndexOptions { Name = "ix_produtos_busca_texto" })
            ],
            cancellationToken);

        await EnsureIndexesAsync(context.GetCollection<EasyStock.Domain.Entities.ProdutoVariacao>(MongoCollectionNames.ProdutosVariacao),
            [
                new CreateIndexModel<EasyStock.Domain.Entities.ProdutoVariacao>(
                    Builders<EasyStock.Domain.Entities.ProdutoVariacao>.IndexKeys
                        .Ascending(x => x.EmpresaId)
                        .Ascending(x => x.ProdutoId),
                    new CreateIndexOptions { Name = "ix_variacoes_empresa_produto" }),
                new CreateIndexModel<EasyStock.Domain.Entities.ProdutoVariacao>(
                    Builders<EasyStock.Domain.Entities.ProdutoVariacao>.IndexKeys
                        .Text(x => x.Nome)
                        .Text(x => x.Cor)
                        .Text(x => x.Tamanho)
                        .Text(x => x.DescricaoComercial)
                        .Text(x => x.CodigoBarras),
                    new CreateIndexOptions { Name = "ix_variacoes_busca_texto" })
            ],
            cancellationToken);

        await EnsureIndexesAsync(context.GetCollection<EasyStock.Domain.Entities.ItemEstoque>(MongoCollectionNames.ItensEstoque),
            [
                new CreateIndexModel<EasyStock.Domain.Entities.ItemEstoque>(
                    Builders<EasyStock.Domain.Entities.ItemEstoque>.IndexKeys.Ascending(x => x.EmpresaId),
                    new CreateIndexOptions { Name = "ix_itens_empresa" }),
                new CreateIndexModel<EasyStock.Domain.Entities.ItemEstoque>(
                    Builders<EasyStock.Domain.Entities.ItemEstoque>.IndexKeys.Ascending(x => x.CodigoInterno),
                    new CreateIndexOptions { Name = "ix_itens_codigo_interno", Sparse = true }),
                new CreateIndexModel<EasyStock.Domain.Entities.ItemEstoque>(
                    Builders<EasyStock.Domain.Entities.ItemEstoque>.IndexKeys.Ascending(x => x.CodigoMarketplace),
                    new CreateIndexOptions { Name = "ix_itens_codigo_marketplace", Sparse = true }),
                new CreateIndexModel<EasyStock.Domain.Entities.ItemEstoque>(
                    Builders<EasyStock.Domain.Entities.ItemEstoque>.IndexKeys
                        .Text(x => x.CodigoInterno)
                        .Text(x => x.CodigoMarketplace)
                        .Text(x => x.ChavePesquisa)
                        .Text(x => x.VariacaoDescricao)
                        .Text(x => x.Cor)
                        .Text(x => x.Tamanho)
                        .Text(x => x.DescricaoAnuncio),
                    new CreateIndexOptions { Name = "ix_itens_busca_texto" })
            ],
            cancellationToken);

        await EnsureIndexesAsync(context.GetCollection<EasyStock.Domain.Entities.MovimentacaoEstoque>(MongoCollectionNames.MovimentacoesEstoque),
            [new CreateIndexModel<EasyStock.Domain.Entities.MovimentacaoEstoque>(
                Builders<EasyStock.Domain.Entities.MovimentacaoEstoque>.IndexKeys
                    .Ascending(x => x.EmpresaId)
                    .Ascending(x => x.DataMovimentacao),
                new CreateIndexOptions { Name = "ix_movimentacoes_empresa_data" })],
            cancellationToken);

        await EnsureIndexesAsync(context.GetCollection<EasyStock.Domain.Entities.Notificacao>(MongoCollectionNames.Notificacoes),
            [new CreateIndexModel<EasyStock.Domain.Entities.Notificacao>(
                Builders<EasyStock.Domain.Entities.Notificacao>.IndexKeys
                    .Ascending(x => x.EmpresaId)
                    .Ascending(x => x.Lida)
                    .Ascending(x => x.TipoAlerta)
                    .Ascending(x => x.ReferenciaId),
                new CreateIndexOptions { Name = "ix_notificacoes_empresa_tipo_referencia" })],
            cancellationToken);

        await EnsureIndexesAsync(context.GetCollection<EasyStock.Domain.Entities.ConfiguracaoLoja>(MongoCollectionNames.ConfiguracoesLoja),
            [new CreateIndexModel<EasyStock.Domain.Entities.ConfiguracaoLoja>(
                Builders<EasyStock.Domain.Entities.ConfiguracaoLoja>.IndexKeys
                    .Ascending(x => x.LojaId),
                new CreateIndexOptions { Name = "ux_configuracoes_loja_loja", Unique = true })],
            cancellationToken);

        await EnsureIndexesAsync(context.GetCollection<EasyStock.Domain.Entities.Fornecedor>(MongoCollectionNames.Fornecedores),
            [
                new CreateIndexModel<EasyStock.Domain.Entities.Fornecedor>(
                    Builders<EasyStock.Domain.Entities.Fornecedor>.IndexKeys
                        .Ascending(x => x.EmpresaId)
                        .Ascending(x => x.Nome),
                    new CreateIndexOptions { Name = "ix_fornecedores_empresa_nome" }),
                new CreateIndexModel<EasyStock.Domain.Entities.Fornecedor>(
                    Builders<EasyStock.Domain.Entities.Fornecedor>.IndexKeys
                        .Text(x => x.Nome)
                        .Text(x => x.Documento)
                        .Text(x => x.Email)
                        .Text(x => x.Contato),
                    new CreateIndexOptions { Name = "ix_fornecedores_busca_texto" })
            ],
            cancellationToken);

        await EnsureIndexesAsync(context.GetCollection<EasyStock.Domain.Entities.PedidoFornecedor>(MongoCollectionNames.PedidosFornecedor),
            [
                new CreateIndexModel<EasyStock.Domain.Entities.PedidoFornecedor>(
                    Builders<EasyStock.Domain.Entities.PedidoFornecedor>.IndexKeys
                        .Ascending(x => x.EmpresaId)
                        .Ascending(x => x.FornecedorId)
                        .Ascending(x => x.Status),
                    new CreateIndexOptions { Name = "ix_pedidos_fornecedor_empresa_fornecedor_status" }),
                new CreateIndexModel<EasyStock.Domain.Entities.PedidoFornecedor>(
                    Builders<EasyStock.Domain.Entities.PedidoFornecedor>.IndexKeys
                        .Ascending(x => x.EmpresaId)
                        .Ascending(x => x.DataPedido),
                    new CreateIndexOptions { Name = "ix_pedidos_fornecedor_empresa_data" })
            ],
            cancellationToken);
    }

    private static Task EnsureIndexesAsync<TDocument>(
        IMongoCollection<TDocument> collection,
        IEnumerable<CreateIndexModel<TDocument>> indexes,
        CancellationToken cancellationToken) =>
        collection.Indexes.CreateManyAsync(indexes, cancellationToken);
}
