using System.Text;
using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Storefront.Agendamento;

/// <summary>
/// Lista janelas de entrega disponíveis para um storefront num período (ADR-0011).
///
/// <para>
/// Para cada combinação (data × JanelaEntrega) ativa calcula
/// <c>vagasRestantes = capacidade - COUNT(VagaOcupada ativas)</c>.
/// Filtra dias bloqueados (<c>BloqueioEntrega</c>) e janelas inativas.
/// </para>
///
/// <para>
/// <strong>Concorrência</strong> (ADR-0014): a contagem é EVENTUAL — dois clientes
/// podem ver "1 vaga" no mesmo segundo. O acerto ocorre no checkout via INSERT
/// atômico com WHERE COUNT &lt; capacidade.
/// </para>
///
/// <para>
/// <strong>CEP</strong>: opcional. Sem CEP retorna todas as janelas. Com CEP
/// valida que pelo menos uma <c>FreteZona</c> ativa cobre o CEP; se não cobre →
/// <see cref="CepSemCoberturaException"/>.
/// </para>
/// </summary>
public sealed class ListarJanelasDisponiveisUseCase(
    IStorefrontRepository storefrontRepository,
    IJanelaEntregaRepository janelaRepository,
    IBloqueioEntregaRepository bloqueioRepository,
    IVagaOcupadaRepository vagaOcupadaRepository,
    IFreteZonaRepository freteZonaRepository)
{
    private const int MaxDiasPeriodo = 60;
    private const int DefaultDiasPeriodo = 14;
    private static readonly TimeZoneInfo TzBrasilia =
        TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly Regex CepDigitosRegex = new(@"^\d{8}$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<JanelaDisponivelDto>> ExecuteAsync(
        ListarJanelasDisponiveisInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // 1. Validar e normalizar CEP (se fornecido)
        string? cepNormalizado = null;
        if (!string.IsNullOrWhiteSpace(input.Cep))
        {
            cepNormalizado = NormalizarCep(input.Cep);
            if (!CepDigitosRegex.IsMatch(cepNormalizado))
                throw new CepInvalidoException();
        }

        // 2. Calcular período usando timezone de Brasília
        var hoje = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TzBrasilia));
        var inicio = input.DataInicio ?? hoje;
        var fim = input.DataFim ?? hoje.AddDays(DefaultDiasPeriodo);

        // 3. Anti-DoS: período máximo 60 dias
        if (fim.DayNumber - inicio.DayNumber > MaxDiasPeriodo)
            throw new RegraDeDominioVioladaException(
                $"Período máximo permitido é {MaxDiasPeriodo} dias.");

        // 4. Resolver storefront
        var storefront = await storefrontRepository.GetBySlugAsync(input.Slug, ct);
        if (storefront is null || !storefront.Ativo)
            throw new StorefrontNaoEncontradoException(input.Slug);

        // 5. Validar cobertura de CEP (se fornecido)
        if (cepNormalizado is not null)
        {
            var zonas = await freteZonaRepository.GetAtivasDoStorefrontOrdenadasAsync(storefront.Id, ct);
            var cobre = zonas.Any(z => z.CobreCep(cepNormalizado));
            if (!cobre)
                throw new CepSemCoberturaException();
        }

        // 6. Carregar janelas ativas
        var janelas = await janelaRepository.GetAtivasDoStorefrontAsync(storefront.Id, ct);
        if (janelas.Count == 0)
            return Array.Empty<JanelaDisponivelDto>();

        // 7. Carregar bloqueios do período
        var bloqueios = await bloqueioRepository.GetByStorefrontPeriodoAsync(
            storefront.Id, inicio, fim, ct);

        var diaInteiroBloqueado = bloqueios
            .Where(b => b.JanelaEspecificaId == null)
            .Select(b => b.Data)
            .ToHashSet();

        var bloqueioEspecifico = bloqueios
            .Where(b => b.JanelaEspecificaId != null)
            .ToLookup(b => (b.Data, b.JanelaEspecificaId!.Value));

        // 8. Contar vagas ocupadas — query única anti-N+1
        var janelaIds = janelas.Select(j => j.Id).ToList();
        var contagens = await vagaOcupadaRepository.ContarPorJanelaPeriodoAsync(
            janelaIds, inicio, fim, ct);

        // 9. Montar matriz resultado
        var resultado = new List<JanelaDisponivelDto>();
        for (var data = inicio; data <= fim; data = data.AddDays(1))
        {
            if (diaInteiroBloqueado.Contains(data))
                continue;

            foreach (var janela in janelas)
            {
                if (janela.DiaDaSemana != (int)data.DayOfWeek)
                    continue;

                if (bloqueioEspecifico[(data, janela.Id)].Any())
                    continue;

                var ocupadas = contagens.TryGetValue((janela.Id, data), out var c) ? c : 0;
                var vagasRestantes = Math.Max(0, janela.CapacidadeMaxima - ocupadas);

                resultado.Add(new JanelaDisponivelDto(
                    Data: data,
                    JanelaId: janela.Id,
                    Label: janela.Label,
                    HoraInicio: janela.HoraInicio,
                    HoraFim: janela.HoraFim,
                    VagasRestantes: vagasRestantes,
                    Capacidade: janela.CapacidadeMaxima,
                    Esgotado: vagasRestantes == 0));
            }
        }

        return resultado.OrderBy(r => r.Data).ThenBy(r => r.HoraInicio).ToList();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

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
}
