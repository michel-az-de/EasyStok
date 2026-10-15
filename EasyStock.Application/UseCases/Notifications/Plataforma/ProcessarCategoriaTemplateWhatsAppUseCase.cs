using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;

namespace EasyStock.Application.UseCases.Notifications.Plataforma;

/// <summary>
/// <c>template_category_update</c> (N6). A Meta não deixa sobrescrever o webhook de template, então o evento chega ao
/// callback do app. Categoria <c>MARKETING</c>, no aviso (<c>correct_category</c>, 24 h antes) ou consumada
/// (<c>new_category</c>), grava o template em <c>notif_templates_meta_estado</c>: a Meta o mantém <c>APPROVED</c> e passa a
/// cobrar como marketing, e o envio de plataforma seguinte falha permanente sem chamar a Meta. A categoria consumada de
/// outro tipo também é gravada, o que desfaz um bloqueio antigo e nunca cria um.
/// </summary>
public sealed class ProcessarCategoriaTemplateWhatsAppUseCase(
    ITemplateMetaEstadoRepository estados,
    IUnitOfWork unitOfWork,
    ILogger<ProcessarCategoriaTemplateWhatsAppUseCase> logger)
{
    /// <returns><c>false</c> só em falha transitória (banco): a Meta reenvia.</returns>
    public async Task<bool> ExecuteAsync(string rawBody, CancellationToken ct = default)
    {
        List<(string Nome, string Idioma, string Categoria)> mudancas;
        try
        {
            mudancas = Ler(rawBody);
        }
        catch (JsonException)
        {
            logger.LogWarning("Webhook template_category_update: corpo não é JSON, ignorado.");
            return true;
        }

        if (mudancas.Count == 0) return true;

        try
        {
            foreach (var (nome, idioma, categoria) in mudancas)
            {
                await estados.GravarCategoriaAsync(nome, idioma, categoria, ct);
                if (categoria.Equals(TemplateMetaEstado.CategoriaMarketing, StringComparison.OrdinalIgnoreCase))
                    logger.LogWarning(
                        "Template {Template} ({Idioma}) recategorizado como marketing pela Meta: bloqueado na plataforma.",
                        Limpar(nome), Limpar(idioma));
            }

            await unitOfWork.CommitAsync();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Webhook template_category_update: falha transitória ao gravar o estado do template.");
            return false;
        }
    }

    private static List<(string Nome, string Idioma, string Categoria)> Ler(string rawBody)
    {
        var saida = new List<(string, string, string)>();
        using var doc = JsonDocument.Parse(rawBody);
        if (!doc.RootElement.TryGetProperty("entry", out var entradas) || entradas.ValueKind != JsonValueKind.Array)
            return saida;

        foreach (var entrada in entradas.EnumerateArray())
        {
            if (!entrada.TryGetProperty("changes", out var mudancas) || mudancas.ValueKind != JsonValueKind.Array) continue;
            foreach (var mudanca in mudancas.EnumerateArray())
            {
                if (Texto(mudanca, "field") != CamposWebhookMeta.FieldTemplateCategoryUpdate) continue;
                if (!mudanca.TryGetProperty("value", out var v)) continue;

                var nome = Texto(v, "message_template_name");
                var idioma = Texto(v, "message_template_language");
                if (string.IsNullOrWhiteSpace(nome) || string.IsNullOrWhiteSpace(idioma)) continue;

                // Consumada: qualquer categoria nova. Aviso (24 h antes): só importa se for marketing.
                var nova = Texto(v, "new_category");
                var aviso = Texto(v, "correct_category");
                if (!string.IsNullOrWhiteSpace(nova))
                    saida.Add((nome, idioma, nova));
                else if (string.Equals(aviso, TemplateMetaEstado.CategoriaMarketing, StringComparison.OrdinalIgnoreCase))
                    saida.Add((nome, idioma, aviso!));
            }
        }

        return saida;
    }

    private static string? Texto(JsonElement e, string nome) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nome, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    /// <summary>Nome vindo do webhook, sem quebra de linha no log (log forging).</summary>
    private static string Limpar(string valor) => valor.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
}
