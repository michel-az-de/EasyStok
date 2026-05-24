using EasyStock.Domain.Entities.Storefront;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// Testes da entity <see cref="ClienteSession"/>.
///
/// Cobertura: factory (estado inicial via TimeProvider) + revogação manual +
/// sliding window de 30 dias (inatividade absoluta) + idempotência da revogação.
///
/// ADR-0012: cookie tem Max-Age=30d mas JWT roda 24h e middleware refresha;
/// EstaValida fica false após 30 dias sem RegistrarUso (sliding) ou
/// imediatamente após Revogar.
///
/// TDD red phase: todos os cenários abaixo devem FALHAR até a entity ser
/// implementada na green phase.
/// </summary>
public class ClienteSessionTests
{
    private static readonly DateTimeOffset Inicio =
        new(2026, 5, 24, 10, 0, 0, TimeSpan.Zero);

    private static ClienteSession NovaSessaoValida(FakeTime time)
    {
        return ClienteSession.Criar(
            clienteId: Guid.NewGuid(),
            empresaId: Guid.NewGuid(),
            time: time,
            ipInicial: "203.0.113.42",
            uaInicial: "Mozilla/5.0 (TestAgent)");
    }

    // ── Factory: happy path ────────────────────────────────────────────

    [Fact]
    public void Criar_define_estado_inicial_via_TimeProvider()
    {
        var time = new FakeTime(Inicio);
        var clienteId = Guid.NewGuid();
        var empresaId = Guid.NewGuid();

        var session = ClienteSession.Criar(
            clienteId: clienteId,
            empresaId: empresaId,
            time: time,
            ipInicial: "203.0.113.42",
            uaInicial: "Mozilla/5.0");

        session.Id.Should().NotBeEmpty("Id = sid no JWT (ADR-0012)");
        session.ClienteId.Should().Be(clienteId);
        session.EmpresaId.Should().Be(empresaId);
        session.CriadoEm.Should().Be(Inicio.UtcDateTime);
        session.UltimoUsoEm.Should().Be(Inicio.UtcDateTime, "ao criar, UltimoUsoEm = CriadoEm");
        session.IpInicial.Should().Be("203.0.113.42");
        session.UaInicial.Should().Be("Mozilla/5.0");
        session.Revogada.Should().BeFalse();
        session.MotivoRevogacao.Should().BeNull();
    }

    // ── Revogação manual ───────────────────────────────────────────────

    [Fact]
    public void Revogar_define_flag_e_motivo()
    {
        var time = new FakeTime(Inicio);
        var session = NovaSessaoValida(time);

        session.Revogar("logout do cliente");

        session.Revogada.Should().BeTrue();
        session.MotivoRevogacao.Should().Be("logout do cliente");
    }

    [Fact]
    public void Revogar_e_idempotente_nao_sobrescreve_motivo()
    {
        var time = new FakeTime(Inicio);
        var session = NovaSessaoValida(time);

        session.Revogar("motivo original");
        var act = () => session.Revogar("outro motivo");

        act.Should().NotThrow("Revogar 2x não deve quebrar");
        session.MotivoRevogacao.Should().Be("motivo original",
            "primeira revogação registra o motivo; chamadas subsequentes são no-op");
    }

    // ── RegistrarUso (sliding window) ──────────────────────────────────

    [Fact]
    public void RegistrarUso_atualiza_UltimoUsoEm_via_TimeProvider()
    {
        var time = new FakeTime(Inicio);
        var session = NovaSessaoValida(time);

        time.Advance(TimeSpan.FromHours(2));
        session.RegistrarUso(time);

        session.UltimoUsoEm.Should().Be(Inicio.UtcDateTime.AddHours(2));
        session.CriadoEm.Should().Be(Inicio.UtcDateTime, "CriadoEm é imutável");
    }

    // ── EstaValida ─────────────────────────────────────────────────────

    [Fact]
    public void EstaValida_false_apos_Revogar()
    {
        var time = new FakeTime(Inicio);
        var session = NovaSessaoValida(time);

        session.EstaValida(time).Should().BeTrue();

        session.Revogar("teste");
        session.EstaValida(time).Should().BeFalse();
    }

    [Fact]
    public void EstaValida_false_apos_30_dias_sem_uso_sliding_window()
    {
        var time = new FakeTime(Inicio);
        var session = NovaSessaoValida(time);

        time.Advance(TimeSpan.FromDays(29));
        session.EstaValida(time).Should().BeTrue("29 dias < 30 dias");

        time.Advance(TimeSpan.FromDays(2)); // total 31 dias
        session.EstaValida(time).Should().BeFalse("31 dias > 30 dias (sliding inativo)");
    }
}
