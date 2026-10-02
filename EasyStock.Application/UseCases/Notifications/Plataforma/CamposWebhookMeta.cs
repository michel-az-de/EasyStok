using System.Text.Json;
using System.Text.Json.Nodes;

namespace EasyStock.Application.UseCases.Notifications.Plataforma;

/// <summary>
/// O corpo do callback do app dividido pelo <c>field</c> de cada mudança (N6). A Meta não deixa sobrescrever o webhook de
/// template: <c>template_category_update</c> chega sempre ao callback do app, ao lado de <c>messages</c> do atendimento.
/// <see cref="Mensagens"/> é o corpo só com as mudanças que o atendimento entende (<c>messages</c> ou sem <c>field</c>,
/// como os testes antigos); <see cref="CategoriaTemplate"/> só com as de <c>template_category_update</c>.
/// <see cref="Ignoradas"/> conta as de outro <c>field</c>, descartadas de propósito.
/// </summary>
public sealed record CamposWebhookMeta(string? Mensagens, string? CategoriaTemplate, int Ignoradas)
{
    public const string FieldMessages = "messages";
    public const string FieldTemplateCategoryUpdate = "template_category_update";

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

        int mensagens = 0, categoria = 0, ignoradas = 0;
        foreach (var entrada in entradas)
        {
            if (entrada?["changes"] is not JsonArray mudancas) continue;
            foreach (var mudanca in mudancas)
            {
                switch (Campo(mudanca))
                {
                    case FieldMessages: mensagens++; break;
                    case FieldTemplateCategoryUpdate: categoria++; break;
                    default: ignoradas++; break;
                }
            }
        }

        if (categoria == 0 && ignoradas == 0)
            return new CamposWebhookMeta(rawBody, null, 0);

        return new CamposWebhookMeta(
            mensagens > 0 ? Filtrar(rawBody, FieldMessages) : null,
            categoria > 0 ? Filtrar(rawBody, FieldTemplateCategoryUpdate) : null,
            ignoradas);
    }

    /// <summary>Sem <c>field</c> vale <c>messages</c>: o formato antigo que o atendimento sempre aceitou.</summary>
    private static string Campo(JsonNode? mudanca) =>
        mudanca?["field"]?.GetValueKind() == JsonValueKind.String
            ? mudanca["field"]!.GetValue<string>()
            : FieldMessages;

    private static string Filtrar(string rawBody, string campo)
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
                if (Campo(mudancas[j]) != campo) mudancas.RemoveAt(j);

            if (mudancas.Count == 0) entradas.RemoveAt(i);
        }

        return raiz.ToJsonString();
    }
}
