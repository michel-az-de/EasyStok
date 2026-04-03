using EasyStok.Domain.Entities;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.IdGenerators;

namespace EasyStock.Infra.MongoDb.Data.Mappings
{
 public static class EmpresaMap
 {
 public static void Register()
 {
 if (BsonClassMap.IsClassMapRegistered(typeof(Empresa))) return;
 BsonClassMap.RegisterClassMap<Empresa>(cm =>
 {
 cm.AutoMap();
 cm.MapIdMember(c => c.Id).SetIdGenerator(GuidGenerator.Instance).SetSerializer(new MongoDB.Bson.Serialization.Serializers.GuidSerializer(MongoDB.Bson.BsonType.String));
 cm.MapMember(c => c.Nome).SetElementName("nome");
 cm.MapMember(c => c.Documento).SetElementName("documento");
 });
 }
 }
}
