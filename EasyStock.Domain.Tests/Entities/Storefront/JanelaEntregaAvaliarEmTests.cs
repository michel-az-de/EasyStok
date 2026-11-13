using EasyStock.Domain.Entities.Storefront;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// #2018 (comanda-15): "a janela atende esta data" é <see cref="JanelaEntrega.AvaliarEm"/>, usada pela listagem de janelas e
/// pelo checkout. Terça-feira 02/06/2026, janela 9h às 12h.
/// </summary>
public class JanelaEntregaAvaliarEmTests
{
    private static readonly DateOnly Terca = new(2026, 6, 2);
    private static readonly Guid Loja = Guid.NewGuid();

    // 12:00 em Brasília do dia anterior (15:00Z).
    private static readonly DateTime Vespera = new(2026, 6, 1, 15, 0, 0, DateTimeKind.Utc);

    private static JanelaEntrega Janela(int dia = 2)
        => JanelaEntrega.Criar(Loja, dia, new TimeOnly(9, 0), new TimeOnly(12, 0), 5, "Manhã 9-12h");

    private static BloqueioEntrega Bloqueio(DateOnly data, Guid? janela = null)
        => BloqueioEntrega.Criar(Loja, data, "Feriado", janela);

    [Fact]
    public void JanelaDoDiaSemBloqueioComAntecedencia_Atende() =>
        Janela().AvaliarEm(Terca, [], Vespera, 100).Should().Be(DisponibilidadeDaJanela.Atende);

    [Fact]
    public void JanelaDesativada_Inativa()
    {
        var janela = Janela();
        janela.Desativar();

        janela.AvaliarEm(Terca, [], Vespera, 0).Should().Be(DisponibilidadeDaJanela.Inativa);
    }

    [Fact]
    public void OutroDiaDaSemana_NaoAtende() =>
        Janela(dia: 3).AvaliarEm(Terca, [], Vespera, 0).Should().Be(DisponibilidadeDaJanela.OutroDiaDaSemana);

    [Fact]
    public void BloqueioDoDiaInteiro_NaoAtende() =>
        Janela().AvaliarEm(Terca, [Bloqueio(Terca)], Vespera, 0).Should().Be(DisponibilidadeDaJanela.DiaBloqueado);

    [Fact]
    public void BloqueioSoDestaJanela_NaoAtende()
    {
        var janela = Janela();

        janela.AvaliarEm(Terca, [Bloqueio(Terca, janela.Id)], Vespera, 0).Should().Be(DisponibilidadeDaJanela.JanelaBloqueada);
    }

    [Fact]
    public void BloqueioDeOutraJanelaOuDeOutraData_NaoImpede()
    {
        var janela = Janela();

        janela.AvaliarEm(Terca, [Bloqueio(Terca, Guid.NewGuid()), Bloqueio(Terca.AddDays(7))], Vespera, 0)
            .Should().Be(DisponibilidadeDaJanela.Atende);
    }

    [Fact]
    public void JanelaQueJaTerminou_JaPassou_MesmoSemPrazo()
    {
        // 15:00 em Brasília do mesmo dia (18:00Z): a janela das 9h às 12h acabou (#1506).
        var agora = new DateTime(2026, 6, 2, 18, 0, 0, DateTimeKind.Utc);

        Janela().AvaliarEm(Terca, [], agora, 0).Should().Be(DisponibilidadeDaJanela.JaPassou);
    }

    [Fact]
    public void JanelaEmCurso_SemPrazo_AbaixoDoPrazo()
    {
        // 10:00 em Brasília (13:00Z): começou há uma hora e não terminou (#1874).
        var agora = new DateTime(2026, 6, 2, 13, 0, 0, DateTimeKind.Utc);

        Janela().AvaliarEm(Terca, [], agora, 0).Should().Be(DisponibilidadeDaJanela.AbaixoDoPrazo);
    }

    [Fact]
    public void LimiteDoPrazoEInclusivo()
    {
        // 09:00 em Brasília (12:00Z) com prazo 0: o início é exatamente agora, e segue (S16).
        var agora = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);

        Janela().AvaliarEm(Terca, [], agora, 0).Should().Be(DisponibilidadeDaJanela.Atende);
        Janela().AvaliarEm(Terca, [], agora, 1).Should().Be(DisponibilidadeDaJanela.AbaixoDoPrazo);
    }

    [Fact]
    public void PrazoMinimoCortaAJanelaCurta()
    {
        // 08:00 em Brasília (11:00Z), prazo 100 min: 09:40 passa das 09:00.
        var agora = new DateTime(2026, 6, 2, 11, 0, 0, DateTimeKind.Utc);

        Janela().AvaliarEm(Terca, [], agora, 100).Should().Be(DisponibilidadeDaJanela.AbaixoDoPrazo);
        Janela().AvaliarEm(Terca, [], agora, 30).Should().Be(DisponibilidadeDaJanela.Atende);
    }

    [Fact]
    public void ValoresEstruturaisVemAntesDoTempo()
    {
        // Bloqueada e já terminada: o motivo é o bloqueio (a ordem do checkout).
        var agora = new DateTime(2026, 6, 2, 18, 0, 0, DateTimeKind.Utc);

        Janela().AvaliarEm(Terca, [Bloqueio(Terca)], agora, 0).Should().Be(DisponibilidadeDaJanela.DiaBloqueado);
    }
}
