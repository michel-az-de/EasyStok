using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using System.IO;

namespace EasyStock.Infra.MongoDb.Data
{
 public static class MongoDbContextFactory
 {
 public static IMongoDatabase CreateDatabase(string? databaseName = null)
 {
 var builder = new ConfigurationBuilder()
 .SetBasePath(Directory.GetCurrentDirectory())
 .AddJsonFile("appsettings.json", optional: true)
 .AddEnvironmentVariables();
 var configuration = builder.Build();
 var connectionString = configuration.GetConnectionString("MongoConnection") ?? "mongodb://localhost:27017";
 var client = new MongoClient(connectionString);
 var dbName = databaseName ?? configuration.GetValue<string>("MongoDatabase") ?? "easystock_mongo";
 return client.GetDatabase(dbName);
 }
 }
}
