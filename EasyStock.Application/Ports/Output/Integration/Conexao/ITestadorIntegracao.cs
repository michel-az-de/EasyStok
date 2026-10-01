using EasyStock.Domain.Integration;

namespace EasyStock.Application.Ports.Output.Integration.Conexao;

/// <summary>De onde veio a chave testada: a da loja (cifrada no banco) ou a global da FMA (config).</summary>
public enum OrigemChaveIntegracao
{
    Loja = 1,
    Global = 2,
}

/// <summary>
/// Chave já decifrada, pronta para o teste. <see cref="Campos"/> segue o formato do provider
/// (ex.: <c>accessToken</c>; <c>apiKey</c> + <c>apiSecret</c>). Vive só durante o teste: nunca
/// vai para log, resposta da API ou mensagem de erro.
/// </summary>
public sealed record ChaveParaTeste(
    string Provider,
    AmbienteIntegracao Ambiente,
    IReadOnlyDictionary<string, string> Campos,
    OrigemChaveIntegracao Origem)
{
    public string? Campo(string nome) => Campos.TryGetValue(nome, out var valor) ? valor : null;

    /// <summary>Sem o segredo, para nenhum log ou depurador mostrar o valor.</summary>
    public override string ToString() => $"ChaveParaTeste({Provider}, {Ambiente}, {Origem})";
}

/// <summary>Resultado do teste de conexão, já em português e sem segredo.</summary>
public sealed record ResultadoTesteIntegracao(bool Ok, string Mensagem)
{
    public static ResultadoTesteIntegracao Passou(string mensagem) => new(true, mensagem);
    public static ResultadoTesteIntegracao Falhou(string mensagem) => new(false, mensagem);
}

/// <summary>
/// Teste de conexão de UM provider (F16, #1246): uma chamada barata, sem efeito colateral (nada é
/// cobrado, pedido nem enviado). Nunca lança por erro do provedor: devolve
/// <see cref="ResultadoTesteIntegracao"/> com a mensagem em português. Cancelamento do chamador
/// (o teto de 5 s) propaga como <see cref="OperationCanceledException"/>.
/// </summary>
public interface ITestadorIntegracao
{
    /// <summary>Chave do provider no catálogo (<c>mercadopago</c>, <c>whatsapp</c>, <c>googlemaps</c>, <c>lalamove</c>).</summary>
    string Provider { get; }

    Task<ResultadoTesteIntegracao> TestarAsync(ChaveParaTeste chave, CancellationToken ct = default);
}
