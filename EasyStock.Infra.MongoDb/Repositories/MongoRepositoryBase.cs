using System.Text.RegularExpressions;
using EasyStock.Infra.MongoDb.Data;
using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.Repositories;

public abstract class MongoRepositoryBase
{
    protected MongoRepositoryBase(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    {
        Context = context;
        UnitOfWork = unitOfWork;
    }

    protected MongoEasyStockContext Context { get; }
    protected MongoUnitOfWork UnitOfWork { get; }

    protected static string BuildContainsPattern(string value) => Regex.Escape(value.Trim());

    protected void EnqueueInsert<TDocument>(IMongoCollection<TDocument> collection, TDocument document) =>
        UnitOfWork.Enqueue((session, ct) =>
            session is null
                ? collection.InsertOneAsync(document, cancellationToken: ct)
                : collection.InsertOneAsync(session, document, cancellationToken: ct));

    protected void EnqueueReplace<TDocument>(IMongoCollection<TDocument> collection, Guid id, TDocument document) =>
        UnitOfWork.Enqueue((session, ct) =>
            session is null
                ? collection.ReplaceOneAsync(Builders<TDocument>.Filter.Eq("Id", id), document, cancellationToken: ct)
                : collection.ReplaceOneAsync(session, Builders<TDocument>.Filter.Eq("Id", id), document, cancellationToken: ct));

    protected void EnqueueDelete<TDocument>(IMongoCollection<TDocument> collection, Guid id) =>
        UnitOfWork.Enqueue((session, ct) =>
            session is null
                ? collection.DeleteOneAsync(Builders<TDocument>.Filter.Eq("Id", id), ct)
                : collection.DeleteOneAsync(session, Builders<TDocument>.Filter.Eq("Id", id), null, ct));

    /// <summary>
    /// Variante multi-tenant de <see cref="EnqueueReplace{TDocument}"/>. O update só é aplicado
    /// se o documento pertencer à empresa informada. Protege contra cross-tenant tampering:
    /// mesmo que um atacante conheça o Id de outra empresa, o filtro composto impede a escrita.
    /// Use este método em qualquer entidade que exponha <c>EmpresaId</c>.
    /// </summary>
    protected void EnqueueReplaceScoped<TDocument>(
        IMongoCollection<TDocument> collection,
        Guid id,
        Guid empresaId,
        TDocument document)
    {
        var filter = Builders<TDocument>.Filter.And(
            Builders<TDocument>.Filter.Eq("Id", id),
            Builders<TDocument>.Filter.Eq("EmpresaId", empresaId));

        UnitOfWork.Enqueue((session, ct) =>
            session is null
                ? collection.ReplaceOneAsync(filter, document, cancellationToken: ct)
                : collection.ReplaceOneAsync(session, filter, document, cancellationToken: ct));
    }

    /// <summary>
    /// Variante multi-tenant de <see cref="EnqueueDelete{TDocument}"/>. O delete só é aplicado
    /// se o documento pertencer à empresa informada.
    /// </summary>
    protected void EnqueueDeleteScoped<TDocument>(
        IMongoCollection<TDocument> collection,
        Guid id,
        Guid empresaId)
    {
        var filter = Builders<TDocument>.Filter.And(
            Builders<TDocument>.Filter.Eq("Id", id),
            Builders<TDocument>.Filter.Eq("EmpresaId", empresaId));

        UnitOfWork.Enqueue((session, ct) =>
            session is null
                ? collection.DeleteOneAsync(filter, ct)
                : collection.DeleteOneAsync(session, filter, null, ct));
    }
}
