using System.Security.Cryptography;
using System.Text;

namespace EasyStock.Domain.Integration;

/// <summary>
/// Evento de integração externa pendente de despacho. Persistido na MESMA
/// transação do use case que origina o evento (transactional outbox pattern):
/// se o use case commit, o evento estará disponível pro dispatcher; se
/// rollback, o evento some — sem fantasmas.
///
/// <para>
/// <b>Quem dispara</b>: use cases / sagas. Ex: <c>ConfirmarPedidoUseCase</c>
/// publica <c>pedido.confirmado</c>; <c>WebhookPagamentosController</c>
/// publica <c>pagamento.capturado</c>.
/// </para>
///
/// <para>
/// <b>Quem consome</b>: <c>IntegrationOutboxDispatcherService</c>
/// (BackgroundService a ser criado em Fase 4.c). Resolve handler pelo
/// <see cref="TipoEvento"/> e despacha — pode ser HTTP a provider externo
/// (marketplace SKU sync, NFe emission), publicação em fila persistente,
/// ou trigger de saga interna.
/// </para>
///
/// <para>
/// <b>Vs OutboxMensagemNotificacao</b>: aquele é específico de notificação
/// (Email/SMS/WhatsApp/InApp) com schema estável (Destinatario, Canal,
/// AssuntoRenderizado, etc.). Este aqui é genérico — payload é JSON livre
/// definido pelo handler do <see cref="TipoEvento"/>. Não compartilham
/// dispatcher infrastructure (ainda); quando ambos amadurecerem podemos
/// extrair OutboxDispatcherBase&lt;T&gt;.
/// </para>
///
/// <para>
/// <b>Versionamento</b>: <see cref="PayloadSchemaVersion"/> é incrementado
/// quando o JSON do tipo de evento muda incompatibly. Handlers deserializam
/// conforme a versão pra suportar payloads antigos durante a transição.
/// </para>
/// </summary>
public class OutboxEventoIntegracao
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }

    /// <summary>
    /// Identificador do evento. Snake-case dot-namespaced.
    /// Ex: "pedido.confirmado", "pagamento.capturado", "nfe.emitida",
    /// "marketplace.sku.sincronizado", "envio.contratado".
    /// </summary>
    public string TipoEvento { get; set; } = null!;

    public string AggregateType { get; set; } = null!;
    public Guid AggregateId { get; set; }

    public string PayloadJson { get; set; } = null!;
    public int PayloadSchemaVersion { get; set; } = 1;

    /// <summary>
    /// Identificador de correlação que liga eventos relacionados. Geralmente
    /// vem de um header HTTP (W3C traceparent), de outro evento upstream
    /// (causation chain), ou de um job batch.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Id do evento que causou este (parent na cadeia de causação).
    /// Útil em eventos derivados (saga publica B em resposta a A).
    /// </summary>
    public Guid? CausationEventId { get; set; }

    public StatusOutboxIntegracao Status { get; set; } = StatusOutboxIntegracao.Pendente;
    public int Tentativas { get; set; }
    public int MaxTentativas { get; set; } = 5;
    public DateTime ProximaTentativaEm { get; set; }
    public DateTime? ProcessadoEm { get; set; }
    public string? ErroUltimaTentativa { get; set; }

    /// <summary>
    /// Hash determinístico (SHA-256 hex) calculado a partir do conteúdo.
    /// Permite ao caller usar UNIQUE constraint pra deduplicação em
    /// retries idempotentes — ex: handler que falhou meio-caminho pode
    /// reescrever sem criar 2 entries.
    /// </summary>
    public string IdempotencyKey { get; set; } = null!;

    /// <summary>
    /// Shard pra distribuir carga entre múltiplas réplicas do dispatcher.
    /// Mod 4 a partir do byte 0 do <see cref="IdempotencyKey"/>.
    /// </summary>
    public int ShardKey { get; set; }

    public DateTime CriadoEm { get; set; }

    public Empresa? Empresa { get; set; }

    public static OutboxEventoIntegracao Criar(
        Guid empresaId,
        string tipoEvento,
        string aggregateType,
        Guid aggregateId,
        string payloadJson,
        int payloadSchemaVersion = 1,
        string? correlationId = null,
        Guid? causationEventId = null,
        int maxTentativas = 5)
    {
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId é obrigatório.", nameof(empresaId));
        if (string.IsNullOrWhiteSpace(tipoEvento))
            throw new ArgumentException("TipoEvento é obrigatório.", nameof(tipoEvento));
        if (string.IsNullOrWhiteSpace(aggregateType))
            throw new ArgumentException("AggregateType é obrigatório.", nameof(aggregateType));
        if (aggregateId == Guid.Empty)
            throw new ArgumentException("AggregateId é obrigatório.", nameof(aggregateId));
        if (string.IsNullOrWhiteSpace(payloadJson))
            throw new ArgumentException("PayloadJson é obrigatório.", nameof(payloadJson));
        if (maxTentativas < 1)
            throw new ArgumentOutOfRangeException(nameof(maxTentativas), "MaxTentativas mínimo é 1.");

        var id = Guid.NewGuid();
        var idempotencyKey = ComputarIdempotencyKey(empresaId, tipoEvento, aggregateId, id);
        var agora = DateTime.UtcNow;

        return new OutboxEventoIntegracao
        {
            Id = id,
            EmpresaId = empresaId,
            TipoEvento = tipoEvento.Trim(),
            AggregateType = aggregateType.Trim(),
            AggregateId = aggregateId,
            PayloadJson = payloadJson,
            PayloadSchemaVersion = payloadSchemaVersion,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim(),
            CausationEventId = causationEventId,
            Status = StatusOutboxIntegracao.Pendente,
            Tentativas = 0,
            MaxTentativas = maxTentativas,
            ProximaTentativaEm = agora,
            IdempotencyKey = idempotencyKey,
            ShardKey = Convert.FromHexString(idempotencyKey)[0] % 4,
            CriadoEm = agora,
        };
    }

    public void MarcarEmEnvio()
    {
        Status = StatusOutboxIntegracao.EmEnvio;
    }

    public void MarcarEnviado()
    {
        Status = StatusOutboxIntegracao.Enviado;
        ProcessadoEm = DateTime.UtcNow;
        ErroUltimaTentativa = null;
    }

    /// <summary>
    /// Marca falha de tentativa. Backoff exponencial: tentativa N
    /// agenda próxima em base * 2^(N-1) com jitter aplicado pelo caller.
    /// Esgotado MaxTentativas, status vai pra Falhado.
    /// </summary>
    public void MarcarFalhaTentativa(string erro, TimeSpan backoff)
    {
        Tentativas++;
        ErroUltimaTentativa = erro;
        ProximaTentativaEm = DateTime.UtcNow.Add(backoff);
        Status = Tentativas >= MaxTentativas
            ? StatusOutboxIntegracao.Falhado
            : StatusOutboxIntegracao.Pendente;
    }

    /// <summary>
    /// Reseta pra Pendente com tentativas zeradas — admin reprocessa
    /// item Falhado após corrigir causa raiz.
    /// </summary>
    public void Reprocessar()
    {
        Status = StatusOutboxIntegracao.Pendente;
        Tentativas = 0;
        ProximaTentativaEm = DateTime.UtcNow;
        ErroUltimaTentativa = null;
    }

    public void Cancelar()
    {
        Status = StatusOutboxIntegracao.Cancelado;
        ProcessadoEm = DateTime.UtcNow;
    }

    public bool TentativasEsgotadas() => Tentativas >= MaxTentativas;

    private static string ComputarIdempotencyKey(Guid empresaId, string tipoEvento, Guid aggregateId, Guid eventId)
    {
        // Inclui eventId pra que cada evento tenha key única — caller que
        // quiser dedup deve encurtar (ex: hash(empresaId|tipoEvento|aggregateId)
        // sem eventId). Default: cada Criar gera key única, sem dedup natural.
        var raw = $"{empresaId:N}|{tipoEvento}|{aggregateId:N}|{eventId:N}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}
