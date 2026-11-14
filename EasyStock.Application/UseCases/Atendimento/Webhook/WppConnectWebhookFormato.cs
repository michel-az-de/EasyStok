using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EasyStock.Application.UseCases.Atendimento.Webhook;

/// <summary>
/// #2591: descreve o FORMATO de um callback do WPPConnect, isto é, nomes de campo e tipo JSON de cada um.
/// Nenhum valor de campo sai daqui, só o tamanho dos textos e o valor dos booleanos. O corpo recusado não
/// fica gravado em lugar nenhum; sem esta descrição o log traz o motivo e a causa segue sem observação.
/// </summary>
public static partial class WppConnectWebhookFormato
{
    private const int NiveisDeObjeto = 3;
    private const int CamposPorObjeto = 40;
    private const int Limite = 1200;

    public static string Descrever(string? rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody)) return "corpo vazio";
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            var texto = new StringBuilder();
            if (root.ValueKind == JsonValueKind.Object)
                texto.Append("evento=").Append(Rotulo(root, "event")).Append(" tipo=").Append(Rotulo(root, "type")).Append(" campos");
            else
                texto.Append("raiz=");
            Valor(texto, root, 0);
            return texto.Length <= Limite ? texto.ToString() : string.Concat(texto.ToString(0, Limite), "...");
        }
        catch (JsonException)
        {
            return $"json inválido ({rawBody.Length} caracteres)";
        }
    }

    /// <summary>Evento e tipo vêm de uma lista fechada do WPPConnect; fora do padrão de rótulo, o valor não é ecoado.</summary>
    private static string Rotulo(JsonElement root, string nome) =>
        !root.TryGetProperty(nome, out var valor) ? "ausente"
        : valor.ValueKind == JsonValueKind.String && RotuloSeguro().IsMatch(valor.GetString()!) ? valor.GetString()!
        : "?";

    private static void Valor(StringBuilder texto, JsonElement valor, int nivel)
    {
        switch (valor.ValueKind)
        {
            case JsonValueKind.Object when nivel < NiveisDeObjeto: Objeto(texto, valor, nivel); break;
            case JsonValueKind.Object: texto.Append("object"); break;
            case JsonValueKind.Array: texto.Append("array(").Append(valor.GetArrayLength()).Append(')'); break;
            case JsonValueKind.String: texto.Append("string(").Append(valor.GetString()!.Length).Append(')'); break;
            case JsonValueKind.Number: texto.Append("number"); break;
            case JsonValueKind.True: texto.Append("true"); break;
            case JsonValueKind.False: texto.Append("false"); break;
            default: texto.Append("null"); break;
        }
    }

    private static void Objeto(StringBuilder texto, JsonElement objeto, int nivel)
    {
        int mostrados = 0, excedentes = 0, indices = 0, foraDoPadrao = 0;
        texto.Append('{');
        foreach (var campo in objeto.EnumerateObject())
        {
            // Nome de campo que não é identificador pode ser dado (mapa por telefone, texto espalhado em índices): só conta.
            if (!NomeDeCampo().IsMatch(campo.Name))
            {
                if (campo.Name.Length is > 0 and <= 4 && campo.Name.All(char.IsAsciiDigit)) indices++; else foraDoPadrao++;
                continue;
            }
            if (mostrados == CamposPorObjeto) { excedentes++; continue; }
            if (mostrados++ > 0) texto.Append(',');
            texto.Append(campo.Name).Append(':');
            Valor(texto, campo.Value, nivel + 1);
        }
        var primeiro = mostrados == 0;
        Resto(texto, excedentes, "campos", ref primeiro);
        Resto(texto, indices, "índices", ref primeiro);
        Resto(texto, foraDoPadrao, "fora do padrão", ref primeiro);
        texto.Append('}');
    }

    private static void Resto(StringBuilder texto, int quantos, string oQue, ref bool primeiro)
    {
        if (quantos == 0) return;
        if (!primeiro) texto.Append(',');
        primeiro = false;
        texto.Append('+').Append(quantos).Append(' ').Append(oQue);
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z_-]{0,31}$")]
    private static partial Regex RotuloSeguro();

    /// <summary>Identificador sem sequência de 4 dígitos ou mais: telefone e identificador de mensagem não passam.</summary>
    [GeneratedRegex(@"^(?!.*\d{4})[A-Za-z_][A-Za-z0-9_]{0,39}$")]
    private static partial Regex NomeDeCampo();
}
