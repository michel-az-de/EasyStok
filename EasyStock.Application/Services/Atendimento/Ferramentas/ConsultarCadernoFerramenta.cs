using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>consultar_caderno(codigos)</c> (S54): devolve o texto dos trechos do caderno da loja pelos códigos do
/// índice que está no prompt. Só trechos ativos da empresa da conversa. Só lê.
/// </summary>
public sealed class ConsultarCadernoFerramenta(ICadernoRepository caderno) : IFerramentaAgente
{
    public const int MaximoCodigos = 5;

    public string Nome => "consultar_caderno";

    public string Descricao =>
        "Lê o texto de trechos do caderno da loja (políticas, entrega, troca, preparo, perguntas frequentes) " +
        "pelos códigos do índice do caderno. Use antes de responder sobre um tema do índice. Até 5 códigos por vez.";

    public string SchemaJson =>
        """{"type":"object","properties":{"codigos":{"type":"array","items":{"type":"string"},"minItems":1,"maxItems":5,"description":"Códigos do índice, ex.: [\"a1b2c3d4\"]"}},"required":["codigos"],"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        var codigos = LerCodigos(entrada);
        if (codigos.Count == 0)
            return FerramentaJson.Serializar(new { erro = "codigos_obrigatorios" });

        var porCodigo = (await caderno.ListarAsync(contexto.EmpresaId, incluirArquivados: false, ct))
            .ToDictionary(t => t.Codigo, StringComparer.OrdinalIgnoreCase);
        var achados = codigos.Where(porCodigo.ContainsKey).Select(c => porCodigo[c]).ToList();
        var faltando = codigos.Where(c => !porCodigo.ContainsKey(c)).ToList();

        return FerramentaJson.Serializar(new
        {
            trechos = achados.Select(t => new { codigo = t.Codigo, titulo = t.Titulo, texto = t.Texto }),
            naoEncontrados = faltando.Count > 0 ? faltando : null
        });
    }

    private static List<string> LerCodigos(JsonElement entrada) =>
        entrada.ValueKind == JsonValueKind.Object
        && entrada.TryGetProperty("codigos", out var lista)
        && lista.ValueKind == JsonValueKind.Array
            ? lista.EnumerateArray()
                .Where(c => c.ValueKind == JsonValueKind.String)
                .Select(c => c.GetString()!.Trim())
                .Where(c => c.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaximoCodigos)
                .ToList()
            : [];
}
