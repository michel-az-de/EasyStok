using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Notifications;

public class EventoNotificacaoTests
{
    [Fact]
    public void Criar_inicia_pendente_e_gera_correlation_id()
    {
        var e = EventoNotificacao.Criar(
            TipoEventoNotificacao.ResetSenha,
            empresaId: Guid.NewGuid(),
            payloadJson: "{\"usuarioId\":\"abc\"}");

        e.Status.Should().Be(StatusEventoNotificacao.Pendente);
        e.CorrelationId.Should().NotBeNullOrEmpty();
        e.PayloadJson.Should().Contain("usuarioId");
        e.ProcessadoEm.Should().BeNull();
    }

    [Fact]
    public void MarcarComoProcessado_seta_processado_e_data()
    {
        var e = EventoNotificacao.Criar(TipoEventoNotificacao.ResetSenha, Guid.NewGuid(), "{}");

        e.MarcarComoProcessado();

        e.Status.Should().Be(StatusEventoNotificacao.Processado);
        e.ProcessadoEm.Should().NotBeNull();
    }

    [Fact]
    public void MarcarComoFalhado_grava_erro()
    {
        var e = EventoNotificacao.Criar(TipoEventoNotificacao.ResetSenha, Guid.NewGuid(), "{}");

        e.MarcarComoFalhado("template não encontrado");

        e.Status.Should().Be(StatusEventoNotificacao.Falhado);
        e.ErroProcessamento.Should().Be("template não encontrado");
    }

    [Fact]
    public void PurgarPayload_troca_o_json_por_objeto_vazio()
    {
        // N2: o payload do evento de segurança carrega o segredo (token, código). Apagado, ainda é JSON válido.
        var e = EventoNotificacao.Criar(
            TipoEventoNotificacao.ResetSenha,
            Guid.NewGuid(),
            """{"usuarioId":"abc","token":"482913"}""");

        e.PurgarPayload();

        e.PayloadJson.Should().Be("{}");
        e.Status.Should().Be(StatusEventoNotificacao.Pendente, "purgar não muda o estado do evento");
    }

    [Fact]
    public void MarcarComoExpirado_e_terminal_e_guarda_o_motivo()
    {
        var evento = EventoNotificacao.Criar(TipoEventoNotificacao.PedidoPagoConfirmado, Guid.NewGuid(), """{"pedido":"1"}""");

        evento.MarcarComoExpirado("passou do prazo de 120 min");

        evento.Status.Should().Be(StatusEventoNotificacao.Expirado);
        evento.ProcessadoEm.Should().NotBeNull();
        evento.ErroProcessamento.Should().Be("passou do prazo de 120 min");
        evento.PayloadJson.Should().Be("""{"pedido":"1"}""", "só o evento de segurança apaga o payload ao expirar");
    }

    [Fact]
    public void MarcarComoExpirado_apaga_o_payload_quando_pedido()
    {
        // ResetSenha e ConfirmacaoEmail carregam o token no payload: expirar sem purgar deixaria o segredo no banco.
        var evento = EventoNotificacao.Criar(TipoEventoNotificacao.ResetSenha, Guid.NewGuid(), """{"token":"482913"}""");

        evento.MarcarComoExpirado("passou do prazo", purgarPayload: true);

        evento.PayloadJson.Should().Be("{}");
    }
}
