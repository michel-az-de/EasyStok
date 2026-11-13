using EasyStock.Domain.Defaults;

namespace EasyStock.Domain.Entities.Storefront;

/// <summary>#2018: por que uma janela de entrega não atende uma data (<see cref="JanelaEntrega.AvaliarEm"/>).</summary>
public enum DisponibilidadeDaJanela
{
    /// <summary>A janela atende a data.</summary>
    Atende,

    /// <summary>A janela está desativada no cadastro da loja.</summary>
    Inativa,

    /// <summary>A janela é de outro dia da semana.</summary>
    OutroDiaDaSemana,

    /// <summary>A data tem bloqueio do dia inteiro (feriado, férias).</summary>
    DiaBloqueado,

    /// <summary>A data tem bloqueio só desta janela.</summary>
    JanelaBloqueada,

    /// <summary>A janela já terminou (fuso da loja).</summary>
    JaPassou,

    /// <summary>A janela começa antes de agora mais o prazo mínimo de preparo.</summary>
    AbaixoDoPrazo,
}

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

    /// <summary>#1776 (item 20): teto de vagas numa janela; 99.999 passava e escondia o erro de digitação.</summary>
    public const int CapacidadeTeto = 500;

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

        Validar(diaDaSemana, horaInicio, horaFim, capacidadeMaxima, label);

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

    /// <summary>Edição pelo cadastro da loja (S45). Mesmas regras da criação; inválido não muda nada.</summary>
    public void Atualizar(int diaDaSemana, TimeOnly horaInicio, TimeOnly horaFim, int capacidadeMaxima, string label)
    {
        Validar(diaDaSemana, horaInicio, horaFim, capacidadeMaxima, label);
        DiaDaSemana = diaDaSemana;
        HoraInicio = horaInicio;
        HoraFim = horaFim;
        CapacidadeMaxima = capacidadeMaxima;
        Label = label.Trim();
        AlteradoEm = DateTime.UtcNow;
    }

    private static void Validar(int diaDaSemana, TimeOnly horaInicio, TimeOnly horaFim, int capacidadeMaxima, string label)
    {
        if (diaDaSemana is < 0 or > 6)
            throw new RegraDeDominioVioladaException(
                $"Dia da semana inválido (recebido: {diaDaSemana}). Use 0=Domingo a 6=Sábado.");

        if (horaFim <= horaInicio)
            throw new RegraDeDominioVioladaException(
                $"HoraFim ({horaFim}) deve ser maior que HoraInicio ({horaInicio}).");

        if (capacidadeMaxima <= 0)
            throw new RegraDeDominioVioladaException(
                $"Capacidade máxima deve ser positiva (recebido: {capacidadeMaxima}).");

        if (capacidadeMaxima > CapacidadeTeto)
            throw new RegraDeDominioVioladaException(
                $"Uma janela aceita até {CapacidadeTeto} pedidos (recebido: {capacidadeMaxima}).");

        if (string.IsNullOrWhiteSpace(label))
            throw new RegraDeDominioVioladaException("Label é obrigatório.");
    }

    /// <summary>
    /// #2018 (comanda-15): a ÚNICA regra de "a janela atende esta data". A listagem de janelas do site e da comanda e o
    /// checkout chamam isto, então oferecer e aceitar nunca divergem. Ordem: ativa, dia da semana, bloqueios, janela já
    /// terminada e prazo mínimo (<see cref="MotivoEstruturalEm"/>, depois <see cref="MotivoDeTempoEm"/>).
    /// </summary>
    /// <param name="bloqueios">Bloqueios da loja; só contam os da <paramref name="data"/> (do dia inteiro ou desta janela).</param>
    /// <param name="prazoMinimoMinutos">Antecedência mínima em minutos; 0 quando o canal não informa (a janela que já começou não serve).</param>
    public DisponibilidadeDaJanela AvaliarEm(
        DateOnly data, IEnumerable<BloqueioEntrega> bloqueios, DateTime agoraUtc, int prazoMinimoMinutos)
    {
        var estrutural = MotivoEstruturalEm(data, bloqueios);
        return estrutural != DisponibilidadeDaJanela.Atende ? estrutural : MotivoDeTempoEm(data, agoraUtc, prazoMinimoMinutos);
    }

    /// <summary>Ativa, no dia da semana certo e sem bloqueio na data (o que independe do relógio e dos pratos do pedido).</summary>
    public DisponibilidadeDaJanela MotivoEstruturalEm(DateOnly data, IEnumerable<BloqueioEntrega> bloqueios)
    {
        ArgumentNullException.ThrowIfNull(bloqueios);
        if (!Ativa) return DisponibilidadeDaJanela.Inativa;
        if (DiaDaSemana != (int)data.DayOfWeek) return DisponibilidadeDaJanela.OutroDiaDaSemana;

        var daData = bloqueios.Where(b => b.Data == data).ToList();
        if (daData.Any(b => b.JanelaEspecificaId is null)) return DisponibilidadeDaJanela.DiaBloqueado;
        if (daData.Any(b => b.JanelaEspecificaId == Id)) return DisponibilidadeDaJanela.JanelaBloqueada;
        return DisponibilidadeDaJanela.Atende;
    }

    /// <summary>
    /// Janela já terminada nunca vale (#1506), nem sem prazo mínimo (site); e só serve a que não começa antes de agora mais o
    /// prazo (S16, RN-21; #1874: com prazo 0 a janela que já começou também sai). Fuso da loja (<see cref="OperacionalFuso"/>).
    /// </summary>
    public DisponibilidadeDaJanela MotivoDeTempoEm(DateOnly data, DateTime agoraUtc, int prazoMinimoMinutos)
    {
        var agoraLoja = OperacionalFuso.ParaBrasilia(agoraUtc);
        if (data.ToDateTime(HoraFim) <= agoraLoja) return DisponibilidadeDaJanela.JaPassou;
        if (data.ToDateTime(HoraInicio) < agoraLoja.AddMinutes(prazoMinimoMinutos)) return DisponibilidadeDaJanela.AbaixoDoPrazo;
        return DisponibilidadeDaJanela.Atende;
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
