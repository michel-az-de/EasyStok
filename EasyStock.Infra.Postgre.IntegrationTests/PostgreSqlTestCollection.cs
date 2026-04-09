using Xunit;

namespace EasyStock.Infra.Postgre.IntegrationTests;

[CollectionDefinition("PostgreSqlTestCollection")]
public sealed class PostgreSqlTestCollection : ICollectionFixture<PostgreSqlDatabaseFixture>
{
}
