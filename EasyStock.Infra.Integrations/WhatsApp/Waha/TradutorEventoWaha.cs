using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.WhatsApp.Waha;

/// <summary>
/// Traduz o webhook do WAHA para o corpo do webhook da Meta que o <c>ProcessarEventoWhatsAppUseCase</c> já processa
/// (#2072). Nada do atendimento muda: conversa, idempotência pelo id, mídia, eco da loja e status seguem o caminho da
/// Meta.
/// <list type="bullet">
/// <item><c>message.any</c> do cliente vira <c>messages</c>; a resposta a um menu numerado vira clique de botão.</item>
/// <item>O que a loja escreve pelo celular (<c>fromMe</c> com <c>source=app</c>) vira <c>message_echoes</c>; o que o
/// próprio EasyStok mandou (<c>source=api</c>) é ignorado, porque já está no histórico.</item>
/// <item><c>message.ack</c> vira <c>statuses</c> (sent, delivered, read, failed).</item>
/// <item>Grupo, status, canal, outra sessão e conta conectada a outro número são descartados.</item>
/// </list>
/// O id da mensagem fica sempre na forma do telefone (<c>false_5541...@c.us_ID</c>): a confirmação que chega pelo id
/// oculto encontra a mensagem gravada.
/// </summary>
public sealed class TradutorEventoWaha(
    ResolvedorContatoWaha resolvedor,
    MenuNumeradoWaha menus,
    IOptions<WahaOptions> options,
    ILogger<TradutorEventoWaha> logger)
{
    private readonly WahaOptions _opcoes = options.Value;

    /// <summary>Corpo no formato da Meta, ou <c>null</c> quando o evento não interessa ao atendimento.</summary>
    public async Task<string?> TraduzirAsync(string corpo, CancellationToken ct)
    {
        JsonDocument documento;
        try
        {
            documento = JsonDocument.Parse(corpo);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Ponte WAHA: webhook não é JSON válido, ignorado.");
            return null;
        }

        using (documento)
        {
            var raiz = documento.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return null;

            if (!string.Equals(Texto(raiz, "session"), _opcoes.Sessao, StringComparison.Ordinal))
            {
                logger.LogWarning("Ponte WAHA: evento de outra sessão ignorado.");
                return null;
            }

            var payload = raiz.TryGetProperty("payload", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
            if (raiz.TryGetProperty("me", out var me) && me.ValueKind == JsonValueKind.Object)
                AlertarRestricoes(me);

            var evento = Texto(raiz, "event");
            if (evento == "session.status")
            {
                RegistrarStatusDaSessao(payload);
                return null;
            }

            if (payload.ValueKind != JsonValueKind.Object || !ContaDaLoja(raiz)) return null;

            return evento switch
            {
                "message" or "message.any" => await TraduzirMensagemAsync(payload, ct),
                "message.ack" => await TraduzirConfirmacaoAsync(payload, ct),
                _ => null,
            };
        }
    }

    private async Task<string?> TraduzirMensagemAsync(JsonElement payload, CancellationToken ct)
    {
        var deMim = Booleano(payload, "fromMe");
        var id = WahaIds.LerIdMensagem(Texto(payload, "id"));
        if (id is null)
        {
            logger.LogWarning("Ponte WAHA: mensagem sem id no formato do WAHA, ignorada.");
            return null;
        }

        if (!WahaIds.EhConversaIndividual(id.Conversa) || WahaIds.Usuario(id.Conversa) == _opcoes.PhoneNumberIdNormalizado)
            return null;

        // O que o próprio EasyStok mandou volta como eco do aparelho da ponte: já está gravado, e como eco assumiria a
        // conversa no lugar da dona.
        if (deMim && string.Equals(Texto(payload, "source"), "api", StringComparison.OrdinalIgnoreCase)) return null;

        var contato = await resolvedor.ContatoAsync(id.Conversa, ct);
        if (contato is null) return null;

        var mensagem = new JsonObject
        {
            ["id"] = WahaIds.MontarIdMensagem(deMim, contato.Conversa, id.Id),
            ["timestamp"] = Instante(payload).ToString(CultureInfo.InvariantCulture),
        };

        var (tipo, conteudo) = Conteudo(payload);
        if (!deMim && tipo == "text")
        {
            var escolha = await menus.EscolhaAsync(contato.Contato, Texto(payload, "body"), ct);
            if (escolha is not null)
            {
                tipo = "interactive";
                conteudo = new JsonObject
                {
                    ["type"] = "button_reply",
                    ["button_reply"] = new JsonObject { ["id"] = escolha.Id, ["title"] = escolha.Titulo },
                };
            }
        }

        mensagem["type"] = tipo;
        if (conteudo is not null) mensagem[tipo] = conteudo;

        var valor = Valor();
        if (deMim)
        {
            mensagem["from"] = _opcoes.PhoneNumberIdNormalizado;
            mensagem["to"] = contato.Contato;
            valor["message_echoes"] = new JsonArray(mensagem);
            return Envelope("smb_message_echoes", valor);
        }

        mensagem["from"] = contato.Contato;
        var contatoMeta = new JsonObject { ["wa_id"] = contato.Contato };
        if (NomeDoContato(payload) is { } nome) contatoMeta["profile"] = new JsonObject { ["name"] = nome };
        valor["contacts"] = new JsonArray(contatoMeta);
        valor["messages"] = new JsonArray(mensagem);
        return Envelope("messages", valor);
    }

    private async Task<string?> TraduzirConfirmacaoAsync(JsonElement payload, CancellationToken ct)
    {
        if (!Booleano(payload, "fromMe")) return null;
        var id = WahaIds.LerIdMensagem(Texto(payload, "id"));
        if (id is null || !WahaIds.EhConversaIndividual(id.Conversa)) return null;

        var status = (payload.TryGetProperty("ack", out var ack) && ack.ValueKind == JsonValueKind.Number && ack.TryGetInt32(out var n) ? n : 0) switch
        {
            -1 => "failed",
            1 => "sent",
            2 => "delivered",
            3 or 4 => "read",
            _ => null,
        };
        if (status is null) return null;

        var contato = await resolvedor.ContatoAsync(id.Conversa, ct);
        if (contato is null) return null;

        var item = new JsonObject
        {
            ["id"] = WahaIds.MontarIdMensagem(true, contato.Conversa, id.Id),
            ["status"] = status,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ["recipient_id"] = contato.Contato,
        };
        if (status == "failed")
            item["errors"] = new JsonArray(new JsonObject { ["code"] = 0, ["title"] = "O WhatsApp informou falha na entrega pela ponte." });

        var valor = Valor();
        valor["statuses"] = new JsonArray(item);
        return Envelope("messages", valor);
    }

    private static (string Tipo, JsonObject? Conteudo) Conteudo(JsonElement payload)
    {
        var corpo = Texto(payload, "body");

        if (payload.TryGetProperty("location", out var local) && local.ValueKind == JsonValueKind.Object)
        {
            var localizacao = new JsonObject();
            if (Numero(local, "latitude") is { } latitude) localizacao["latitude"] = latitude;
            if (Numero(local, "longitude") is { } longitude) localizacao["longitude"] = longitude;
            if ((Texto(local, "name") ?? Texto(local, "description")) is { } nome) localizacao["name"] = nome;
            if (Texto(local, "address") is { } endereco) localizacao["address"] = endereco;
            return ("location", localizacao);
        }

        if (Booleano(payload, "hasMedia"))
        {
            string? mime = null, url = null;
            if (payload.TryGetProperty("media", out var midia) && midia.ValueKind == JsonValueKind.Object)
            {
                mime = Texto(midia, "mimetype");
                url = Texto(midia, "url");
            }

            var tipo = TipoDaMidia(mime);
            var conteudo = new JsonObject();
            if (CaminhoDoArquivo(url) is { } caminho) conteudo["id"] = caminho;
            if (mime is not null) conteudo["mime_type"] = mime;
            if (!string.IsNullOrWhiteSpace(corpo) && tipo is "image" or "document" or "video") conteudo["caption"] = corpo;
            return (tipo, conteudo);
        }

        return string.IsNullOrWhiteSpace(corpo)
            ? ("unsupported", null)
            : ("text", new JsonObject { ["body"] = corpo });
    }

    private static string TipoDaMidia(string? mime)
    {
        var tipo = mime?.Split(';')[0].Trim().ToLowerInvariant();
        if (tipo is null) return "document";
        if (tipo == "image/webp") return "sticker";
        if (tipo.StartsWith("image/", StringComparison.Ordinal)) return "image";
        if (tipo.StartsWith("audio/", StringComparison.Ordinal)) return "audio";
        if (tipo.StartsWith("video/", StringComparison.Ordinal)) return "video";
        return "document";
    }

    /// <summary>Só o caminho do arquivo no WAHA: o download vai sempre ao endereço configurado, nunca ao do evento.</summary>
    private static string? CaminhoDoArquivo(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var caminho = Uri.TryCreate(url, UriKind.Absolute, out var absoluta) ? absoluta.PathAndQuery : url;
        caminho = caminho.TrimStart('/');
        return caminho.StartsWith("api/files/", StringComparison.Ordinal) && !caminho.Contains("..", StringComparison.Ordinal)
            ? caminho
            : null;
    }

    private static string? NomeDoContato(JsonElement payload)
    {
        if (!payload.TryGetProperty("_data", out var bruto) || bruto.ValueKind != JsonValueKind.Object) return null;
        var nome = bruto.TryGetProperty("Info", out var info) && info.ValueKind == JsonValueKind.Object
            ? Texto(info, "PushName")
            : Texto(bruto, "notifyName") ?? Texto(bruto, "pushName");
        return string.IsNullOrWhiteSpace(nome) ? null : nome.Trim();
    }

    private static long Instante(JsonElement payload) =>
        payload.TryGetProperty("timestamp", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt64(out var segundos) && segundos > 0
            ? segundos
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>
    /// Evento de uma conta que não é a da loja é descartado: alguém escaneou o QR com outro celular e as conversas dele
    /// iriam parar no atendimento da loja.
    /// </summary>
    private bool ContaDaLoja(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("me", out var me) || me.ValueKind != JsonValueKind.Object || Texto(me, "id") is not { } conta)
            return true;

        var numero = WahaIds.Usuario(conta);
        if (numero.Length > 0 && numero == _opcoes.PhoneNumberIdNormalizado) return true;

        logger.LogError(
            "Ponte WAHA conectada a um número que não é o da loja (final {Final}): evento descartado. Desconecte o aparelho e conecte de novo com o celular da loja.",
            numero.Length >= 4 ? numero[^4..] : numero);
        return false;
    }

    private void RegistrarStatusDaSessao(JsonElement payload)
    {
        var status = payload.ValueKind == JsonValueKind.Object ? Texto(payload, "status") : null;
        if (status == "WORKING")
        {
            logger.LogInformation("Ponte WAHA conectada (WORKING).");
            if (payload.TryGetProperty("data", out var dados) && dados.ValueKind == JsonValueKind.Object) AlertarRestricoes(dados);
            return;
        }

        if (status is "SCAN_QR_CODE" or "FAILED" or "STOPPED")
        {
            logger.LogError(
                "Ponte WAHA fora do ar (sessão {Status}). As mensagens seguem chegando no celular da loja, mas o atendimento automático parou. Reconecte pelo guia docs/dev/whatsapp-ponte-waha.md.",
                status);
            return;
        }

        logger.LogInformation("Ponte WAHA: sessão em {Status}.", status ?? "?");
    }

    /// <summary>Restrição do WhatsApp sobre o número: o primeiro sinal antes de um banimento.</summary>
    private void AlertarRestricoes(JsonElement conta)
    {
        if (conta.TryGetProperty("reachoutTimelock", out var trava) && trava.ValueKind == JsonValueKind.Object && Booleano(trava, "isActive"))
        {
            var fim = trava.TryGetProperty("timeEnforcementEnds", out var f) && f.ValueKind == JsonValueKind.Number && f.TryGetInt64(out var s)
                ? DateTimeOffset.FromUnixTimeSeconds(s).ToString("u", CultureInfo.InvariantCulture)
                : "?";
            logger.LogError("WhatsApp restringiu o número da ponte para iniciar conversas até {Fim}. Pare envios que não sejam resposta.", fim);
        }

        if (conta.TryGetProperty("messageCapping", out var cota) && cota.ValueKind == JsonValueKind.Object
            && string.Equals(Texto(cota, "cappingStatus"), "CAPPED", StringComparison.OrdinalIgnoreCase))
            logger.LogError("Número da ponte atingiu a cota de conversas novas do WhatsApp: conversa nova bloqueada até o fim do ciclo.");
    }

    private JsonObject Valor() => new()
    {
        ["messaging_product"] = "whatsapp",
        ["metadata"] = new JsonObject
        {
            ["display_phone_number"] = _opcoes.PhoneNumberIdNormalizado,
            ["phone_number_id"] = _opcoes.PhoneNumberIdNormalizado,
        },
    };

    private static string Envelope(string campo, JsonObject valor) => new JsonObject
    {
        ["object"] = "whatsapp_business_account",
        ["entry"] = new JsonArray(new JsonObject
        {
            ["id"] = "waha",
            ["changes"] = new JsonArray(new JsonObject { ["field"] = campo, ["value"] = valor }),
        }),
    }.ToJsonString();

    private static string? Texto(JsonElement elemento, string propriedade) =>
        elemento.TryGetProperty(propriedade, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Booleano(JsonElement elemento, string propriedade) =>
        elemento.TryGetProperty(propriedade, out var v) && v.ValueKind == JsonValueKind.True;

    private static double? Numero(JsonElement elemento, string propriedade) =>
        elemento.TryGetProperty(propriedade, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;
}
