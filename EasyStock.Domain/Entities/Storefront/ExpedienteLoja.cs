using System.Globalization;
using System.Text.Json;
using EasyStock.Domain.Enums.Storefront;

namespace EasyStock.Domain.Entities.Storefront;

/// <summary>
/// Um turno de funcionamento num dia da semana (0 = domingo, compatível com <see cref="DayOfWeek"/>).
/// <see cref="Fecha"/> antes de <see cref="Abre"/> quer dizer que o turno vira a meia-noite
/// (ex.: sexta das 18 h às 02 h de sábado).
/// </summary>
public sealed record HorarioFuncionamento(int DiaDaSemana, TimeOnly Abre, TimeOnly Fecha)
{
    public bool ViraMeiaNoite => Fecha < Abre;
}

/// <summary>
/// Expediente da loja de UMA empresa (S40, ADR-0051): horário por dia e controle manual. Mesmo
/// molde de <c>ConfiguracaoAtendimento</c>: <see cref="EmpresaId"/> é a chave, a linha nasce sob
/// demanda e a ausência dela vale o <see cref="CriarPadrao"/> (08–22 h todos os dias).
///
/// <para>
/// O manual vence o relógio e <strong>não volta sozinho</strong> (protótipo:
/// <c>app/Moldura.jsx</c>, <c>ALTERNAR_LOJA</c>). O horário governa o atendimento (mensagem de
/// fora do horário do agente e das automações); o checkout do site, que é sempre agendado, só é
/// recusado com <see cref="ControleManualLoja.ForcarFechada"/>.
/// </para>
///
/// <para>O instante vem sempre por parâmetro em UTC; a loja opera em America/Sao_Paulo.</para>
/// </summary>
public class ExpedienteLoja
{
    public const int MensagemTamanhoMaximo = 500;
    public const string MarcadorAbre = "{abre}";

    private static readonly TimeZoneInfo Fuso = ResolverFuso();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Guid EmpresaId { get; private set; }

    /// <summary>Turnos em JSON (<c>jsonb</c>): no máximo um por dia; dia ausente é dia fechado.</summary>
    public string HorariosJson { get; private set; } = "[]";

    public ControleManualLoja ControleManual { get; private set; }
    public Guid? ControleAlteradoPorUsuarioId { get; private set; }
    public DateTime? ControleAlteradoEm { get; private set; }

    /// <summary>Enviada fora do horário; <c>{abre}</c> vira a próxima abertura ("amanhã às 08:00").</summary>
    public string MensagemForaDoHorario { get; private set; } =
        "Oi! No momento estamos fechados. Abrimos {abre} e te respondemos assim que der.";

    /// <summary>Enviada com a loja fechada na mão (sem hora para voltar).</summary>
    public string MensagemLojaFechada { get; private set; } =
        "Oi! Hoje não estamos atendendo. Assim que voltarmos, te respondemos por aqui.";

    public DateTime CriadoEm { get; private set; }
    public DateTime AlteradoEm { get; private set; }

    public IReadOnlyList<HorarioFuncionamento> Horarios =>
        JsonSerializer.Deserialize<List<HorarioDto>>(HorariosJson, JsonOptions)!
            .Select(h => new HorarioFuncionamento(h.Dia, TimeOnly.Parse(h.Abre, CultureInfo.InvariantCulture), TimeOnly.Parse(h.Fecha, CultureInfo.InvariantCulture)))
            .ToList();

    // EF Core ctor sem parâmetros
    private ExpedienteLoja() { }

    public static ExpedienteLoja CriarPadrao(Guid empresaId)
    {
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");

        var agora = DateTime.UtcNow;
        var expediente = new ExpedienteLoja
        {
            EmpresaId = empresaId,
            ControleManual = ControleManualLoja.Automatico,
            CriadoEm = agora,
            AlteradoEm = agora,
        };
        expediente.GravarHorarios(Enumerable.Range(0, 7)
            .Select(d => new HorarioFuncionamento(d, new TimeOnly(8, 0), new TimeOnly(22, 0))));
        return expediente;
    }

    public void DefinirHorarios(IEnumerable<HorarioFuncionamento> horarios)
    {
        var lista = horarios?.ToList() ?? throw new RegraDeDominioVioladaException("Informe os horários.");
        foreach (var h in lista)
        {
            if (h.DiaDaSemana is < 0 or > 6)
                throw new RegraDeDominioVioladaException(
                    $"Dia da semana inválido (recebido: {h.DiaDaSemana}). Use 0=Domingo a 6=Sábado.");
            if (h.Abre == h.Fecha)
                throw new RegraDeDominioVioladaException(
                    $"Abertura e fechamento iguais no dia {h.DiaDaSemana} ({h.Abre}).");
        }
        if (lista.GroupBy(h => h.DiaDaSemana).Any(g => g.Count() > 1))
            throw new RegraDeDominioVioladaException("Um turno por dia: há dia repetido nos horários.");

        GravarHorarios(lista);
        AlteradoEm = DateTime.UtcNow;
    }

    public void DefinirControle(ControleManualLoja controle, Guid? usuarioId, DateTime agoraUtc)
    {
        if (!Enum.IsDefined(controle))
            throw new RegraDeDominioVioladaException($"Controle manual inválido: {(int)controle}.");

        ControleManual = controle;
        ControleAlteradoPorUsuarioId = usuarioId is { } u && u != Guid.Empty ? u : null;
        ControleAlteradoEm = Utc(agoraUtc);
        AlteradoEm = DateTime.UtcNow;
    }

    public void DefinirMensagens(string? foraDoHorario, string? lojaFechada)
    {
        if (!string.IsNullOrWhiteSpace(foraDoHorario)) MensagemForaDoHorario = Limitar(foraDoHorario);
        if (!string.IsNullOrWhiteSpace(lojaFechada)) MensagemLojaFechada = Limitar(lojaFechada);
        AlteradoEm = DateTime.UtcNow;
    }

    public bool EstaAberta(DateTime agoraUtc) => ControleManual switch
    {
        ControleManualLoja.ForcarAberta => true,
        ControleManualLoja.ForcarFechada => false,
        _ => DentroDoHorario(ParaLocal(agoraUtc)),
    };

    /// <summary>
    /// Se o relógio está dentro de algum turno, ignorando o controle manual (#1443): é o que decide se
    /// fechar na mão é "fechar no horário de funcionamento", que pede gerente e justificativa.
    /// </summary>
    public bool DentroDoHorarioDeFuncionamento(DateTime agoraUtc) => DentroDoHorario(ParaLocal(agoraUtc));

    /// <summary>
    /// Próxima abertura em horário local (America/Sao_Paulo), olhando até 7 dias. Nula com a loja
    /// fechada na mão (não volta sozinha) ou sem nenhum turno cadastrado.
    /// </summary>
    public DateTime? ProximaAberturaLocal(DateTime agoraUtc)
    {
        if (ControleManual == ControleManualLoja.ForcarFechada) return null;

        var agora = ParaLocal(agoraUtc);
        var porDia = Horarios.ToDictionary(h => h.DiaDaSemana);
        for (var d = 0; d <= 7; d++)
        {
            var data = agora.Date.AddDays(d);
            if (!porDia.TryGetValue((int)data.DayOfWeek, out var turno)) continue;
            var abertura = data.Add(turno.Abre.ToTimeSpan());
            if (abertura > agora) return abertura;
        }
        return null;
    }

    /// <summary>
    /// Aviso ao cliente quando a loja não está aberta; nulo quando aberta. Fechada na mão usa
    /// <see cref="MensagemLojaFechada"/>; fora do horário usa <see cref="MensagemForaDoHorario"/>.
    /// </summary>
    public string? MensagemParaCliente(DateTime agoraUtc)
    {
        if (EstaAberta(agoraUtc)) return null;
        if (ControleManual == ControleManualLoja.ForcarFechada) return MensagemLojaFechada;

        var proxima = ProximaAberturaLocal(agoraUtc);
        var quando = proxima is { } p ? DescreverAbertura(ParaLocal(agoraUtc), p) : "em breve";
        return MensagemForaDoHorario.Replace(MarcadorAbre, quando, StringComparison.Ordinal);
    }

    private bool DentroDoHorario(DateTime local)
    {
        var hora = TimeOnly.FromDateTime(local);
        var hoje = (int)local.DayOfWeek;
        var ontem = (hoje + 6) % 7;

        foreach (var turno in Horarios)
        {
            if (turno.DiaDaSemana == hoje)
            {
                var fechaHoje = turno.ViraMeiaNoite ? TimeOnly.MaxValue : turno.Fecha;
                if (hora >= turno.Abre && (turno.ViraMeiaNoite || hora < fechaHoje)) return true;
            }
            if (turno.DiaDaSemana == ontem && turno.ViraMeiaNoite && hora < turno.Fecha) return true;
        }
        return false;
    }

    private static string DescreverAbertura(DateTime agoraLocal, DateTime abertura)
    {
        var dias = (abertura.Date - agoraLocal.Date).Days;
        var hora = abertura.ToString("HH:mm", CultureInfo.InvariantCulture);
        return dias switch
        {
            0 => $"hoje às {hora}",
            1 => $"amanhã às {hora}",
            _ => $"{NomeDoDia(abertura.DayOfWeek)} às {hora}",
        };
    }

    private static string NomeDoDia(DayOfWeek dia) => dia switch
    {
        DayOfWeek.Sunday => "domingo",
        DayOfWeek.Monday => "segunda",
        DayOfWeek.Tuesday => "terça",
        DayOfWeek.Wednesday => "quarta",
        DayOfWeek.Thursday => "quinta",
        DayOfWeek.Friday => "sexta",
        _ => "sábado",
    };

    private void GravarHorarios(IEnumerable<HorarioFuncionamento> horarios) =>
        HorariosJson = JsonSerializer.Serialize(
            horarios.OrderBy(h => h.DiaDaSemana)
                .Select(h => new HorarioDto(h.DiaDaSemana, h.Abre.ToString("HH:mm", CultureInfo.InvariantCulture), h.Fecha.ToString("HH:mm", CultureInfo.InvariantCulture))),
            JsonOptions);

    private static string Limitar(string texto)
    {
        var limpo = texto.Trim();
        return limpo.Length > MensagemTamanhoMaximo ? limpo[..MensagemTamanhoMaximo] : limpo;
    }

    private static DateTime ParaLocal(DateTime agoraUtc) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(Utc(agoraUtc), Fuso), DateTimeKind.Unspecified);

    private static DateTime Utc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
    };

    private static TimeZoneInfo ResolverFuso()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        // Brasília sem horário de verão desde 2019: -03:00 fixo é o comportamento real.
        return TimeZoneInfo.CreateCustomTimeZone("America/Sao_Paulo (fixo -03:00)", TimeSpan.FromHours(-3), "Brasília", "BRT");
    }

    private sealed record HorarioDto(int Dia, string Abre, string Fecha);
}
