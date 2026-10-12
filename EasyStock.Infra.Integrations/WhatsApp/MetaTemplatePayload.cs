namespace EasyStock.Infra.Integrations.WhatsApp;

/// <summary>
/// Monta o JSON de mensagem de template da Cloud API num ponto só (N6), compartilhado pelo envio da loja e pelo da
/// plataforma. Ordem dos componentes: cabeçalho, corpo, botões.
/// </summary>
internal static class MetaTemplatePayload
{
    /// <summary>Limite da Meta para o código do botão de copiar código de template de autenticação.</summary>
    public const int CopyCodeTamanhoMaximo = 15;

    public static object Montar(
        string waId,
        string nomeTemplate,
        string idioma,
        IReadOnlyList<string> parametrosCorpo,
        IReadOnlyList<(string Id, string Titulo)>? botoesQuickReply = null,
        string? imagemCabecalho = null,
        string? botaoUrl0 = null,
        string? botaoUrl1 = null,
        string? opacoCallback = null)
    {
        var components = new List<object>();
        if (!string.IsNullOrWhiteSpace(imagemCabecalho))
        {
            components.Add(new
            {
                type = "header",
                parameters = new object[] { new { type = "image", image = new { link = imagemCabecalho } } }
            });
        }

        if (parametrosCorpo.Count > 0)
        {
            components.Add(new
            {
                type = "body",
                parameters = parametrosCorpo.Select(p => new { type = "text", text = p }).ToArray()
            });
        }

        if (botoesQuickReply is { Count: > 0 })
        {
            for (var i = 0; i < botoesQuickReply.Count; i++)
            {
                components.Add(new
                {
                    type = "button",
                    sub_type = "quick_reply",
                    index = i.ToString(),
                    parameters = new object[] { new { type = "payload", payload = botoesQuickReply[i].Id } }
                });
            }
        }

        AdicionarBotaoUrl(components, "0", botaoUrl0);
        AdicionarBotaoUrl(components, "1", botaoUrl1);

        var template = new { name = nomeTemplate, language = new { code = idioma }, components };
        if (string.IsNullOrEmpty(opacoCallback))
        {
            return new { messaging_product = "whatsapp", to = waId, type = "template", template };
        }

        return new
        {
            messaging_product = "whatsapp",
            to = waId,
            type = "template",
            template,
            biz_opaque_callback_data = opacoCallback
        };
    }

    private static void AdicionarBotaoUrl(List<object> components, string indice, string? valor)
    {
        if (string.IsNullOrEmpty(valor)) return;
        components.Add(new
        {
            type = "button",
            sub_type = "url",
            index = indice,
            parameters = new object[] { new { type = "text", text = valor } }
        });
    }
}
