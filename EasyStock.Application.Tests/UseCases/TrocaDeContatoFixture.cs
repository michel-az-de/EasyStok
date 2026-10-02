using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Monta o <see cref="TrocaDeContatoService"/> com dependências falsas (N4) e guarda os eventos enfileirados para os
/// testes da troca de contato, do admin e da confirmação.
/// </summary>
internal sealed class TrocaDeContatoFixture
{
    public const string SenhaCerta = "SenhaAntiga@123";

    public sealed record EventoEnfileirado(TipoEventoNotificacao Tipo, Guid EmpresaId, Dictionary<string, JsonElement> Payload);

    public IUsuarioRepository Usuarios { get; }
    public IEmailConfirmationTokenRepository Tokens { get; } = Substitute.For<IEmailConfirmationTokenRepository>();
    public INotificadorService Notificador { get; } = Substitute.For<INotificadorService>();
    public IEmpresaPadraoResolver EmpresaPadrao { get; } = Substitute.For<IEmpresaPadraoResolver>();
    public ITenantContextAccessor Tenant { get; } = Substitute.For<ITenantContextAccessor>();
    public ICurrentUserAccessor UsuarioAtual { get; } = Substitute.For<ICurrentUserAccessor>();
    public FakeUnitOfWork UnitOfWork { get; }
    public List<EventoEnfileirado> Eventos { get; } = [];
    public List<EmailConfirmationToken> TokensGravados { get; } = [];
    public Guid EmpresaId { get; } = Guid.NewGuid();
    public TrocaDeContatoService Servico { get; }

    public TrocaDeContatoFixture(
        Dictionary<string, string?>? configuracao = null, IUsuarioRepository? usuarios = null, FakeUnitOfWork? unitOfWork = null)
    {
        Usuarios = usuarios ?? Substitute.For<IUsuarioRepository>();
        UnitOfWork = unitOfWork ?? new FakeUnitOfWork();
        UsuarioAtual.EmpresaId.Returns(EmpresaId);
        Notificador.EnfileirarEventoAsync(
                Arg.Any<TipoEventoNotificacao>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(c =>
            {
                Eventos.Add(new EventoEnfileirado(
                    c.Arg<TipoEventoNotificacao>(), c.ArgAt<Guid>(1),
                    JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(c.ArgAt<string>(2))!));
                return Guid.NewGuid();
            });
        Tokens.When(t => t.AddAsync(Arg.Any<EmailConfirmationToken>()))
            .Do(c => TokensGravados.Add(c.Arg<EmailConfirmationToken>()));

        Servico = new TrocaDeContatoService(
            Usuarios, Tokens, Notificador, EmpresaPadrao, Tenant, UsuarioAtual, new FakePasswordHasher(), UnitOfWork,
            new ConfigurationBuilder().AddInMemoryCollection(configuracao ?? []).Build(),
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 17, 30, 0, TimeSpan.Zero)),
            Substitute.For<ILogger<TrocaDeContatoService>>());
    }

    /// <summary>Usuário com a senha <see cref="SenhaCerta"/> e e-mail confirmado, já devolvido pelo repositório.</summary>
    public Usuario NovoUsuario(string email = "ana@casadababa.com", params Guid[] empresas)
    {
        var usuario = Usuario.Criar("Ana", email, FakePasswordHasher.MakeHash(SenhaCerta));
        usuario.EmailConfirmado = true;
        foreach (var empresa in empresas.Length > 0 ? empresas : [EmpresaId])
            usuario.Empresas.Add(new UsuarioEmpresa
            {
                Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = empresa, Ativo = true, CriadoEm = DateTime.UtcNow
            });
        Usuarios.GetByIdAsync(usuario.Id).Returns(usuario);
        return usuario;
    }
}
