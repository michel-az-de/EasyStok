using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// Testes da entity <see cref="JanelaEntrega"/>.
///
/// JanelaEntrega é um template recorrente por dia da semana (ADR-0011).
/// Define horário (HoraInicio/HoraFim) e CapacidadeMaxima — mas a contagem
/// real de vagas usadas vem de <see cref="VagaOcupada"/> via COUNT (ADR-0014),
/// nunca de contador agregado nesta entity.
///
/// TDD red phase: todos os cenários abaixo devem FALHAR até a entity ser
/// implementada na green phase.
/// </summary>
public class JanelaEntregaTests
{
    // ── Helpers ────────────────────────────────────────────────────────

    private static JanelaEntrega NovaJanelaValida(
        Guid? storefrontId = null,
        int diaDaSemana = 1,
        string label = "Manhã 9-12h",
        int capacidade = 10)
    {
        return JanelaEntrega.Criar(
            storefrontId: storefrontId ?? Guid.NewGuid(),
            diaDaSemana: diaDaSemana,
            horaInicio: new TimeOnly(9, 0),
            horaFim: new TimeOnly(12, 0),
            capacidadeMaxima: capacidade,
            label: label);
    }

    // ── Factory: happy path ────────────────────────────────────────────

    [Fact]
    public void Criar_define_estado_inicial_correto()
    {
        var storefrontId = Guid.NewGuid();

        var janela = JanelaEntrega.Criar(
            storefrontId: storefrontId,
            diaDaSemana: 1,
            horaInicio: new TimeOnly(9, 0),
            horaFim: new TimeOnly(12, 0),
            capacidadeMaxima: 10,
            label: "Manhã 9-12h");

        janela.Id.Should().NotBeEmpty();
        janela.StorefrontId.Should().Be(storefrontId);
        janela.DiaDaSemana.Should().Be(1);
        janela.HoraInicio.Should().Be(new TimeOnly(9, 0));
        janela.HoraFim.Should().Be(new TimeOnly(12, 0));
        janela.CapacidadeMaxima.Should().Be(10);
        janela.Label.Should().Be("Manhã 9-12h");
        janela.Ativa.Should().BeTrue("janela nasce ativa por default");
        janela.CriadoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        janela.AlteradoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    // ── Factory: validações ───────────────────────────────────────────

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(10)]
    public void Criar_rejeita_dia_da_semana_fora_do_intervalo_0_a_6(int diaInvalido)
    {
        var act = () => JanelaEntrega.Criar(
            storefrontId: Guid.NewGuid(),
            diaDaSemana: diaInvalido,
            horaInicio: new TimeOnly(9, 0),
            horaFim: new TimeOnly(12, 0),
            capacidadeMaxima: 10,
            label: "Manhã");

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*dia*semana*");
    }

    [Fact]
    public void Criar_rejeita_hora_fim_igual_a_hora_inicio()
    {
        var act = () => JanelaEntrega.Criar(
            storefrontId: Guid.NewGuid(),
            diaDaSemana: 1,
            horaInicio: new TimeOnly(9, 0),
            horaFim: new TimeOnly(9, 0),
            capacidadeMaxima: 10,
            label: "Janela vazia");

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*HoraFim*");
    }

    [Fact]
    public void Criar_rejeita_hora_fim_anterior_a_hora_inicio()
    {
        var act = () => JanelaEntrega.Criar(
            storefrontId: Guid.NewGuid(),
            diaDaSemana: 1,
            horaInicio: new TimeOnly(14, 0),
            horaFim: new TimeOnly(9, 0),
            capacidadeMaxima: 10,
            label: "Invertida");

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*HoraFim*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Criar_rejeita_capacidade_zero_ou_negativa(int capacidadeInvalida)
    {
        var act = () => JanelaEntrega.Criar(
            storefrontId: Guid.NewGuid(),
            diaDaSemana: 1,
            horaInicio: new TimeOnly(9, 0),
            horaFim: new TimeOnly(12, 0),
            capacidadeMaxima: capacidadeInvalida,
            label: "Manhã");

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Capacidade*");
    }

    [Fact]
    public void Criar_rejeita_storefront_id_vazio()
    {
        var act = () => JanelaEntrega.Criar(
            storefrontId: Guid.Empty,
            diaDaSemana: 1,
            horaInicio: new TimeOnly(9, 0),
            horaFim: new TimeOnly(12, 0),
            capacidadeMaxima: 10,
            label: "Manhã");

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Storefront*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_rejeita_label_vazio(string? labelInvalido)
    {
        var act = () => JanelaEntrega.Criar(
            storefrontId: Guid.NewGuid(),
            diaDaSemana: 1,
            horaInicio: new TimeOnly(9, 0),
            horaFim: new TimeOnly(12, 0),
            capacidadeMaxima: 10,
            label: labelInvalido!);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Label*");
    }

    // ── Transições: Ativar/Desativar ──────────────────────────────────

    [Fact]
    public void Desativar_marca_ativa_false_e_atualiza_data()
    {
        var janela = NovaJanelaValida();
        var antes = janela.AlteradoEm;
        Thread.Sleep(10);

        janela.Desativar();

        janela.Ativa.Should().BeFalse();
        janela.AlteradoEm.Should().BeAfter(antes);
    }

    [Fact]
    public void Desativar_eh_idempotente_em_janela_ja_inativa()
    {
        var janela = NovaJanelaValida();
        janela.Desativar();
        var alteradoEm = janela.AlteradoEm;
        Thread.Sleep(10);

        janela.Desativar();

        janela.Ativa.Should().BeFalse();
        janela.AlteradoEm.Should().Be(alteradoEm, "no-op quando já inativa");
    }

    [Fact]
    public void Ativar_reverte_janela_para_ativa()
    {
        var janela = NovaJanelaValida();
        janela.Desativar();
        var antes = janela.AlteradoEm;
        Thread.Sleep(10);

        janela.Ativar();

        janela.Ativa.Should().BeTrue();
        janela.AlteradoEm.Should().BeAfter(antes);
    }
}
