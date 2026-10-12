using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

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

    [Fact]
    public void Seguranca_ignora_consentimento_como_transacional()
    {
        // N2: redefinir senha não pode depender de opt-in. Mesmo com opt-out registrado, o canal sai.
        var consentimentos = new List<ConsentimentoNotificacao>
        {
            ConsentimentoNotificacao.Registrar(Guid.NewGuid(), CanalNotificacao.Email,
                CategoriaConteudoNotificacao.Seguranca, optIn: false, "user@x.com")
        };
        var configs = new List<ConfiguracaoCanal> { CanalAtivo(CanalNotificacao.Email) };

        var resultado = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Seguranca,
            [CanalNotificacao.Email],
            consentimentos, configs, [], Agora);

        resultado.Should().Contain(CanalNotificacao.Email);
    }

    [Fact]
    public void Seguranca_continua_sujeita_ao_kill_switch_e_ao_canal_ativo()
    {
        // Ignorar o consentimento não ignora o kill switch global nem o canal desligado.
        var bloqueio = BloqueioNotificacao.Criar("manutencao", "admin@x.com");
        var configs = new List<ConfiguracaoCanal>
        {
            CanalAtivo(CanalNotificacao.Email),
            ConfiguracaoCanal.Criar(CanalNotificacao.Sms, "stub", empresaId: null),
        };
        configs[1].Desativar("admin@x.com");

        var comKillSwitch = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Seguranca, [CanalNotificacao.Email],
            consentimentos: [], configs, [bloqueio], Agora);
        var comCanalInativo = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Seguranca, [CanalNotificacao.Sms],
            consentimentos: [], configs, [], Agora);

        comKillSwitch.Should().BeEmpty();
        comCanalInativo.Should().BeEmpty();
    }

    [Fact]
    public void PausaDaEmpresaSuprimeOCanalDaEmpresaENaoDasOutras()
    {
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        var pausaDeEmail = BloqueioNotificacao.Criar("pausa", "admin@x.com", empresaA, CanalNotificacao.Email);
        var configs = new List<ConfiguracaoCanal> { CanalAtivo(CanalNotificacao.Email), CanalAtivo(CanalNotificacao.Sms) };

        var daA = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Operacional, [CanalNotificacao.Email, CanalNotificacao.Sms],
            consentimentos: [], configs, [pausaDeEmail], Agora, empresaA, inAppTemTemplate: false);
        var daB = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Operacional, [CanalNotificacao.Email, CanalNotificacao.Sms],
            consentimentos: [], configs, [pausaDeEmail], Agora, empresaB, inAppTemTemplate: false);

        daA.Should().Equal(CanalNotificacao.Sms);
        daB.Should().Equal(CanalNotificacao.Email, CanalNotificacao.Sms);
    }

    [Fact]
    public void PausaDaEmpresaNaoBloqueiaSeguranca()
    {
        var empresa = Guid.NewGuid();
        var pausa = BloqueioNotificacao.Criar("pausa", "admin@x.com", empresa);
        var configs = new List<ConfiguracaoCanal> { CanalAtivo(CanalNotificacao.Email) };

        var seguranca = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Seguranca, [CanalNotificacao.Email],
            consentimentos: [], configs, [pausa], Agora, empresa);
        var transacional = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Transacional, [CanalNotificacao.Email],
            consentimentos: [], configs, [pausa], Agora, empresa);

        seguranca.Should().Equal(CanalNotificacao.Email);
        transacional.Should().BeEmpty();
    }

    [Fact]
    public void InAppAcrescentadoSoComTemplate()
    {
        var configs = new List<ConfiguracaoCanal> { CanalAtivo(CanalNotificacao.InApp) };

        var comTemplate = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Operacional, [CanalNotificacao.Email],
            consentimentos: [], configs, [], Agora, inAppTemTemplate: true);
        var semTemplate = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Operacional, [CanalNotificacao.Email],
            consentimentos: [], configs, [], Agora, inAppTemTemplate: false);

        comTemplate.Should().Contain(CanalNotificacao.InApp);
        semTemplate.Should().NotContain(CanalNotificacao.InApp);
    }

    // N6: WhatsApp de plataforma exige opt-in explícito de usuário identificado, até em Segurança.

    [Fact]
    public void WhatsAppDePlataformaSemOptInPulaOCanalMesmoEmSeguranca()
    {
        var usuarioId = Guid.NewGuid();
        var configs = new List<ConfiguracaoCanal> { CanalAtivo(CanalNotificacao.WhatsApp), CanalAtivo(CanalNotificacao.Email) };
        var semRegistro = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Seguranca, [CanalNotificacao.WhatsApp, CanalNotificacao.Email],
            consentimentos: [], configs, [], Agora, remetente: OrigemRemetente.Plataforma, usuarioDestinoId: usuarioId);
        var optOut = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Seguranca, [CanalNotificacao.WhatsApp, CanalNotificacao.Email],
            [ConsentimentoNotificacao.Registrar(usuarioId, CanalNotificacao.WhatsApp,
                CategoriaConteudoNotificacao.Seguranca, optIn: false, "u")],
            configs, [], Agora, remetente: OrigemRemetente.Plataforma, usuarioDestinoId: usuarioId);
        var comOptIn = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Seguranca, [CanalNotificacao.WhatsApp, CanalNotificacao.Email],
            [ConsentimentoNotificacao.Registrar(usuarioId, CanalNotificacao.WhatsApp,
                CategoriaConteudoNotificacao.Seguranca, optIn: true, "u")],
            configs, [], Agora, remetente: OrigemRemetente.Plataforma, usuarioDestinoId: usuarioId);

        semRegistro.Should().Equal(CanalNotificacao.Email);
        optOut.Should().Equal(CanalNotificacao.Email);
        comOptIn.Should().Equal(CanalNotificacao.WhatsApp, CanalNotificacao.Email);
    }

    [Fact]
    public void WhatsAppDePlataformaSemUsuarioEhPulado()
    {
        // Telefone solto no payload: sem usuário não há quem tenha dado opt-in.
        var configs = new List<ConfiguracaoCanal> { CanalAtivo(CanalNotificacao.WhatsApp), CanalAtivo(CanalNotificacao.Email) };

        var r = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Operacional, [CanalNotificacao.WhatsApp, CanalNotificacao.Email],
            consentimentos: [], configs, [], Agora, remetente: OrigemRemetente.Plataforma, usuarioDestinoId: null,
            inAppTemTemplate: false);

        r.Should().Equal(CanalNotificacao.Email);
    }

    [Fact]
    public void EmailDePlataformaSegueIgnorandoOptOutEmSeguranca()
    {
        var usuarioId = Guid.NewGuid();
        var configs = new List<ConfiguracaoCanal> { CanalAtivo(CanalNotificacao.Email) };

        var r = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Seguranca, [CanalNotificacao.Email],
            [ConsentimentoNotificacao.Registrar(usuarioId, CanalNotificacao.Email,
                CategoriaConteudoNotificacao.Seguranca, optIn: false, "u")],
            configs, [], Agora, remetente: OrigemRemetente.Plataforma, usuarioDestinoId: usuarioId);

        r.Should().Equal(CanalNotificacao.Email);
    }

    [Fact]
    public void WhatsAppDaLojaSegueComoHoje()
    {
        var configs = new List<ConfiguracaoCanal> { CanalAtivo(CanalNotificacao.WhatsApp) };

        var semParametro = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Transacional, [CanalNotificacao.WhatsApp], [], configs, [], Agora);
        var lojaExplicita = Sut.ResolverCanaisPermitidos(
            CategoriaConteudoNotificacao.Operacional, [CanalNotificacao.WhatsApp], [], configs, [], Agora,
            remetente: OrigemRemetente.Loja, inAppTemTemplate: false);

        semParametro.Should().Equal(CanalNotificacao.WhatsApp);
        lojaExplicita.Should().Equal(CanalNotificacao.WhatsApp);
    }
}
