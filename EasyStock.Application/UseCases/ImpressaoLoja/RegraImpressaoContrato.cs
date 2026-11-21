using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Application.UseCases.ImpressaoLoja;

/// <summary>
/// Regra de impressão da loja v1 (#2199; spec impressão v2, D2; auditoria 3, seção 8.4): diz "como sai" cada documento,
/// igual no PWA, no APK e na Web. O "onde sai" (impressora, MAC, calibração) fica no aparelho. A fixture
/// <c>docs/contracts/impressao-loja.v1.fixture.json</c> prende este contrato ao normalizador do cliente.
/// </summary>
public static class RegraImpressaoCatalogo
{
    public const int Versao = 1;
    public const int CopiasMin = 1;
    public const int CopiasMax = 10;
    public const int CopiasPadrao = 1;

    public static readonly IReadOnlyList<string> Documentos = ["etiqueta-producao", "etiqueta-pedido", "pedido"];

    public static readonly IReadOnlyList<string> Papeis =
        ["cupom-58", "cupom-80", "etiqueta-52", "etiqueta-80", "etiqueta-10x15", "a4", "a5"];

    public static readonly IReadOnlyList<string> Orientacoes = ["vertical", "horizontal"];

    /// <summary>Ordem canônica: QR primeiro. O renderizador esconde o elemento <c>code</c> que não estiver na lista.</summary>
    public static readonly IReadOnlyList<string> Codigos = ["qr", "barras"];

    public static readonly IReadOnlyList<string> OrigensTemplate = ["Sistema", "Empresa"];
}

/// <summary>Corpo recebido no PUT, ainda sem validar. Campo ausente e <c>null</c> são a mesma coisa.</summary>
public sealed record RegraImpressaoLojaEntrada(
    [property: JsonPropertyName("v")] int? V,
    [property: JsonPropertyName("documentos")] Dictionary<string, RegraImpressaoDocumentoEntrada?>? Documentos);

public sealed record RegraImpressaoDocumentoEntrada(
    [property: JsonPropertyName("papel")] string? Papel,
    [property: JsonPropertyName("template")] TemplateImpressaoEntrada? Template,
    [property: JsonPropertyName("orientacao")] string? Orientacao,
    [property: JsonPropertyName("codigos")] List<string?>? Codigos,
    [property: JsonPropertyName("copias")] int? Copias);

public sealed record TemplateImpressaoEntrada(
    [property: JsonPropertyName("origem")] string? Origem,
    [property: JsonPropertyName("id")] Guid? Id);

/// <summary>Forma canônica: documentos na ordem do catálogo, só os configurados.</summary>
public sealed record RegraImpressaoLoja(IReadOnlyList<RegraImpressaoDocumento> Documentos);

/// <param name="Template"><c>null</c> usa o modelo padrão da empresa.</param>
/// <param name="Orientacao"><c>null</c> herda a orientação do template.</param>
/// <param name="Codigos"><c>null</c> mostra os códigos do template; lista vazia esconde todos.</param>
public sealed record RegraImpressaoDocumento(
    string Documento,
    string Papel,
    TemplateImpressao? Template,
    string? Orientacao,
    IReadOnlyList<string>? Codigos,
    int Copias);

public sealed record TemplateImpressao(string Origem, Guid Id);

/// <summary>
/// Normaliza para a forma canônica e escreve o texto canônico, byte a byte estável: a comparação de "mesmo corpo" do PUT
/// e o JSON do GET saem daqui. Total: o que não é do contrato é descartado em vez de lançar, para a leitura nunca cair.
/// </summary>
public static class RegraImpressaoNormalizador
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static RegraImpressaoLoja Normalizar(RegraImpressaoLojaEntrada entrada)
    {
        var documentos = new List<RegraImpressaoDocumento>();
        foreach (var nome in RegraImpressaoCatalogo.Documentos)
        {
            if (entrada.Documentos is null || !entrada.Documentos.TryGetValue(nome, out var doc) || doc is null)
                continue;
            if (doc.Papel is null || !RegraImpressaoCatalogo.Papeis.Contains(doc.Papel))
                continue;

            documentos.Add(new RegraImpressaoDocumento(
                nome,
                doc.Papel,
                NormalizarTemplate(doc.Template),
                doc.Orientacao is not null && RegraImpressaoCatalogo.Orientacoes.Contains(doc.Orientacao) ? doc.Orientacao : null,
                doc.Codigos is null ? null : RegraImpressaoCatalogo.Codigos.Where(c => doc.Codigos.Contains(c)).ToList(),
                Math.Clamp(doc.Copias ?? RegraImpressaoCatalogo.CopiasPadrao, RegraImpressaoCatalogo.CopiasMin, RegraImpressaoCatalogo.CopiasMax)));
        }

        return new RegraImpressaoLoja(documentos);
    }

    /// <summary>Lê o que o banco guardou (o jsonb reordena as chaves) de volta na forma canônica.</summary>
    public static RegraImpressaoLoja? DoJson(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : Normalizar(JsonSerializer.Deserialize<RegraImpressaoLojaEntrada>(json, Web)
                ?? new RegraImpressaoLojaEntrada(RegraImpressaoCatalogo.Versao, null));

    public static string ParaJson(RegraImpressaoLoja regra)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteNumber("v", RegraImpressaoCatalogo.Versao);
            w.WriteStartObject("documentos");
            foreach (var doc in regra.Documentos)
            {
                w.WriteStartObject(doc.Documento);
                w.WriteString("papel", doc.Papel);
                if (doc.Template is null)
                {
                    w.WriteNull("template");
                }
                else
                {
                    w.WriteStartObject("template");
                    w.WriteString("origem", doc.Template.Origem);
                    w.WriteString("id", doc.Template.Id.ToString("D"));
                    w.WriteEndObject();
                }

                if (doc.Orientacao is null) w.WriteNull("orientacao");
                else w.WriteString("orientacao", doc.Orientacao);

                if (doc.Codigos is null)
                {
                    w.WriteNull("codigos");
                }
                else
                {
                    w.WriteStartArray("codigos");
                    foreach (var codigo in doc.Codigos) w.WriteStringValue(codigo);
                    w.WriteEndArray();
                }

                w.WriteNumber("copias", doc.Copias);
                w.WriteEndObject();
            }

            w.WriteEndObject();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static TemplateImpressao? NormalizarTemplate(TemplateImpressaoEntrada? template) =>
        template is { Origem: not null, Id: { } id }
        && id != Guid.Empty
        && RegraImpressaoCatalogo.OrigensTemplate.Contains(template.Origem)
            ? new TemplateImpressao(template.Origem, id)
            : null;
}
