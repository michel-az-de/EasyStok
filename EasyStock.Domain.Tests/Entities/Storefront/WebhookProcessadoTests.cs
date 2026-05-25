using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// Testes da entity <see cref="WebhookProcessado"/>.
///
/// WebhookProcessado é o registro de cada notificação inbound de provider
/// (MercadoPago hoje, possivelmente outros depois). Implementa o padrão
/// "receive-then-process" (ADR-0006): persistimos o webhook cru com status
/// 'received' antes de qualquer processamento — assim qualquer falha
/// downstream é debug-friendly e nunca perdemos a notificação.
///
/// <para>
/// Dedup é garantido por índice único <c>(Provider, EventoId)</c> em EF Config —
/// a entity confia no banco e não tenta detectar duplicata em memória.
/// </para>
///
/// TDD red phase: todos os cenários abaixo devem FALHAR até a entity ser
/// implementada na green phase.
/// </summary>
public class WebhookProcessadoTests
{
    // ── Helpers ────────────────────────────────────────────────────────

    private static WebhookProcessado NovoRecebido(
        string provider = "mercadopago",
        string eventoId = "payment-123",
        string tipo = "payment.updated",
        string payload = "{\"id\":\"payment-123\"}")
    {
        return WebhookProcessado.Receber(provider, eventoId, tipo, payload);
    }

    // ── Factory: happy path ────────────────────────────────────────────

    [Fact]
    public void Receber_define_estado_inicial_received()
    {
        var antes = DateTime.UtcNow;

        var webhook = WebhookProcessado.Receber(
            provider: "mercadopago",
            eventoId: "payment-987654321",
            tipo: "payment.updated",
            payloadRaw: "{\"id\":\"payment-987654321\",\"status\":\"approved\"}");

        webhook.Id.Should().NotBeEmpty();
        webhook.Provider.Should().Be("mercadopago");
        webhook.EventoId.Should().Be("payment-987654321");
        webhook.Tipo.Should().Be("payment.updated");
        webhook.PayloadRaw.Should().Be("{\"id\":\"payment-987654321\",\"status\":\"approved\"}");

        webhook.Status.Should().Be(WebhookProcessadoStatus.Received,
            "ADR-0006: persistimos como 'received' antes de processar nada");
        webhook.Motivo.Should().BeNull();
        webhook.ProcessadoEm.Should().BeNull();
        webhook.EmpresaId.Should().BeNull("EmpresaId é resolvido só depois de processar payload");

        webhook.RecebidoEm.Should().BeOnOrAfter(antes).And.BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public void Receber_normaliza_provider_para_lowercase_e_trim()
    {
        var webhook = WebhookProcessado.Receber(
            provider: "  MercadoPago  ",
            eventoId: "evt-1",
            tipo: "payment.updated",
            payloadRaw: "{}");

        webhook.Provider.Should().Be("mercadopago",
            "provider é case-insensitive — normalizamos para consistência no índice único");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Receber_rejeita_provider_vazio(string? provider)
    {
        var act = () => WebhookProcessado.Receber(
            provider: provider!,
            eventoId: "evt-1",
            tipo: "payment.updated",
            payloadRaw: "{}");

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Provider*");
    }

    [Fact]
    public void Receber_rejeita_provider_acima_de_32_caracteres()
    {
        var act = () => WebhookProcessado.Receber(
            provider: new string('a', 33),
            eventoId: "evt-1",
            tipo: "payment.updated",
            payloadRaw: "{}");

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Provider*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Receber_rejeita_evento_id_vazio(string? eventoId)
    {
        var act = () => WebhookProcessado.Receber(
            provider: "mercadopago",
            eventoId: eventoId!,
            tipo: "payment.updated",
            payloadRaw: "{}");

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*EventoId*");
    }

    [Fact]
    public void Receber_rejeita_evento_id_acima_de_128_caracteres()
    {
        var act = () => WebhookProcessado.Receber(
            provider: "mercadopago",
            eventoId: new string('a', 129),
            tipo: "payment.updated",
            payloadRaw: "{}");

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*EventoId*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Receber_rejeita_tipo_vazio(string? tipo)
    {
        var act = () => WebhookProcessado.Receber(
            provider: "mercadopago",
            eventoId: "evt-1",
            tipo: tipo!,
            payloadRaw: "{}");

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Tipo*");
    }

    [Fact]
    public void Receber_rejeita_tipo_acima_de_64_caracteres()
    {
        var act = () => WebhookProcessado.Receber(
            provider: "mercadopago",
            eventoId: "evt-1",
            tipo: new string('a', 65),
            payloadRaw: "{}");

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Tipo*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Receber_rejeita_payload_raw_vazio(string? payload)
    {
        var act = () => WebhookProcessado.Receber(
            provider: "mercadopago",
            eventoId: "evt-1",
            tipo: "payment.updated",
            payloadRaw: payload!);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Payload*");
    }

    // ── Transição: MarcarProcessado ────────────────────────────────────

    [Fact]
    public void MarcarProcessado_seta_status_e_empresa_id_e_processado_em()
    {
        var webhook = NovoRecebido();
        var empresaId = Guid.NewGuid();

        webhook.MarcarProcessado(empresaId);

        webhook.Status.Should().Be(WebhookProcessadoStatus.Processed);
        webhook.EmpresaId.Should().Be(empresaId);
        webhook.ProcessadoEm.Should().NotBeNull();
        webhook.ProcessadoEm!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        webhook.Motivo.Should().BeNull();
    }

    [Fact]
    public void MarcarProcessado_rejeita_empresa_id_vazio()
    {
        var webhook = NovoRecebido();

        var act = () => webhook.MarcarProcessado(Guid.Empty);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Empresa*");
    }

    [Fact]
    public void MarcarProcessado_rejeita_transicao_de_status_nao_received()
    {
        var webhook = NovoRecebido();
        webhook.MarcarOrfao("teste");

        var act = () => webhook.MarcarProcessado(Guid.NewGuid());

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*status*");
    }

    // ── Transição: MarcarOrfao ─────────────────────────────────────────

    [Fact]
    public void MarcarOrfao_seta_status_orphan_e_motivo_e_processado_em()
    {
        var webhook = NovoRecebido();

        webhook.MarcarOrfao("payment.external_reference não bate com nenhuma Fatura");

        webhook.Status.Should().Be(WebhookProcessadoStatus.Orphan);
        webhook.Motivo.Should().Be("payment.external_reference não bate com nenhuma Fatura");
        webhook.ProcessadoEm.Should().NotBeNull();
        webhook.EmpresaId.Should().BeNull("órfão = não conseguimos resolver a empresa");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MarcarOrfao_rejeita_motivo_vazio(string? motivo)
    {
        var webhook = NovoRecebido();

        var act = () => webhook.MarcarOrfao(motivo!);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Motivo*");
    }

    [Fact]
    public void MarcarOrfao_rejeita_transicao_de_status_nao_received()
    {
        var webhook = NovoRecebido();
        webhook.MarcarProcessado(Guid.NewGuid());

        var act = () => webhook.MarcarOrfao("motivo qualquer");

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*status*");
    }

    // ── Transição: MarcarErro ──────────────────────────────────────────

    [Fact]
    public void MarcarErro_seta_status_error_e_motivo_e_processado_em()
    {
        var webhook = NovoRecebido();

        webhook.MarcarErro("NullReferenceException ao mapear payload");

        webhook.Status.Should().Be(WebhookProcessadoStatus.Error);
        webhook.Motivo.Should().Be("NullReferenceException ao mapear payload");
        webhook.ProcessadoEm.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MarcarErro_rejeita_motivo_vazio(string? motivo)
    {
        var webhook = NovoRecebido();

        var act = () => webhook.MarcarErro(motivo!);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Motivo*");
    }

    [Fact]
    public void MarcarErro_eh_permitido_apos_orfao_para_retry_que_falhou()
    {
        // Cenário: webhook ficou órfão (não bateu Fatura), depois job reprocessa
        // e desta vez bate de fato mas mapeamento falha. Aceitamos a transição
        // orphan -> error para deixar o erro mais visível que ficar como orphan.
        var webhook = NovoRecebido();
        webhook.MarcarOrfao("não bateu");

        var act = () => webhook.MarcarErro("crashou no retry");

        act.Should().NotThrow();
        webhook.Status.Should().Be(WebhookProcessadoStatus.Error);
    }
}
