using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.UseCases.EsqueciSenha;
using EasyStock.Application.UseCases.ResetarSenha;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.ValueObjects;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.Services.Auth;

/// <summary>Um evento que o use case estagiou no motor: o tipo, a empresa e o payload já lido.</summary>
internal sealed record EventoEnfileirado(TipoEventoNotificacao Tipo, Guid EmpresaId, JsonElement Payload);

/// <summary>
/// Monta o esqueci a senha (N8) inteiro com fakes: relógio, cache com TTL, <c>reset_tokens</c> com UPDATE condicional,
/// motor que só grava o que foi enfileirado. Nada de rede: se o use case tentasse enviar e-mail, o teste nem compilaria.
/// </summary>
internal sealed class CenarioDeAcesso
{
    public const string LinkConfigurado = "https://app.easystok.com.br/auth/redefinir-senha?token={0}";
    public static readonly DateTimeOffset Agora = new(2026, 10, 2, 13, 0, 0, TimeSpan.Zero);

    public FakeTimeProvider Relogio { get; } = new(Agora);
    public IUsuarioRepository Usuarios { get; } = Substitute.For<IUsuarioRepository>();
    public FakeResetTokenRepository Tokens { get; } = new();
    public IAuditLogRepository Auditoria { get; } = Substitute.For<IAuditLogRepository>();
    public List<AuditLog> Auditorias { get; } = [];
    public IConsentimentoRepository Consentimentos { get; } = Substitute.For<IConsentimentoRepository>();
    public INotificadorService Notificador { get; } = Substitute.For<INotificadorService>();
    public List<EventoEnfileirado> Eventos { get; } = [];
    public IEmpresaPadraoResolver EmpresaPadrao { get; } = Substitute.For<IEmpresaPadraoResolver>();
    public ITenantContextAccessor Tenant { get; } = Substitute.For<ITenantContextAccessor>();
    public IRefreshTokenRepository RefreshTokens { get; } = Substitute.For<IRefreshTokenRepository>();
    public ICacheService CacheDoRevogador { get; } = Substitute.For<ICacheService>();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeCacheComRelogio Cache { get; }
    public Guid EmpresaPadraoId { get; } = Guid.NewGuid();
    public Dictionary<string, string?> Configuracao { get; } = new()
    {
        ["Auth:LinkRedefinirSenha"] = LinkConfigurado,
        ["Notifications:WhatsApp:Plataforma:PhoneNumberId"] = "123456789012345",
    };

    public CenarioDeAcesso()
    {
        Cache = new FakeCacheComRelogio(Relogio);
        Auditoria.AddAsync(Arg.Do<AuditLog>(Auditorias.Add)).Returns(Task.CompletedTask);
        EmpresaPadrao.ResolverAsync(Arg.Any<CancellationToken>()).Returns(EmpresaPadraoId);
        Consentimentos.ListarPorUsuariosAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ConsentimentoNotificacao>());
        Notificador.EnfileirarEventoAsync(
                Arg.Any<TipoEventoNotificacao>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(call =>
            {
                Eventos.Add(new EventoEnfileirado(
                    call.ArgAt<TipoEventoNotificacao>(0), call.ArgAt<Guid>(1), JsonDocument.Parse(call.ArgAt<string>(2)).RootElement.Clone()));
                return Task.FromResult(Guid.NewGuid());
            });
    }

    public IConfiguration Config => new ConfigurationBuilder().AddInMemoryCollection(Configuracao).Build();

    public DateTime AgoraUtc => Relogio.GetUtcNow().UtcDateTime;

    public Usuario CriarUsuario(string email = "ana@casadababa.com", bool comEmpresa = true, string senha = "Senha@12345")
    {
        var usuario = Usuario.Criar("Ana", email, FakePasswordHasher.MakeHash(senha));
        if (comEmpresa)
            usuario.Empresas.Add(new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = Guid.NewGuid(), Ativo = true });
        Usuarios.GetByEmailAsync(usuario.Email).Returns(usuario);
        Usuarios.GetByIdAsync(usuario.Id).Returns(usuario);
        return usuario;
    }

    /// <summary>Telefone verificado e opt-in de WhatsApp em Segurança: a conta que recebe o código.</summary>
    public Usuario CriarUsuarioElegivelAoCodigo(string email = "ana@casadababa.com")
    {
        var usuario = CriarUsuario(email);
        usuario.DefinirTelefone(TelefoneE164.From("+5511999998888"));
        usuario.MarcarTelefoneVerificado(AgoraUtc.AddDays(-3));
        DarOptInDeWhatsApp(usuario);
        return usuario;
    }

    public void DarOptInDeWhatsApp(Usuario usuario) =>
        Consentimentos.ListarPorUsuariosAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([ConsentimentoNotificacao.Registrar(
                usuario.Id, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Seguranca, true, "teste")]);

    public static void TornarSuperAdmin(Usuario usuario) =>
        usuario.Perfis.Add(new UsuarioPerfil
        {
            UsuarioId = usuario.Id,
            Perfil = new Perfil { Nome = "SuperAdmin", Nivel = NivelAcesso.SuperAdmin },
        });

    public RevogadorSessoes Revogador() => new(
        Usuarios, RefreshTokens, CacheDoRevogador, Relogio, Substitute.For<ILogger<RevogadorSessoes>>());

    public EmpresaDoEventoAnonimo EmpresaDoEvento() =>
        new(EmpresaPadrao, Tenant, Substitute.For<ILogger<EmpresaDoEventoAnonimo>>());

    public LimitePedidosAcesso Limite() => new(Cache);

    public EsqueciSenhaUseCase EsqueciSenha(ILogger<EsqueciSenhaUseCase>? logger = null) => new(
        Usuarios, Tokens, Auditoria, Consentimentos, Notificador, EmpresaDoEvento(), Limite(), UnitOfWork, Config,
        Relogio, logger ?? Substitute.For<ILogger<EsqueciSenhaUseCase>>());

    public ConcluidorDeReset Concluidor(ILogger<ConcluidorDeReset>? logger = null) => new(
        Usuarios, Tokens, Auditoria, Revogador(), Notificador, EmpresaDoEvento(), new FakePasswordHasher(), Relogio,
        logger ?? Substitute.For<ILogger<ConcluidorDeReset>>());

    public ResetarSenhaUseCase ResetarSenha() => new(
        Tokens, Usuarios, Concluidor(), Limite(), UnitOfWork, Relogio, Substitute.For<ILogger<ResetarSenhaUseCase>>());

    public ResetarSenhaPorCodigoUseCase ResetarSenhaPorCodigo(ILogger<ResetarSenhaPorCodigoUseCase>? logger = null) => new(
        Tokens, Usuarios, Concluidor(), Limite(), UnitOfWork, Relogio,
        logger ?? Substitute.For<ILogger<ResetarSenhaPorCodigoUseCase>>());

    /// <summary>Pede a redefinição e devolve o link e o código (se houve) lidos do evento enfileirado.</summary>
    public async Task<(string Token, string? Codigo)> PedirAsync(Usuario usuario, string? ip = "203.0.113.7")
    {
        await EsqueciSenha().ExecuteAsync(new EsqueciSenhaCommand(usuario.Email, ip, "TesteAgent/1.0"));
        var payload = Eventos[^1].Payload;
        var link = payload.GetProperty("link_redefinicao").GetString()!;
        var token = Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..]);
        var codigo = payload.TryGetProperty("codigo", out var c) ? c.GetString() : null;
        return (token, codigo);
    }
}
