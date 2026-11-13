namespace EasyStock.Application.UseCases.Atendimento.Webhook;

/// <summary>Payload do webhook da Meta já normalizado — um <see cref="EntradaWhatsApp"/> por <c>entry[].changes[].value</c>.</summary>
public sealed record EventoWhatsApp(IReadOnlyList<EntradaWhatsApp> Entradas);

public sealed record EntradaWhatsApp(
    string PhoneNumberId,
    string RawJson,
    IReadOnlyList<ContatoWhatsApp> Contatos,
    IReadOnlyList<MensagemRecebidaWhatsApp> Mensagens,
    IReadOnlyList<StatusRecebidoWhatsApp> Statuses,
    IReadOnlyList<EcoWhatsApp> Ecos);

public sealed record ContatoWhatsApp(string WaId, string? Nome);

/// <summary>
/// <see cref="Tipo"/> é o <c>type</c> bruto da Meta (text, image, audio, document, sticker, video, location, interactive,
/// button, reaction, unsupported, system). <see cref="TextoCorpo"/> é o texto, a legenda da mídia ou a localização.
/// </summary>
public sealed record MensagemRecebidaWhatsApp(
    string De,
    string Wamid,
    DateTimeOffset Timestamp,
    string Tipo,
    string? TextoCorpo,
    string? MidiaId,
    string? MidiaMime,
    string? BotaoRespostaId,
    string? BotaoPayload,
    LocalizacaoWhatsApp? Localizacao = null);

/// <summary>Mensagem do tipo <c>location</c>: as coordenadas e, quando o cliente escolheu um lugar, nome e endereço.</summary>
public sealed record LocalizacaoWhatsApp(double Latitude, double Longitude, string? Nome, string? Endereco)
{
    /// <summary>Latitude e longitude dentro do globo; payload com coordenada inventada não vira localização.</summary>
    public static LocalizacaoWhatsApp? Criar(double? latitude, double? longitude, string? nome, string? endereco) =>
        latitude is { } lat && longitude is { } lng && lat is >= -90 and <= 90 && lng is >= -180 and <= 180
            && !(lat == 0 && lng == 0)
            ? new(lat, lng, Limpo(nome), Limpo(endereco))
            : null;

    /// <summary>"lat,long" com ponto decimal, como a Meta escreve no texto da mensagem.</summary>
    public string Coordenadas => FormattableString.Invariant($"{Latitude},{Longitude}");

    private static string? Limpo(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}

/// <summary><see cref="Status"/> é o status bruto da Meta (sent, delivered, read, failed).</summary>
public sealed record StatusRecebidoWhatsApp(
    string Wamid,
    string Status,
    string? RecipientId,
    string? ErroMensagem,
    int? ErroCodigo = null);

/// <summary>
/// Mensagem que a loja mandou pelo app WhatsApp Business do celular (coexistência, #1417, field
/// <c>smb_message_echoes</c>). <see cref="Para"/> é o <c>wa_id</c> do cliente; o resto segue <see cref="MensagemRecebidaWhatsApp"/>.
/// </summary>
public sealed record EcoWhatsApp(
    string Para,
    string Wamid,
    DateTimeOffset Timestamp,
    string Tipo,
    string? TextoCorpo,
    string? MidiaId,
    string? MidiaMime);
