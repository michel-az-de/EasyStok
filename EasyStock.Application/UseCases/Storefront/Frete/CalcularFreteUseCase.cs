using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Storefront.Frete;

namespace EasyStock.Application.UseCases.Storefront.Frete;

/// <summary>
/// Calcula o frete para um CEP em um storefront, usando as
/// <see cref="FreteZona"/>s ativas configuradas pelo admin.
///
/// <para>
/// <strong>Fluxo</strong>:
/// </para>
/// <list type="number">
///   <item>Normaliza CEP (remove máscara → 8 dígitos puros).</item>
///   <item>Valida formato → <see cref="CepInvalidoException"/> se inválido.</item>
///   <item>Resolve storefront via slug → <see cref="StorefrontNaoEncontradoException"/> se inexistente/inativo.</item>
///   <item>(Opcional) consulta <see cref="ICepLookupClient"/> para enriquecer com bairro — best-effort, sem bloquear.</item>
///   <item>Pede ao <see cref="IFreteZonaRepository.BuscarZonaPorCepAsync"/> a primeira zona ativa que cobre.</item>
///   <item>Sem match → <see cref="CepSemCoberturaException"/>.</item>
///   <item>Match → <see cref="FreteCalculadoDto"/> com valor em centavos e ETA textual.</item>
/// </list>
///
/// <para>
/// <strong>Sem Haversine</strong> (ADR-0011): topografia e padrão de bairros
/// em SP tornam distância euclidiana enganosa. Admin configura zonas
/// explícitas — calculadora apenas avalia.
/// </para>
/// </summary>
public sealed class CalcularFreteUseCase(
    IStorefrontRepository storefrontRepository,
    IFreteZonaRepository freteZonaRepository,
    ICepLookupClient cepLookupClient,
    IGeocodingClient geocodingClient,
    ILogger<CalcularFreteUseCase> logger)
{
    private static readonly Regex CepDigitosRegex = new(@"^\d{8}$", RegexOptions.Compiled);

    public async Task<FreteCalculadoDto> ExecuteAsync(
        CalcularFreteInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var cepNormalizado = NormalizarCep(input.Cep);
        if (!CepDigitosRegex.IsMatch(cepNormalizado))
            throw new CepInvalidoException();

        var storefront = await storefrontRepository.GetBySlugAsync(input.Slug, ct);
        if (storefront is null || !storefront.Ativo)
        {
            logger.LogInformation(
                "Calculo de frete para storefront inexistente/inativo: slug={Slug}",
                input.Slug);
            throw new StorefrontNaoEncontradoException(input.Slug);
        }

        // ── (opcional) enriquecimento via ViaCEP — best-effort ─────────────
        CepLookupResult? endereco = null;
        try
        {
            endereco = await cepLookupClient.LookupAsync(cepNormalizado, ct);
        }
        catch (Exception ex)
        {
            // Defense-in-depth: contrato diz que client NUNCA lança, mas se uma
            // implementação futura violar isso, o checkout não pode cair.
            logger.LogWarning(ex,
                "ICepLookupClient lançou (violando contrato). Seguindo sem endereço. cep={Cep}",
                cepNormalizado);
        }

        var bairroNormalizado = endereco is not null && !string.IsNullOrWhiteSpace(endereco.Bairro)
            ? NormalizarBairro(endereco.Bairro)
            : string.Empty;

        // ── Frete por raio (ADR-0017) — quando o storefront tem config E o
        // geocode é confiável. Geocode ausente/impreciso cai pra zona (fallback). ──
        var raio = await TentarFreteRaioAsync(storefront, input, cepNormalizado, endereco, ct);
        if (raio is not null)
            return raio;

        // ── Fallback: frete por zona (ADR-0011) ────────────────────────────
        var zona = await freteZonaRepository.BuscarZonaPorCepAsync(
            storefront.Id, cepNormalizado, bairroNormalizado, ct);

        if (zona is null)
        {
            logger.LogInformation(
                "Sem cobertura para cep={Cep} bairro={Bairro} storefrontId={StorefrontId}",
                cepNormalizado, bairroNormalizado, storefront.Id);
            throw new CepSemCoberturaException();
        }

        var valorCentavos = ToCentavos(zona.Valor);
        return new FreteCalculadoDto(
            ZonaId: zona.Id,
            Valor: valorCentavos,
            ValorFormatado: FormatarValor(zona.Valor),
            EtaLabel: FormatarEta(zona.TempoEstimadoMinutos),
            ZonaLabel: zona.Label);
    }

    /// <summary>
    /// Tenta o frete por raio (ADR-0017). Retorna o DTO quando o storefront tem
    /// config E o geocode é confiável. <see langword="null"/> (cai pra zona) quando
    /// não há config, o geocode falhou ou veio impreciso. Lança
    /// <see cref="CepSemCoberturaException"/> quando o geocode confiável aponta
    /// distância acima do raio (fora da área — retirada, resultado autoritativo).
    /// </summary>
    private async Task<FreteCalculadoDto?> TentarFreteRaioAsync(
        EasyStock.Domain.Entities.Storefront.Storefront storefront,
        CalcularFreteInput input,
        string cepNormalizado,
        CepLookupResult? endereco,
        CancellationToken ct)
    {
        var config = FreteRaioConfigFactory.TentarCriar(storefront);
        if (config is null) return null; // raio não configurado → zona

        var geo = await geocodingClient.GeocodificarAsync(
            new GeocodeQuery(
                Logradouro: endereco?.Logradouro,
                Numero: input.Numero,
                Bairro: endereco?.Bairro,
                Cidade: endereco?.Cidade,
                Uf: endereco?.Uf,
                Cep: cepNormalizado),
            ct);

        // Geocode ausente ou impreciso → não cobra raio chutado; cai pra zona.
        if (geo is null || !geo.Confiavel)
        {
            logger.LogInformation(
                "Frete raio: geocode ausente/impreciso, caindo pra zona. cep={Cep} storefrontId={StorefrontId}",
                cepNormalizado, storefront.Id);
            return null;
        }

        var r = FreteRaioCalculadora.Calcular(new Coordenada(geo.Lat, geo.Lng), config);

        if (r.ForaDeCobertura)
        {
            logger.LogInformation(
                "Frete raio: fora de cobertura ({Metros}m rota). cep={Cep} storefrontId={StorefrontId}",
                r.DistanciaRotaMetros, cepNormalizado, storefront.Id);
            throw new CepSemCoberturaException();
        }

        // ETA estimada a partir da distância de rota (não há roteamento real no MVP).
        var minutos = 20 + (int)Math.Round(r.DistanciaRotaMetros / 1000.0 * 4, MidpointRounding.AwayFromZero);

        return new FreteCalculadoDto(
            ZonaId: Guid.Empty,
            Valor: r.ValorCentavos,
            ValorFormatado: FormatarValor(r.ValorCentavos / 100m),
            EtaLabel: FormatarEta(minutos),
            ZonaLabel: r.Gratis ? "Frete grátis" : "Entrega por raio");
    }

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>Remove tudo que não for dígito. Aceita "05500-000", "05500000", " 05500000 ".</summary>
    private static string NormalizarCep(string? cep)
    {
        if (string.IsNullOrWhiteSpace(cep)) return string.Empty;
        var sb = new StringBuilder(cep.Length);
        foreach (var c in cep)
        {
            if (char.IsDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Lowercase + remove acentos + trim. "Butantã" → "butanta".</summary>
    private static string NormalizarBairro(string bairro)
    {
        var trimmed = bairro.Trim();
        var formD = trimmed.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    private static int ToCentavos(decimal valor) =>
        (int)Math.Round(valor * 100m, MidpointRounding.AwayFromZero);

    private static string FormatarValor(decimal valor)
    {
        var ptBr = CultureInfo.GetCultureInfo("pt-BR");
        return valor.ToString("C2", ptBr);
    }

    /// <summary>Formata minutos em texto legível: 45 → "45 min", 90 → "1h30", 60 → "1h".</summary>
    private static string FormatarEta(int minutos)
    {
        if (minutos < 60) return $"{minutos} min";
        var horas = minutos / 60;
        var resto = minutos % 60;
        return resto == 0 ? $"{horas}h" : $"{horas}h{resto:D2}";
    }
}
