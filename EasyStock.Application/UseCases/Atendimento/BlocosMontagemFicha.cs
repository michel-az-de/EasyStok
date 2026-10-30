using System.Text.Json;
using System.Text.Json.Serialization;
using EasyStock.Application.Ports.Output.Ai;

namespace EasyStock.Application.UseCases.Atendimento;

[JsonConverter(typeof(JsonStringEnumConverter<TipoBlocoMontagemFicha>))]
public enum TipoBlocoMontagemFicha
{
    [JsonStringEnumMemberName("resumo")] Resumo,
    [JsonStringEnumMemberName("comanda")] Comanda,
    [JsonStringEnumMemberName("nota")] Nota,
    [JsonStringEnumMemberName("frete")] Frete,
    [JsonStringEnumMemberName("acao")] Acao
}

[JsonConverter(typeof(JsonStringEnumConverter<AcaoBlocoMontagemFicha>))]
public enum AcaoBlocoMontagemFicha
{
    [JsonStringEnumMemberName("abrir_cardapio")] AbrirCardapio,
    [JsonStringEnumMemberName("rascunho")] Rascunho
}

public sealed record BlocoMontagemFichaResult(string Id, TipoBlocoMontagemFicha Tipo, string Texto, AcaoBlocoMontagemFicha? Acao = null);
public sealed record MontagemFichaResult(Guid ConversaId, Guid GeracaoId, DateTime GeradoEm, IReadOnlyList<BlocoMontagemFichaResult> Blocos);

/// <summary>Contrato fechado de apresentação. Nenhuma ferramenta executa ações ou aceita HTML, URLs, preços ou IDs do modelo.</summary>
public static class BlocosMontagemFicha
{
    public const string NomeFerramenta = "montar_ficha";
    public const int MaximoBlocos = 6;
    public static readonly FerramentaLlm Ferramenta = new(NomeFerramenta,
        "Organiza de 1 a 6 blocos úteis na ordem de leitura da atendente. Só propõe; não salva, envia ou altera pedidos. " +
        "Comanda e frete exibem os dados reais do sistema, não números gerados. Nota é proposta interna. " +
        "Use ação abrir_cardapio ou rascunho apenas quando ajudar agora.",
        """{"type":"object","properties":{"blocos":{"type":"array","minItems":1,"maxItems":6,"items":{"type":"object","properties":{"tipo":{"type":"string","enum":["resumo","comanda","nota","frete","acao"]},"texto":{"type":"string","minLength":1,"maxLength":1000},"acao":{"type":"string","enum":["abrir_cardapio","rascunho"]}},"required":["tipo","texto"],"additionalProperties":false}}},"required":["blocos"],"additionalProperties":false}""");

    public static IReadOnlyList<BlocoMontagemFichaResult> Traduzir(RespostaLlm resposta)
    {
        var usos = resposta.Conteudo.OfType<BlocoUsoFerramentaLlm>().ToArray();
        if (resposta.StopReason != RespostaLlm.StopToolUse || usos.Length != 1 || usos[0].Nome != NomeFerramenta)
            throw new FormatException("A montagem exige uma única ferramenta conhecida.");
        var entrada = usos[0].Entrada;
        if (!Propriedades(entrada, "blocos") || !entrada.TryGetProperty("blocos", out var blocos)
            || blocos.ValueKind != JsonValueKind.Array || blocos.GetArrayLength() is < 1 or > MaximoBlocos)
            throw new FormatException("Quantidade ou estrutura de blocos inválida.");
        var resultado = new List<BlocoMontagemFichaResult>();
        var contagens = new Dictionary<string, int>(StringComparer.Ordinal);
        var repetidos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bloco in blocos.EnumerateArray())
        {
            if (!Propriedades(bloco, "tipo", "texto", "acao")) throw new FormatException("Campo de bloco desconhecido.");
            var nome = Texto(bloco, "tipo", 20);
            var tipo = nome switch
            {
                "resumo" => TipoBlocoMontagemFicha.Resumo,
                "comanda" => TipoBlocoMontagemFicha.Comanda,
                "nota" => TipoBlocoMontagemFicha.Nota,
                "frete" => TipoBlocoMontagemFicha.Frete,
                "acao" => TipoBlocoMontagemFicha.Acao,
                _ => throw new FormatException("Tipo de bloco desconhecido.")
            };
            AcaoBlocoMontagemFicha? acao = null;
            if (tipo == TipoBlocoMontagemFicha.Acao)
                acao = Texto(bloco, "acao", 30) switch
                {
                    "abrir_cardapio" => AcaoBlocoMontagemFicha.AbrirCardapio,
                    "rascunho" => AcaoBlocoMontagemFicha.Rascunho,
                    _ => throw new FormatException("Ação de bloco desconhecida.")
                };
            else if (bloco.TryGetProperty("acao", out _)) throw new FormatException("Ação não permitida neste bloco.");
            var limite = tipo == TipoBlocoMontagemFicha.Nota ? ClienteNota.TextoTamanhoMaximo
                : acao == AcaoBlocoMontagemFicha.Rascunho ? PropostasAssistenteDona.TamanhoMaximoRascunho
                : tipo == TipoBlocoMontagemFicha.Resumo ? 500 : 180;
            var texto = Texto(bloco, "texto", limite);
            UseCaseGuards.EnsureSemTagsHtml(texto, "Bloco da ficha");
            if (!repetidos.Add($"{nome}|{acao}|{texto}")) throw new FormatException("Bloco repetido.");
            var ordinal = contagens.GetValueOrDefault(nome) + 1;
            contagens[nome] = ordinal;
            resultado.Add(new($"{nome}-{ordinal}", tipo, texto, acao));
        }
        return resultado;
    }

    private static bool Propriedades(JsonElement valor, params string[] permitidas)
    {
        if (valor.ValueKind != JsonValueKind.Object) return false;
        var nomes = new HashSet<string>(StringComparer.Ordinal);
        return valor.EnumerateObject().All(p => permitidas.Contains(p.Name, StringComparer.Ordinal) && nomes.Add(p.Name));
    }

    private static string Texto(JsonElement valor, string campo, int limite)
    {
        if (valor.TryGetProperty(campo, out var bruto) && bruto.ValueKind == JsonValueKind.String
            && bruto.GetString()?.Trim() is { Length: > 0 } texto && texto.Length <= limite) return texto;
        throw new FormatException("Texto ausente ou fora do limite.");
    }
}
