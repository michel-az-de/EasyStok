using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.Extensions.Logging;

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
        var bairroNormalizado = string.Empty;
        try
        {
            var endereco = await cepLookupClient.LookupAsync(cepNormalizado, ct);
            if (endereco is not null && !string.IsNullOrWhiteSpace(endereco.Bairro))
                bairroNormalizado = NormalizarBairro(endereco.Bairro);
        }
        catch (Exception ex)
        {
            // Defense-in-depth: contrato diz que client NUNCA lança, mas se uma
            // implementação futura violar isso, o checkout não pode cair.
            logger.LogWarning(ex,
                "ICepLookupClient lançou (violando contrato). Seguindo sem bairro. cep={Cep}",
                cepNormalizado);
        }

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
