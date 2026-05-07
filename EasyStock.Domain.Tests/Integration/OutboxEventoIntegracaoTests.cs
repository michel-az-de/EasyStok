using EasyStock.Domain.Integration;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Integration;

public class OutboxEventoIntegracaoTests
{
    private static OutboxEventoIntegracao CriarValido(
        Guid? empresaId = null,
        string? tipoEvento = null,
        Guid? aggregateId = null) =>
        OutboxEventoIntegracao.Criar(
            empresaId: empresaId ?? Guid.NewGuid(),
            tipoEvento: tipoEvento ?? "pedido.confirmado",
            aggregateType: "Pedido",
            aggregateId: aggregateId ?? Guid.NewGuid(),
            payloadJson: """{"pedidoId":"abc","total":42.50}""");

    [Fact]
    public void Criar_evento_valido_inicializa_campos_basicos()
    {
        var evt = CriarValido();

        evt.Id.Should().NotBe(Guid.Empty);
        evt.TipoEvento.Should().Be("pedido.confirmado");
        evt.AggregateType.Should().Be("Pedido");
        evt.Status.Should().Be(StatusOutboxIntegracao.Pendente);
        evt.Tentativas.Should().Be(0);
        evt.MaxTentativas.Should().Be(5);
        evt.ProximaTentativaEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        evt.PayloadSchemaVersion.Should().Be(1);
    }

    [Fact]
    public void Criar_gera_IdempotencyKey_em_hex_de_64_chars()
    {
        var evt = CriarValido();
        evt.IdempotencyKey.Should().HaveLength(64);
        evt.IdempotencyKey.Should().MatchRegex("^[A-F0-9]+$");
    }

    [Fact]
    public void Criar_eventos_iguais_geram_IdempotencyKey_distintos_porque_inclui_eventId()
    {
        var empresaId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        var evt1 = CriarValido(empresaId, "pedido.confirmado", aggregateId);
        var evt2 = CriarValido(empresaId, "pedido.confirmado", aggregateId);

        evt1.IdempotencyKey.Should().NotBe(evt2.IdempotencyKey);
    }

    [Fact]
    public void Criar_ShardKey_distribui_em_0_a_3()
    {
        var shards = new HashSet<int>();
        for (int i = 0; i < 100; i++)
            shards.Add(CriarValido().ShardKey);

        // Em 100 eventos, deve cobrir os 4 shards (probabilisticamente)
        shards.Should().Contain(0).And.Contain(1).And.Contain(2).And.Contain(3);
    }

    [Fact]
    public void Criar_empresaId_vazio_lanca()
    {
        Action act = () => OutboxEventoIntegracao.Criar(
            Guid.Empty, "evt", "Agg", Guid.NewGuid(), "{}");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_tipoEvento_invalido_lanca(string? tipo)
    {
        Action act = () => OutboxEventoIntegracao.Criar(
            Guid.NewGuid(), tipo!, "Agg", Guid.NewGuid(), "{}");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Criar_aggregateId_vazio_lanca()
    {
        Action act = () => OutboxEventoIntegracao.Criar(
            Guid.NewGuid(), "evt", "Agg", Guid.Empty, "{}");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Criar_payloadJson_invalido_lanca(string? payload)
    {
        Action act = () => OutboxEventoIntegracao.Criar(
            Guid.NewGuid(), "evt", "Agg", Guid.NewGuid(), payload!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Criar_maxTentativas_zero_lanca()
    {
        Action act = () => OutboxEventoIntegracao.Criar(
            Guid.NewGuid(), "evt", "Agg", Guid.NewGuid(), "{}", maxTentativas: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Criar_aplica_trim_em_strings()
    {
        var evt = OutboxEventoIntegracao.Criar(
            Guid.NewGuid(),
            tipoEvento: "  pedido.confirmado  ",
            aggregateType: "  Pedido  ",
            aggregateId: Guid.NewGuid(),
            payloadJson: "{}",
            correlationId: "  trace-abc  ");

        evt.TipoEvento.Should().Be("pedido.confirmado");
        evt.AggregateType.Should().Be("Pedido");
        evt.CorrelationId.Should().Be("trace-abc");
    }

    [Fact]
    public void MarcarEmEnvio_atualiza_status()
    {
        var evt = CriarValido();
        evt.MarcarEmEnvio();
        evt.Status.Should().Be(StatusOutboxIntegracao.EmEnvio);
    }

    [Fact]
    public void MarcarEnviado_seta_ProcessadoEm_e_limpa_erro()
    {
        var evt = CriarValido();
        evt.MarcarFalhaTentativa("erro temp", TimeSpan.FromSeconds(1));
        evt.MarcarEnviado();

        evt.Status.Should().Be(StatusOutboxIntegracao.Enviado);
        evt.ProcessadoEm.Should().NotBeNull();
        evt.ProcessadoEm!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        evt.ErroUltimaTentativa.Should().BeNull();
    }

    [Fact]
    public void MarcarFalhaTentativa_incrementa_e_volta_para_Pendente_se_nao_esgotou()
    {
        var evt = CriarValido();
        evt.MarcarFalhaTentativa("falha 1", TimeSpan.FromMinutes(1));

        evt.Tentativas.Should().Be(1);
        evt.Status.Should().Be(StatusOutboxIntegracao.Pendente);
        evt.ProximaTentativaEm.Should().BeCloseTo(
            DateTime.UtcNow.AddMinutes(1),
            TimeSpan.FromSeconds(2));
        evt.ErroUltimaTentativa.Should().Be("falha 1");
    }

    [Fact]
    public void MarcarFalhaTentativa_esgotando_tentativas_vai_para_Falhado()
    {
        var evt = OutboxEventoIntegracao.Criar(
            Guid.NewGuid(), "evt", "Agg", Guid.NewGuid(), "{}",
            maxTentativas: 2);

        evt.MarcarFalhaTentativa("1", TimeSpan.Zero);
        evt.Status.Should().Be(StatusOutboxIntegracao.Pendente);

        evt.MarcarFalhaTentativa("2", TimeSpan.Zero);
        evt.Status.Should().Be(StatusOutboxIntegracao.Falhado);
        evt.TentativasEsgotadas().Should().BeTrue();
    }

    [Fact]
    public void Reprocessar_reseta_tentativas_e_status()
    {
        var evt = OutboxEventoIntegracao.Criar(
            Guid.NewGuid(), "evt", "Agg", Guid.NewGuid(), "{}",
            maxTentativas: 1);
        evt.MarcarFalhaTentativa("falha", TimeSpan.Zero);
        evt.Status.Should().Be(StatusOutboxIntegracao.Falhado);

        evt.Reprocessar();

        evt.Status.Should().Be(StatusOutboxIntegracao.Pendente);
        evt.Tentativas.Should().Be(0);
        evt.ErroUltimaTentativa.Should().BeNull();
        evt.ProximaTentativaEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Cancelar_seta_status_e_processadoEm()
    {
        var evt = CriarValido();
        evt.Cancelar();

        evt.Status.Should().Be(StatusOutboxIntegracao.Cancelado);
        evt.ProcessadoEm.Should().NotBeNull();
    }

    [Fact]
    public void TentativasEsgotadas_funciona_com_limite()
    {
        var evt = OutboxEventoIntegracao.Criar(
            Guid.NewGuid(), "evt", "Agg", Guid.NewGuid(), "{}",
            maxTentativas: 3);

        evt.TentativasEsgotadas().Should().BeFalse();
        evt.MarcarFalhaTentativa("1", TimeSpan.Zero);
        evt.TentativasEsgotadas().Should().BeFalse();
        evt.MarcarFalhaTentativa("2", TimeSpan.Zero);
        evt.TentativasEsgotadas().Should().BeFalse();
        evt.MarcarFalhaTentativa("3", TimeSpan.Zero);
        evt.TentativasEsgotadas().Should().BeTrue();
    }
}
