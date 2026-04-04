using EasyStok.Domain.Entities;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.IdGenerators;

namespace EasyStock.Infra.MongoDb.Data.Mappings
{
    public static class ProdutoMap
    {
        public static void Register()
        {
            if (BsonClassMap.IsClassMapRegistered(typeof(Produto))) return;
            BsonClassMap.RegisterClassMap<Produto>(cm =>
            {
                cm.AutoMap();
                cm.MapIdMember(p => p.Id).SetIdGenerator(GuidGenerator.Instance).SetSerializer(new MongoDB.Bson.Serialization.Serializers.GuidSerializer(MongoDB.Bson.BsonType.String));
                cm.MapMember(p => p.Nome).SetElementName("nome");
                cm.MapMember(p => p.CodigoBarras).SetElementName("codigo_barras");
            });
        }
    }
}
