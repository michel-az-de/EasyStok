using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;

namespace EasyStock.Application.Tests.Services.Notifications;

public class ResolvedorCanalTests
{
    private static readonly ResolvedorCanal Sut = new();
    private static readonly DateTime Agora = DateTime.UtcNow;

    private static ConfiguracaoCanal CanalAtivo(CanalNotificacao canal) =>
        ConfiguracaoCanal.Criar(canal, "stub", empresaId: null);

    [Fact]
    public void Transacional_ignora_optout()
    {
        var consentimentos = new List<ConsentimentoNotificacao>
        {
            ConsentimentoNotificacao.Registrar(Guid.NewGuid(), CanalNotificacao.Email,
                CategoriaConteudoNotificacao.Transacional, optIn: false, "user@x.com")
        };
        var configs = new List<ConfiguracaoCanal> { CanalAtivo(CanalNotificacao.Email) };

        var resultado = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Transacional,
            [CanalNotificacao.Email],
            consentimentos, configs, [], Agora);

        resultado.Should().Contain(CanalNotificacao.Email);
    }

    [Fact]
    public void Marketing_exige_optin_explicito()
    {
        var configs = new List<ConfiguracaoCanal> { CanalAtivo(CanalNotificacao.Email) };

        var resultado = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Marketing,
            [CanalNotificacao.Email],
            consentimentos: [], configs, [], Agora);

        resultado.Should().NotContain(CanalNotificacao.Email);
    }

    [Fact]
    public void Marketing_com_optin_permite_canal()
    {
        var usuarioId = Guid.NewGuid();
        var consentimentos = new List<ConsentimentoNotificacao>
        {
            ConsentimentoNotificacao.Registrar(usuarioId, CanalNotificacao.Email,
                CategoriaConteudoNotificacao.Marketing, optIn: true, "user@x.com")
        };
        var configs = new List<ConfiguracaoCanal> { CanalAtivo(CanalNotificacao.Email) };

        var resultado = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Marketing,
            [CanalNotificacao.Email],
            consentimentos, configs, [], Agora);

        resultado.Should().Contain(CanalNotificacao.Email);
    }

    [Fact]
    public void KillSwitch_global_bloqueia_canal()
    {
        var bloqueio = BloqueioNotificacao.Criar("manutencao", "admin@x.com");
        var configs = new List<ConfiguracaoCanal> { CanalAtivo(CanalNotificacao.Email) };

        var resultado = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Transacional,
            [CanalNotificacao.Email],
            consentimentos: [], configs, [bloqueio], Agora);

        resultado.Should().NotContain(CanalNotificacao.Email);
    }

    [Fact]
    public void Canal_inativo_e_excluido()
    {
        var config = ConfiguracaoCanal.Criar(CanalNotificacao.Sms, "stub", empresaId: null);
        config.Desativar("admin@x.com");

        var resultado = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Transacional,
            [CanalNotificacao.Sms],
            consentimentos: [], [config], [], Agora);

        resultado.Should().NotContain(CanalNotificacao.Sms);
    }

    [Fact]
    public void Operacional_adiciona_inapp_como_fallback_minimo()
    {
        var configs = new List<ConfiguracaoCanal> { CanalAtivo(CanalNotificacao.InApp) };

        // Pede somente Email (bloqueado), mas InApp deve ser adicionado automaticamente
        var resultado = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Operacional,
            [CanalNotificacao.Email],
            consentimentos: [], configs, [], Agora);

        resultado.Should().Contain(CanalNotificacao.InApp);
    }

    [Fact]
    public void Resposta_mantem_ordem_de_preferencia_da_rotina()
    {
        var configs = new List<ConfiguracaoCanal>
        {
            CanalAtivo(CanalNotificacao.Email),
            CanalAtivo(CanalNotificacao.Sms),
            CanalAtivo(CanalNotificacao.InApp)
        };

        var resultado = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Transacional,
            [CanalNotificacao.Sms, CanalNotificacao.Email, CanalNotificacao.InApp],
            consentimentos: [], configs, [], Agora);

        resultado.Should().ContainInOrder(
            CanalNotificacao.Sms, CanalNotificacao.Email, CanalNotificacao.InApp);
    }
}
