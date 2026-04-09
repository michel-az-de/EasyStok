using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.Data;

public sealed class MongoEasyStockContext
{
    public MongoEasyStockContext(IMongoClient client, string databaseName)
    {
        Client = client;
        Database = client.GetDatabase(databaseName);
    }

    public IMongoClient Client { get; }
    public IMongoDatabase Database { get; }

    public IMongoCollection<TDocument> GetCollection<TDocument>(string collectionName) =>
        Database.GetCollection<TDocument>(collectionName);
}
