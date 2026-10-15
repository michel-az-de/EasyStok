using System.Text.Json;
using System.Text.Json.Nodes;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Lê e grava chaves do <see cref="Conversa.ContextoJson"/> sem perder as outras (carrinho, etc.).
/// Chaves da S14: <see cref="EnderecoPendente"/>, <see cref="ForaDeAreaLiberado"/>, <see cref="ForaDeAreaMotivo"/>.
/// </summary>
public static class ContextoConversaJson
{
    public const string EnderecoPendente = "enderecoPendente";
    public const string ForaDeAreaLiberado = "foraDeAreaLiberado";
    public const string ForaDeAreaMotivo = "foraDeAreaMotivo";

    private static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web);

    public static T? Ler<T>(Conversa conversa, string chave)
    {
        var raiz = JsonNode.Parse(conversa.ContextoJson) as JsonObject;
        var no = raiz?[chave];
        return no is null ? default : no.Deserialize<T>(Opcoes);
    }

    /// <summary>Grava a chave; valor nulo remove.</summary>
    public static void Gravar<T>(Conversa conversa, string chave, T? valor)
    {
        var raiz = JsonNode.Parse(conversa.ContextoJson) as JsonObject ?? new JsonObject();
        if (valor is null)
            raiz.Remove(chave);
        else
            raiz[chave] = JsonSerializer.SerializeToNode(valor, Opcoes);
        conversa.DefinirContexto(raiz.ToJsonString());
    }
}
