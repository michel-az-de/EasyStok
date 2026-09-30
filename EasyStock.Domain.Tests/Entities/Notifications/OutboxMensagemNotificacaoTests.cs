using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Notifications;

public class OutboxMensagemNotificacaoTests
{
    private static OutboxMensagemNotificacao Novo(
        Guid? eventoId = null,
        Guid? usuarioId = null,
        CanalNotificacao canal = CanalNotificacao.Email)
        => OutboxMensagemNotificacao.Criar(
            eventoId ?? Guid.NewGuid(),
            templateId: Guid.NewGuid(),
            empresaId: Guid.NewGuid(),
            canal: canal,
            destinatario: "x@x.com",
            assuntoRenderizado: "s",
            corpoRenderizado: "b",
            categoria: CategoriaConteudoNotificacao.Transacional,
            usuarioDestinoId: usuarioId);

    [Fact]
    public void Criar_define_pendente_tentativa_zero_e_idempotency_key()
    {
        var m = Novo();

        m.Status.Should().Be(StatusOutbox.Pendente);
        m.Tentativas.Should().Be(0);
        m.MaxTentativas.Should().Be(3);
        m.IdempotencyKey.Should().HaveLength(64);
        m.ShardKey.Should().BeInRange(0, 3);
        m.ProximaTentativaEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void IdempotencyKey_e_estavel_para_mesma_combinacao()
    {
        var eventoId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        var m1 = Novo(eventoId, usuarioId, CanalNotificacao.Email);
        var m2 = Novo(eventoId, usuarioId, CanalNotificacao.Email);

        m1.IdempotencyKey.Should().Be(m2.IdempotencyKey);
    }

    [Fact]
    public void IdempotencyKey_difere_por_canal_para_permitir_fallback()
    {
        var eventoId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        var email = Novo(eventoId, usuarioId, CanalNotificacao.Email);
        var sms = Novo(eventoId, usuarioId, CanalNotificacao.Sms);

        email.IdempotencyKey.Should().NotBe(sms.IdempotencyKey);
    }

    [Fact]
    public void MarcarFalhaTentativa_volta_a_pendente_se_ainda_ha_tentativas()
    {
        var m = Novo();

        m.MarcarFalhaTentativa("timeout", TimeSpan.FromMinutes(1));

        m.Tentativas.Should().Be(1);
        m.Status.Should().Be(StatusOutbox.Pendente);
        m.ErroUltimaTentativa.Should().Be("timeout");
        m.ProximaTentativaEm.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(1), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void MarcarFalhaTentativa_marca_falhado_quando_esgota()
    {
        var m = Novo();
        m.MarcarFalhaTentativa("err", TimeSpan.Zero);
        m.MarcarFalhaTentativa("err", TimeSpan.Zero);
        m.MarcarFalhaTentativa("err", TimeSpan.Zero);

        m.Status.Should().Be(StatusOutbox.Falhado);
        m.TentativasEsgotadas().Should().BeTrue();
    }

    [Fact]
    public void FalhaPermanenteNaoReagenda()
    {
        // S09: erro que nunca vai passar (ex.: fora da janela de 24 h sem template) nao volta
        // para Pendente, mesmo sobrando tentativas.
        var m = Novo(canal: CanalNotificacao.WhatsApp);

        m.MarcarFalhaTentativa("fora_da_janela_24h_sem_template", TimeSpan.FromMinutes(1), permanente: true);

        m.Tentativas.Should().Be(1);
        m.Status.Should().Be(StatusOutbox.Falhado);
        m.ErroUltimaTentativa.Should().Be("fora_da_janela_24h_sem_template");
    }

    [Fact]
    public void MarcarEnviado_seta_provider_e_data()
    {
        var m = Novo();

        m.MarcarEnviado("smtp");

        m.Status.Should().Be(StatusOutbox.Enviado);
        m.ProviderUsado.Should().Be("smtp");
        m.EnviadoEm.Should().NotBeNull();
        m.ErroUltimaTentativa.Should().BeNull();
    }

    [Fact]
    public void Suprimir_registra_motivo_e_status()
    {
        var m = Novo();

        m.Suprimir("opt-out marketing");

        m.Status.Should().Be(StatusOutbox.Suprimido);
        m.ErroUltimaTentativa.Should().Be("opt-out marketing");
    }

    // ===== S13: metadados do envio e chave de idempotencia do negocio =====

    [Fact]
    public void Metadados_persistidos_voltam_como_dicionario()
    {
        var m = OutboxMensagemNotificacao.Criar(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CanalNotificacao.WhatsApp, "+5511999990001",
            "", "corpo", CategoriaConteudoNotificacao.Transacional,
            metadadosJson: """{"template":"pedido_em_preparo","param1":"Maria"}""");

        m.LerMetadados().Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["template"] = "pedido_em_preparo",
            ["param1"] = "Maria",
        });
    }

    [Fact]
    public void Sem_metadados_ou_json_invalido_le_nulo()
    {
        Novo().LerMetadados().Should().BeNull();
        var invalido = Novo();
        invalido.MetadadosJson = "nao-e-json";
        invalido.LerMetadados().Should().BeNull();
    }

    [Fact]
    public void Chave_de_idempotencia_do_negocio_ignora_o_evento()
    {
        OutboxMensagemNotificacao Com(string chave) => OutboxMensagemNotificacao.Criar(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CanalNotificacao.WhatsApp, "+5511999990001",
            "", "corpo", CategoriaConteudoNotificacao.Transacional, chaveIdempotencia: chave);

        var a = Com("pedido:abc|preparando");
        var b = Com("pedido:abc|preparando");
        var c = Com("pedido:abc|entregue");

        a.IdempotencyKey.Should().Be(b.IdempotencyKey, "reprocessar o mesmo status do pedido gera a mesma chave");
        a.IdempotencyKey.Should().NotBe(c.IdempotencyKey);
        a.IdempotencyKey.Should().HaveLength(64);
    }
}
