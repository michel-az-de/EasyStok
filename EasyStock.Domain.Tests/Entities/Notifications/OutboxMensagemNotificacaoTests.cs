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

    // ===== N2: Simulado, Indeterminado e a categoria Seguranca =====

    [Fact]
    public void MarcarSimulado_guarda_o_provider_e_nao_preenche_EnviadoEm()
    {
        var m = Novo(canal: CanalNotificacao.WhatsApp);

        m.MarcarSimulado("stub");

        m.Status.Should().Be(StatusOutbox.Simulado);
        m.ProviderUsado.Should().Be("stub");
        m.EnviadoEm.Should().BeNull("nada saiu: Simulado não é Enviado");
        m.ErroUltimaTentativa.Should().BeNull();
    }

    [Fact]
    public void MarcarIndeterminado_e_terminal_e_conta_a_tentativa()
    {
        var m = Novo(canal: CanalNotificacao.WhatsApp);

        m.MarcarIndeterminado("timeout", "twilio");

        m.Status.Should().Be(StatusOutbox.Indeterminado);
        m.Tentativas.Should().Be(1);
        m.ErroUltimaTentativa.Should().Be("timeout");
        m.ProviderUsado.Should().Be("twilio");
        m.EnviadoEm.Should().BeNull("não há como confirmar a entrega");
    }

    [Fact]
    public void MarcarIndeterminado_sem_provider_deixa_o_provider_como_estava()
    {
        // N1: o lease vencido não sabe qual provider foi chamado.
        var m = Novo(canal: CanalNotificacao.Sms);

        m.MarcarIndeterminado("lease vencido");

        m.Status.Should().Be(StatusOutbox.Indeterminado);
        m.ProviderUsado.Should().BeNull();
    }

    private static OutboxMensagemNotificacao NovoComSegredo(CategoriaConteudoNotificacao categoria) =>
        OutboxMensagemNotificacao.Criar(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CanalNotificacao.Email, "x@x.com",
            assuntoRenderizado: "Redefinir senha",
            corpoRenderizado: "Seu código: 482913",
            categoria: categoria,
            metadadosJson: """{"template":"reset","param1":"482913"}""");

    /// <summary>Cada transição que termina a mensagem: qualquer status fora de Pendente e EmEnvio.</summary>
    public static TheoryData<string, Action<OutboxMensagemNotificacao>> TransicoesTerminais => new()
    {
        { "Enviado", m => m.MarcarEnviado("smtp") },
        { "Simulado", m => m.MarcarSimulado("console") },
        { "Indeterminado", m => m.MarcarIndeterminado("timeout") },
        { "Falhado por falha permanente", m => m.MarcarFalhaTentativa("550", TimeSpan.Zero, permanente: true) },
        {
            "Falhado por tentativas esgotadas",
            m =>
            {
                m.Tentativas = m.MaxTentativas - 1;
                m.MarcarFalhaTentativa("421", TimeSpan.Zero);
            }
        },
        { "Cancelado", m => m.Cancelar() },
        { "Suprimido", m => m.Suprimir("kill switch") },
        { "Expirado", m => m.Expirar("passou do prazo") },
        {
            "Indeterminado por lease vencido (canal de entrega única)",
            m =>
            {
                m.Canal = CanalNotificacao.WhatsApp;
                m.ReclamarLeaseVencido();
            }
        },
    };

    [Theory]
    [MemberData(nameof(TransicoesTerminais))]
    public void Seguranca_ao_terminar_apaga_corpo_assunto_e_metadados(string transicao, Action<OutboxMensagemNotificacao> terminar)
    {
        var m = NovoComSegredo(CategoriaConteudoNotificacao.Seguranca);

        terminar(m);

        m.Status.Should().NotBe(StatusOutbox.Pendente, transicao);
        m.Status.Should().NotBe(StatusOutbox.EmEnvio, transicao);
        m.CorpoRenderizado.Should().Be("[apagado]", transicao);
        m.CorpoRenderizado.Should().Be(OutboxMensagemNotificacao.CorpoApagado);
        m.AssuntoRenderizado.Should().BeEmpty(transicao);
        m.MetadadosJson.Should().BeNull(transicao);
        m.LerMetadados().Should().BeNull(transicao);
        m.Destinatario.Should().Be("x@x.com", "o destinatário fica até o anonimizador (90 dias)");
    }

    [Theory]
    [MemberData(nameof(TransicoesTerminais))]
    public void Operacional_ao_terminar_nao_apaga_nada(string transicao, Action<OutboxMensagemNotificacao> terminar)
    {
        var m = NovoComSegredo(CategoriaConteudoNotificacao.Operacional);

        terminar(m);

        m.CorpoRenderizado.Should().Be("Seu código: 482913", transicao);
        m.AssuntoRenderizado.Should().Be("Redefinir senha", transicao);
        m.MetadadosJson.Should().Be("""{"template":"reset","param1":"482913"}""", transicao);
    }

    [Fact]
    public void Seguranca_enquanto_aberta_mantem_o_corpo_para_a_proxima_tentativa()
    {
        // Falha transitória com tentativas sobrando volta a Pendente e ainda precisa do corpo para reenviar.
        var m = NovoComSegredo(CategoriaConteudoNotificacao.Seguranca);

        m.MarcarFalhaTentativa("421 tente depois", TimeSpan.FromMinutes(1));
        m.Status.Should().Be(StatusOutbox.Pendente);
        m.CorpoRenderizado.Should().Be("Seu código: 482913");

        m.MarcarEmEnvio();
        m.Status.Should().Be(StatusOutbox.EmEnvio);
        m.CorpoRenderizado.Should().Be("Seu código: 482913");
        m.AssuntoRenderizado.Should().Be("Redefinir senha");
        m.MetadadosJson.Should().NotBeNull();
    }

    [Fact]
    public void PurgarSegredos_e_idempotente_e_preserva_o_resto_da_mensagem()
    {
        var m = NovoComSegredo(CategoriaConteudoNotificacao.Seguranca);
        var id = m.Id;

        m.PurgarSegredos();
        m.PurgarSegredos();

        m.CorpoRenderizado.Should().Be(OutboxMensagemNotificacao.CorpoApagado);
        m.AssuntoRenderizado.Should().BeEmpty();
        m.MetadadosJson.Should().BeNull();
        m.Id.Should().Be(id);
        m.Categoria.Should().Be(CategoriaConteudoNotificacao.Seguranca);
    }

    [Fact]
    public void MarcarEmEnvio_grava_o_lease_em_ProximaTentativaEm_sem_mexer_nas_tentativas()
    {
        // N1: o claim reserva a mensagem em EmEnvio e o lease (5 min, como o OutboxEventoIntegracao) vai em
        // ProximaTentativaEm; vencido, o claim reclama a mensagem (volta a Pendente ou vira Indeterminado).
        var m = Novo();

        m.MarcarEmEnvio();

        m.Status.Should().Be(StatusOutbox.EmEnvio);
        m.Tentativas.Should().Be(0);
        m.ProximaTentativaEm.Should().BeCloseTo(DateTime.UtcNow.Add(OutboxMensagemNotificacao.LeaseEmEnvio), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Expirar_e_terminal_sem_EnviadoEm_e_guarda_o_motivo()
    {
        var m = Novo(canal: CanalNotificacao.WhatsApp);

        m.Expirar("passou do prazo de 120 min");

        m.Status.Should().Be(StatusOutbox.Expirado);
        m.EnviadoEm.Should().BeNull("nada saiu");
        m.ErroUltimaTentativa.Should().Be("passou do prazo de 120 min");
        m.Tentativas.Should().Be(0, "expirar não é tentativa de envio");
    }

    [Fact]
    public void ReclamarLeaseVencido_em_email_volta_a_Pendente_contando_a_tentativa_e_elegivel_na_hora()
    {
        var m = Novo(canal: CanalNotificacao.Email);
        m.MarcarEmEnvio();

        m.ReclamarLeaseVencido();

        m.Status.Should().Be(StatusOutbox.Pendente);
        m.Tentativas.Should().Be(1);
        m.ProximaTentativaEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        m.ErroUltimaTentativa.Should().Contain("Lease de envio vencido");
    }

    [Fact]
    public void ReclamarLeaseVencido_em_email_esgotado_vira_Falhado_para_nao_repetir_para_sempre()
    {
        var m = Novo(canal: CanalNotificacao.Email);
        m.Tentativas = m.MaxTentativas - 1;
        m.MarcarEmEnvio();

        m.ReclamarLeaseVencido();

        m.Status.Should().Be(StatusOutbox.Falhado);
    }

    [Theory]
    [InlineData(CanalNotificacao.WhatsApp)]
    [InlineData(CanalNotificacao.Sms)]
    public void ReclamarLeaseVencido_em_whatsapp_e_sms_vira_Indeterminado_e_nunca_Pendente(CanalNotificacao canal)
    {
        // No máximo uma vez: o processo pode ter caído depois de chamar o provider.
        var m = Novo(canal: canal);
        m.MarcarEmEnvio();

        m.ReclamarLeaseVencido();

        m.Status.Should().Be(StatusOutbox.Indeterminado);
        m.Tentativas.Should().Be(1);
        m.ProviderUsado.Should().BeNull();
    }

    [Fact]
    public void RegistrarProviderMensagemId_grava_o_id_ignora_vazio_e_corta_no_limite_da_coluna()
    {
        var m = Novo(canal: CanalNotificacao.WhatsApp);

        m.RegistrarProviderMensagemId("  ");
        m.ProviderMensagemId.Should().BeNull();

        m.RegistrarProviderMensagemId("wamid.HBgM123");
        m.ProviderMensagemId.Should().Be("wamid.HBgM123");

        m.RegistrarProviderMensagemId(new string('x', 300));
        m.ProviderMensagemId.Should().HaveLength(OutboxMensagemNotificacao.ProviderMensagemIdMaxLength,
            "o id maior que o varchar(128) quebraria o commit do resultado do envio");
    }
}
