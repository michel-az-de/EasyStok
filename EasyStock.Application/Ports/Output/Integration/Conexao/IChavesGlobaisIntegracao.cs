namespace EasyStock.Application.Ports.Output.Integration.Conexao;

/// <summary>
/// Chaves globais da FMA, vindas de configuração (F16, #1246): a reserva quando a loja não tem a
/// própria (Mercado Pago, Google Maps) e o que nunca é da loja (token da app da Meta). Devolve os
/// campos no mesmo formato da chave da loja, ou null quando não há chave global.
/// </summary>
public interface IChavesGlobaisIntegracao
{
    IReadOnlyDictionary<string, string>? Obter(string provider);
}
