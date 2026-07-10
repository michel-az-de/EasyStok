using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.Ports.Output.Notifications;
using Microsoft.Extensions.Logging;
using Scriban;
using Scriban.Syntax;

namespace EasyStock.Infra.Notifications.Templating;

public sealed class ScribanRenderer(ILogger<ScribanRenderer> logger) : IRendererTemplate
{
    // Guarda de sanidade: um template nao pode custar segundos. Default 500ms -- prod e dev local
    // ficam INALTERADOS. Configuravel por EASYSTOK_SCRIBAN_TIMEOUT_MS so para o CI relaxar para 2s:
    // o runner, sob JIT frio/GC, estoura 500ms na primeira renderizacao (flaky reproduzido 2/2 sem
    // carga -- issue #910, docs/dev/flaky-tests.md). Lido uma vez (static); o ci.yml seta a env var
    // no processo de teste antes de rodar. Fallback para 500ms se ausente, vazia ou <= 0.
    private static readonly TimeSpan RenderTimeout =
        int.TryParse(Environment.GetEnvironmentVariable("EASYSTOK_SCRIBAN_TIMEOUT_MS"), out var ms) && ms > 0
            ? TimeSpan.FromMilliseconds(ms)
            : TimeSpan.FromMilliseconds(500);
    private const int TemplateMaxBytes = 256 * 1024;
    private const int CacheCapacity = 256;

    private readonly ConcurrentDictionary<string, Template> _cache = new();

    public Task<string> RenderizarAsync(
        string templateText,
        IDictionary<string, object?> variaveis,
        CancellationToken ct = default)
        => RenderizarInternoAsync(templateText, variaveis, htmlEscape: false, ct);

    public Task<string> RenderizarAsync(
        string templateText,
        IDictionary<string, object?> variaveis,
        bool htmlEscape,
        CancellationToken ct = default)
        => RenderizarInternoAsync(templateText, variaveis, htmlEscape, ct);

    private async Task<string> RenderizarInternoAsync(
        string templateText,
        IDictionary<string, object?> variaveis,
        bool htmlEscape,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(templateText);
        ArgumentNullException.ThrowIfNull(variaveis);
        ct.ThrowIfCancellationRequested();

        var sourceBytes = Encoding.UTF8.GetByteCount(templateText);
        if (sourceBytes > TemplateMaxBytes)
        {
            logger.LogWarning(
                "Template excede tamanho maximo: {Bytes}B > {Max}B", sourceBytes, TemplateMaxBytes);
            throw new InvalidOperationException(
                $"Template excede tamanho maximo de {TemplateMaxBytes} bytes (recebido {sourceBytes}).");
        }

        var template = ObterTemplateCompilado(templateText);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RenderTimeout);

        var variaveisPreparadas = htmlEscape ? EscaparHtml(variaveis) : variaveis;
        var context = ScribanSandbox.CriarContexto(variaveisPreparadas, cts.Token);

        try
        {
            var resultado = await template.RenderAsync(context).ConfigureAwait(false);
            return resultado ?? string.Empty;
        }
        catch (ScriptAbortException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "Timeout ao renderizar template (>{Timeout}ms)", RenderTimeout.TotalMilliseconds);
            throw new TimeoutException(
                $"Renderizacao de template excedeu {RenderTimeout.TotalMilliseconds}ms.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao renderizar template Scriban");
            throw new InvalidOperationException($"Erro na renderizacao: {ex.Message}", ex);
        }
    }

    private Template ObterTemplateCompilado(string templateText)
    {
        var key = ComputarHash(templateText);

        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var parsed = Template.Parse(templateText);

        if (parsed.HasErrors)
        {
            var erros = string.Join("; ", parsed.Messages.Select(m => m.Message));
            logger.LogWarning("Template com erros de parse: {Erros}", erros);
            throw new InvalidOperationException($"Template invalido: {erros}");
        }

        if (_cache.Count >= CacheCapacity)
            _cache.Clear();

        _cache[key] = parsed;
        return parsed;
    }

    private static string ComputarHash(string text)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(text), hash);
        return Convert.ToHexString(hash);
    }

    private static IDictionary<string, object?> EscaparHtml(IDictionary<string, object?> variaveis)
    {
        var resultado = new Dictionary<string, object?>(variaveis.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in variaveis)
            resultado[kv.Key] = kv.Value is string s ? WebUtility.HtmlEncode(s) : kv.Value;
        return resultado;
    }
}
