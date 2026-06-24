using System.Text.Json;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Domain.Storefront.Frete;

/// <summary>
/// Constrói o <see cref="FreteRaioConfig"/> a partir da config persistida no
/// <see cref="EasyStock.Domain.Entities.Storefront.Storefront"/> (ADR-0017, #673 S3).
///
/// <para>
/// Retorna <see langword="null"/> se o raio NÃO está configurado (faltam coordenadas
/// da cozinha ou parâmetros) — nesse caso o frete cai para a tabela de zonas (fallback).
/// </para>
/// </summary>
public static class FreteRaioConfigFactory
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Mapeia a config de raio do storefront, ou null se não configurado.</summary>
    public static FreteRaioConfig? TentarCriar(StorefrontEntity storefront)
    {
        ArgumentNullException.ThrowIfNull(storefront);

        // Todos os escalares precisam estar presentes e válidos; senão raio desligado.
        if (storefront.CozinhaLat is not { } lat
            || storefront.CozinhaLng is not { } lng
            || storefront.FreteFatorRota is not { } fator
            || storefront.FreteFaixaGratisMetros is not { } gratis
            || storefront.FreteRaioMaxMetros is not { } raioMax
            || fator <= 0
            || raioMax <= 0)
            return null;

        return new FreteRaioConfig(
            Origem: new Coordenada(lat, lng),
            FatorRota: fator,
            FaixaGratisMetros: gratis,
            RaioMaxMetros: raioMax,
            Faixas: ParseFaixas(storefront.FreteFaixasJson));
    }

    private static IReadOnlyList<FreteFaixa> ParseFaixas(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<FreteFaixa>();

        try
        {
            var dtos = JsonSerializer.Deserialize<List<FaixaJson>>(json, JsonOpts);
            if (dtos is null) return Array.Empty<FreteFaixa>();

            var faixas = new List<FreteFaixa>(dtos.Count);
            foreach (var d in dtos)
            {
                if (!string.IsNullOrWhiteSpace(d.Id) && d.AteMetros > 0 && d.ValorCentavos >= 0)
                    faixas.Add(new FreteFaixa(d.Id, d.AteMetros, d.ValorCentavos));
            }
            return faixas;
        }
        catch (JsonException)
        {
            // Config corrompida não derruba o cálculo: sem faixas → fora de cobertura
            // após o grátis (o caller decide o fallback). Defensivo.
            return Array.Empty<FreteFaixa>();
        }
    }

    private sealed record FaixaJson
    {
        public string? Id { get; init; }
        public int AteMetros { get; init; }
        public int ValorCentavos { get; init; }
    }
}
