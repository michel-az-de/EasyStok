using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>N4: quem a rotina de plataforma avisa, com repositórios falsos.</summary>
public class ResolvedorAudienciaTests
{
    private static readonly DateTime Verificado = new(2026, 10, 2, 15, 0, 0, DateTimeKind.Utc);

    private readonly IAudienciaUsuarios _usuarios = Substitute.For<IAudienciaUsuarios>();
    private readonly ISuperAdminsDaPlataforma _superAdmins = Substitute.For<ISuperAdminsDaPlataforma>();
    private readonly IConsentimentoRepository _consentimentos = Substitute.For<IConsentimentoRepository>();
    private readonly IPreferenciaNotificacaoRepository _preferencias = Substitute.For<IPreferenciaNotificacaoRepository>();
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Dictionary<string, string?> _config = new()
    {
        [ResolvedorAudiencia.ChaveWhatsAppPlataforma] = "1234567890"
    };

    public ResolvedorAudienciaTests()
    {
        _consentimentos.ListarPorUsuariosAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        _preferencias.ListarDaRotinaAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private ResolvedorAudiencia Criar() => new(
        _usuarios, _superAdmins, _consentimentos, _preferencias,
        new ConfigurationBuilder().AddInMemoryCollection(_config).Build(),
        NullLogger<ResolvedorAudiencia>.Instance);

    private RotinaNotificacao Rotina(
        string? audiencia, CategoriaConteudoNotificacao categoria = CategoriaConteudoNotificacao.Operacional, Guid? empresaId = null)
    {
        var rotina = RotinaNotificacao.Criar(
            "prazo_estourado_global", "Prazo", TipoEventoNotificacao.PrazoEstourado, TriggerTipoRotina.Evento,
            "prazo_estourado_email_v1", categoria, empresaId: empresaId);
        if (audiencia is not null)
            rotina.DefinirParametros($$"""{"modoCanais":"todos","audiencia":"{{audiencia}}"}""", "system");
        return rotina;
    }

    private static UsuarioParaAudiencia Pessoa(
        string nome, bool confirmado = true, bool ativo = true, string? telefone = null, DateTime? verificadoEm = null,
        bool convitePendente = false) =>
        new(Guid.NewGuid(), nome, $"{nome.ToLowerInvariant()}@casadababa.com", confirmado, ativo, telefone, verificadoEm, convitePendente);

    private void Gestores(params UsuarioParaAudiencia[] pessoas) =>
        _usuarios.ListarDaEmpresaAsync(_empresaId, Arg.Any<IReadOnlyCollection<EasyStock.Domain.Enums.NivelAcesso>>(), Arg.Any<CancellationToken>())
            .Returns(pessoas);

    private void OptIn(Guid usuarioId, CanalNotificacao canal, CategoriaConteudoNotificacao categoria, bool optIn = true) =>
        _consentimentos.ListarPorUsuariosAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(usuarioId)), Arg.Any<CancellationToken>())
            .Returns([ConsentimentoNotificacao.Registrar(usuarioId, canal, categoria, optIn, "teste")]);

    [Fact]
    public async Task UsuarioComEmailConfirmadoRecebePorEmail()
    {
        var ana = Pessoa("Ana");
        Gestores(ana);

        var r = await Criar().ResolverAsync(Rotina("gestores"), _empresaId, null);

        r.Should().ContainSingle().Which.Email.Should().Be("ana@casadababa.com");
    }

    [Fact]
    public async Task EmailNaoConfirmadoNaoRecebeEmAudienciaColetiva()
    {
        Gestores(Pessoa("Ana", confirmado: false));

        var r = await Criar().ResolverAsync(Rotina("gestores"), _empresaId, null);

        r.Should().ContainSingle().Which.Email.Should().BeNull();
    }

    // ── N9: o convidado ainda não verificou o telefone; o atestado da dona faz o papel do opt-in até o aceite ───

    [Fact]
    public async Task ConvidadoComAtestadoRecebePorWhatsAppSemTelefoneVerificado()
    {
        var ana = Pessoa("Ana", confirmado: false, telefone: "+5511999991234", verificadoEm: null, convitePendente: true);
        _usuarios.ObterAsync(ana.Id, Arg.Any<CancellationToken>()).Returns(ana);
        OptIn(ana.Id, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Seguranca);

        var r = await Criar().ResolverAsync(
            Rotina("convidado", CategoriaConteudoNotificacao.Seguranca), _empresaId, ana.Id);

        var destinatario = r.Should().ContainSingle().Subject;
        destinatario.Telefone.Should().Be("+5511999991234");
        destinatario.Email.Should().Be("ana@casadababa.com", "o e-mail do convidado sempre recebe, mesmo sem confirmar");
    }

    [Fact]
    public async Task ConvidadoSemAtestadoSoRecebePorEmail()
    {
        var ana = Pessoa("Ana", confirmado: false, telefone: "+5511999991234", verificadoEm: null, convitePendente: true);
        _usuarios.ObterAsync(ana.Id, Arg.Any<CancellationToken>()).Returns(ana);

        var r = await Criar().ResolverAsync(
            Rotina("convidado", CategoriaConteudoNotificacao.Seguranca), _empresaId, ana.Id);

        var destinatario = r.Should().ContainSingle().Subject;
        destinatario.Telefone.Should().BeNull();
        destinatario.Email.Should().Be("ana@casadababa.com");
    }

    [Fact]
    public async Task ConvidadoQueJaAceitouNaoTemARelaxacaoDoTelefone()
    {
        var ana = Pessoa("Ana", telefone: "+5511999991234", verificadoEm: null, convitePendente: false);
        _usuarios.ObterAsync(ana.Id, Arg.Any<CancellationToken>()).Returns(ana);
        OptIn(ana.Id, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Seguranca);

        var r = await Criar().ResolverAsync(
            Rotina("convidado", CategoriaConteudoNotificacao.Seguranca), _empresaId, ana.Id);

        r.Should().ContainSingle().Which.Telefone.Should().BeNull("a relaxação vale só até o aceite");
    }

    [Fact]
    public async Task ConvidadoSemUsuarioIdNoPayloadCaiNasChavesDoPayload()
    {
        (await Criar().ResolverAsync(Rotina("convidado", CategoriaConteudoNotificacao.Seguranca), _empresaId, null))
            .Should().BeNull();
    }

    [Fact]
    public async Task AudienciaUsuarioContinuaExigindoTelefoneVerificado()
    {
        var ana = Pessoa("Ana", telefone: "+5511999991234", verificadoEm: null, convitePendente: true);
        _usuarios.ObterAsync(ana.Id, Arg.Any<CancellationToken>()).Returns(ana);
        OptIn(ana.Id, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Seguranca);

        var r = await Criar().ResolverAsync(
            Rotina("usuario", CategoriaConteudoNotificacao.Seguranca), _empresaId, ana.Id);

        r.Should().ContainSingle().Which.Telefone.Should().BeNull("só a audiência convidado relaxa a verificação");
    }

    [Fact]
    public async Task UsuarioExplicitoComEmailNaoConfirmadoRecebe()
    {
        var ana = Pessoa("Ana", confirmado: false);
        _usuarios.ObterAsync(ana.Id, Arg.Any<CancellationToken>()).Returns(ana);

        var r = await Criar().ResolverAsync(Rotina("usuario"), _empresaId, ana.Id);

        r.Should().ContainSingle().Which.Email.Should().Be("ana@casadababa.com");
    }

    [Fact]
    public async Task UsuarioExplicitoInativoNaoRecebe()
    {
        var ana = Pessoa("Ana", ativo: false);
        _usuarios.ObterAsync(ana.Id, Arg.Any<CancellationToken>()).Returns(ana);

        (await Criar().ResolverAsync(Rotina("usuario"), _empresaId, ana.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task TelefoneNaoVerificadoNaoRecebePorWhatsApp()
    {
        var ana = Pessoa("Ana", telefone: "+5511997573992", verificadoEm: null);
        Gestores(ana);
        OptIn(ana.Id, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Operacional);

        var r = await Criar().ResolverAsync(Rotina("gestores"), _empresaId, null);

        r.Should().ContainSingle().Which.Telefone.Should().BeNull();
    }

    [Fact]
    public async Task SemOptInExplicitoNaoRecebePorWhatsApp()
    {
        Gestores(Pessoa("Ana", telefone: "+5511997573992", verificadoEm: Verificado));

        var r = await Criar().ResolverAsync(Rotina("gestores"), _empresaId, null);

        r.Should().ContainSingle().Which.Telefone.Should().BeNull("Operacional é permissivo para os outros canais, mas o WhatsApp da equipe exige registro");
    }

    [Fact]
    public async Task OptInDeOutraCategoriaOuRevogadoNaoLiberaOWhatsApp()
    {
        var ana = Pessoa("Ana", telefone: "+5511997573992", verificadoEm: Verificado);
        Gestores(ana);
        OptIn(ana.Id, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Seguranca);

        (await Criar().ResolverAsync(Rotina("gestores"), _empresaId, null))!
            .Single().Telefone.Should().BeNull("o opt-in é na categoria da rotina");

        OptIn(ana.Id, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Operacional, optIn: false);
        (await Criar().ResolverAsync(Rotina("gestores"), _empresaId, null))!.Single().Telefone.Should().BeNull();
    }

    [Fact]
    public async Task TelefoneVerificadoComOptInRecebePorWhatsApp()
    {
        var ana = Pessoa("Ana", telefone: "+5511997573992", verificadoEm: Verificado);
        Gestores(ana);
        OptIn(ana.Id, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Operacional);

        var r = await Criar().ResolverAsync(Rotina("gestores"), _empresaId, null);

        r.Should().ContainSingle().Which.Telefone.Should().Be("+5511997573992");
    }

    [Fact]
    public async Task SemWhatsAppDePlataformaConfiguradoNaoEntregaWhatsApp()
    {
        _config.Remove(ResolvedorAudiencia.ChaveWhatsAppPlataforma);
        var ana = Pessoa("Ana", telefone: "+5511997573992", verificadoEm: Verificado);
        Gestores(ana);
        OptIn(ana.Id, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Operacional);

        var r = await Criar().ResolverAsync(Rotina("gestores"), _empresaId, null);

        r.Should().ContainSingle().Which.Telefone.Should().BeNull();
    }

    [Fact]
    public async Task PreferenciaDesligadaPulaAPessoa()
    {
        var ana = Pessoa("Ana");
        var leo = Pessoa("Leo");
        Gestores(ana, leo);
        _preferencias.ListarDaRotinaAsync(
                _empresaId, "prazo_estourado_global", Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([PreferenciaNotificacaoUsuario.Criar(ana.Id, _empresaId, "prazo_estourado_global", habilitada: false)]);

        var r = await Criar().ResolverAsync(Rotina("gestores"), _empresaId, null);

        r.Should().ContainSingle().Which.UsuarioId.Should().Be(leo.Id);
    }

    [Fact]
    public async Task PreferenciaNaoDesligaSeguranca()
    {
        var ana = Pessoa("Ana");
        _usuarios.ObterAsync(ana.Id, Arg.Any<CancellationToken>()).Returns(ana);
        _preferencias.ListarDaRotinaAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([PreferenciaNotificacaoUsuario.Criar(ana.Id, _empresaId, "prazo_estourado_global", habilitada: false)]);

        var r = await Criar().ResolverAsync(
            Rotina("usuario", CategoriaConteudoNotificacao.Seguranca), _empresaId, ana.Id);

        r.Should().ContainSingle().Which.UsuarioId.Should().Be(ana.Id);
    }

    [Fact]
    public async Task GestoresSaoAdminEGerenteDaEmpresaDoEvento()
    {
        Gestores(Pessoa("Ana"));

        await Criar().ResolverAsync(Rotina("gestores"), _empresaId, null);
        await Criar().ResolverAsync(Rotina("admins"), _empresaId, null);

        await _usuarios.Received(1).ListarDaEmpresaAsync(
            _empresaId,
            Arg.Is<IReadOnlyCollection<EasyStock.Domain.Enums.NivelAcesso>>(n =>
                n.Count == 2 && n.Contains(EasyStock.Domain.Enums.NivelAcesso.Admin) && n.Contains(EasyStock.Domain.Enums.NivelAcesso.Gerente)),
            Arg.Any<CancellationToken>());
        await _usuarios.Received(1).ListarDaEmpresaAsync(
            _empresaId,
            Arg.Is<IReadOnlyCollection<EasyStock.Domain.Enums.NivelAcesso>>(n =>
                n.Count == 1 && n.Contains(EasyStock.Domain.Enums.NivelAcesso.Admin)),
            Arg.Any<CancellationToken>());
        await _superAdmins.DidNotReceiveWithAnyArgs().ListarAsync();
    }

    [Fact]
    public async Task UsuarioDoPayloadVenceAAudienciaDaRotina()
    {
        var ana = Pessoa("Ana");
        _usuarios.ObterAsync(ana.Id, Arg.Any<CancellationToken>()).Returns(ana);

        var r = await Criar().ResolverAsync(Rotina("gestores"), _empresaId, ana.Id);

        r.Should().ContainSingle().Which.UsuarioId.Should().Be(ana.Id);
        await _usuarios.DidNotReceiveWithAnyArgs().ListarDaEmpresaAsync(default, default!, default);
    }

    [Fact]
    public async Task SuperadminsEmRotinaGlobalVemDaConsultaPropria()
    {
        var felipe = Pessoa("Felipe");
        _superAdmins.ListarAsync(Arg.Any<CancellationToken>()).Returns([felipe]);

        var r = await Criar().ResolverAsync(Rotina("superadmins"), _empresaId, null);

        r.Should().ContainSingle().Which.UsuarioId.Should().Be(felipe.Id);
    }

    [Fact]
    public async Task SuperadminsEmRotinaDaEmpresaEhIgnorado()
    {
        _superAdmins.ListarAsync(Arg.Any<CancellationToken>()).Returns([Pessoa("Felipe")]);

        var r = await Criar().ResolverAsync(Rotina("superadmins", empresaId: _empresaId), _empresaId, null);

        r.Should().BeNull("a rotina da empresa não pode apontar para os superadmins; o motor volta ao payload");
        await _superAdmins.DidNotReceiveWithAnyArgs().ListarAsync();
    }

    [Fact]
    public async Task AudienciaUsuarioSemUsuarioIdCaiNasChavesDoPayload()
    {
        var r = await Criar().ResolverAsync(Rotina("usuario"), _empresaId, null);

        r.Should().BeNull();
    }

    [Fact]
    public async Task SemAudienciaMantemAsChavesDoPayload()
    {
        var r = await Criar().ResolverAsync(Rotina(null), _empresaId, Guid.NewGuid());

        r.Should().BeNull();
        await _usuarios.DidNotReceiveWithAnyArgs().ObterAsync(default);
    }

    [Fact]
    public async Task InterruptorDesligadoVoltaAoPayload()
    {
        _config[ResolvedorAudiencia.ChaveHabilitada] = "false";
        Gestores(Pessoa("Ana"));

        var r = await Criar().ResolverAsync(Rotina("gestores"), _empresaId, null);

        r.Should().BeNull();
        await _usuarios.DidNotReceiveWithAnyArgs().ListarDaEmpresaAsync(default, default!, default);
    }

    [Fact]
    public async Task UsuarioInativoNaoEntraNaAudienciaColetiva()
    {
        Gestores(Pessoa("Ana", ativo: false), Pessoa("Leo"));

        var r = await Criar().ResolverAsync(Rotina("gestores"), _empresaId, null);

        r.Should().ContainSingle().Which.Nome.Should().Be("Leo");
    }
}
