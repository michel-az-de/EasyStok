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

    /// <summary>
    /// Wrapper null-safe para serializers de Value Objects (sealed record). VOs em entities
    /// podem ser nullable (ex: <c>Produto.SkuBase = CodigoSku?</c>); sem este guard, o MongoDB
    /// driver chama Serialize com value=null e estoura NRE em prod. Em Deserialize, ReadNull
    /// quando documento tem o campo como BsonNull.
    /// </summary>
    private abstract class VOSerializerBase<T> : SerializerBase<T> where T : class
    {
        public sealed override T Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            if (context.Reader.GetCurrentBsonType() == BsonType.Null)
            {
                context.Reader.ReadNull();
                return null!;
            }
            return DeserializeValue(context, args);
        }

        public sealed override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, T value)
        {
            if (value is null)
            {
                context.Writer.WriteNull();
                return;
            }
            SerializeValue(context, args, value);
        }

        protected abstract T DeserializeValue(BsonDeserializationContext context, BsonDeserializationArgs args);
        protected abstract void SerializeValue(BsonSerializationContext context, BsonSerializationArgs args, T value);
    }

    private sealed class CodigoSkuSerializer : VOSerializerBase<CodigoSku>
    {
        protected override CodigoSku DeserializeValue(BsonDeserializationContext context, BsonDeserializationArgs args) =>
            CodigoSku.From(context.Reader.ReadString());

        protected override void SerializeValue(BsonSerializationContext context, BsonSerializationArgs args, CodigoSku value) =>
            context.Writer.WriteString(value.Value);
    }

    private sealed class CodigoLoteSerializer : VOSerializerBase<CodigoLote>
    {
        protected override CodigoLote DeserializeValue(BsonDeserializationContext context, BsonDeserializationArgs args) =>
            CodigoLote.From(context.Reader.ReadString());

        protected override void SerializeValue(BsonSerializationContext context, BsonSerializationArgs args, CodigoLote value) =>
            context.Writer.WriteString(value.Value);
    }

    private sealed class QuantidadeSerializer : VOSerializerBase<Quantidade>
    {
        protected override Quantidade DeserializeValue(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            // Backward-compatible: existing documents stored as Int32; new writes use Decimal128.
            var bsonType = context.Reader.GetCurrentBsonType();
            return bsonType switch
            {
                BsonType.Int32     => Quantidade.From((decimal)context.Reader.ReadInt32()),
                BsonType.Int64     => Quantidade.From((decimal)context.Reader.ReadInt64()),
                BsonType.Double    => Quantidade.From((decimal)context.Reader.ReadDouble()),
                BsonType.Decimal128 => Quantidade.From((decimal)context.Reader.ReadDecimal128()),
                _                  => Quantidade.From((decimal)context.Reader.ReadInt32()),
            };
        }

        protected override void SerializeValue(BsonSerializationContext context, BsonSerializationArgs args, Quantidade value) =>
            context.Writer.WriteDecimal128(new MongoDB.Bson.Decimal128(value.Value));
    }

    private sealed class DinheiroSerializer : VOSerializerBase<Dinheiro>
    {
        protected override Dinheiro DeserializeValue(BsonDeserializationContext context, BsonDeserializationArgs args)
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

        protected override void SerializeValue(BsonSerializationContext context, BsonSerializationArgs args, Dinheiro value) =>
            context.Writer.WriteDecimal128(new Decimal128(value.Valor));
    }

    private sealed class ValidadeSerializer : VOSerializerBase<Validade>
    {
        protected override Validade DeserializeValue(BsonDeserializationContext context, BsonDeserializationArgs args) =>
            Validade.From(BsonUtils.ToDateTimeFromMillisecondsSinceEpoch(context.Reader.ReadDateTime()));

        protected override void SerializeValue(BsonSerializationContext context, BsonSerializationArgs args, Validade value) =>
            context.Writer.WriteDateTime(BsonUtils.ToMillisecondsSinceEpoch(value.DataValidade));
    }

    private sealed class DimensoesSerializer : VOSerializerBase<Dimensoes>
    {
        protected override Dimensoes DeserializeValue(BsonDeserializationContext context, BsonDeserializationArgs args)
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

        protected override void SerializeValue(BsonSerializationContext context, BsonSerializationArgs args, Dimensoes value)
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
