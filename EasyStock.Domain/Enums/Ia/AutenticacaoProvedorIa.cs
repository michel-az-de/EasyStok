namespace EasyStock.Domain.Enums.Ia;

/// <summary>#2957: cabeçalho que leva a chave do provedor de IA.</summary>
public enum AutenticacaoProvedorIa
{
    /// <summary>Cabeçalho <c>x-api-key</c>, formato Anthropic.</summary>
    XApiKey = 1,

    /// <summary>Cabeçalho <c>Authorization: Bearer</c>.</summary>
    Bearer = 2,
}
