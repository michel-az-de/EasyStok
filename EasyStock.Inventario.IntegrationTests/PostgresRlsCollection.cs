namespace EasyStock.Inventario.IntegrationTests;

/// <summary>
/// Serializa as classes de teste que tocam o Postgres compartilhado. No service
/// container do CI o banco e unico (easystock_ci) e o reset dropa/recria o schema;
/// rodar classes em paralelo corromperia o estado uma da outra. A Fatia 1 herda a
/// serializacao + fixture compartilhado de graca ao usar [Collection("PostgresRlsCollection")].
/// </summary>
[CollectionDefinition("PostgresRlsCollection")]
public sealed class PostgresRlsCollection : ICollectionFixture<PostgresRlsFixture>
{
}
