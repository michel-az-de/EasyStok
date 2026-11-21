using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>Preço em dólar por milhão de tokens de entrada, saída e cache lido.</summary>
public sealed record PrecoModeloAgente(decimal Entrada, decimal Saida, decimal CacheLido);

/// <summary>
/// S67 (#2534): tabela de preço do Assistente, versionada na configuração em <c>Anthropic:Precos:&lt;modelo&gt;</c>
/// (<c>Entrada</c>, <c>Saida</c>, <c>CacheLido</c>, por milhão de tokens). O provedor responde com a versão datada
/// (<c>claude-sonnet-5-20261101</c>): vale o nome cadastrado mais longo que é prefixo dela. Modelo sem preço dá custo
/// nulo, nunca zero, para o painel não mostrar "grátis".
/// </summary>
public sealed class PrecosModelosAgenteOptions
{
    public const string Secao = "Anthropic:Precos";

    private const decimal Milhao = 1_000_000m;

    public Dictionary<string, PrecoModeloAgente> Modelos { get; } = new(StringComparer.OrdinalIgnoreCase);

    public decimal? Estimar(string? modelo, int tokensEntrada, int tokensSaida, int tokensCacheLido)
    {
        if (string.IsNullOrWhiteSpace(modelo)) return null;
        var preco = Modelos
            .Where(par => modelo.StartsWith(par.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(par => par.Key.Length)
            .Select(par => par.Value)
            .FirstOrDefault();
        if (preco is null) return null;

        return (tokensEntrada * preco.Entrada + tokensSaida * preco.Saida + tokensCacheLido * preco.CacheLido) / Milhao;
    }

    /// <summary>Modelo sem os três preços fica de fora: preço pela metade daria custo errado.</summary>
    public void Ler(IConfiguration configuracao)
    {
        foreach (var secao in configuracao.GetSection(Secao).GetChildren())
        {
            if (Valor(secao["Entrada"]) is { } entrada && Valor(secao["Saida"]) is { } saida
                && Valor(secao["CacheLido"]) is { } cacheLido)
                Modelos[secao.Key] = new PrecoModeloAgente(entrada, saida, cacheLido);
        }
    }

    private static decimal? Valor(string? texto) =>
        decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor) && valor >= 0 ? valor : null;
}
