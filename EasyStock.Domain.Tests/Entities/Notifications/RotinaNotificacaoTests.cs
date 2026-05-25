using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Notifications;

public class RotinaNotificacaoTests
{
    [Fact]
    public void Criar_com_trigger_cron_exige_expressao()
    {
        var ex = Record.Exception(() => RotinaNotificacao.Criar(
            "rot", "Rotina", TipoEventoNotificacao.ProdutoVencendo,
            TriggerTipoRotina.Cron, "produto_vencendo_email",
            CategoriaConteudoNotificacao.Operacional,
            cronExpression: null));

        ex.Should().BeOfType<ArgumentException>();
    }

    [Fact]
    public void Criar_com_trigger_evento_nao_exige_cron()
    {
        var rot = RotinaNotificacao.Criar(
            "rot_reset", "Reset", TipoEventoNotificacao.ResetSenha,
            TriggerTipoRotina.Evento, "reset_senha_email",
            CategoriaConteudoNotificacao.Transacional);

        rot.CronExpression.Should().BeNull();
        rot.Ativa.Should().BeFalse();
    }

    [Fact]
    public void Ativar_e_Desativar_atualizam_metadata()
    {
        var rot = RotinaNotificacao.Criar(
            "r", "n", TipoEventoNotificacao.ProdutoVencendo, TriggerTipoRotina.Cron,
            "t", CategoriaConteudoNotificacao.Operacional, "0 8 * * *");

        rot.Ativar("admin@x.com");
        rot.Ativa.Should().BeTrue();
        rot.AtualizadaPor.Should().Be("admin@x.com");

        rot.Desativar("outro@x.com");
        rot.Ativa.Should().BeFalse();
        rot.AtualizadaPor.Should().Be("outro@x.com");
    }

    [Fact]
    public void DefinirParametros_sobrescreve_json()
    {
        var rot = RotinaNotificacao.Criar(
            "r", "n", TipoEventoNotificacao.ProdutoVencendo, TriggerTipoRotina.Cron,
            "t", CategoriaConteudoNotificacao.Operacional, "0 8 * * *");

        rot.DefinirParametros("{\"diasAntes\":[7,3]}", "admin@x.com");

        rot.ParametrosJson.Should().Be("{\"diasAntes\":[7,3]}");
    }
}
