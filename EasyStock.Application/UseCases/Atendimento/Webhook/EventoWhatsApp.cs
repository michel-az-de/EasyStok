namespace EasyStock.Application.UseCases.Atendimento.Webhook;

/// <summary>Payload do webhook da Meta já normalizado — um <see cref="EntradaWhatsApp"/> por <c>entry[].changes[].value</c>.</summary>
public sealed record EventoWhatsApp(IReadOnlyList<EntradaWhatsApp> Entradas);

public sealed record EntradaWhatsApp(
    string PhoneNumberId,
    string RawJson,
    IReadOnlyList<ContatoWhatsApp> Contatos,
    IReadOnlyList<MensagemRecebidaWhatsApp> Mensagens,
    IReadOnlyList<StatusRecebidoWhatsApp> Statuses);

public sealed record ContatoWhatsApp(string WaId, string? Nome);

/// <summary><see cref="Tipo"/> é o <c>type</c> bruto da Meta (text, image, audio, document, sticker, location, interactive, button, reaction, unsupported).</summary>
public sealed record MensagemRecebidaWhatsApp(
    string De,
    string Wamid,
    DateTimeOffset Timestamp,
    string Tipo,
    string? TextoCorpo,
    string? MidiaId,
    string? MidiaMime,
    string? BotaoRespostaId,
    string? BotaoPayload);

/// <summary><see cref="Status"/> é o status bruto da Meta (sent, delivered, read, failed).</summary>
public sealed record StatusRecebidoWhatsApp(
    string Wamid,
    string Status,
    string? RecipientId,
    string? ErroMensagem);
