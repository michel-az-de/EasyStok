using System.Collections;
using System.Reflection;
using EasyStock.Domain.ValueObjects;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace EasyStock.Infra.MongoDb.Data;

internal static class MongoClassMapRegistrar
{
    private static int _initialized;

    public static void RegisterAll()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
            return;

        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        BsonSerializer.RegisterSerializer(new CodigoSkuSerializer());
        BsonSerializer.RegisterSerializer(new CodigoLoteSerializer());
        BsonSerializer.RegisterSerializer(new QuantidadeSerializer());
        BsonSerializer.RegisterSerializer(new DinheiroSerializer());
        BsonSerializer.RegisterSerializer(new ValidadeSerializer());
        BsonSerializer.RegisterSerializer(new DimensoesSerializer());

        var entityAssembly = typeof(EasyStock.Domain.Entities.Empresa).Assembly;
        var entityTypes = entityAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Namespace == "EasyStock.Domain.Entities");

        foreach (var type in entityTypes)
            RegisterEntityClassMap(type);
    }

    private static void RegisterEntityClassMap(Type type)
    {
        if (BsonClassMap.IsClassMapRegistered(type))
            return;

        var classMap = new BsonClassMap(type);
        classMap.AutoMap();
        classMap.SetIgnoreExtraElements(true);

        var idProperty = type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
        if (idProperty is not null)
        {
            var idMember = classMap.GetMemberMap(idProperty.Name);
            idMember.SetSerializer(new GuidSerializer(GuidRepresentation.Standard));
            classMap.SetIdMember(idMember);
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name == "Id")
                continue;

            if (!classMap.AllMemberMaps.Any(x => x.MemberName == property.Name))
                continue;

            if (IsNavigationProperty(property.PropertyType))
                classMap.UnmapMember(property);
        }

        BsonClassMap.RegisterClassMap(classMap);
    }

    private static bool IsNavigationProperty(Type type)
    {
        if (type == typeof(string))
            return false;

        if (type.Namespace == "EasyStock.Domain.Entities")
            return true;

        if (typeof(IEnumerable).IsAssignableFrom(type) && type.IsGenericType)
        {
            var itemType = type.GetGenericArguments()[0];
            return itemType.Namespace == "EasyStock.Domain.Entities";
        }

        return false;
    }

    private sealed class CodigoSkuSerializer : SerializerBase<CodigoSku>
    {
        public override CodigoSku Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
            CodigoSku.From(context.Reader.ReadString());

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, CodigoSku value) =>
            context.Writer.WriteString(value.Value);
    }

    private sealed class CodigoLoteSerializer : SerializerBase<CodigoLote>
    {
        public override CodigoLote Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
            CodigoLote.From(context.Reader.ReadString());

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, CodigoLote value) =>
            context.Writer.WriteString(value.Value);
    }

    private sealed class QuantidadeSerializer : SerializerBase<Quantidade>
    {
        public override Quantidade Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
            Quantidade.From(context.Reader.ReadInt32());

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Quantidade value) =>
            context.Writer.WriteInt32(value.Value);
    }

    private sealed class DinheiroSerializer : SerializerBase<Dinheiro>
    {
        public override Dinheiro Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var bsonType = context.Reader.GetCurrentBsonType();
            var value = bsonType switch
            {
                BsonType.Decimal128 => (decimal)context.Reader.ReadDecimal128(),
                BsonType.Double => (decimal)context.Reader.ReadDouble(),
                BsonType.Int32 => context.Reader.ReadInt32(),
                BsonType.Int64 => context.Reader.ReadInt64(),
                _ => throw new FormatException($"Tipo BSON '{bsonType}' invalido para Dinheiro.")
            };

            return Dinheiro.FromDecimal(value);
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Dinheiro value) =>
            context.Writer.WriteDecimal128(new Decimal128(value.Valor));
    }

    private sealed class ValidadeSerializer : SerializerBase<Validade>
    {
        public override Validade Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
            Validade.From(BsonUtils.ToDateTimeFromMillisecondsSinceEpoch(context.Reader.ReadDateTime()));

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Validade value) =>
            context.Writer.WriteDateTime(BsonUtils.ToMillisecondsSinceEpoch(value.DataValidade));
    }

    private sealed class DimensoesSerializer : SerializerBase<Dimensoes>
    {
        public override Dimensoes Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            context.Reader.ReadStartDocument();
            decimal peso = 0m;
            decimal largura = 0m;
            decimal altura = 0m;
            decimal comprimento = 0m;

            while (context.Reader.ReadBsonType() != BsonType.EndOfDocument)
            {
                var name = context.Reader.ReadName(Utf8NameDecoder.Instance);
                var value = (decimal)context.Reader.ReadDecimal128();

                switch (name)
                {
                    case "peso":
                        peso = value;
                        break;
                    case "largura":
                        largura = value;
                        break;
                    case "altura":
                        altura = value;
                        break;
                    case "comprimento":
                        comprimento = value;
                        break;
                }
            }

            context.Reader.ReadEndDocument();
            return Dimensoes.From(peso, largura, altura, comprimento);
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Dimensoes value)
        {
            context.Writer.WriteStartDocument();
            context.Writer.WriteName("peso");
            context.Writer.WriteDecimal128(new Decimal128(value.Peso));
            context.Writer.WriteName("largura");
            context.Writer.WriteDecimal128(new Decimal128(value.Largura));
            context.Writer.WriteName("altura");
            context.Writer.WriteDecimal128(new Decimal128(value.Altura));
            context.Writer.WriteName("comprimento");
            context.Writer.WriteDecimal128(new Decimal128(value.Comprimento));
            context.Writer.WriteEndDocument();
        }
    }
}
