using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EasyStock.Domain.Exceptions;

namespace EasyStock.Domain.Entities.Storefront;

/// <summary>
/// Zona de entrega de um <see cref="Storefront"/> — modelada por CEP range OU
/// lista de bairros (NÃO por coordenadas — ver ADR-0011, ADR-0013).
///
/// <para>
/// Cliente informa CEP (ou bairro) no checkout, backend itera as zonas ativas
/// do storefront ordenadas por <see cref="Ordem"/> ascendente e retorna a
/// primeira que <see cref="CobreCep"/> ou <see cref="CobreBairro"/> aceitar.
/// </para>
///
/// <para>
/// <strong>Modelagem da cobertura:</strong> uma instância é EXCLUSIVAMENTE
/// "cep_range" (com <see cref="CepInicio"/>/<see cref="CepFim"/>) OU
/// "bairros_lista" (com <see cref="BairrosJson"/>). As factories
/// <see cref="CriarPorCep"/> e <see cref="CriarPorBairros"/> garantem a
/// invariante — não há construtor genérico público.
/// </para>
///
/// <para>
/// <strong>Normalização:</strong> CEPs são armazenados como 8 dígitos puros
/// (sem máscara). Bairros são armazenados lowercase, sem acentos e sem
/// espaços em volta, para casamento case/diacritic-insensitive contra o
/// que o cliente digita.
/// </para>
/// </summary>
public class FreteZona
{
    /// <summary>CEP normalizado = 8 dígitos. Sem máscara, sem espaços.</summary>
    private static readonly Regex CepRegex = new(@"^\d{8}$", RegexOptions.Compiled);

    public const string TipoCepRange = "cep_range";
    public const string TipoBairrosLista = "bairros_lista";

    public Guid Id { get; private set; }
    public Guid StorefrontId { get; private set; }

    /// <summary>
    /// Ordem de avaliação quando 2+ zonas cobrem o mesmo CEP/bairro — menor
    /// ordem ganha. Sem unicidade: empate é resolvido por <see cref="Id"/>.
    /// </summary>
    public int Ordem { get; private set; }

    /// <summary>Texto exibido pro cliente, ex: "Butantã proximidade".</summary>
    public string Label { get; private set; } = null!;

    /// <summary>Valor do frete (positivo).</summary>
    public decimal Valor { get; private set; }

    /// <summary>Tempo estimado de entrega em minutos (positivo).</summary>
    public int TempoEstimadoMinutos { get; private set; }

    /// <summary>Default true — admin desativa explicitamente via <see cref="Desativar"/>.</summary>
    public bool Ativa { get; private set; }

    /// <summary>Discriminator: <c>"cep_range"</c> ou <c>"bairros_lista"</c>.</summary>
    public string TipoCobertura { get; private set; } = null!;

    /// <summary>Início do range CEP (8 dígitos). Null quando <see cref="TipoCobertura"/> = <c>bairros_lista</c>.</summary>
    public string? CepInicio { get; private set; }

    /// <summary>Fim do range CEP (8 dígitos). Null quando <see cref="TipoCobertura"/> = <c>bairros_lista</c>.</summary>
    public string? CepFim { get; private set; }

    /// <summary>
    /// JSON array de strings normalizadas (lowercase, sem acentos), ex:
    /// <c>["butanta","pinheiros"]</c>. Null quando <see cref="TipoCobertura"/> = <c>cep_range</c>.
    /// </summary>
    public string? BairrosJson { get; private set; }

    public DateTime CriadoEm { get; private set; }
    public DateTime AlteradoEm { get; private set; }

    // EF Core ctor sem parâmetros
    private FreteZona() { }

    /// <summary>
    /// Factory: zona modelada por range de CEP. Ambos os CEPs são normalizados
    /// (removida máscara/espaços) e validados como 8 dígitos. Range invertido
    /// (<paramref name="cepInicio"/> &gt; <paramref name="cepFim"/>) é rejeitado.
    /// </summary>
    public static FreteZona CriarPorCep(
        Guid storefrontId,
        string label,
        string cepInicio,
        string cepFim,
        decimal valor,
        int tempoEstimadoMinutos,
        int ordem = 0)
    {
        ValidarStorefrontId(storefrontId);
        ValidarLabel(label);
        ValidarValor(valor);
        ValidarTempo(tempoEstimadoMinutos);
        ValidarOrdem(ordem);

        var inicioNorm = NormalizarCep(cepInicio);
        var fimNorm = NormalizarCep(cepFim);
        ValidarCep(inicioNorm, nome: "CEP inicial");
        ValidarCep(fimNorm, nome: "CEP final");

        // Comparação lexicográfica funciona para CEPs sempre com 8 dígitos.
        if (string.CompareOrdinal(inicioNorm, fimNorm) > 0)
            throw new RegraDeDominioVioladaException(
                $"intervalo de CEP invertido: início '{inicioNorm}' > fim '{fimNorm}'.");

        var agora = DateTime.UtcNow;
        return new FreteZona
        {
            Id = Guid.NewGuid(),
            StorefrontId = storefrontId,
            Label = label.Trim(),
            Valor = valor,
            TempoEstimadoMinutos = tempoEstimadoMinutos,
            Ordem = ordem,
            Ativa = true,
            TipoCobertura = TipoCepRange,
            CepInicio = inicioNorm,
            CepFim = fimNorm,
            BairrosJson = null,
            CriadoEm = agora,
            AlteradoEm = agora,
        };
    }

    /// <summary>
    /// Factory: zona modelada por lista de bairros. Bairros são normalizados
    /// (lowercase, sem acentos, trim) e duplicatas resultantes são removidas.
    /// Lista vazia ou bairro em branco rejeitados.
    /// </summary>
    public static FreteZona CriarPorBairros(
        Guid storefrontId,
        string label,
        string[] bairros,
        decimal valor,
        int tempoEstimadoMinutos,
        int ordem = 0)
    {
        ValidarStorefrontId(storefrontId);
        ValidarLabel(label);
        ValidarValor(valor);
        ValidarTempo(tempoEstimadoMinutos);
        ValidarOrdem(ordem);

        if (bairros is null || bairros.Length == 0)
            throw new RegraDeDominioVioladaException(
                "Lista de bairros é obrigatória — informe ao menos 1 bairro.");

        var normalizados = new List<string>(bairros.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in bairros)
        {
            if (string.IsNullOrWhiteSpace(b))
                throw new RegraDeDominioVioladaException(
                    "bairro em branco não é permitido na lista.");

            var norm = NormalizarBairro(b);
            if (seen.Add(norm))
                normalizados.Add(norm);
        }

        var json = JsonSerializer.Serialize(normalizados);

        var agora = DateTime.UtcNow;
        return new FreteZona
        {
            Id = Guid.NewGuid(),
            StorefrontId = storefrontId,
            Label = label.Trim(),
            Valor = valor,
            TempoEstimadoMinutos = tempoEstimadoMinutos,
            Ordem = ordem,
            Ativa = true,
            TipoCobertura = TipoBairrosLista,
            CepInicio = null,
            CepFim = null,
            BairrosJson = json,
            CriadoEm = agora,
            AlteradoEm = agora,
        };
    }

    /// <summary>
    /// Retorna true se o CEP informado está dentro do range desta zona.
    /// Entrada inválida (formato errado, vazio) retorna false — não throw —
    /// para não derrubar o checkout em caso de input do cliente.
    /// Zona modelada por bairros sempre retorna false.
    /// </summary>
    public bool CobreCep(string cep)
    {
        if (TipoCobertura != TipoCepRange) return false;
        if (CepInicio is null || CepFim is null) return false;
        if (string.IsNullOrWhiteSpace(cep)) return false;

        var cepNorm = NormalizarCep(cep);
        if (!CepRegex.IsMatch(cepNorm)) return false;

        return string.CompareOrdinal(cepNorm, CepInicio) >= 0
            && string.CompareOrdinal(cepNorm, CepFim) <= 0;
    }

    /// <summary>
    /// Retorna true se o bairro informado está na lista desta zona, comparando
    /// case/diacritic-insensitive. Zona modelada por CEP sempre retorna false.
    /// </summary>
    public bool CobreBairro(string bairro)
    {
        if (TipoCobertura != TipoBairrosLista) return false;
        if (string.IsNullOrWhiteSpace(BairrosJson)) return false;
        if (string.IsNullOrWhiteSpace(bairro)) return false;

        var bairroNorm = NormalizarBairro(bairro);

        using var doc = JsonDocument.Parse(BairrosJson);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String &&
                string.Equals(el.GetString(), bairroNorm, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    public void Ativar()
    {
        if (Ativa) return;
        Ativa = true;
        AlteradoEm = DateTime.UtcNow;
    }

    public void Desativar()
    {
        if (!Ativa) return;
        Ativa = false;
        AlteradoEm = DateTime.UtcNow;
    }

    public void DefinirOrdem(int ordem)
    {
        ValidarOrdem(ordem);
        if (Ordem == ordem) return; // idempotente
        Ordem = ordem;
        AlteradoEm = DateTime.UtcNow;
    }

    // ── Validações privadas ────────────────────────────────────────────

    private static void ValidarStorefrontId(Guid storefrontId)
    {
        if (storefrontId == Guid.Empty)
            throw new RegraDeDominioVioladaException("StorefrontId é obrigatório.");
    }

    private static void ValidarLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new RegraDeDominioVioladaException("Label é obrigatório.");
        if (label.Trim().Length > 80)
            throw new RegraDeDominioVioladaException(
                $"Label não pode exceder 80 caracteres (recebido: {label.Trim().Length}).");
    }

    private static void ValidarValor(decimal valor)
    {
        if (valor <= 0m)
            throw new RegraDeDominioVioladaException(
                $"Valor deve ser positivo (recebido: {valor.ToString(CultureInfo.InvariantCulture)}).");
    }

    private static void ValidarTempo(int minutos)
    {
        if (minutos <= 0)
            throw new RegraDeDominioVioladaException(
                $"Tempo estimado deve ser positivo (recebido: {minutos} min).");
    }

    private static void ValidarOrdem(int ordem)
    {
        if (ordem < 0)
            throw new RegraDeDominioVioladaException(
                $"Ordem não pode ser negativa (recebido: {ordem}).");
    }

    private static void ValidarCep(string cepNormalizado, string nome)
    {
        if (string.IsNullOrEmpty(cepNormalizado))
            throw new RegraDeDominioVioladaException($"{nome} é obrigatório.");
        if (!CepRegex.IsMatch(cepNormalizado))
            throw new RegraDeDominioVioladaException(
                $"{nome} inválido: '{cepNormalizado}'. Esperado: 8 dígitos (com ou sem máscara).");
    }

    /// <summary>Remove tudo que não for dígito — aceita "05500-000", " 05500000 ", "05500000".</summary>
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
}
