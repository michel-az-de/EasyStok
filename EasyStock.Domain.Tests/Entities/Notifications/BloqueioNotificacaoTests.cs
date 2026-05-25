using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Notifications;

public class BloqueioNotificacaoTests
{
    [Fact]
    public void EstaAtivo_retorna_true_para_bloqueio_recem_criado()
    {
        var b = BloqueioNotificacao.Criar("Manutenção", "admin@x.com");

        b.EstaAtivo(DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void EstaAtivo_retorna_false_apos_remocao()
    {
        var b = BloqueioNotificacao.Criar("X", "admin@x.com");
        b.Remover("admin@x.com");

        b.EstaAtivo(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void EstaAtivo_retorna_false_apos_expiracao()
    {
        var b = BloqueioNotificacao.Criar("X", "admin@x.com", expiraEm: DateTime.UtcNow.AddMinutes(-1));

        b.EstaAtivo(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void Bloqueio_global_sem_canal_nem_empresa()
    {
        var b = BloqueioNotificacao.Criar("kill switch", "admin@x.com");

        b.EmpresaId.Should().BeNull();
        b.Canal.Should().BeNull();
    }
}
