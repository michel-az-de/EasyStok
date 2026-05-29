namespace EasyStock.Domain.Entities.Storefront;

/// <summary>
/// Template recorrente de janela de entrega por dia da semana (ADR-0011).
///
/// <para>
/// Representa <em>capacidade nominal</em> (CapacidadeMaxima). A contagem real
/// de vagas consumidas em uma data específica vem de <see cref="VagaOcupada"/>
/// via COUNT (ADR-0014) — nunca é cacheada nesta entity, sob risco de TOCTOU
/// e dessincronização com replicações/cancelamentos.
/// </para>
///
/// <para>
/// <strong>Defaults seguros</strong>: <see cref="Ativa"/> = true ao criar.
/// Bloqueios pontuais (feriados, férias) são modelados em <see cref="BloqueioEntrega"/>,
/// não desativando a janela inteira.
/// </para>
/// </summary>
public class JanelaEntrega
{
    public Guid Id { get; private set; }
    public Guid StorefrontId { get; private set; }

    /// <summary>Dia da semana — 0=Domingo, 1=Segunda, ..., 6=Sábado (compatível com <see cref="DayOfWeek"/>).</summary>
    public int DiaDaSemana { get; private set; }

    public TimeOnly HoraInicio { get; private set; }
    public TimeOnly HoraFim { get; private set; }

    /// <summary>Vagas nominais. Capacidade efetiva = max - COUNT(VagaOcupada ativas) (ADR-0014).</summary>
    public int CapacidadeMaxima { get; private set; }

    /// <summary>Rótulo público (ex: "Manhã 9-12h"). Aparece na UI do cliente.</summary>
    public string Label { get; private set; } = null!;

    public bool Ativa { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AlteradoEm { get; private set; }

    // EF Core ctor sem parâmetros
    private JanelaEntrega() { }

    /// <summary>
    /// Factory. Default: <see cref="Ativa"/> = true.
    /// </summary>
    public static JanelaEntrega Criar(
        Guid storefrontId,
        int diaDaSemana,
        TimeOnly horaInicio,
        TimeOnly horaFim,
        int capacidadeMaxima,
        string label)
    {
        if (storefrontId == Guid.Empty)
            throw new RegraDeDominioVioladaException("StorefrontId é obrigatório.");

        if (diaDaSemana is < 0 or > 6)
            throw new RegraDeDominioVioladaException(
                $"Dia da semana inválido (recebido: {diaDaSemana}). Use 0=Domingo a 6=Sábado.");

        if (horaFim <= horaInicio)
            throw new RegraDeDominioVioladaException(
                $"HoraFim ({horaFim}) deve ser maior que HoraInicio ({horaInicio}).");

        if (capacidadeMaxima <= 0)
            throw new RegraDeDominioVioladaException(
                $"Capacidade máxima deve ser positiva (recebido: {capacidadeMaxima}).");

        if (string.IsNullOrWhiteSpace(label))
            throw new RegraDeDominioVioladaException("Label é obrigatório.");

        var agora = DateTime.UtcNow;
        return new JanelaEntrega
        {
            Id = Guid.NewGuid(),
            StorefrontId = storefrontId,
            DiaDaSemana = diaDaSemana,
            HoraInicio = horaInicio,
            HoraFim = horaFim,
            CapacidadeMaxima = capacidadeMaxima,
            Label = label.Trim(),
            Ativa = true,
            CriadoEm = agora,
            AlteradoEm = agora,
        };
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
}
