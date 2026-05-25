namespace EasyStock.Infra.MongoDb.IntegrationTests;

[CollectionDefinition("MongoDbTestCollection")]
public sealed class MongoDbTestCollection : ICollectionFixture<MongoDbFixture>
{
}
