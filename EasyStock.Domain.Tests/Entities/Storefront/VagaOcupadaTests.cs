using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Exceptions.Storefront;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// Testes da entity <see cref="VagaOcupada"/>.
///
/// VagaOcupada é a fonte da verdade de capacidade (ADR-0014): cada linha
/// representa uma ocupação concreta (janela × data × pedido). Liberação
/// deve ser idempotente — handler de cancelamento pode disparar mais de uma vez.
///
/// TDD red phase: cenários abaixo devem FALHAR até a entity ser implementada.
/// </summary>
public class VagaOcupadaTests
{
    // ── Helpers ────────────────────────────────────────────────────────

    private static VagaOcupada NovaVagaValida(
        Guid? janelaId = null,
        DateOnly? data = null,
        Guid? pedidoId = null)
    {
        return VagaOcupada.Ocupar(
            janelaEntregaId: janelaId ?? Guid.NewGuid(),
            dataEntrega: data ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            pedidoId: pedidoId ?? Guid.NewGuid());
    }

    // ── Factory: happy path ────────────────────────────────────────────

    [Fact]
    public void Ocupar_define_estado_inicial_ativa()
    {
        var janelaId = Guid.NewGuid();
        var pedidoId = Guid.NewGuid();
        var data = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        var vaga = VagaOcupada.Ocupar(janelaId, data, pedidoId);

        vaga.Id.Should().NotBeEmpty();
        vaga.JanelaEntregaId.Should().Be(janelaId);
        vaga.PedidoId.Should().Be(pedidoId);
        vaga.DataEntrega.Should().Be(data);
        vaga.OcupadoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        vaga.OcupadoEm.Kind.Should().Be(DateTimeKind.Utc, "timestamps em UTC para auditoria");
        vaga.LiberadoEm.Should().BeNull();
        vaga.MotivoLiberacao.Should().BeNull();
        vaga.IsAtiva().Should().BeTrue("vaga recém-ocupada está ativa (LiberadoEm null)");
    }

    // ── Factory: validações ───────────────────────────────────────────

    [Fact]
    public void Ocupar_rejeita_janela_id_vazio()
    {
        var act = () => VagaOcupada.Ocupar(
            janelaEntregaId: Guid.Empty,
            dataEntrega: DateOnly.FromDateTime(DateTime.UtcNow),
            pedidoId: Guid.NewGuid());

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Janela*");
    }

    [Fact]
    public void Ocupar_rejeita_pedido_id_vazio()
    {
        var act = () => VagaOcupada.Ocupar(
            janelaEntregaId: Guid.NewGuid(),
            dataEntrega: DateOnly.FromDateTime(DateTime.UtcNow),
            pedidoId: Guid.Empty);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Pedido*");
    }

    // ── Liberar: caminho normal ───────────────────────────────────────

    [Fact]
    public void Liberar_marca_LiberadoEm_e_motivo_e_vira_inativa()
    {
        var vaga = NovaVagaValida();

        vaga.Liberar("cliente_cancelou");

        vaga.LiberadoEm.Should().NotBeNull();
        vaga.LiberadoEm!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        vaga.LiberadoEm.Value.Kind.Should().Be(DateTimeKind.Utc);
        vaga.MotivoLiberacao.Should().Be("cliente_cancelou");
        vaga.IsAtiva().Should().BeFalse();
    }

    // ── Liberar: idempotente (ADR-0014) ───────────────────────────────

    [Fact]
    public void Liberar_eh_idempotente_se_ja_liberada()
    {
        var vaga = NovaVagaValida();
        vaga.Liberar("mp_falha");
        var liberadoEmOriginal = vaga.LiberadoEm;
        var motivoOriginal = vaga.MotivoLiberacao;
        Thread.Sleep(10);

        vaga.Liberar("baba_recusou");

        vaga.LiberadoEm.Should().Be(liberadoEmOriginal,
            "ADR-0014: handler pode disparar 2 vezes — manter primeiro timestamp");
        vaga.MotivoLiberacao.Should().Be(motivoOriginal,
            "primeiro motivo é o canônico — segunda chamada é no-op");
        vaga.IsAtiva().Should().BeFalse();
    }

    // ── Helper IsAtiva() ──────────────────────────────────────────────

    [Fact]
    public void IsAtiva_retorna_true_quando_LiberadoEm_null()
    {
        var vaga = NovaVagaValida();

        vaga.IsAtiva().Should().BeTrue();
    }

    [Fact]
    public void IsAtiva_retorna_false_quando_LiberadoEm_preenchido()
    {
        var vaga = NovaVagaValida();
        vaga.Liberar("teste");

        vaga.IsAtiva().Should().BeFalse();
    }

    // ── JanelaSemVagasException ───────────────────────────────────────

    [Fact]
    public void JanelaSemVagasException_herda_de_RegraDeDominioVioladaException()
    {
        var ex = new JanelaSemVagasException();

        ex.Should().BeAssignableTo<RegraDeDominioVioladaException>(
            "deve fluir pelo mesmo handler global de violação de domínio");
    }

    [Fact]
    public void JanelaSemVagasException_aceita_mensagem_customizada()
    {
        var ex = new JanelaSemVagasException("janela X esgotada para 2026-05-25");

        ex.Message.Should().Contain("janela X");
    }
}
