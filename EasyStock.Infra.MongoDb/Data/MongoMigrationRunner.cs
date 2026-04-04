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
            [new CreateIndexModel<EasyStock.Domain.Entities.Produto>(
                Builders<EasyStock.Domain.Entities.Produto>.IndexKeys
                    .Ascending(x => x.EmpresaId)
                    .Ascending(x => x.Nome),
                new CreateIndexOptions { Name = "ix_produtos_empresa_nome" })],
            cancellationToken);

        await EnsureIndexesAsync(context.GetCollection<EasyStock.Domain.Entities.ProdutoVariacao>(MongoCollectionNames.ProdutosVariacao),
            [new CreateIndexModel<EasyStock.Domain.Entities.ProdutoVariacao>(
                Builders<EasyStock.Domain.Entities.ProdutoVariacao>.IndexKeys
                    .Ascending(x => x.EmpresaId)
                    .Ascending(x => x.ProdutoId),
                new CreateIndexOptions { Name = "ix_variacoes_empresa_produto" })],
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
                    new CreateIndexOptions { Name = "ix_itens_codigo_marketplace", Sparse = true })
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
    }

    private static Task EnsureIndexesAsync<TDocument>(
        IMongoCollection<TDocument> collection,
        IEnumerable<CreateIndexModel<TDocument>> indexes,
        CancellationToken cancellationToken) =>
        collection.Indexes.CreateManyAsync(indexes, cancellationToken);
}
