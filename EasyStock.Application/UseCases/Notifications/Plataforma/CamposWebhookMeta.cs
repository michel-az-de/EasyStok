using System.Text.Json;
using System.Text.Json.Nodes;

namespace EasyStock.Application.UseCases.Notifications.Plataforma;

/// <summary>
/// O corpo do callback do app dividido pelo <c>field</c> de cada mudança (N6). A Meta não deixa sobrescrever o webhook de
/// template: <c>template_category_update</c> chega sempre ao callback do app, ao lado de <c>messages</c> do atendimento.
/// <see cref="Mensagens"/> é o corpo só com as mudanças que o atendimento entende (<c>messages</c> ou sem <c>field</c>,
/// como os testes antigos); <see cref="CategoriaTemplate"/> só com as de <c>template_category_update</c>.
/// <see cref="Ignoradas"/> conta as de outro <c>field</c>, descartadas de propósito.
/// Coexistência (#1417): <c>smb_message_echoes</c> (o que a loja respondeu pelo app do celular) vai ao atendimento junto
/// de <c>messages</c>; <c>history</c> e <c>smb_app_state_sync</c> são só contados em <see cref="Historico"/> e
/// <see cref="EstadoApp"/> (a importação fica para depois).
/// </summary>
public sealed record CamposWebhookMeta(string? Mensagens, string? CategoriaTemplate, int Ignoradas, int Historico = 0, int EstadoApp = 0)
{
    public const string FieldMessages = "messages";
    public const string FieldTemplateCategoryUpdate = "template_category_update";
    public const string FieldSmbMessageEchoes = "smb_message_echoes";
    public const string FieldHistory = "history";
    public const string FieldSmbAppStateSync = "smb_app_state_sync";

    /// <summary>Os fields que o atendimento processa.</summary>
    private static readonly string[] FieldsDoAtendimento = [FieldMessages, FieldSmbMessageEchoes];

    /// <summary>
    /// Corpo que não é JSON, ou sem nenhuma mudança de outro tipo, segue inteiro para o atendimento: ele já o ignora com
    /// 200, como antes.
    /// </summary>
    public static CamposWebhookMeta Separar(string rawBody)
    {
        JsonNode? raiz;
        try
        {
            raiz = JsonNode.Parse(rawBody);
        }
        catch (JsonException)
        {
            return new CamposWebhookMeta(rawBody, null, 0);
        }

        if (raiz?["entry"] is not JsonArray entradas)
            return new CamposWebhookMeta(rawBody, null, 0);

        int mensagens = 0, categoria = 0, historico = 0, estadoApp = 0, ignoradas = 0;
        foreach (var entrada in entradas)
        {
            if (entrada?["changes"] is not JsonArray mudancas) continue;
            foreach (var mudanca in mudancas)
            {
                switch (Campo(mudanca))
                {
                    case FieldMessages or FieldSmbMessageEchoes: mensagens++; break;
                    case FieldTemplateCategoryUpdate: categoria++; break;
                    case FieldHistory: historico++; break;
                    case FieldSmbAppStateSync: estadoApp++; break;
                    default: ignoradas++; break;
                }
            }
        }

        if (categoria == 0 && ignoradas == 0 && historico == 0 && estadoApp == 0)
            return new CamposWebhookMeta(rawBody, null, 0);

        return new CamposWebhookMeta(
            mensagens > 0 ? Filtrar(rawBody, FieldsDoAtendimento) : null,
            categoria > 0 ? Filtrar(rawBody, [FieldTemplateCategoryUpdate]) : null,
            ignoradas, historico, estadoApp);
    }

    /// <summary>Sem <c>field</c> vale <c>messages</c>: o formato antigo que o atendimento sempre aceitou.</summary>
    private static string Campo(JsonNode? mudanca) =>
        mudanca?["field"]?.GetValueKind() == JsonValueKind.String
            ? mudanca["field"]!.GetValue<string>()
            : FieldMessages;

    private static string Filtrar(string rawBody, string[] campos)
    {
        var raiz = JsonNode.Parse(rawBody)!;
        var entradas = (JsonArray)raiz["entry"]!;
        for (var i = entradas.Count - 1; i >= 0; i--)
        {
            if (entradas[i]?["changes"] is not JsonArray mudancas)
            {
                entradas.RemoveAt(i);
                continue;
            }

            for (var j = mudancas.Count - 1; j >= 0; j--)
                if (!campos.Contains(Campo(mudancas[j]))) mudancas.RemoveAt(j);

            if (mudancas.Count == 0) entradas.RemoveAt(i);
        }

        return raiz.ToJsonString();
    }
}
