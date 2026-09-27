using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Storefront;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// Expediente da loja (S40, ADR-0051): horário por dia com virada da meia-noite e controle manual
/// que vence o relógio e não volta sozinho (protótipo: <c>dominio/funcionamento.js</c>). Instantes
/// em UTC; a loja opera em America/Sao_Paulo (UTC-3, sem horário de verão desde 2019).
/// </summary>
public class ExpedienteLojaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    // 28/09/2026 é segunda-feira. Local = UTC - 3 h.
    private static DateTime Local(int dia, int hora, int minuto = 0) =>
        new DateTime(2026, 9, dia, hora, minuto, 0, DateTimeKind.Utc).AddHours(3);

    private static DateTime LocalOutubro(int dia, int hora) =>
        new DateTime(2026, 10, dia, hora, 0, 0, DateTimeKind.Utc).AddHours(3);

    [Fact]
    public void Padrao_AbreDasOitoAsVinteEDuasTodosOsDias()
    {
        var expediente = ExpedienteLoja.CriarPadrao(Empresa);

        expediente.Horarios.Should().HaveCount(7);
        expediente.ControleManual.Should().Be(ControleManualLoja.Automatico);
        expediente.EstaAberta(Local(28, 12)).Should().BeTrue();
        expediente.EstaAberta(Local(28, 23)).Should().BeFalse();
        expediente.EstaAberta(Local(28, 7, 59)).Should().BeFalse();
    }

    [Fact]
    public void ViradaDaMeiaNoite()
    {
        // Sexta (5) das 18 h às 02 h de sábado.
        var expediente = ExpedienteLoja.CriarPadrao(Empresa);
        expediente.DefinirHorarios([new HorarioFuncionamento(5, new TimeOnly(18, 0), new TimeOnly(2, 0))]);

        expediente.EstaAberta(LocalOutubro(2, 17)).Should().BeFalse("sexta antes de abrir");
        expediente.EstaAberta(LocalOutubro(2, 23)).Should().BeTrue("sexta à noite");
        expediente.EstaAberta(LocalOutubro(3, 1)).Should().BeTrue("01 h de sábado ainda é o turno de sexta");
        expediente.EstaAberta(LocalOutubro(3, 3)).Should().BeFalse("depois das 02 h fechou");
    }

    [Fact]
    public void ManualVenceRelogioENaoVolta()
    {
        var expediente = ExpedienteLoja.CriarPadrao(Empresa);

        expediente.DefinirControle(ControleManualLoja.ForcarFechada, usuarioId: Guid.NewGuid(), Local(28, 12));

        expediente.EstaAberta(Local(28, 12)).Should().BeFalse("fechou na mão dentro do horário");
        expediente.EstaAberta(Local(29, 12)).Should().BeFalse("não volta sozinho no dia seguinte");

        expediente.DefinirControle(ControleManualLoja.Automatico, usuarioId: null, Local(29, 12));
        expediente.EstaAberta(Local(29, 12)).Should().BeTrue();
    }

    [Fact]
    public void ForcarAberta_AbreForaDoHorario()
    {
        var expediente = ExpedienteLoja.CriarPadrao(Empresa);

        expediente.DefinirControle(ControleManualLoja.ForcarAberta, usuarioId: null, Local(28, 23));

        expediente.EstaAberta(Local(28, 23)).Should().BeTrue();
    }

    [Fact]
    public void MensagemForaDoHorario_TrocaAbrePelaProximaAbertura()
    {
        var expediente = ExpedienteLoja.CriarPadrao(Empresa);
        expediente.DefinirMensagens(foraDoHorario: "Estamos fechados. Abrimos {abre}.", lojaFechada: null);

        expediente.MensagemParaCliente(Local(28, 12)).Should().BeNull("aberta não manda aviso");
        expediente.MensagemParaCliente(Local(28, 23)).Should().Be("Estamos fechados. Abrimos amanhã às 08:00.");
        expediente.MensagemParaCliente(Local(28, 6)).Should().Be("Estamos fechados. Abrimos hoje às 08:00.");
    }

    [Fact]
    public void MensagemComForcarFechada_UsaLojaFechadaESemProximaAbertura()
    {
        var expediente = ExpedienteLoja.CriarPadrao(Empresa);
        expediente.DefinirMensagens(foraDoHorario: null, lojaFechada: "Hoje não estamos atendendo.");
        expediente.DefinirControle(ControleManualLoja.ForcarFechada, null, Local(28, 12));

        expediente.MensagemParaCliente(Local(28, 12)).Should().Be("Hoje não estamos atendendo.");
        expediente.ProximaAberturaLocal(Local(28, 12)).Should().BeNull("fechada na mão não tem hora para voltar");
    }

    [Fact]
    public void ProximaAbertura_PulaDiaSemHorario()
    {
        // Só abre na quarta (3) às 10 h.
        var expediente = ExpedienteLoja.CriarPadrao(Empresa);
        expediente.DefinirHorarios([new HorarioFuncionamento(3, new TimeOnly(10, 0), new TimeOnly(14, 0))]);

        expediente.ProximaAberturaLocal(Local(28, 12))
            .Should().Be(new DateTime(2026, 9, 30, 10, 0, 0));
    }

    [Theory]
    [InlineData(7, 8, 22)]
    [InlineData(-1, 8, 22)]
    [InlineData(1, 8, 8)]
    public void DefinirHorarios_Invalido_Lanca(int dia, int abre, int fecha)
    {
        var expediente = ExpedienteLoja.CriarPadrao(Empresa);

        var act = () => expediente.DefinirHorarios([new HorarioFuncionamento(dia, new TimeOnly(abre, 0), new TimeOnly(fecha, 0))]);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void DefinirHorarios_DiaRepetido_Lanca()
    {
        var expediente = ExpedienteLoja.CriarPadrao(Empresa);

        var act = () => expediente.DefinirHorarios([
            new HorarioFuncionamento(1, new TimeOnly(8, 0), new TimeOnly(12, 0)),
            new HorarioFuncionamento(1, new TimeOnly(14, 0), new TimeOnly(18, 0))]);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }
}
